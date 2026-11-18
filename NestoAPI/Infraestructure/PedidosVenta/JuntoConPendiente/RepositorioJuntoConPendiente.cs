using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Sugerencia 1340: cliente y contacto (la dirección de entrega) de un pedido.</summary>
    public class ClaveClienteContacto
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
    }

    /// <summary>Sugerencia 1340: un pedido en modo 6 que lleva tiempo esperando (para el aviso de los 15 días).</summary>
    public class PedidoEsperandoJunto
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Desde cuándo está en modo 6: la última fila de PedidosModoServicioOrigen (#704) que lo dejó en 6; sin ella, la fecha del pedido.</summary>
        public DateTime Desde { get; set; }
    }

    /// <summary>Sugerencia 1340: el acceso a datos del modo 6 (sustituible en los tests).</summary>
    public interface IRepositorioJuntoConPendiente
    {
        /// <summary>
        /// ¿Se puede usar el modo 6? Solo con el script (Scripts/Sugerencia1340_ModoJuntoConLoPendiente.sql) lanzado: la restricción
        /// CK_CabPedidoVta_ModoServicio admite el 6 (lección de #698) y existe la tabla de avisos. Cacheado; nunca lanza (false).
        /// </summary>
        bool ModoAdmitido();
        /// <summary>Lo pendiente (sin filtrar por la regla) de esos clientes en esa empresa, en UNA consulta. Nunca null.</summary>
        List<PendienteDelCliente> LeerPendientes(string empresa, IEnumerable<ClaveClienteContacto> claves);
        /// <summary>Los pedidos vivos en modo 6 que entraron en él antes de <paramref name="limite"/> y aún no se han avisado.</summary>
        List<PedidoEsperandoJunto> LeerEsperandoSinAvisar(DateTime limite);
        /// <summary>Apunta el aviso (una sola vez por pedido). True si lo ha apuntado ESTA llamada.</summary>
        bool MarcarAvisado(string empresa, int pedido, DateTime cuando);
    }

    /// <summary>
    /// Sugerencia 1340. Lo pendiente se lee con EF (CabPedidoVta y LinPedidoVta están en el EDMX); la comprobación del script y la tabla
    /// de avisos, con SQL (fuera del EDMX). Cada operación abre su propio contexto (no se mezcla con el SaveChanges de quien llama).
    /// </summary>
    public class RepositorioJuntoConPendiente : IRepositorioJuntoConPendiente
    {
        public const string TABLA_AVISOS = "PedidosJuntoConPendienteAvisos";

        /// <summary>
        /// El script ha corrido: existe la tabla de avisos (la crea DESPUÉS de ampliar la restricción) y la restricción, si se ve su
        /// definición, admite el 6. Si el login no ve la definición (NULL), manda la tabla.
        /// </summary>
        internal const string SQL_ADMITIDO = @"SELECT CAST(CASE WHEN OBJECT_ID('dbo." + TABLA_AVISOS + @"', 'U') IS NULL THEN 0
    WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CabPedidoVta_ModoServicio'
                 AND definition IS NOT NULL AND definition NOT LIKE '%(6)%') THEN 0
    ELSE 1 END AS bit)";

        /// <summary>{0} = la expresión de «desde» (con o sin PedidosModoServicioOrigen).</summary>
        internal const string SQL_ESPERANDO = @"SELECT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(c.Usuario) AS Usuario, RTRIM(c.Vendedor) AS Vendedor, {0} AS Desde
