using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>Una fila nueva de ClientesRiesgoAbandono (lo que escribe el job nocturno).</summary>
    public class FilaRiesgoAbandono
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        public DateTime FechaInstantanea { get; set; }
        /// <summary><see cref="TiposFilaAbandono.ALERTA"/> o <see cref="TiposFilaAbandono.INSTANTANEA"/>.</summary>
        public string Tipo { get; set; }
        /// <summary>Del modelo (corte 3); null con la regla sola.</summary>
        public decimal? Probabilidad { get; set; }
        public bool Baseline { get; set; }
        public int Cadencia { get; set; }
        public int DiasSinComprar { get; set; }
        public int Horizonte { get; set; }
        public DateTime FechaLimite { get; set; }
        public string Motivo { get; set; }
        public string Features { get; set; }
        public string VersionModelo { get; set; }
        public string Modo { get; set; }
    }

    public static class TiposFilaAbandono
    {
        /// <summary>El primer día que la regla marca al cliente sin tener ya una alerta abierta (sin resolver): lo que vería el vendedor.</summary>
        public const string ALERTA = "Alerta";
        /// <summary>Instantánea semanal de TODOS los compradores regulares de la cartera (marcados o no): negativos y positivos para entrenar.</summary>
        public const string INSTANTANEA = "Instantanea";
    }

    /// <summary>Una fila para el informe (GET api/Clientes/RiesgoAbandono/Informe).</summary>
    public class FilaInformeAbandono
    {
        public string Vendedor { get; set; }
        public DateTime FechaInstantanea { get; set; }
        public string Tipo { get; set; }
        public bool Baseline { get; set; }
        public DateTime? Resuelto { get; set; }
        public string Resultado { get; set; }
    }

    /// <summary>NestoAPI#714: lo que el job nocturno, el de resolución y el informe leen y escriben.</summary>
    public interface IRepositorioRiesgoAbandono
    {
        /// <summary>¿Existe la tabla (Scripts/Issue714_ClientesRiesgoAbandono.sql lanzado)? Sin ella no se hace nada.</summary>
        bool TablaExiste();
        /// <summary>Vendedores activos de la empresa 1 (Vendedores.Estado = 0): los que tienen cartera de llamadas.</summary>
        List<string> LeerVendedores();
        /// <summary>La cartera de llamadas del vendedor (mismas exclusiones y asignación que las llamadas sugeridas) con lo leído hasta <paramref name="dia"/> incluido.</summary>
        List<DatosClienteAbandono> LeerCartera(string vendedor, DateTime dia);
        /// <summary>Clientes (cliente + contacto) del vendedor con una alerta sin resolver.</summary>
        HashSet<string> LeerAlertasAbiertas(string empresa, string vendedor);
        /// <summary>Inserta las filas (una transacción). Devuelve cuántas.</summary>
        int Insertar(List<FilaRiesgoAbandono> filas);
        /// <summary>Filas sin resolver con FechaLimite anterior a <paramref name="hoy"/>, con los hechos para decidirlas (como mucho <paramref name="maximo"/>).</summary>
        List<PendienteResolverAbandono> LeerPendientesResolver(DateTime hoy, int maximo);
        void Resolver(int id, string resultado, DateTime? fechaEvento, DateTime cuando);
        List<FilaInformeAbandono> LeerInforme(DateTime desde, DateTime hastaExclusivo);
    }

    /// <summary>
    /// NestoAPI#714: SQL a mano, fuera del EDMX (como PedidosPromesaRiesgo #693 y CorreosPostCompraRecomendaciones #707): sin
    /// el script, la API no se cae; no registra nada. La cartera reutiliza la asignación y las exclusiones de las llamadas
    /// sugeridas (<see cref="RepositorioCarteraContactoSql.SQL_ASIGNACION"/>, <see cref="RepositorioCarteraContactoSql.FILTRO_GRUPO_LINEA"/>):
    /// la población es exactamente la cartera de llamadas. Todo con NOLOCK; por vendedor tarda menos de un segundo (10/26).
    /// </summary>
    public class RepositorioRiesgoAbandonoSql : IRepositorioRiesgoAbandono
    {
        internal const int TIMEOUT_SEGUNDOS = 120;
        internal const string TABLA = "dbo.ClientesRiesgoAbandono";
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('" + TABLA + "', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_VENDEDORES = "SELECT RTRIM(v.Número) FROM Vendedores v WITH (NOLOCK) WHERE v.Empresa = '1' AND v.Estado = 0 ORDER BY v.Número";

        /// <summary>
        /// Siete resultados: perfil de la cartera, compras (un día = una fila por facturado/en curso), último pedido por grupo,
        /// resumen de 24 meses y devoluciones, rapports, impagos y vencidos, y último cambio de vendedor. Todo hasta @Hoy
        /// INCLUIDO (el job pasa el día anterior, ya completo): nada posterior entra en las features.
        /// </summary>
        internal const string SQL_CARTERA = @"
            SET NOCOUNT ON;
            DECLARE @Manana date = DATEADD(day, 1, @Hoy);
            DECLARE @Hace3Meses date = DATEADD(month, -3, @Hoy);
            DECLARE @Hace12Meses date = DATEADD(month, -12, @Hoy);
            DECLARE @Hace24Meses date = DATEADD(month, -24, @Hoy);
            DECLARE @Hace60Dias date = DATEADD(day, -60, @Hoy);
" + RepositorioCarteraContactoSql.SQL_ASIGNACION + @"

            SELECT c.[Nº Cliente] Cliente, c.Contacto, a.TodosLosGrupos, c.Estado, c.Grupo, c.Provincia, c.Empleados
            INTO #Cartera
            FROM Clientes c WITH (NOLOCK)
            INNER JOIN #Asignacion a ON a.Cliente = c.[Nº Cliente] AND a.Contacto = c.Contacto
            WHERE c.Empresa = '1' AND c.Estado >= 0 AND c.Estado NOT IN (" + RepositorioCarteraContactoSql.ESTADOS_EXCLUIDOS_SQL + @");
            CREATE CLUSTERED INDEX IX_Cartera ON #Cartera (Cliente, Contacto);

            SELECT DISTINCT o.Cliente, o.Contacto, o.Vendedor
            INTO #OtrosVendedores
            FROM (
                SELECT c.[Nº Cliente] Cliente, c.Contacto, c.Vendedor
                FROM Clientes c WITH (NOLOCK)
                INNER JOIN #Cartera x ON x.Cliente = c.[Nº Cliente] AND x.Contacto = c.Contacto
                WHERE c.Empresa = '1'
                UNION ALL
                SELECT v.Cliente, v.Contacto, v.Vendedor
                FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
                INNER JOIN #Cartera x ON x.Cliente = v.Cliente AND x.Contacto = v.Contacto
                WHERE v.Empresa = '1'
            ) o
            WHERE o.Vendedor IS NOT NULL AND o.Vendedor <> @Vendedor AND o.Vendedor <> '" + Constantes.Vendedores.VENDEDOR_GENERAL + @"';

            -- Líneas facturadas de 24 meses (fecha del pedido, como la cartera de llamadas), con el filtro de grupo del vendedor
            SELECT l.[Nº Cliente] Cliente, l.Contacto, l.Número Pedido, CAST(cab.Fecha AS date) Dia, l.Grupo, l.[Base Imponible] Importe
            INTO #Lineas
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado = 4 AND l.[Base Imponible] <> 0 AND l.SubGrupo <> 'MMP'
                AND l.[Fecha Factura] >= @Hace24Meses AND cab.Fecha >= @Hace24Meses AND cab.Fecha < @Manana
                AND " + RepositorioCarteraContactoSql.FILTRO_GRUPO_LINEA + @";

            -- 1. Perfil
            SELECT Cliente, Contacto, TodosLosGrupos, Estado, Grupo, Provincia, Empleados FROM #Cartera;

            -- 2. Compras de 12 meses: facturadas (importe) y, de los últimos 60 días, las aún sin facturar (EnCurso = 1)
            SELECT Cliente, Contacto, Dia, SUM(Importe) Importe, CAST(0 AS bit) EnCurso
            FROM #Lineas
            WHERE Importe > 0 AND Dia > @Hace12Meses
            GROUP BY Cliente, Contacto, Dia
            UNION ALL
            SELECT l.[Nº Cliente], l.Contacto, CAST(cab.Fecha AS date), SUM(l.[Base Imponible]), CAST(1 AS bit)
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            WHERE l.Empresa = '1' AND l.Estado BETWEEN -1 AND 2 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND cab.Fecha >= @Hace60Dias AND cab.Fecha < @Manana
                AND " + RepositorioCarteraContactoSql.FILTRO_GRUPO_LINEA + @"
            GROUP BY l.[Nº Cliente], l.Contacto, CAST(cab.Fecha AS date);

            -- 3. Por grupo de producto (12 meses, facturado)
            SELECT Cliente, Contacto, Grupo, MAX(Dia) UltimaCompra, COUNT(DISTINCT Pedido) Pedidos12Meses
            FROM #Lineas
            WHERE Importe > 0 AND Dia > @Hace12Meses AND Grupo IS NOT NULL
            GROUP BY Cliente, Contacto, Grupo;

            -- 4. Resumen de 24 meses y devoluciones de 12
            SELECT Cliente, Contacto,
                COUNT(DISTINCT CASE WHEN Importe > 0 THEN Pedido END) Pedidos24Meses,
                MIN(CASE WHEN Importe > 0 THEN Dia END) PrimeraCompra24Meses,
                COUNT(DISTINCT CASE WHEN Importe < 0 AND Dia > @Hace12Meses THEN Pedido END) Devoluciones12Meses,
                ISNULL(SUM(CASE WHEN Importe < 0 AND Dia > @Hace12Meses THEN -Importe ELSE 0 END), 0) ImporteDevoluciones12Meses
            FROM #Lineas
            GROUP BY Cliente, Contacto;

            -- 5. Rapports (T/V/W) de 12 meses que cuentan para este vendedor
            SELECT s.Número Cliente, s.Contacto,
                SUM(CASE WHEN s.Estado = 0 THEN 1 ELSE 0 END) Contactos12Meses,
                SUM(CASE WHEN s.Estado = 1 THEN 1 ELSE 0 END) NoContactados12Meses,
                SUM(CASE WHEN s.Estado = 0 AND s.Fecha > @Hace3Meses THEN 1 ELSE 0 END) Contactos3Meses,
                SUM(CASE WHEN s.Estado = 1 AND s.Fecha > @Hace3Meses THEN 1 ELSE 0 END) NoContactados3Meses,
                SUM(CASE WHEN s.Estado = 0 AND s.Pedido = 1 THEN 1 ELSE 0 END) RapportsConPedido12Meses,
                MAX(CASE WHEN s.Estado = 0 THEN s.Fecha END) UltimoContacto,
                MAX(CASE WHEN s.Estado = 1 THEN s.Fecha END) UltimoIntento
            FROM SeguimientoCliente s WITH (NOLOCK)
            INNER JOIN #Cartera x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
            WHERE s.Fecha > @Hace12Meses AND s.Fecha < @Manana AND s.Estado IN (0, 1) AND s.Tipo IN ('T', 'V', 'W')
                AND NOT EXISTS (SELECT 1 FROM #OtrosVendedores o
                    WHERE o.Cliente = s.Número AND o.Contacto = s.Contacto AND o.Vendedor = s.Vendedor)
            GROUP BY s.Número, s.Contacto;

            -- 6. Impagos (TipoApunte 4) de 12 meses y pendiente/vencido a fecha @Hoy, por cliente (todos sus contactos)
            SELECT e.Número Cliente,
                SUM(CASE WHEN e.TipoApunte = '4' AND e.Fecha > @Hace12Meses THEN 1 ELSE 0 END) Impagos12Meses,
                ISNULL(SUM(CASE WHEN e.ImportePdte <> 0 AND e.FechaVto < @Hoy THEN e.ImportePdte ELSE 0 END), 0) ImporteVencido,
                ISNULL(SUM(CASE WHEN e.ImportePdte <> 0 THEN e.ImportePdte ELSE 0 END), 0) ImportePendiente
            FROM ExtractoCliente e WITH (NOLOCK)
            WHERE e.Empresa = '1' AND e.Fecha < @Manana
                AND e.Número IN (SELECT DISTINCT Cliente FROM #Cartera)
            GROUP BY e.Número;

            -- 7. Último cambio de vendedor de la ficha (LogModificaciones: índice por Columna + Numero)
            SELECT l.Numero Cliente, MAX(l.FechaModificacion) UltimoCambioVendedor
            FROM LogModificaciones l WITH (NOLOCK)
            WHERE l.Empresa = '1' AND l.Tabla = 'Clientes' AND l.Columna = 'Vendedor' AND l.FechaModificacion < @Manana
                AND l.Numero IN (SELECT DISTINCT Cliente FROM #Cartera)
            GROUP BY l.Numero;";

        internal const string SQL_ABIERTAS = "SELECT RTRIM(Cliente) + '/' + RTRIM(Contacto) FROM " + TABLA +
            " WHERE Empresa = @Empresa AND Vendedor = @Vendedor AND Tipo = '" + TiposFilaAbandono.ALERTA + "' AND Resuelto IS NULL";

        internal const string SQL_INSERTAR = "INSERT INTO " + TABLA + @" (Empresa, Cliente, Contacto, Vendedor, FechaInstantanea, Tipo, Probabilidad, Baseline,
                Cadencia, DiasSinComprar, Horizonte, FechaLimite, Motivo, Features, VersionModelo, Modo, Creado)
            VALUES (@Empresa, @Cliente, @Contacto, @Vendedor, @FechaInstantanea, @Tipo, @Probabilidad, @Baseline,
                @Cadencia, @DiasSinComprar, @Horizonte, @FechaLimite, @Motivo, @Features, @VersionModelo, @Modo, GETDATE())";

        /// <summary>
        /// Ventana de resolución (d, FechaLimite]: días completos desde el siguiente a la instantánea hasta FechaLimite incluida.
        /// Compró = algún pedido del cliente (de cualquier grupo; pendiente, en curso, albarán o factura) con base imponible &gt; 0
        /// y sin MMP. Baja = algún cambio a un estado de <see cref="ResolucionAbandono.ESTADOS_BAJA"/> en SeguimientoCambiosEstadoCliente.
        /// </summary>
        internal const string SQL_PENDIENTES = @"
            SELECT TOP (@Maximo) r.Id, RTRIM(r.Empresa) Empresa, RTRIM(r.Cliente) Cliente, RTRIM(r.Contacto) Contacto, r.FechaInstantanea, r.FechaLimite,
                CAST(CASE WHEN c.[Nº Cliente] IS NULL THEN 0 ELSE 1 END AS bit) ClienteExiste,
                (SELECT MIN(s.Fecha) FROM SeguimientoCambiosEstadoCliente s WITH (NOLOCK)
                    WHERE s.Empresa = r.Empresa AND s.Cliente = r.Cliente AND s.Contacto = r.Contacto
                        AND s.Fecha >= DATEADD(day, 1, r.FechaInstantanea) AND s.Fecha < DATEADD(day, 1, r.FechaLimite)
                        AND s.EstadoNuevo IN (" + ResolucionAbandono.ESTADOS_BAJA_SQL + @")) FechaBaja,
                (SELECT MIN(CAST(cab.Fecha AS date)) FROM LinPedidoVta l WITH (NOLOCK)
                    INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
                    WHERE l.Empresa = r.Empresa AND l.[Nº Cliente] = r.Cliente AND l.Contacto = r.Contacto
                        AND cab.Fecha >= DATEADD(day, 1, r.FechaInstantanea) AND cab.Fecha < DATEADD(day, 1, r.FechaLimite)
                        AND (l.Estado BETWEEN -1 AND 2 OR l.Estado = 4) AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP') FechaPrimeraCompra
            FROM " + TABLA + @" r
            LEFT JOIN Clientes c WITH (NOLOCK) ON c.Empresa = r.Empresa AND c.[Nº Cliente] = r.Cliente AND c.Contacto = r.Contacto
            WHERE r.Resuelto IS NULL AND r.FechaLimite < @Hoy
            ORDER BY r.Id";

        internal const string SQL_RESOLVER = "UPDATE " + TABLA + " SET Resuelto = @Cuando, Resultado = @Resultado, FechaEvento = @FechaEvento WHERE Id = @Id AND Resuelto IS NULL";

        internal const string SQL_INFORME = "SELECT RTRIM(Vendedor) Vendedor, FechaInstantanea, Tipo, Baseline, Resuelto, Resultado FROM " + TABLA +
            " WITH (NOLOCK) WHERE FechaInstantanea >= @Desde AND FechaInstantanea < @Hasta";

        private static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(5);
        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        public bool TablaExiste()
        {
            lock (cerrojo)
            {
                if (existe || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_TABLA)
                {
                    return existe;
                }
                ultimaComprobacion = DateTime.Now;
                try
                {
                    using (SqlConnection conexion = AbrirConexion())
                    using (var comando = new SqlCommand(SQL_EXISTE, conexion))
                    {
                        existe = (bool)comando.ExecuteScalar();
                    }
                }
                catch (Exception)
                {
                    existe = false;
                }
                return existe;
            }
        }

        public List<string> LeerVendedores()
        {
            var vendedores = new List<string>();
            using (SqlConnection conexion = AbrirConexion())
            using (var comando = new SqlCommand(SQL_VENDEDORES, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    vendedores.Add(lector.GetString(0).Trim());
                }
            }
            return vendedores;
        }

        public List<DatosClienteAbandono> LeerCartera(string vendedor, DateTime dia)
        {
            return ReintentosSql.ReintentarSiDeadlock(() =>
            {
                var cartera = new Dictionary<string, DatosClienteAbandono>();
                using (SqlConnection conexion = AbrirConexion())
                using (var comando = new SqlCommand(SQL_CARTERA, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.Date) { Value = dia.Date });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        // 1. Perfil
                        while (lector.Read())
                        {
                            var datos = new DatosClienteAbandono
                            {
                                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                                Cliente = Texto(lector["Cliente"]),
                                Contacto = Texto(lector["Contacto"]),
                                Vendedor = vendedor.Trim(),
                                TodosLosGrupos = Convert.ToInt32(lector["TodosLosGrupos"]) == 1,
                                Estado = Convert.ToInt32(lector["Estado"]),
                                Grupo = Texto(lector["Grupo"]),
                                Provincia = Texto(lector["Provincia"]),
                                Empleados = lector["Empleados"] == DBNull.Value ? (int?)null : Convert.ToInt32(lector["Empleados"])
                            };
                            cartera[Clave(datos.Cliente, datos.Contacto)] = datos;
                        }
                        // 2. Compras
                        _ = lector.NextResult();
                        while (lector.Read())
                        {
                            if (cartera.TryGetValue(Clave(Texto(lector["Cliente"]), Texto(lector["Contacto"])), out DatosClienteAbandono datos))
                            {
                                datos.Compras.Add(new CompraAbandono
                                {
                                    Dia = Convert.ToDateTime(lector["Dia"]).Date,
                                    Importe = Convert.ToDecimal(lector["Importe"]),
                                    EnCurso = Convert.ToBoolean(lector["EnCurso"])
                                });
                            }
                        }
                        // 3. Por grupo
                        _ = lector.NextResult();
                        while (lector.Read())
                        {
                            if (cartera.TryGetValue(Clave(Texto(lector["Cliente"]), Texto(lector["Contacto"])), out DatosClienteAbandono datos))
                            {
                                datos.PorGrupo.Add(new CompraGrupoAbandono
                                {
                                    Grupo = Texto(lector["Grupo"]),
                                    UltimaCompra = Convert.ToDateTime(lector["UltimaCompra"]).Date,
                                    Pedidos12Meses = Convert.ToInt32(lector["Pedidos12Meses"])
                                });
                            }
                        }
                        // 4. Resumen 24 meses y devoluciones
                        _ = lector.NextResult();
                        while (lector.Read())
                        {
                            if (cartera.TryGetValue(Clave(Texto(lector["Cliente"]), Texto(lector["Contacto"])), out DatosClienteAbandono datos))
                            {
                                datos.Pedidos24Meses = Convert.ToInt32(lector["Pedidos24Meses"]);
                                datos.PrimeraCompra24Meses = Fecha(lector["PrimeraCompra24Meses"]);
                                datos.Devoluciones12Meses = Convert.ToInt32(lector["Devoluciones12Meses"]);
                                datos.ImporteDevoluciones12Meses = Convert.ToDecimal(lector["ImporteDevoluciones12Meses"]);
                            }
                        }
                        // 5. Rapports
                        _ = lector.NextResult();
                        while (lector.Read())
                        {
                            if (cartera.TryGetValue(Clave(Texto(lector["Cliente"]), Texto(lector["Contacto"])), out DatosClienteAbandono datos))
                            {
                                datos.Contactos12Meses = Convert.ToInt32(lector["Contactos12Meses"]);
                                datos.NoContactados12Meses = Convert.ToInt32(lector["NoContactados12Meses"]);
                                datos.Contactos3Meses = Convert.ToInt32(lector["Contactos3Meses"]);
                                datos.NoContactados3Meses = Convert.ToInt32(lector["NoContactados3Meses"]);
                                datos.RapportsConPedido12Meses = Convert.ToInt32(lector["RapportsConPedido12Meses"]);
                                datos.UltimoContacto = Fecha(lector["UltimoContacto"]);
                                datos.UltimoIntento = Fecha(lector["UltimoIntento"]);
                            }
                        }
                        // 6. Impagos (por cliente: a todos sus contactos de la cartera)
                        _ = lector.NextResult();
                        var impagos = new Dictionary<string, (int impagos, decimal vencido, decimal pendiente)>();
                        while (lector.Read())
                        {
                            impagos[Texto(lector["Cliente"])] = (Convert.ToInt32(lector["Impagos12Meses"]), Convert.ToDecimal(lector["ImporteVencido"]), Convert.ToDecimal(lector["ImportePendiente"]));
                        }
                        // 7. Cambio de vendedor (por cliente)
                        _ = lector.NextResult();
                        var cambios = new Dictionary<string, DateTime>();
                        while (lector.Read())
                        {
                            DateTime? cambio = Fecha(lector["UltimoCambioVendedor"]);
                            if (cambio.HasValue)
                            {
                                cambios[Texto(lector["Cliente"])] = cambio.Value;
                            }
                        }
                        foreach (DatosClienteAbandono datos in cartera.Values)
                        {
                            if (impagos.TryGetValue(datos.Cliente, out var imp))
                            {
                                datos.Impagos12Meses = imp.impagos;
                                datos.ImporteVencido = imp.vencido;
                                datos.ImportePendiente = imp.pendiente;
                            }
                            if (cambios.TryGetValue(datos.Cliente, out DateTime cambio))
                            {
                                datos.UltimoCambioVendedor = cambio;
                            }
                        }
                    }
                }
                return cartera.Values.ToList();
            });
        }

        public HashSet<string> LeerAlertasAbiertas(string empresa, string vendedor)
        {
            var abiertas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SqlConnection conexion = AbrirConexion())
            using (var comando = new SqlCommand(SQL_ABIERTAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa });
                _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        _ = abiertas.Add(lector.GetString(0));
                    }
                }
            }
            return abiertas;
        }

        public int Insertar(List<FilaRiesgoAbandono> filas)
        {
            if (filas == null || filas.Count == 0)
            {
                return 0;
            }
            using (SqlConnection conexion = AbrirConexion())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                int insertadas = 0;
                foreach (FilaRiesgoAbandono fila in filas)
                {
                    using (var comando = new SqlCommand(SQL_INSERTAR, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        _ = comando.Parameters.Add(new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = fila.Empresa });
                        _ = comando.Parameters.Add(new SqlParameter("@Cliente", SqlDbType.VarChar, 10) { Value = fila.Cliente });
                        _ = comando.Parameters.Add(new SqlParameter("@Contacto", SqlDbType.VarChar, 3) { Value = fila.Contacto });
                        _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = fila.Vendedor });
                        _ = comando.Parameters.Add(new SqlParameter("@FechaInstantanea", SqlDbType.Date) { Value = fila.FechaInstantanea.Date });
                        _ = comando.Parameters.Add(new SqlParameter("@Tipo", SqlDbType.VarChar, 12) { Value = fila.Tipo });
                        _ = comando.Parameters.Add(new SqlParameter("@Probabilidad", SqlDbType.Decimal) { Precision = 5, Scale = 4, Value = (object)fila.Probabilidad ?? DBNull.Value });
                        _ = comando.Parameters.Add(new SqlParameter("@Baseline", SqlDbType.Bit) { Value = fila.Baseline });
                        _ = comando.Parameters.Add(new SqlParameter("@Cadencia", SqlDbType.Int) { Value = fila.Cadencia });
                        _ = comando.Parameters.Add(new SqlParameter("@DiasSinComprar", SqlDbType.Int) { Value = fila.DiasSinComprar });
                        _ = comando.Parameters.Add(new SqlParameter("@Horizonte", SqlDbType.Int) { Value = fila.Horizonte });
                        _ = comando.Parameters.Add(new SqlParameter("@FechaLimite", SqlDbType.Date) { Value = fila.FechaLimite.Date });
                        _ = comando.Parameters.Add(new SqlParameter("@Motivo", SqlDbType.NVarChar, 300) { Value = (object)fila.Motivo ?? DBNull.Value });
                        _ = comando.Parameters.Add(new SqlParameter("@Features", SqlDbType.NVarChar, -1) { Value = (object)fila.Features ?? DBNull.Value });
                        _ = comando.Parameters.Add(new SqlParameter("@VersionModelo", SqlDbType.VarChar, 30) { Value = (object)fila.VersionModelo ?? DBNull.Value });
                        _ = comando.Parameters.Add(new SqlParameter("@Modo", SqlDbType.VarChar, 10) { Value = (object)fila.Modo ?? DBNull.Value });
                        insertadas += comando.ExecuteNonQuery();
                    }
                }
                transaccion.Commit();
                return insertadas;
            }
        }

        public List<PendienteResolverAbandono> LeerPendientesResolver(DateTime hoy, int maximo)
        {
            var pendientes = new List<PendienteResolverAbandono>();
            using (SqlConnection conexion = AbrirConexion())
            using (var comando = new SqlCommand(SQL_PENDIENTES, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.Date) { Value = hoy.Date });
                _ = comando.Parameters.Add(new SqlParameter("@Maximo", SqlDbType.Int) { Value = maximo });
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        pendientes.Add(new PendienteResolverAbandono
                        {
                            Id = Convert.ToInt32(lector["Id"]),
                            Empresa = Texto(lector["Empresa"]),
                            Cliente = Texto(lector["Cliente"]),
                            Contacto = Texto(lector["Contacto"]),
                            FechaInstantanea = Convert.ToDateTime(lector["FechaInstantanea"]).Date,
                            FechaLimite = Convert.ToDateTime(lector["FechaLimite"]).Date,
                            ClienteExiste = Convert.ToBoolean(lector["ClienteExiste"]),
                            FechaBaja = Fecha(lector["FechaBaja"]),
                            FechaPrimeraCompra = Fecha(lector["FechaPrimeraCompra"])
                        });
                    }
                }
            }
            return pendientes;
        }

        public void Resolver(int id, string resultado, DateTime? fechaEvento, DateTime cuando)
        {
            using (SqlConnection conexion = AbrirConexion())
            using (var comando = new SqlCommand(SQL_RESOLVER, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                _ = comando.Parameters.Add(new SqlParameter("@Resultado", SqlDbType.VarChar, 12) { Value = resultado });
                _ = comando.Parameters.Add(new SqlParameter("@FechaEvento", SqlDbType.Date) { Value = fechaEvento.HasValue ? (object)fechaEvento.Value.Date : DBNull.Value });
                _ = comando.Parameters.Add(new SqlParameter("@Cuando", SqlDbType.DateTime) { Value = cuando });
                _ = comando.ExecuteNonQuery();
            }
        }

        public List<FilaInformeAbandono> LeerInforme(DateTime desde, DateTime hastaExclusivo)
        {
            var filas = new List<FilaInformeAbandono>();
            using (SqlConnection conexion = AbrirConexion())
            using (var comando = new SqlCommand(SQL_INFORME, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.Date) { Value = desde.Date });
                _ = comando.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.Date) { Value = hastaExclusivo.Date });
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new FilaInformeAbandono
                        {
                            Vendedor = Texto(lector["Vendedor"]),
                            FechaInstantanea = Convert.ToDateTime(lector["FechaInstantanea"]).Date,
                            Tipo = Texto(lector["Tipo"]),
                            Baseline = Convert.ToBoolean(lector["Baseline"]),
                            Resuelto = Fecha(lector["Resuelto"]),
                            Resultado = Texto(lector["Resultado"])
                        });
                    }
                }
            }
            return filas;
        }

        public static string Clave(string cliente, string contacto) => (cliente ?? "").Trim() + "/" + (contacto ?? "").Trim();

        private static SqlConnection AbrirConexion()
        {
            string cadena;
            using (var contexto = new NVEntities())
            {
                cadena = contexto.Database.Connection.ConnectionString;
            }
            var conexion = new SqlConnection(cadena);
            conexion.Open();
            return conexion;
        }

        private static string Texto(object valor) => valor == null || valor == DBNull.Value ? null : valor.ToString().Trim();
        private static DateTime? Fecha(object valor) => valor == null || valor == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(valor);
    }
}
