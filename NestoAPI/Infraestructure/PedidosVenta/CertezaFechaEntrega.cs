using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#853: un correo de seguimiento de la agencia aplicado a un pedido de compra (NestoAPI#857, fase 3).</summary>
    public class CorreoAplicadoPedidoCompra
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime FechaRecepcionAplicada { get; set; }
        public string Agencia { get; set; }
        public DateTime? AplicadoEl { get; set; }
        /// <summary>
        /// NestoAPI#857e: es un aviso de salida del PROVEEDOR (remitente marcado sin agencia) sin código ni enlace de seguimiento: sabemos
        /// que ha salido, pero ninguna agencia lo está siguiendo. No cuenta como seguimiento (la fecha queda Aproximada, no CasiSegura).
        /// </summary>
        public bool SinSeguimientoAgencia { get; set; }
        /// <summary>
        /// 20/11/26 (Scripts/Issue857h): el correo puso la fecha SOLO a las líneas de estos productos (las del aviso del proveedor). Null = a todo
        /// el pedido (como antes): cuenta para cualquier producto.
        /// </summary>
        public List<string> ProductosAplicados { get; set; }
    }

    /// <summary>NestoAPI#853: lo que lee el cálculo de la fecha de entrega para la certeza (sustituible en los tests). Por tanda, sin N+1.</summary>
    public interface ILectorCertezaFechaEntrega
    {
        /// <summary>Los correos aplicados (no descartados) a esos pedidos de compra. Vacía sin las columnas de #857.</summary>
        List<CorreoAplicadoPedidoCompra> CorreosAplicados(IEnumerable<int> pedidos);
        /// <summary>Los plazos de los proveedores (con caché). Null sin tabla o vacía.</summary>
        PlazosProveedores Plazos(string empresa);
        /// <summary>Producto → proveedor habitual.</summary>
        Dictionary<string, string> ProveedorHabitual(string empresa, IEnumerable<string> productos);
    }

    /// <summary>
    /// NestoAPI#853: un pedido de compra está «con seguimiento» si tiene un correo aplicado, no descartado, cuya FechaRecepcionAplicada es
    /// la FechaRecepción que tiene ahora (si Compras la ha cambiado después, el correo ya no manda). NestoAPI#857e: un aviso de salida del
    /// proveedor sin código de seguimiento (<see cref="CorreoAplicadoPedidoCompra.SinSeguimientoAgencia"/>) pone la fecha, pero NO es
    /// seguimiento de la agencia: con solo ese correo, la línea queda Aproximada (Carlos 18/11/26). Pura.
    /// </summary>
    public static class ReglaSeguimientoAgencia
    {
        /// <summary>El correo vigente (el último aplicado), o null.</summary>
        /// <param name="producto">
        /// 20/11/26: el producto de la línea. Un correo que puso la fecha solo a unas líneas (<see cref="CorreoAplicadoPedidoCompra.ProductosAplicados"/>)
        /// no da por «con seguimiento» las de otros productos del pedido. Null = no se mira (como antes).
        /// </param>
        public static CorreoAplicadoPedidoCompra Vigente(IEnumerable<CorreoAplicadoPedidoCompra> correos, string empresa, int pedido, DateTime fechaRecepcion,
            string producto = null)
        {
            List<CorreoAplicadoPedidoCompra> suyos = (correos ?? Enumerable.Empty<CorreoAplicadoPedidoCompra>())
                .Where(c => c != null && !c.SinSeguimientoAgencia && c.Pedido == pedido
                    && AplicadoAlProducto(c, producto)
                    && (string.IsNullOrWhiteSpace(empresa) || string.Equals(c.Empresa?.Trim(), empresa.Trim(), StringComparison.OrdinalIgnoreCase))
                    && c.FechaRecepcionAplicada.Date == fechaRecepcion.Date)
                .OrderByDescending(c => c.AplicadoEl ?? DateTime.MinValue)
                .ToList();
            if (!suyos.Any())
            {
                return null;
            }
            CorreoAplicadoPedidoCompra vigente = suyos.First();
            // La agencia de cualquiera de los correos del envío (uno de MBE puede no decirla y el de UPS sí).
            string agencia = suyos.Select(c => c.Agencia?.Trim()).FirstOrDefault(a => !string.IsNullOrEmpty(a));
            return new CorreoAplicadoPedidoCompra
            {
                Empresa = vigente.Empresa,
                Pedido = vigente.Pedido,
                FechaRecepcionAplicada = vigente.FechaRecepcionAplicada,
                AplicadoEl = vigente.AplicadoEl,
                Agencia = agencia
            };
        }

        /// <summary>¿Puso ese correo la fecha a las líneas de ese producto? Sin líneas del aviso (todo el pedido) o sin producto, sí.</summary>
        public static bool AplicadoAlProducto(CorreoAplicadoPedidoCompra correo, string producto) =>
            correo?.ProductosAplicados == null || string.IsNullOrWhiteSpace(producto)
                || correo.ProductosAplicados.Any(p => string.Equals(p?.Trim(), producto.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// NestoAPI#853: completa las líneas ya repartidas (<see cref="RepartidorStockFechaEntregaAgencia"/>) con lo que hace falta para la
    /// certeza: el seguimiento de la agencia de su pedido a proveedor y la llegada orientativa de lo que aún no se ha pedido. NO cambia ninguna
    /// fecha del cálculo. Lecturas por tanda.
    /// </summary>
    public static class CertezaFechaEntrega
    {
        public static void Completar(IList<LineaFechaEntregaAgencia> lineas, ILectorCertezaFechaEntrega lector, string empresa, DateTime hoy)
        {
            if (lector == null || lineas == null || !lineas.Any())
            {
                return;
            }
            // NestoAPI#606 (packs montables): los componentes que faltan para montar un pack, igual que cualquier línea (una lectura para todos)
            lineas = lineas
                .Concat(lineas.Where(l => l?.ComponentesMontaje != null).SelectMany(l => l.ComponentesMontaje))
                .Where(l => l != null)
                .Distinct()
                .ToList();

            // Seguimiento de la agencia de los pedidos a proveedor con fecha
            List<LineaFechaEntregaAgencia> conPedido = lineas.Where(l => l != null && l.DelProveedor > 0 && l.PedidoProveedor.HasValue && l.FechaProveedor.HasValue).ToList();
            if (conPedido.Any())
            {
                List<CorreoAplicadoPedidoCompra> correos = lector.CorreosAplicados(conPedido.Select(l => l.PedidoProveedor.Value).Distinct())
                    ?? new List<CorreoAplicadoPedidoCompra>();
                foreach (LineaFechaEntregaAgencia linea in conPedido)
                {
                    CorreoAplicadoPedidoCompra vigente = ReglaSeguimientoAgencia.Vigente(correos, linea.EmpresaProveedor, linea.PedidoProveedor.Value, linea.FechaProveedor.Value,
                        linea.Producto);
                    linea.ConSeguimientoAgencia = vigente != null;
                    linea.AgenciaSeguimiento = vigente?.Agencia;
                }
            }

            // Lo que aún no se ha pedido: cuándo podría llegar (orientativo)
            List<LineaFechaEntregaAgencia> sinPedir = lineas.Where(l => l != null && SinStock(l) > 0 && !l.FechaProveedorLejana.HasValue && !string.IsNullOrWhiteSpace(l.Producto)).ToList();
            if (!sinPedir.Any())
            {
                return;
            }
            PlazosProveedores plazos = lector.Plazos(empresa);
            if (plazos == null || !plazos.TieneDatos)
            {
                return;
            }
            Dictionary<string, string> proveedores = lector.ProveedorHabitual(empresa, sinPedir.Select(l => l.Producto.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                ?? new Dictionary<string, string>();
            foreach (LineaFechaEntregaAgencia linea in sinPedir)
            {
                if (proveedores.TryGetValue(linea.Producto.Trim(), out string proveedor))
                {
                    linea.LlegadaOrientativaSinPedir = ReglaFechaOrientativaSinPedir.LlegadaEstimada(hoy, plazos.DeProveedor(proveedor), plazos.Generico);
                }
            }
        }

        /// <summary>Las unidades de la línea que se quedan sin fecha (lo que no cubre ningún origen).</summary>
        internal static int SinStock(LineaFechaEntregaAgencia l)
        {
            if (l.YaEnPicking)
            {
                return 0;
            }
            int cubiertas = Math.Max(0, l.EnAlgete) + (l.DeKits?.Values.Sum() ?? 0) + (l.EnTiendas?.Values.Sum() ?? 0)
                + Math.Max(0, l.EnCaminoDeTiendas) + Math.Max(0, l.DelProveedor)
                + Math.Max(0, l.Montaje) + Math.Max(0, l.MontajeConComponentes);
            return Math.Max(0, l.Cantidad - cubiertas);
        }
    }

    /// <summary>
    /// NestoAPI#853: <see cref="ILectorCertezaFechaEntrega"/> contra la BD. Sin las columnas de NestoAPI#857 (Scripts/Issue857d), nadie
    /// tiene seguimiento; sin la tabla de plazos (Scripts/Issue853), no hay orientativas. Conexiones con <c>Enlist=false</c>.
    /// </summary>
    public class LectorCertezaFechaEntregaSql : ILectorCertezaFechaEntrega
    {
        internal const string SQL_EXISTEN_COLUMNAS = "SELECT CASE WHEN OBJECT_ID('dbo.ComprasCorreosSeguimiento', 'U') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'EmpresaAplicada') IS NULL OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'PedidoAplicado') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'FechaRecepcionAplicada') IS NULL OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'AplicadoEl') IS NULL " +
            "OR COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'Descartado') IS NULL THEN 0 ELSE 1 END";

        // {0} = los pedidos; {1} = si es un aviso del proveedor sin seguimiento (con Scripts/Issue857e) o 0
        internal const string SQL_CORREOS_APLICADOS = @"
SELECT RTRIM(c.EmpresaAplicada), c.PedidoAplicado, CAST(c.FechaRecepcionAplicada AS datetime), RTRIM(c.Agencia), c.AplicadoEl, {1}, {2}
FROM dbo.ComprasCorreosSeguimiento c WITH (NOLOCK)
WHERE c.PedidoAplicado IN ({0}) AND ISNULL(c.Descartado, 0) = 0 AND c.FechaRecepcionAplicada IS NOT NULL";
        internal const string SIN_SEGUIMIENTO_AVISO = "CAST(CASE WHEN ISNULL(c.AvisoProveedor, 0) = 1 AND c.Codigo IS NULL AND c.Url IS NULL THEN 1 ELSE 0 END AS bit)";
        internal const string SIN_SEGUIMIENTO_NUNCA = "CAST(0 AS bit)";
        internal const string SQL_EXISTE_AVISO = "SELECT CASE WHEN COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'AvisoProveedor') IS NULL THEN 0 ELSE 1 END";
        // 20/11/26: Scripts/Issue857h (las líneas a las que el correo puso la fecha) o NULL
        internal const string LINEAS_APLICADAS = "c.LineasAplicadas";
        internal const string LINEAS_APLICADAS_NULAS = "CAST(NULL AS nvarchar(max))";
        internal const string SQL_EXISTEN_LINEAS = "SELECT CASE WHEN COL_LENGTH('dbo.ComprasCorreosSeguimiento', 'LineasAplicadas') IS NULL THEN 0 ELSE 1 END";

        private const int TIMEOUT = 15;
        private static readonly object cerrojo = new object();
        private static bool? columnas;
        private static DateTime columnasComprobadasUtc;
        private static bool? columnaAviso;
        private static DateTime columnaAvisoComprobadaUtc;
        private static bool? columnaLineas;
        private static DateTime columnaLineasComprobadaUtc;

        private readonly string cadenaConexion;
        private readonly Func<IRepositorioPlazosProveedores> plazos;
        private readonly Action<Exception> registrar;

        public LectorCertezaFechaEntregaSql(Action<Exception> registrar = null)
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = RepositorioPlazosProveedores.NOMBRE_APLICACION
            }.ConnectionString);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
            RepositorioPlazosProveedores repositorio = new RepositorioPlazosProveedores(cadenaConexion);
            plazos = () => repositorio;
        }

        public List<CorreoAplicadoPedidoCompra> CorreosAplicados(IEnumerable<int> pedidos)
        {
            var resultado = new List<CorreoAplicadoPedidoCompra>();
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any() || !ColumnasExisten())
            {
                return resultado;
            }
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                string sql = string.Format(SQL_CORREOS_APLICADOS, string.Join(", ", lista.Select((p, i) => "@p" + i)),
                    Comprobar(SQL_EXISTE_AVISO, ref columnaAviso, ref columnaAvisoComprobadaUtc) ? SIN_SEGUIMIENTO_AVISO : SIN_SEGUIMIENTO_NUNCA,
                    Comprobar(SQL_EXISTEN_LINEAS, ref columnaLineas, ref columnaLineasComprobadaUtc) ? LINEAS_APLICADAS : LINEAS_APLICADAS_NULAS);
                using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT })
                {
                    for (int i = 0; i < lista.Count; i++)
                    {
                        comando.Parameters.Add("@p" + i, SqlDbType.Int).Value = lista[i];
                    }
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            resultado.Add(new CorreoAplicadoPedidoCompra
                            {
                                Empresa = lector.IsDBNull(0) ? null : lector.GetString(0),
                                Pedido = lector.GetInt32(1),
                                FechaRecepcionAplicada = lector.GetDateTime(2),
                                Agencia = lector.IsDBNull(3) ? null : Convert.ToString(lector.GetValue(3)),
                                AplicadoEl = lector.IsDBNull(4) ? (DateTime?)null : lector.GetDateTime(4),
                                SinSeguimientoAgencia = !lector.IsDBNull(5) && lector.GetBoolean(5),
                                ProductosAplicados = lector.IsDBNull(6) ? null : ProductosAplicados(lector.GetString(6))
                            });
                        }
                    }
                }
            }
            return resultado;
        }

        /// <summary>Los productos de LineasAplicadas (JSON de las líneas del aviso); null si no hay o no se entiende (todo el pedido).</summary>
        internal static List<string> ProductosAplicados(string json)
        {
            List<NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento.LineaCasadaAviso> lineas =
                NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento.LineasAvisoCorreo.LeerAplicadas(json);
            return lineas == null || !lineas.Any() ? null
                : lineas.Select(l => l.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public PlazosProveedores Plazos(string empresa) => CachePlazosProveedores.Obtener(plazos, empresa, registrar);

        public Dictionary<string, string> ProveedorHabitual(string empresa, IEnumerable<string> productos) =>
            plazos().ProveedorHabitual(CachePlazosProveedores.EmpresaPlazos(empresa), productos);

        /// <summary>¿Están las columnas de Scripts/Issue857d? Con caché de 10 minutos; ante un fallo, que no.</summary>
        private bool ColumnasExisten() => Comprobar(SQL_EXISTEN_COLUMNAS, ref columnas, ref columnasComprobadasUtc);

        /// <summary>Una comprobación de columnas opcionales con caché de 10 minutos (Issue857d, Issue857e); ante un fallo, que no.</summary>
        private bool Comprobar(string sql, ref bool? valor, ref DateTime comprobadaUtc)
        {
            lock (cerrojo)
            {
                if (valor.HasValue && DateTime.UtcNow - comprobadaUtc < CachePlazosProveedores.DURACION)
                {
                    return valor.Value;
                }
            }
            bool existen;
            try
            {
                using (var conexion = new SqlConnection(cadenaConexion))
                using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT })
                {
                    conexion.Open();
                    existen = Convert.ToInt32(comando.ExecuteScalar()) == 1;
                }
            }
            catch (Exception)
            {
                existen = false;
            }
            lock (cerrojo)
            {
                valor = existen;
                comprobadaUtc = DateTime.UtcNow;
            }
            return existen;
        }
    }
}