FROM dbo.CabPedidoVta c WITH (NOLOCK)
WHERE c.ModoServicio = 6 AND c.ServirJunto = 1
  AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1)
  AND NOT EXISTS (SELECT 1 FROM dbo." + TABLA_AVISOS + @" a WHERE a.Empresa = c.Empresa AND a.Pedido = c.Número)
  AND {0} < @limite";

        internal const string DESDE_CON_ORIGEN = "ISNULL((SELECT MAX(o.Fecha) FROM dbo.PedidosModoServicioOrigen o WITH (NOLOCK) " +
            "WHERE o.Empresa = c.Empresa AND o.Pedido = c.Número AND o.ModoGuardado = 6), c.Fecha)";
        internal const string DESDE_SIN_ORIGEN = "c.Fecha";

        internal const string SQL_EXISTE_ORIGEN = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.PedidosModoServicioOrigen', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_MARCAR = @"IF NOT EXISTS (SELECT 1 FROM dbo." + TABLA_AVISOS + @" WHERE Empresa = @empresa AND Pedido = @pedido)
    INSERT INTO dbo." + TABLA_AVISOS + @" (Empresa, Pedido, Avisado) VALUES (@empresa, @pedido, @cuando);";

        private static readonly TimeSpan REINTENTO_SIN_SCRIPT = TimeSpan.FromMinutes(5);
        private static readonly object cerrojo = new object();
        private static bool admitido;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly Func<NVEntities> crearContexto;

        public RepositorioJuntoConPendiente() : this(null)
        {
        }

        internal RepositorioJuntoConPendiente(Func<NVEntities> crearContexto)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        /// <summary>Para los tests: olvida lo cacheado.</summary>
        internal static void OlvidarCache()
        {
            lock (cerrojo)
            {
                admitido = false;
                ultimaComprobacion = DateTime.MinValue;
            }
        }

        public bool ModoAdmitido()
        {
            lock (cerrojo)
            {
                // Una vez admitido no se vuelve a preguntar; mientras no, como mucho cada 5 minutos.
                if (admitido || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_SCRIPT)
                {
                    return admitido;
                }
            }
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(SQL_ADMITIDO).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Junto con lo pendiente, sugerencia 1340] No se ha podido comprobar si la BD admite el modo 6: {ex.Message}", ex));
                ahora = false;
            }
            lock (cerrojo)
            {
                admitido = ahora;
                ultimaComprobacion = DateTime.Now;
                return admitido;
            }
        }

        public List<PendienteDelCliente> LeerPendientes(string empresa, IEnumerable<ClaveClienteContacto> claves)
        {
            List<string> clientes = (claves ?? Enumerable.Empty<ClaveClienteContacto>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Cliente))
                .Select(c => c.Cliente.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            if (!clientes.Any())
            {
                return new List<PendienteDelCliente>();
            }
            using (NVEntities db = crearContexto())
            {
                var filas = db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Empresa == empresaLimpia && clientes.Contains(l.Nº_Cliente)
                        && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                    .Select(l => new FilaPendiente
                    {
                        Pedido = l.Número,
                        TipoLinea = l.TipoLinea,
                        Producto = l.Producto,
                        Texto = l.Texto,
                        Cantidad = l.Cantidad,
                        Recoger = l.Recoger,
                        FechaEntrega = l.Fecha_Entrega,
                        BaseImponible = l.Base_Imponible,
                        Picking = l.Picking,
                        Almacen = l.Almacén,
                        Estado = l.Estado,
                        Cliente = l.CabPedidoVta.Nº_Cliente,
                        Contacto = l.CabPedidoVta.Contacto,
                        NotaEntrega = l.CabPedidoVta.NotaEntrega,
                        ModoServicio = l.CabPedidoVta.ModoServicio,
                        ServirJunto = l.CabPedidoVta.ServirJunto
                    })
                    .ToList();
                return Agrupar(empresaLimpia, filas);
            }
        }

        /// <summary>Una fila de la lectura (línea + su cabecera).</summary>
        internal class FilaPendiente
        {
            public int Pedido { get; set; }
            public byte? TipoLinea { get; set; }
            public string Producto { get; set; }
            public string Texto { get; set; }
            public short? Cantidad { get; set; }
            public int Recoger { get; set; }
            public DateTime FechaEntrega { get; set; }
            public decimal BaseImponible { get; set; }
            public int? Picking { get; set; }
            public string Almacen { get; set; }
            public short Estado { get; set; }
            public string Cliente { get; set; }
            public string Contacto { get; set; }
            public bool NotaEntrega { get; set; }
            public byte? ModoServicio { get; set; }
            public bool ServirJunto { get; set; }
        }

        /// <summary>Puro: las líneas leídas agrupadas por pedido (solo los que tienen algo de producto pendiente en Algete sin picking).</summary>
        internal static List<PendienteDelCliente> Agrupar(string empresa, IEnumerable<FilaPendiente> filas)
        {
            var resultado = new List<PendienteDelCliente>();
            foreach (IGrouping<int, FilaPendiente> grupo in (filas ?? Enumerable.Empty<FilaPendiente>()).Where(f => f != null).GroupBy(f => f.Pedido))
            {
                FilaPendiente cabecera = grupo.First();
                List<FilaPendiente> deProducto = grupo.Where(EsProductoPendienteEnAlgete).ToList();
                if (!deProducto.Any())
                {
                    continue;
                }
                resultado.Add(new PendienteDelCliente
                {
                    Empresa = empresa,
                    Pedido = grupo.Key,
                    Cliente = cabecera.Cliente?.Trim(),
                    Contacto = cabecera.Contacto?.Trim(),
                    EsNotaEntrega = cabecera.NotaEntrega,
                    ModoServicio = cabecera.ModoServicio,
                    ServirJunto = cabecera.ServirJunto,
                    BasePendiente = deProducto.Sum(f => f.BaseImponible),
                    LlevaPortes = grupo.Any(f => GestorPortes.EsLineaPortesViva(new LinPedidoVta
                    {
                        TipoLinea = f.TipoLinea,
                        Producto = f.Producto,
                        Texto = f.Texto,
                        Estado = f.Estado
                    })),
                    Lineas = deProducto.Select(f => new LineaPendienteDelCliente
                    {
                        Producto = f.Producto?.Trim(),
                        Texto = f.Texto?.Trim(),
                        Cantidad = (f.Cantidad ?? 0) - f.Recoger,
                        FechaEntrega = f.FechaEntrega
                    }).ToList()
                });
            }
            return resultado.OrderBy(p => p.Pedido).ToList();
        }

        private static bool EsProductoPendienteEnAlgete(FilaPendiente f)
            => f.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && string.Equals(f.Almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase)
                && (f.Cantidad ?? 0) - f.Recoger > 0
                && (f.Picking ?? 0) == 0
                && f.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && f.Estado <= Constantes.EstadosLineaVenta.EN_CURSO;

        public List<PedidoEsperandoJunto> LeerEsperandoSinAvisar(DateTime limite)
        {
            if (!ModoAdmitido())
            {
                return new List<PedidoEsperandoJunto>();
            }
            using (NVEntities db = crearContexto())
            {
                bool conOrigen = db.Database.SqlQuery<bool>(SQL_EXISTE_ORIGEN).FirstOrDefault();
                string sql = string.Format(SQL_ESPERANDO, conOrigen ? DESDE_CON_ORIGEN : DESDE_SIN_ORIGEN);
                return db.Database.SqlQuery<PedidoEsperandoJunto>(sql, new SqlParameter("@limite", System.Data.SqlDbType.DateTime) { Value = limite })
                    .ToList();
            }
        }

        public bool MarcarAvisado(string empresa, int pedido, DateTime cuando)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = db.Database.ExecuteSqlCommand(SQL_MARCAR,
                    new SqlParameter("@empresa", System.Data.SqlDbType.Char, 3) { Value = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                    new SqlParameter("@pedido", System.Data.SqlDbType.Int) { Value = pedido },
                    new SqlParameter("@cuando", System.Data.SqlDbType.DateTime) { Value = cuando });
                return filas > 0;
            }
        }
    }
}
