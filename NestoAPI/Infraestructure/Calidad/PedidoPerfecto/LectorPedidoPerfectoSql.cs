using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Calidad.PedidoPerfecto
{
    /// <summary>Todo lo leído para un periodo: los hechos de cada pedido y desde cuándo hay rastro de cada cosa.</summary>
    public class LecturaPedidoPerfecto
    {
        public List<HechosPedidoPerfecto> Pedidos { get; set; } = new List<HechosPedidoPerfecto>();
        public DateTime? FotosStockDesde { get; set; }
        public DateTime? QuitarPickingDesde { get; set; }
        public DateTime? EntregaIncompletaDesde { get; set; }
        public Dictionary<string, DateTime> PrimerEventoPorTipo { get; set; } = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Tablas opcionales que no están (para el aviso del resumen).</summary>
        public List<string> TablasQueFaltan { get; set; } = new List<string>();
    }

    public interface ILectorPedidoPerfecto
    {
        /// <summary>Pedidos de <paramref name="empresa"/> con Fecha en [<paramref name="desde"/>, <paramref name="hasta"/>).</summary>
        LecturaPedidoPerfecto Leer(string empresa, DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#914: SOLO LECTURA (READ UNCOMMITTED / NOLOCK), por tandas de un mes (una ida a la BD por mes con varios resultados sobre
    /// la tabla temporal #p de los pedidos del mes, sin N+1) más tres consultas para todo el periodo (Modificaciones, CalidadEventos y
    /// desde cuándo hay fotos). Sin TRY_CONVERT, STRING_SPLIT, STRING_AGG, OFFSET/FETCH ni SUM() OVER (ORDER BY): la BD no los tiene.
    /// Las tablas fuera del EDMX son opcionales (OBJECT_ID): sin ellas, su parte no se lee y el motivo queda «no evaluado».
    /// </summary>
    public class LectorPedidoPerfectoSql : ILectorPedidoPerfecto
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (pedido perfecto #914)";
        internal const int TIMEOUT_SEGUNDOS = 180;
        /// <summary>«Quitar picking» de Nesto deja rastro en Modificaciones desde NestoAPI#590 (09/11/26); Nesto viejo no lo dejaba.</summary>
        public static readonly DateTime QUITAR_PICKING_DESDE = new DateTime(2026, 11, 9);
        /// <summary>Las entregas incompletas del reparto propio se apuntan desde NestoAPI#880 corte 1 (19/11/26).</summary>
        public static readonly DateTime ENTREGA_INCOMPLETA_DESDE = new DateTime(2026, 11, 19);

        internal const string SQL_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.EnviosAgenciaReenvios', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Reenvios,
       CAST(CASE WHEN OBJECT_ID('dbo.EnviosAgenciaHaVuelto', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS HaVuelto,
       CAST(CASE WHEN OBJECT_ID('dbo.RepartoBultosPendientes', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS BultosPendientes,
       CAST(CASE WHEN OBJECT_ID('dbo.FechasEntregaFotos', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Fotos,
       CAST(CASE WHEN OBJECT_ID('dbo.PedidosModoServicioOrigen', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Origen,
       CAST(CASE WHEN OBJECT_ID('dbo.CalidadEventos', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Eventos";

        /// <summary>El universo del mes en #p (y sus facturas en #f). Ver docs/claude/calidad-kpi.md, «Universo».</summary>
        internal const string SQL_UNIVERSO = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
IF OBJECT_ID('tempdb..#p') IS NOT NULL DROP TABLE #p;
IF OBJECT_ID('tempdb..#f') IS NOT NULL DROP TABLE #f;
SELECT c.Empresa, c.[Número] AS Pedido, CAST(CAST(c.Fecha AS date) AS datetime) AS Fecha
INTO #p
FROM CabPedidoVta c WITH (NOLOCK)
WHERE c.Empresa = @empresa AND c.Fecha >= @desde AND c.Fecha < @hasta
  AND NULLIF(RTRIM(c.TipoRectificativa), '') IS NULL
  AND c.PedidoOrigen IS NULL
  AND c.NotaEntrega = 0
  AND EXISTS (SELECT 1 FROM LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.TipoLinea = 1 AND l.Cantidad > 0)
  AND NOT EXISTS (SELECT 1 FROM LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.Estado = -3);
CREATE CLUSTERED INDEX IX_p ON #p (Empresa, Pedido);
SELECT DISTINCT l.Empresa, l.[Número] AS Pedido, l.[Nº Cliente] AS Cliente, l.[Nº Factura] AS Factura
INTO #f
FROM LinPedidoVta l WITH (NOLOCK)
INNER JOIN #p p ON p.Empresa = l.Empresa AND p.Pedido = l.[Número]
WHERE l.[Nº Factura] IS NOT NULL AND RTRIM(l.[Nº Factura]) <> '';
";

        /// <summary>Resultado 1: cabecera + líneas agregadas (TipoLinea 1 = producto; estado ≥ 2 = albarán o factura). Servido, pendiente, albaranes,
        /// fechas, almacén y cursos miran solo las líneas de producto con cantidad POSITIVA (un cheque regalo en −1 o una devolución suelta no
        /// cuentan como «pendiente» ni como otro albarán); el Importe suma todas.</summary>
        internal const string SQL_PEDIDOS = @"
SELECT c.[Número] AS Pedido, c.Fecha, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(ISNULL(c.Contacto, '')) AS Contacto,
       RTRIM(ISNULL(c.Vendedor, '')) AS Vendedor, RTRIM(ISNULL(c.Usuario, '')) AS Usuario, RTRIM(ISNULL(c.[Forma Pago], '')) AS FormaPago,
       RTRIM(ISNULL(c.PlazosPago, '')) AS PlazosPago, c.ModoServicio, c.ServirJunto, RTRIM(ISNULL(c.Ruta, '')) AS Ruta,
       a.LineasProducto, a.Pendientes, a.Servidas, a.Albaranes, a.PrimerAlbaran, a.MinFechaEntrega, a.Importe,
       a.EnTienda, a.Amazon, a.TiendaOnline, a.OtroCanal, a.App, a.Cursos, a.Almacen, a.LineasNegativas
FROM #p p
INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = p.Empresa AND c.[Número] = p.Pedido
CROSS APPLY (
    SELECT SUM(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 THEN 1 ELSE 0 END) AS LineasProducto,
           SUM(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado BETWEEN -1 AND 1 THEN 1 ELSE 0 END) AS Pendientes,
           SUM(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2 THEN 1 ELSE 0 END) AS Servidas,
           COUNT(DISTINCT CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2 THEN l.[Nº Albarán] END) AS Albaranes,
           MIN(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2 THEN l.[Fecha Albarán] END) AS PrimerAlbaran,
           MIN(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 THEN l.[Fecha Entrega] END) AS MinFechaEntrega,
           CAST(ISNULL(SUM(l.[Base Imponible]), 0) AS decimal(18, 2)) AS Importe,
           SUM(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.[Almacén] IN ('REI', 'ALC') THEN 1 ELSE 0 END) AS EnTienda,
           SUM(CASE WHEN l.[Forma Venta] = 'STK' THEN 1 ELSE 0 END) AS Amazon,
           SUM(CASE WHEN l.[Forma Venta] = 'WEB' THEN 1 ELSE 0 END) AS TiendaOnline,
           SUM(CASE WHEN l.[Forma Venta] IN ('QRU', 'BLT') THEN 1 ELSE 0 END) AS OtroCanal,
           SUM(CASE WHEN l.[Forma Venta] = 'APP' THEN 1 ELSE 0 END) AS App,
           SUM(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Grupo = 'CUR' THEN 1 ELSE 0 END) AS Cursos,
           MAX(CASE WHEN l.TipoLinea = 1 AND l.Cantidad > 0 THEN RTRIM(l.[Almacén]) END) AS Almacen,
           SUM(CASE WHEN l.Cantidad < 0 THEN 1 ELSE 0 END) AS LineasNegativas
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número]) a;
";

        /// <summary>Resultados 2-4: copias (otro pedido lo tiene de PedidoOrigen), facturas rectificadas e impagados (como ModeloImpagados).</summary>
        internal const string SQL_RECTIFICATIVAS_IMPAGADOS = @"
SELECT DISTINCT o.PedidoOrigen AS Pedido
FROM CabPedidoVta o WITH (NOLOCK)
INNER JOIN #p p ON p.Empresa = o.Empresa AND p.Pedido = o.PedidoOrigen;
SELECT DISTINCT f.Pedido
FROM #f f
INNER JOIN LinFacturaVtaRectificacion r WITH (NOLOCK) ON r.Empresa = f.Empresa AND r.FacturaOriginalNumero = f.Factura
WHERE r.NumeroFactura <> f.Factura;
SELECT DISTINCT f.Pedido
FROM #f f
INNER JOIN ExtractoCliente e WITH (NOLOCK) ON e.Empresa = f.Empresa AND e.[Número] = f.Cliente AND e.[Nº Documento] = f.Factura
WHERE e.TipoApunte = '4' AND e.Importe > 0 AND ISNULL(e.Concepto, '') NOT LIKE 'Gastos Impagado%';
";

        /// <summary>Resultado 5: los envíos que han salido (Estado ≥ 1). {0} reenvío, {1} ha vuelto, {2} bultos pendientes (o NULL sin tabla).</summary>
        internal const string SQL_ENVIOS = @"
SELECT e.Pedido, e.Numero, e.Agencia, e.Estado, CAST(CAST(e.Fecha AS date) AS datetime) AS Fecha, RTRIM(ISNULL(e.DetalleEstado, '')) AS DetalleEstado,
       {0} AS Reenvio, {1} AS HaVuelto, {2} AS BultosPendientes
FROM EnviosAgencia e WITH (NOLOCK)
INNER JOIN #p p ON p.Empresa = e.Empresa AND p.Pedido = e.Pedido
WHERE e.Estado >= 1;
";
        internal const string SQL_ENVIO_REENVIO = "CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.EnviosAgenciaReenvios r WITH (NOLOCK) WHERE r.EnvioOrigen = e.Numero AND r.Decision = 'Reenviar') THEN 1 ELSE 0 END AS bit)";
        internal const string SQL_ENVIO_HA_VUELTO = "CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.EnviosAgenciaHaVuelto h WITH (NOLOCK) WHERE h.Envio = e.Numero) THEN 1 ELSE 0 END AS bit)";
        internal const string SQL_ENVIO_BULTOS = "CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.RepartoBultosPendientes b WITH (NOLOCK) WHERE b.EnvioId = e.Numero) THEN 1 ELSE 0 END AS bit)";
        internal const string SQL_NULO = "CAST(NULL AS bit)";

        /// <summary>Resultado 6 (con la tabla): pedidos con alguna foto en sus 3 primeros días de algo que no sale del stock de Algete.</summary>
        internal const string SQL_FOTOS = @"
SELECT f.Pedido
FROM dbo.FechasEntregaFotos f WITH (NOLOCK)
INNER JOIN #p p ON p.Empresa = f.Empresa AND p.Pedido = f.Pedido
WHERE f.FechaFoto <= DATEADD(day, 3, p.Fecha) AND f.Origen IN ('Tienda', 'EnCamino', 'Proveedor', 'SinStock')
GROUP BY f.Pedido;
";

        /// <summary>Resultado 7 (con la tabla): quién lo metió (la fila Crear de #704).</summary>
        internal const string SQL_ORIGEN = @"
SELECT o.Pedido, RTRIM(o.Usuario) AS Usuario
FROM dbo.PedidosModoServicioOrigen o WITH (NOLOCK)
INNER JOIN #p p ON p.Empresa = o.Empresa AND p.Pedido = o.Pedido
WHERE o.Operacion = 'Crear' AND o.Usuario IS NOT NULL
ORDER BY o.Id;
";

        internal const string SQL_FIN_LOTE = @"
DROP TABLE #f;
DROP TABLE #p;";

        /// <summary>Para todo el periodo: los «Quitar picking» de Nesto (NestoAPI#590). El pedido va en el texto: «Pedido 930110: sin picking (quitado por …».</summary>
        internal const string SQL_QUITAR_PICKING = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT CAST(m.Nuevo AS nvarchar(400)) AS Nuevo
FROM Modificaciones m WITH (NOLOCK)
WHERE m.Tabla = N'LinPedidoVta' AND m.Fecha >= @desde AND m.Nuevo LIKE N'Pedido %sin picking (quitado por %';";

        internal const string SQL_EVENTOS = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT e.Pedido, RTRIM(e.Tipo) AS Tipo
FROM dbo.CalidadEventos e WITH (NOLOCK)
WHERE e.Empresa = @empresa AND e.Pedido IS NOT NULL AND e.Fecha >= @desde;
SELECT RTRIM(e.Tipo) AS Tipo, MIN(e.Fecha) AS Primero
FROM dbo.CalidadEventos e WITH (NOLOCK)
GROUP BY e.Tipo;";

        internal const string SQL_FOTOS_DESDE = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT MIN(FechaFoto) FROM dbo.FechasEntregaFotos WITH (NOLOCK);";

        private static readonly Regex PEDIDO_EN_TEXTO = new Regex(@"^Pedido (\d+)", RegexOptions.Compiled);

        private readonly string cadenaConexion;

        public LectorPedidoPerfectoSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static LectorPedidoPerfectoSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorPedidoPerfectoSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        /// <summary>El lote de un mes con las tablas opcionales que haya (público para poder lanzarlo a mano).</summary>
        public static string Lote(bool reenvios, bool haVuelto, bool bultos, bool fotos, bool origen) =>
            SQL_UNIVERSO + SQL_PEDIDOS + SQL_RECTIFICATIVAS_IMPAGADOS
            + string.Format(CultureInfo.InvariantCulture, SQL_ENVIOS, reenvios ? SQL_ENVIO_REENVIO : SQL_NULO, haVuelto ? SQL_ENVIO_HA_VUELTO : SQL_NULO,
                bultos ? SQL_ENVIO_BULTOS : SQL_NULO)
            + (fotos ? SQL_FOTOS : string.Empty) + (origen ? SQL_ORIGEN : string.Empty) + SQL_FIN_LOTE;

        /// <summary>Los meses (o trozos de mes) en que se parte [desde, hasta).</summary>
        public static List<Tuple<DateTime, DateTime>> Tandas(DateTime desde, DateTime hasta)
        {
            var tandas = new List<Tuple<DateTime, DateTime>>();
            DateTime inicio = desde.Date;
            while (inicio < hasta)
            {
                DateTime fin = new DateTime(inicio.Year, inicio.Month, 1).AddMonths(1);
                if (fin > hasta)
                {
                    fin = hasta;
                }
                tandas.Add(Tuple.Create(inicio, fin));
                inicio = fin;
            }
            return tandas;
        }

        public LecturaPedidoPerfecto Leer(string empresa, DateTime desde, DateTime hasta)
        {
            var lectura = new LecturaPedidoPerfecto { EntregaIncompletaDesde = ENTREGA_INCOMPLETA_DESDE };
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                bool reenvios, haVuelto, bultos, fotos, origen, eventos;
                using (var comando = new SqlCommand(SQL_TABLAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    _ = r.Read();
                    reenvios = r.GetBoolean(0);
                    haVuelto = r.GetBoolean(1);
                    bultos = r.GetBoolean(2);
                    fotos = r.GetBoolean(3);
                    origen = r.GetBoolean(4);
                    eventos = r.GetBoolean(5);
                }
                AnotarQueFalta(lectura, reenvios, "dbo.EnviosAgenciaReenvios");
                AnotarQueFalta(lectura, haVuelto, "dbo.EnviosAgenciaHaVuelto");
                AnotarQueFalta(lectura, bultos, "dbo.RepartoBultosPendientes");
                AnotarQueFalta(lectura, fotos, "dbo.FechasEntregaFotos");
                AnotarQueFalta(lectura, origen, "dbo.PedidosModoServicioOrigen");
                AnotarQueFalta(lectura, eventos, "dbo.CalidadEventos");

                string lote = Lote(reenvios, haVuelto, bultos, fotos, origen);
                foreach (Tuple<DateTime, DateTime> tanda in Tandas(desde, hasta))
                {
                    LeerTanda(conexion, lote, empresa, tanda.Item1, tanda.Item2, fotos, origen, lectura.Pedidos);
                }
                Dictionary<int, HechosPedidoPerfecto> porNumero = lectura.Pedidos.GroupBy(p => p.Pedido).ToDictionary(g => g.Key, g => g.First());

                if (hasta > QUITAR_PICKING_DESDE)
                {
                    lectura.QuitarPickingDesde = QUITAR_PICKING_DESDE;
                    using (var comando = new SqlCommand(SQL_QUITAR_PICKING, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde > QUITAR_PICKING_DESDE ? desde : QUITAR_PICKING_DESDE;
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                int? pedido = PedidoDelTexto(r.IsDBNull(0) ? null : r.GetString(0));
                                if (pedido.HasValue && porNumero.TryGetValue(pedido.Value, out HechosPedidoPerfecto h))
                                {
                                    h.PickingQuitado = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    lectura.QuitarPickingDesde = QUITAR_PICKING_DESDE;
                }

                if (eventos)
                {
                    using (var comando = new SqlCommand(SQL_EVENTOS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                        comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                if (porNumero.TryGetValue(r.GetInt32(0), out HechosPedidoPerfecto h))
                                {
                                    _ = h.EventosCalidad.Add(r.GetString(1));
                                }
                            }
                            _ = r.NextResult();
                            while (r.Read())
                            {
                                lectura.PrimerEventoPorTipo[r.GetString(0)] = r.GetDateTime(1);
                            }
                        }
                    }
                }

                if (fotos)
                {
                    using (var comando = new SqlCommand(SQL_FOTOS_DESDE, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        object valor = comando.ExecuteScalar();
                        lectura.FotosStockDesde = valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor, CultureInfo.InvariantCulture);
                    }
                }
            }
            return lectura;
        }

        internal static int? PedidoDelTexto(string texto)
        {
            Match m = texto == null ? Match.Empty : PEDIDO_EN_TEXTO.Match(texto);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pedido) ? pedido : (int?)null;
        }

        private static void AnotarQueFalta(LecturaPedidoPerfecto lectura, bool existe, string tabla)
        {
            if (!existe)
            {
                lectura.TablasQueFaltan.Add(tabla);
            }
        }

        private static void LeerTanda(SqlConnection conexion, string lote, string empresa, DateTime desde, DateTime hasta, bool fotos, bool origen,
            List<HechosPedidoPerfecto> pedidos)
        {
            var delLote = new Dictionary<int, HechosPedidoPerfecto>();
            using (var comando = new SqlCommand(lote, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = hasta;
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    // 1. Pedidos
                    while (r.Read())
                    {
                        var h = new HechosPedidoPerfecto
                        {
                            Empresa = empresa,
                            Pedido = r.GetInt32(0),
                            Fecha = r.IsDBNull(1) ? desde : r.GetDateTime(1),
                            Cliente = r.GetString(2),
                            Contacto = r.GetString(3),
                            Vendedor = r.GetString(4),
                            Usuario = r.GetString(5),
                            FormaPago = r.GetString(6),
                            PlazosPago = r.GetString(7),
                            ModoServicio = r.IsDBNull(8) ? (byte?)null : Convert.ToByte(r.GetValue(8), CultureInfo.InvariantCulture),
                            ServirJunto = !r.IsDBNull(9) && Convert.ToBoolean(r.GetValue(9), CultureInfo.InvariantCulture),
                            Ruta = r.GetString(10),
                            LineasProducto = Entero(r, 11),
                            LineasPendientes = Entero(r, 12),
                            LineasServidas = Entero(r, 13),
                            Albaranes = Entero(r, 14),
                            PrimerAlbaran = r.IsDBNull(15) ? (DateTime?)null : r.GetDateTime(15),
                            MinFechaEntrega = r.IsDBNull(16) ? (DateTime?)null : r.GetDateTime(16),
                            Importe = r.IsDBNull(17) ? 0 : r.GetDecimal(17),
                            LineasEnTienda = Entero(r, 18),
                            LineasAmazon = Entero(r, 19),
                            LineasTiendaOnline = Entero(r, 20),
                            LineasOtroCanalExterno = Entero(r, 21),
                            LineasApp = Entero(r, 22),
                            LineasCursos = Entero(r, 23),
                            Almacen = r.IsDBNull(24) ? null : r.GetString(24),
                            LlevaLineasNegativas = Entero(r, 25) > 0
                        };
                        delLote[h.Pedido] = h;
                    }
                    // 2-4. Copias, rectificativas, impagados
                    _ = r.NextResult();
                    Marcar(r, delLote, h => h.TieneCopia = true);
                    _ = r.NextResult();
                    Marcar(r, delLote, h => h.TieneRectificativa = true);
                    _ = r.NextResult();
                    Marcar(r, delLote, h => h.Impagado = true);
                    // 5. Envíos
                    _ = r.NextResult();
                    while (r.Read())
                    {
                        if (r.IsDBNull(0) || !delLote.TryGetValue(r.GetInt32(0), out HechosPedidoPerfecto h))
                        {
                            continue;
                        }
                        AnadirEnvio(h, Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture), Convert.ToInt16(r.GetValue(3), CultureInfo.InvariantCulture),
                            r.GetDateTime(4), r.GetString(5), BoolNulo(r, 6), BoolNulo(r, 7), BoolNulo(r, 8));
                    }
                    if (fotos)
                    {
                        _ = r.NextResult();
                        Marcar(r, delLote, h => h.FaltabaStockEnFoto = true);
                    }
                    if (origen)
                    {
                        _ = r.NextResult();
                        var vistos = new HashSet<int>();
                        while (r.Read())
                        {
                            int pedido = r.GetInt32(0);
                            if (vistos.Add(pedido) && delLote.TryGetValue(pedido, out HechosPedidoPerfecto h) && !r.IsDBNull(1))
                            {
                                h.Usuario = r.GetString(1);
                                h.UsuarioEsDelAlta = true;
                            }
                        }
                    }
                }
            }
            pedidos.AddRange(delLote.Values.OrderBy(p => p.Pedido));
        }

        /// <summary>Lo que aporta un envío a los hechos del pedido (puro, para los tests).</summary>
        internal static void AnadirEnvio(HechosPedidoPerfecto h, int agencia, short estado, DateTime fecha, string detalleEstado,
            bool? reenvio, bool? haVuelto, bool? bultosPendientes)
        {
            h.EnviosSalidos++;
            if (estado == Models.Constantes.Agencias.ESTADO_INCIDENTADO || estado == Models.Constantes.Agencias.ESTADO_DEVUELTO
                || estado == Models.Constantes.Agencias.ESTADO_CERRADO_A_MANO)
            {
                h.EnvioConIncidencia = true;
            }
            if (reenvio.HasValue)
            {
                h.Reenvio = (h.Reenvio ?? false) || reenvio.Value;
            }
            if (haVuelto.HasValue)
            {
                h.HaVuelto = (h.HaVuelto ?? false) || haVuelto.Value;
            }
            if (agencia == Models.Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                h.EnviosRepartoPropio++;
                if (!h.PrimerEnvioRepartoPropio.HasValue || fecha < h.PrimerEnvioRepartoPropio.Value)
                {
                    h.PrimerEnvioRepartoPropio = fecha;
                }
                if (bultosPendientes == true || (detalleEstado != null
                    && detalleEstado.StartsWith(Agencias.RepartoPropio.EntregaIncompletaReparto.PREFIJO_DETALLE, StringComparison.OrdinalIgnoreCase)))
                {
                    h.EntregaIncompleta = true;
                }
            }
        }

        private static void Marcar(SqlDataReader r, Dictionary<int, HechosPedidoPerfecto> pedidos, Action<HechosPedidoPerfecto> marcar)
        {
            while (r.Read())
            {
                if (!r.IsDBNull(0) && pedidos.TryGetValue(Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture), out HechosPedidoPerfecto h))
                {
                    marcar(h);
                }
            }
        }

        private static int Entero(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

        private static bool? BoolNulo(SqlDataReader r, int i) => r.IsDBNull(i) ? (bool?)null : r.GetBoolean(i);
    }
}
