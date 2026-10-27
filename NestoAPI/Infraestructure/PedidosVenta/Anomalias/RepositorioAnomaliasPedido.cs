using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: una línea de un pedido tal y como está hoy en LinPedidoVta (para el etiquetado).</summary>
    public class EstadoLineaActual
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        /// <summary>Descuento sin pronto pago (fracción).</summary>
        public decimal Descuento { get; set; }
        public short Estado { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#715: una línea negativa (devolución o abono) de un cliente, para la etiqueta Abonado.</summary>
    public class AbonoCliente
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public int Pedido { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#715: una fila de Modificaciones (Tabla = 'Pedidos') sin parsear.</summary>
    public class ModificacionCruda
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Nuevo { get; set; }
    }

    /// <summary>NestoAPI#715: la tabla PedidosAnomalias (Scripts/Issue715_PedidosAnomalias.sql) y las lecturas que necesitan las reglas y el etiquetado.</summary>
    public interface IRepositorioAnomaliasPedido
    {
        /// <summary>¿Se ha lanzado el script? (se cachea; sin tabla se reintenta cada 10 minutos).</summary>
        bool TablaExiste();

        /// <summary>El histórico del cliente de los últimos <paramref name="desde"/>: sus compras de <paramref name="productos"/>, el p99 de todos, sus pedidos y grupos, tarifa y grupo de cada producto.</summary>
        HistoricoCliente LeerHistorico(string empresa, string cliente, int pedidoExcluir, IEnumerable<string> productos, DateTime desde);

        /// <summary>Deuda del cliente (ExtractoCliente), forma de pago de la ficha y stock de los productos en los almacenes pedidos.</summary>
        ContextoGrabacion LeerContexto(string empresa, string cliente, string contacto, IEnumerable<KeyValuePair<string, string>> productosAlmacen, DateTime hoy);

        void Insertar(IList<FilaAnomaliaPedido> filas);

        /// <summary>Las filas sin Resultado grabadas antes de <paramref name="hasta"/>.</summary>
        List<FilaAnomaliaPedido> LeerPendientesEtiquetar(DateTime hasta);

        /// <summary>Todas las líneas de producto que hoy tienen esos pedidos.</summary>
        List<EstadoLineaActual> LeerLineasActuales(string empresa, IEnumerable<int> pedidos);

        /// <summary>Las filas de Modificaciones (Tabla = 'Pedidos') entre fechas cuyo JSON nuevo es de alguno de esos pedidos.</summary>
        List<ModificacionCruda> LeerModificaciones(DateTime desde, DateTime hasta, IEnumerable<int> pedidos);

        /// <summary>Las líneas con cantidad negativa de esos clientes modificadas desde <paramref name="desde"/>.</summary>
        List<AbonoCliente> LeerAbonos(string empresa, IEnumerable<string> clientes, DateTime desde);

        void Etiquetar(IEnumerable<FilaAnomaliaPedido> filas);

        /// <summary>Las filas grabadas entre <paramref name="desde"/> (incluido) y <paramref name="hasta"/> (excluido), para el informe.</summary>
        List<FilaAnomaliaPedido> LeerEntre(DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#715: SQL a mano, fuera del EDMX, como PedidosPromesaRiesgo (#693) y CorreosPostCompraRecomendaciones (#707).
    /// Conexión propia con Enlist=false: el registro corre en segundo plano y nunca debe engancharse a una transacción ajena.
    ///
    /// <para>Coste medido en producción el 27/10/26 (NOLOCK, sin join con CabPedidoVta: el filtro de 24 meses va por
    /// [Fecha Albarán] y los pendientes, que cubren los índices por Producto y por Nº Cliente): pedido de 3 líneas ≈ 700 lecturas
    /// lógicas y &lt; 20 ms; pedido de 55 líneas ≈ 1.600 lecturas y ≈ 80 ms de CPU. Todo después de guardar y en segundo plano.</para>
    /// </summary>
    public class RepositorioAnomaliasPedidoSql : IRepositorioAnomaliasPedido
    {
        public const string NOMBRE_APLICACION = "NestoAPI Anomalias #715";
        public static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(10);
        public const int MAXIMO_PRODUCTOS = 200;

        internal const string SQL_EXISTE = "SELECT CASE WHEN OBJECT_ID('dbo.PedidosAnomalias', 'U') IS NULL THEN 0 ELSE 1 END";

        // Compras del cliente de esos productos (líneas de producto con cantidad positiva, en cualquier estado desde pendiente).
        // Los 24 meses: albarán desde la fecha o aún sin albarán. Cubre el índice (Nº Cliente, TipoLinea, Empresa, Producto).
        internal const string SQL_COMPRAS_CLIENTE = @"
SELECT RTRIM(l.Producto), l.Cantidad, l.Precio,
       CASE WHEN l.[Aplicar Dto] = 1 THEN 1 - (1 - l.DescuentoCliente) * (1 - l.DescuentoProducto) * (1 - l.Descuento) ELSE l.Descuento END
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.[Nº Cliente] = @cliente AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= -1
  AND l.Producto IN ({0}) AND (l.[Fecha Albarán] >= @desde OR l.[Fecha Albarán] IS NULL) AND l.Número <> @pedido";

        // Percentil 99 de la cantidad por línea entre todos los clientes (ROW_NUMBER: la BD va en compatibilidad 100, sin PERCENTILE_CONT).
        internal const string SQL_P99_TODOS = @"
SELECT RTRIM(Producto), Cantidad FROM (
    SELECT l.Producto, l.Cantidad, ROW_NUMBER() OVER (PARTITION BY l.Producto ORDER BY l.Cantidad) rn, COUNT(*) OVER (PARTITION BY l.Producto) n
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.Producto IN ({0}) AND l.Estado >= -1 AND l.TipoLinea = 1 AND l.Cantidad > 0
      AND (l.[Fecha Albarán] >= @desde OR l.[Fecha Albarán] IS NULL)
) x WHERE rn = CEILING(0.99 * n)";

        internal const string SQL_PEDIDOS_CLIENTE = @"
SELECT COUNT(*), ISNULL(AVG(Base), 0) FROM (
    SELECT l.Número, SUM(l.[Base Imponible]) Base
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.[Nº Cliente] = @cliente AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Estado >= -1
      AND (l.[Fecha Albarán] >= @desde OR l.[Fecha Albarán] IS NULL) AND l.Número <> @pedido
    GROUP BY l.Número
) x";

        internal const string SQL_GRUPOS_CLIENTE = @"
SELECT DISTINCT RTRIM(l.Grupo)
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.[Nº Cliente] = @cliente AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= -1
  AND (l.[Fecha Albarán] >= @desde OR l.[Fecha Albarán] IS NULL) AND l.Número <> @pedido AND l.Grupo IS NOT NULL";

        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM(p.Número), p.PVP, RTRIM(p.Grupo), RTRIM(p.SubGrupo)
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Número IN ({0})";

        internal const string SQL_DEUDA = @"
SELECT ISNULL(SUM(e.ImportePdte), 0),
       ISNULL(SUM(CASE WHEN e.FechaVto < @hoy THEN e.ImportePdte ELSE 0 END), 0),
       DATEDIFF(day, MIN(CASE WHEN e.FechaVto < @hoy AND e.ImportePdte > 0 THEN e.FechaVto END), @hoy)
FROM ExtractoCliente e WITH (NOLOCK)
WHERE e.Empresa = @empresa AND e.Número = @cliente AND e.ImportePdte <> 0";

        internal const string SQL_FORMA_PAGO_FICHA = @"
SELECT TOP 1 RTRIM(c.FormaPago) FROM CondPagoClientes c WITH (NOLOCK)
WHERE c.Empresa = @empresa AND c.[Nº Cliente] = @cliente AND c.Contacto = @contacto ORDER BY c.ImporteMínimo";

        internal const string SQL_STOCK = @"
SELECT RTRIM(e.Número), RTRIM(e.Almacén), ISNULL(SUM(CAST(e.Cantidad AS int)), 0)
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa = @empresa AND e.Número IN ({0})
GROUP BY e.Número, e.Almacén";

        internal const string COLUMNAS = "Fecha, Empresa, Pedido, Cliente, Contacto, Linea, Producto, Regla, Valor, Esperado, Puntuacion, CantidadGrabada, PrecioGrabado, " +
            "DescuentoGrabado, ImportePedido, NumeroLineas, Usuario, Aplicacion, Vendedor, Operacion, Mostrado, Respuesta, VersionModelo, DeudaTotal, DeudaVencida, " +
            "DiasVencidoMasAntiguo, FormaPagoFicha, FormaPagoPedido, StockAlGrabar, Resultado, ResueltoEl";

        internal const string SQL_LEER = "SELECT Id, " + COLUMNAS + " FROM dbo.PedidosAnomalias WITH (NOLOCK) ";

        internal const string SQL_LINEAS_ACTUALES = @"
SELECT RTRIM(l.Empresa), l.Número, l.[Nº Orden], RTRIM(l.Producto), ISNULL(l.Cantidad, 0), ISNULL(l.Precio, 0),
       CASE WHEN l.[Aplicar Dto] = 1 THEN 1 - (1 - l.DescuentoCliente) * (1 - l.DescuentoProducto) * (1 - l.Descuento) ELSE l.Descuento END,
       l.Estado, l.[Fecha Modificación]
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Número IN ({0}) AND l.TipoLinea = 1";

        // Modificaciones no tiene índice por Fecha ni Tabla (62.800 filas en oct/26, ~200 a la semana): el filtro por fecha recorre la
        // tabla pero solo lee el JSON (LOB) de las filas de la ventana. El pedido va en el JSON como "numero":929449,
        internal const string SQL_MODIFICACIONES = @"
SELECT m.id, m.Fecha, m.Usuario, m.Nuevo
FROM Modificaciones m WITH (NOLOCK)
WHERE m.Tabla = 'Pedidos' AND m.Fecha >= @desde AND m.Fecha < @hasta AND ({0})";

        internal const string SQL_ABONOS = @"
SELECT RTRIM(l.Empresa), RTRIM(l.[Nº Cliente]), RTRIM(l.Producto), l.Número, l.[Fecha Modificación]
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.[Nº Cliente] IN ({0}) AND l.TipoLinea = 1 AND l.Cantidad < 0 AND l.Estado >= -1
  AND l.[Fecha Modificación] >= @desde";

        internal const string SQL_ETIQUETAR = "UPDATE dbo.PedidosAnomalias SET Resultado = @resultado, ResueltoEl = @resueltoEl WHERE Id = @id AND Resultado IS NULL";

        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly string cadenaConexion;

        public RepositorioAnomaliasPedidoSql(string cadenaConexion)
        {
            this.cadenaConexion = new SqlConnectionStringBuilder(cadenaConexion) { Enlist = false }.ConnectionString;
        }

        public static RepositorioAnomaliasPedidoSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioAnomaliasPedidoSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        /// <summary>Para los tests: olvida la caché de «existe la tabla».</summary>
        internal static void OlvidarCache()
        {
            lock (cerrojo)
            {
                existe = false;
                ultimaComprobacion = DateTime.MinValue;
            }
        }

        public bool TablaExiste()
        {
            lock (cerrojo)
            {
                if (existe || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_TABLA)
                {
                    return existe;
                }
            }
            bool hay;
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_EXISTE, conexion))
            {
                hay = Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
            lock (cerrojo)
            {
                existe = hay;
                ultimaComprobacion = DateTime.Now;
            }
            return hay;
        }

        public HistoricoCliente LeerHistorico(string empresa, string cliente, int pedidoExcluir, IEnumerable<string> productos, DateTime desde)
        {
            var historico = new HistoricoCliente();
            List<string> lista = Limpiar(productos);
            using (SqlConnection conexion = Abrir())
            {
                if (lista.Any())
                {
                    using (SqlCommand comando = ConLista(conexion, SQL_COMPRAS_CLIENTE, lista))
                    {
                        Parametros(comando, empresa, cliente, pedidoExcluir, desde);
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                HistoricoProductoCliente producto = historico.De(lector.GetString(0));
                                producto.Cantidades.Add(lector.IsDBNull(1) ? 0 : Convert.ToInt32(lector.GetValue(1)));
                                producto.Precios.Add(lector.IsDBNull(2) ? 0 : Convert.ToDecimal(lector.GetValue(2)));
                                producto.Descuentos.Add(lector.IsDBNull(3) ? 0 : Convert.ToDecimal(lector.GetValue(3)));
                            }
                        }
                    }
                    using (SqlCommand comando = ConLista(conexion, SQL_P99_TODOS, lista))
                    {
                        comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                historico.De(lector.GetString(0)).P99Todos = Convert.ToInt32(lector.GetValue(1));
                            }
                        }
                    }
                    using (SqlCommand comando = ConLista(conexion, SQL_PRODUCTOS, lista))
                    {
                        comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                HistoricoProductoCliente producto = historico.De(lector.GetString(0));
                                producto.PrecioTarifa = lector.IsDBNull(1) ? (decimal?)null : Convert.ToDecimal(lector.GetValue(1));
                                producto.Grupo = lector.IsDBNull(2) ? null : lector.GetString(2);
                                producto.SubGrupo = lector.IsDBNull(3) ? null : lector.GetString(3);
                            }
                        }
                    }
                }
                using (var comando = new SqlCommand(SQL_PEDIDOS_CLIENTE, conexion))
                {
                    Parametros(comando, empresa, cliente, pedidoExcluir, desde);
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            historico.Pedidos = Convert.ToInt32(lector.GetValue(0));
                            historico.ImporteMedio = Convert.ToDecimal(lector.GetValue(1));
                        }
                    }
                }
                using (var comando = new SqlCommand(SQL_GRUPOS_CLIENTE, conexion))
                {
                    Parametros(comando, empresa, cliente, pedidoExcluir, desde);
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            if (!lector.IsDBNull(0))
                            {
                                _ = historico.Grupos.Add(lector.GetString(0));
                            }
                        }
                    }
                }
            }
            return historico;
        }

        public ContextoGrabacion LeerContexto(string empresa, string cliente, string contacto, IEnumerable<KeyValuePair<string, string>> productosAlmacen, DateTime hoy)
        {
            var contexto = new ContextoGrabacion();
            using (SqlConnection conexion = Abrir())
            {
                using (var comando = new SqlCommand(SQL_DEUDA, conexion))
                {
                    comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                    comando.Parameters.Add(Texto("@cliente", SqlDbType.Char, cliente, 10));
                    comando.Parameters.Add(new SqlParameter("@hoy", SqlDbType.DateTime) { Value = hoy.Date });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        if (lector.Read())
                        {
                            contexto.DeudaTotal = Convert.ToDecimal(lector.GetValue(0));
                            contexto.DeudaVencida = Convert.ToDecimal(lector.GetValue(1));
                            contexto.DiasVencidoMasAntiguo = lector.IsDBNull(2) ? (int?)null : Convert.ToInt32(lector.GetValue(2));
                        }
                    }
                }
                using (var comando = new SqlCommand(SQL_FORMA_PAGO_FICHA, conexion))
                {
                    comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                    comando.Parameters.Add(Texto("@cliente", SqlDbType.Char, cliente, 10));
                    comando.Parameters.Add(Texto("@contacto", SqlDbType.Char, contacto, 3));
                    object forma = comando.ExecuteScalar();
                    contexto.FormaPagoFicha = forma == null || forma == DBNull.Value ? null : Convert.ToString(forma);
                }
                List<string> productos = Limpiar((productosAlmacen ?? Enumerable.Empty<KeyValuePair<string, string>>()).Select(p => p.Key));
                if (productos.Any())
                {
                    using (SqlCommand comando = ConLista(conexion, SQL_STOCK, productos))
                    {
                        comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                contexto.Stock[ContextoGrabacion.Clave(lector.GetString(0), lector.IsDBNull(1) ? null : lector.GetString(1))] = Convert.ToInt32(lector.GetValue(2));
                            }
                        }
                    }
                    contexto.StockLeido = true;
                }
            }
            return contexto;
        }

        public void Insertar(IList<FilaAnomaliaPedido> filas)
        {
            if (filas == null || filas.Count == 0)
            {
                return;
            }
            using (SqlConnection conexion = Abrir())
            {
                // De 50 en 50 para no pasar de los 2.100 parámetros de SQL Server (31 columnas por fila).
                for (int inicio = 0; inicio < filas.Count; inicio += 50)
                {
                    List<FilaAnomaliaPedido> trozo = filas.Skip(inicio).Take(50).ToList();
                    var sql = new StringBuilder("INSERT INTO dbo.PedidosAnomalias (" + COLUMNAS + ") VALUES ");
                    using (var comando = new SqlCommand { Connection = conexion })
                    {
                        for (int i = 0; i < trozo.Count; i++)
                        {
                            FilaAnomaliaPedido f = trozo[i];
                            if (i > 0)
                            {
                                _ = sql.Append(", ");
                            }
                            _ = sql.Append("(@fe").Append(i).Append(", @em").Append(i).Append(", @pe").Append(i).Append(", @cl").Append(i).Append(", @co").Append(i)
                                .Append(", @li").Append(i).Append(", @pr").Append(i).Append(", @re").Append(i).Append(", @va").Append(i).Append(", @es").Append(i)
                                .Append(", @pu").Append(i).Append(", @cg").Append(i).Append(", @pg").Append(i).Append(", @dg").Append(i).Append(", @ip").Append(i)
                                .Append(", @nl").Append(i).Append(", @us").Append(i).Append(", @ap").Append(i).Append(", @ve").Append(i).Append(", @op").Append(i)
                                .Append(", @mo").Append(i).Append(", @rp").Append(i).Append(", @vm").Append(i).Append(", @dt").Append(i).Append(", @dv").Append(i)
                                .Append(", @dd").Append(i).Append(", @ff").Append(i).Append(", @fp").Append(i).Append(", @st").Append(i).Append(", @rs").Append(i)
                                .Append(", @rl").Append(i).Append(")");
                            comando.Parameters.Add(new SqlParameter("@fe" + i, SqlDbType.DateTime) { Value = f.Fecha == default ? DateTime.Now : f.Fecha });
                            comando.Parameters.Add(Texto("@em" + i, SqlDbType.Char, f.Empresa, 3));
                            comando.Parameters.Add(new SqlParameter("@pe" + i, SqlDbType.Int) { Value = f.Pedido });
                            comando.Parameters.Add(Texto("@cl" + i, SqlDbType.Char, f.Cliente, 10));
                            comando.Parameters.Add(Texto("@co" + i, SqlDbType.Char, f.Contacto, 3));
                            comando.Parameters.Add(Nulo("@li" + i, SqlDbType.Int, f.Linea));
                            comando.Parameters.Add(Texto("@pr" + i, SqlDbType.Char, f.Producto, 15));
                            comando.Parameters.Add(Texto("@re" + i, SqlDbType.VarChar, f.Regla, 30));
                            comando.Parameters.Add(Decimal("@va" + i, f.Valor, 18, 4));
                            comando.Parameters.Add(Decimal("@es" + i, f.Esperado, 18, 4));
                            comando.Parameters.Add(Decimal("@pu" + i, f.Puntuacion, 9, 4));
                            comando.Parameters.Add(Nulo("@cg" + i, SqlDbType.Int, f.CantidadGrabada));
                            comando.Parameters.Add(Decimal("@pg" + i, f.PrecioGrabado, 18, 4));
                            comando.Parameters.Add(Decimal("@dg" + i, f.DescuentoGrabado, 9, 6));
                            comando.Parameters.Add(Decimal("@ip" + i, f.ImportePedido, 18, 2));
                            comando.Parameters.Add(Nulo("@nl" + i, SqlDbType.Int, f.NumeroLineas));
                            comando.Parameters.Add(Texto("@us" + i, SqlDbType.NVarChar, f.Usuario, 50));
                            comando.Parameters.Add(Texto("@ap" + i, SqlDbType.VarChar, f.Aplicacion, 20));
                            comando.Parameters.Add(Texto("@ve" + i, SqlDbType.Char, f.Vendedor, 3));
                            comando.Parameters.Add(Texto("@op" + i, SqlDbType.VarChar, f.Operacion, 10));
                            comando.Parameters.Add(new SqlParameter("@mo" + i, SqlDbType.Bit) { Value = f.Mostrado });
                            comando.Parameters.Add(Texto("@rp" + i, SqlDbType.VarChar, f.Respuesta, 20));
                            comando.Parameters.Add(Texto("@vm" + i, SqlDbType.VarChar, f.VersionModelo, 30));
                            comando.Parameters.Add(Decimal("@dt" + i, f.DeudaTotal, 18, 2));
                            comando.Parameters.Add(Decimal("@dv" + i, f.DeudaVencida, 18, 2));
                            comando.Parameters.Add(Nulo("@dd" + i, SqlDbType.Int, f.DiasVencidoMasAntiguo));
                            comando.Parameters.Add(Texto("@ff" + i, SqlDbType.Char, f.FormaPagoFicha, 3));
                            comando.Parameters.Add(Texto("@fp" + i, SqlDbType.Char, f.FormaPagoPedido, 3));
                            comando.Parameters.Add(Nulo("@st" + i, SqlDbType.Int, f.StockAlGrabar));
                            comando.Parameters.Add(Texto("@rs" + i, SqlDbType.VarChar, f.Resultado, 20));
                            comando.Parameters.Add(new SqlParameter("@rl" + i, SqlDbType.DateTime) { Value = (object)f.ResueltoEl ?? DBNull.Value });
                        }
                        comando.CommandText = sql.ToString();
                        _ = comando.ExecuteNonQuery();
                    }
                }
            }
        }

        public List<FilaAnomaliaPedido> LeerPendientesEtiquetar(DateTime hasta)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_LEER + "WHERE Resultado IS NULL AND Fecha < @hasta ORDER BY Fecha", conexion))
            {
                comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta });
                return LeerFilas(comando);
            }
        }

        public List<FilaAnomaliaPedido> LeerEntre(DateTime desde, DateTime hasta)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_LEER + "WHERE Fecha >= @desde AND Fecha < @hasta ORDER BY Fecha", conexion))
            {
                comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
                comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta });
                return LeerFilas(comando);
            }
        }

        public List<EstadoLineaActual> LeerLineasActuales(string empresa, IEnumerable<int> pedidos)
        {
            var lineas = new List<EstadoLineaActual>();
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any())
            {
                return lineas;
            }
            using (SqlConnection conexion = Abrir())
            {
                foreach (List<int> trozo in Trozos(lista, 500))
                {
                    using (var comando = new SqlCommand(string.Format(SQL_LINEAS_ACTUALES, string.Join(",", trozo)), conexion))
                    {
                        comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                lineas.Add(new EstadoLineaActual
                                {
                                    Empresa = lector.GetString(0),
                                    Pedido = lector.GetInt32(1),
                                    Linea = lector.GetInt32(2),
                                    Producto = lector.IsDBNull(3) ? null : lector.GetString(3),
                                    Cantidad = Convert.ToInt32(lector.GetValue(4)),
                                    Precio = Convert.ToDecimal(lector.GetValue(5)),
                                    Descuento = lector.IsDBNull(6) ? 0 : Convert.ToDecimal(lector.GetValue(6)),
                                    Estado = Convert.ToInt16(lector.GetValue(7)),
                                    FechaModificacion = lector.GetDateTime(8)
                                });
                            }
                        }
                    }
                }
            }
            return lineas;
        }

        public List<ModificacionCruda> LeerModificaciones(DateTime desde, DateTime hasta, IEnumerable<int> pedidos)
        {
            var modificaciones = new List<ModificacionCruda>();
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any())
            {
                return modificaciones;
            }
            using (SqlConnection conexion = Abrir())
            {
                foreach (List<int> trozo in Trozos(lista, 200))
                {
                    string filtro = string.Join(" OR ", trozo.Select(p => "m.Nuevo LIKE '%\"numero\":" + p + ",%'"));
                    using (var comando = new SqlCommand(string.Format(SQL_MODIFICACIONES, filtro), conexion) { CommandTimeout = 300 })
                    {
                        comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
                        comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta });
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                modificaciones.Add(new ModificacionCruda
                                {
                                    Id = lector.GetInt32(0),
                                    Fecha = lector.GetDateTime(1),
                                    Usuario = lector.IsDBNull(2) ? null : lector.GetString(2),
                                    Nuevo = lector.IsDBNull(3) ? null : lector.GetString(3)
                                });
                            }
                        }
                    }
                }
            }
            return modificaciones;
        }

        public List<AbonoCliente> LeerAbonos(string empresa, IEnumerable<string> clientes, DateTime desde)
        {
            var abonos = new List<AbonoCliente>();
            List<string> lista = Limpiar(clientes);
            if (!lista.Any())
            {
                return abonos;
            }
            using (SqlConnection conexion = Abrir())
            {
                foreach (List<string> trozo in Trozos(lista, 500))
                {
                    using (SqlCommand comando = ConLista(conexion, SQL_ABONOS, trozo))
                    {
                        comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
                        comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
                        using (SqlDataReader lector = comando.ExecuteReader())
                        {
                            while (lector.Read())
                            {
                                abonos.Add(new AbonoCliente
                                {
                                    Empresa = lector.GetString(0),
                                    Cliente = lector.GetString(1),
                                    Producto = lector.IsDBNull(2) ? null : lector.GetString(2),
                                    Pedido = lector.GetInt32(3),
                                    Fecha = lector.GetDateTime(4)
                                });
                            }
                        }
                    }
                }
            }
            return abonos;
        }

        public void Etiquetar(IEnumerable<FilaAnomaliaPedido> filas)
        {
            List<FilaAnomaliaPedido> lista = (filas ?? Enumerable.Empty<FilaAnomaliaPedido>()).Where(f => f != null && f.Resultado != null).ToList();
            if (!lista.Any())
            {
                return;
            }
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_ETIQUETAR, conexion))
            {
                SqlParameter id = comando.Parameters.Add("@id", SqlDbType.Int);
                SqlParameter resultado = comando.Parameters.Add("@resultado", SqlDbType.VarChar, 20);
                SqlParameter resueltoEl = comando.Parameters.Add("@resueltoEl", SqlDbType.DateTime);
                foreach (FilaAnomaliaPedido fila in lista)
                {
                    id.Value = fila.Id;
                    resultado.Value = fila.Resultado;
                    resueltoEl.Value = (object)fila.ResueltoEl ?? DBNull.Value;
                    _ = comando.ExecuteNonQuery();
                }
            }
        }

        private static List<FilaAnomaliaPedido> LeerFilas(SqlCommand comando)
        {
            var filas = new List<FilaAnomaliaPedido>();
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    filas.Add(new FilaAnomaliaPedido
                    {
                        Id = lector.GetInt32(0),
                        Fecha = lector.GetDateTime(1),
                        Empresa = Cadena(lector, 2),
                        Pedido = lector.GetInt32(3),
                        Cliente = Cadena(lector, 4),
                        Contacto = Cadena(lector, 5),
                        Linea = lector.IsDBNull(6) ? (int?)null : lector.GetInt32(6),
                        Producto = Cadena(lector, 7),
                        Regla = Cadena(lector, 8),
                        Valor = Decimal(lector, 9),
                        Esperado = Decimal(lector, 10),
                        Puntuacion = Decimal(lector, 11),
                        CantidadGrabada = lector.IsDBNull(12) ? (int?)null : lector.GetInt32(12),
                        PrecioGrabado = Decimal(lector, 13),
                        DescuentoGrabado = Decimal(lector, 14),
                        ImportePedido = Decimal(lector, 15),
                        NumeroLineas = lector.IsDBNull(16) ? (int?)null : lector.GetInt32(16),
                        Usuario = Cadena(lector, 17),
                        Aplicacion = Cadena(lector, 18),
                        Vendedor = Cadena(lector, 19),
                        Operacion = Cadena(lector, 20),
                        Mostrado = !lector.IsDBNull(21) && lector.GetBoolean(21),
                        Respuesta = Cadena(lector, 22),
                        VersionModelo = Cadena(lector, 23),
                        DeudaTotal = Decimal(lector, 24),
                        DeudaVencida = Decimal(lector, 25),
                        DiasVencidoMasAntiguo = lector.IsDBNull(26) ? (int?)null : lector.GetInt32(26),
                        FormaPagoFicha = Cadena(lector, 27),
                        FormaPagoPedido = Cadena(lector, 28),
                        StockAlGrabar = lector.IsDBNull(29) ? (int?)null : lector.GetInt32(29),
                        Resultado = Cadena(lector, 30),
                        ResueltoEl = lector.IsDBNull(31) ? (DateTime?)null : lector.GetDateTime(31)
                    });
                }
            }
            return filas;
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static void Parametros(SqlCommand comando, string empresa, string cliente, int pedido, DateTime desde)
        {
            comando.Parameters.Add(Texto("@empresa", SqlDbType.Char, empresa, 3));
            comando.Parameters.Add(Texto("@cliente", SqlDbType.Char, cliente, 10));
            comando.Parameters.Add(new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido });
            comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
        }

        /// <summary>Un comando con la lista de textos como parámetros @l0, @l1… en el {0} del SQL.</summary>
        private static SqlCommand ConLista(SqlConnection conexion, string sql, IList<string> valores)
        {
            var comando = new SqlCommand { Connection = conexion };
            var nombres = new List<string>();
            for (int i = 0; i < valores.Count; i++)
            {
                nombres.Add("@l" + i);
                comando.Parameters.Add(new SqlParameter("@l" + i, SqlDbType.VarChar, 15) { Value = valores[i] });
            }
            comando.CommandText = string.Format(sql, string.Join(",", nombres));
            return comando;
        }

        private static List<string> Limpiar(IEnumerable<string> valores)
        {
            return (valores ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MAXIMO_PRODUCTOS)
                .ToList();
        }

        private static IEnumerable<List<T>> Trozos<T>(List<T> lista, int tamano)
        {
            for (int i = 0; i < lista.Count; i += tamano)
            {
                yield return lista.Skip(i).Take(tamano).ToList();
            }
        }

        private static string Cadena(SqlDataReader lector, int indice) => lector.IsDBNull(indice) ? null : lector.GetString(indice).Trim();

        private static decimal? Decimal(SqlDataReader lector, int indice) => lector.IsDBNull(indice) ? (decimal?)null : Convert.ToDecimal(lector.GetValue(indice));

        private static SqlParameter Decimal(string nombre, decimal? valor, byte precision, byte escala)
        {
            return new SqlParameter(nombre, SqlDbType.Decimal) { Precision = precision, Scale = escala, Value = valor.HasValue ? (object)Math.Round(valor.Value, escala) : DBNull.Value };
        }

        private static SqlParameter Nulo(string nombre, SqlDbType tipo, int? valor) => new SqlParameter(nombre, tipo) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };

        private static SqlParameter Texto(string nombre, SqlDbType tipo, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (limpio != null && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            return new SqlParameter(nombre, tipo, largo) { Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio };
        }
    }
}
