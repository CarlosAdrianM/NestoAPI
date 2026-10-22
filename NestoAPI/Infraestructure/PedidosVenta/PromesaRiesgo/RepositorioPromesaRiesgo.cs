using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#693: una fila sin resolver para el listado (corte 2), con el cliente y el vendedor del pedido.</summary>
    public class RiesgoPromesaListado : RiesgoPromesa
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        /// <summary>CabPedidoVta.Vendedor (estética).</summary>
        public string Vendedor { get; set; }
        /// <summary>El de peluquería del pedido (VendedoresPedidosGruposProductos, grupo PEL), si lo tiene.</summary>
        public string VendedorPeluqueria { get; set; }
        public string Ruta { get; set; }
    }

    /// <summary>NestoAPI#693: la tabla PedidosPromesaRiesgo y las lecturas de pedidos con promesa.</summary>
    public interface IRepositorioPromesaRiesgo
    {
        /// <summary>¿Existe la tabla (Scripts/Issue693_PedidosPromesaRiesgo.sql lanzado)? Sin ella no se hace nada.</summary>
        bool TablaExiste();

        /// <summary>
        /// Los pedidos con algo pendiente y con fecha prometida de hoy o futura (o con un riesgo sin resolver, aunque la
        /// prometida ya haya pasado) que llevan alguno de <paramref name="productos"/>. Con <paramref name="productos"/> null,
        /// todos (pasada nocturna).
        /// </summary>
        Task<List<PedidoConPromesa>> LeerCandidatos(string empresa, IEnumerable<string> productos, DateTime hoy);

        /// <summary>Los riesgos sin resolver (de esos pedidos; con null, todos los de la empresa).</summary>
        Task<List<RiesgoPromesa>> LeerSinResolver(string empresa, IEnumerable<int> pedidos);

        /// <summary>La prometida de ahora (null si no tiene) y si le queda algo pendiente.</summary>
        Task<(DateTime? prometida, bool quedaPendiente)> LeerEstadoPedido(string empresa, int pedido);

        Task<Dictionary<string, string>> LeerNombresProductos(string empresa, IEnumerable<string> productos);

        Task<int> Insertar(RiesgoPromesa riesgo);
        /// <summary>FechaNueva, Motivo, Evento, Actualizado, AvisadoCampana y AvisadoCorreo.</summary>
        Task Actualizar(RiesgoPromesa riesgo);
        Task Resolver(int id, DateTime cuando, string motivo);
        Task MarcarAvisadoCampana(int id, DateTime cuando);
        Task MarcarAvisadoCorreo(IEnumerable<int> ids, DateTime cuando);

        /// <summary>Sin resolver y aún no en un correo resumen.</summary>
        Task<List<RiesgoPromesa>> LeerSinCorreo(string empresa);

        /// <summary>Sin resolver y con algo pendiente, con cliente y vendedores (para el listado).</summary>
        Task<List<RiesgoPromesaListado>> LeerListado(string empresa);
    }

    /// <summary>
    /// NestoAPI#693: SQL a mano, fuera del EDMX, como las tablas recientes (DiferenciasReposiciones #634, la prometida #606):
    /// si el script no se ha lanzado, la API no se cae; simplemente no registra nada. Las lecturas de pedidos, con NOLOCK y
    /// entrando por LinPedidoVta.Producto (el mismo camino que la lectura de pendientes de la sombra del modo de servicio).
    /// </summary>
    public class RepositorioPromesaRiesgo : IRepositorioPromesaRiesgo
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_CANDIDATOS = @"SELECT DISTINCT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, c.FechaEntregaAgenciaPrometida AS FechaPrometida
FROM dbo.LinPedidoVta l WITH (NOLOCK)
INNER JOIN dbo.CabPedidoVta c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.Número = l.Número
WHERE l.Empresa = @empresa AND l.Estado >= -1 AND l.Estado <= 1 AND l.TipoLinea = 1{0}
  AND c.FechaEntregaAgenciaPrometida IS NOT NULL
  AND (c.FechaEntregaAgenciaPrometida >= @hoy
       OR EXISTS (SELECT 1 FROM dbo.PedidosPromesaRiesgo r WHERE r.Empresa = c.Empresa AND r.Pedido = c.Número AND r.Resuelto IS NULL))";

        internal const string COLUMNAS = "Id, RTRIM(Empresa) AS Empresa, Pedido, FechaPrometida, FechaNueva, Motivo, Evento, Detectado, Actualizado, " +
            "AvisadoCampana, AvisadoCorreo, Resuelto, MotivoResolucion";

        internal const string SQL_SIN_RESOLVER = "SELECT " + COLUMNAS + " FROM dbo.PedidosPromesaRiesgo WHERE Empresa = @empresa AND Resuelto IS NULL{0}";

        internal const string SQL_SIN_CORREO = "SELECT " + COLUMNAS + " FROM dbo.PedidosPromesaRiesgo WHERE Empresa = @empresa AND Resuelto IS NULL AND AvisadoCorreo IS NULL";

        internal const string SQL_ESTADO_PEDIDO = @"SELECT c.FechaEntregaAgenciaPrometida AS Prometida,
  CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número
       AND l.Estado >= -1 AND l.Estado <= 1 AND l.TipoLinea = 1) THEN 1 ELSE 0 END AS bit) AS QuedaPendiente
FROM dbo.CabPedidoVta c WITH (NOLOCK) WHERE c.Empresa = @empresa AND c.Número = @pedido";

        internal const string SQL_INSERTAR = @"INSERT INTO dbo.PedidosPromesaRiesgo (Empresa, Pedido, FechaPrometida, FechaNueva, Motivo, Evento, Detectado, Actualizado, AvisadoCampana, AvisadoCorreo)
VALUES (@empresa, @pedido, @prometida, @nueva, @motivo, @evento, @detectado, @actualizado, @avisadoCampana, @avisadoCorreo);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        internal const string SQL_ACTUALIZAR = @"UPDATE dbo.PedidosPromesaRiesgo SET FechaNueva = @nueva, Motivo = @motivo, Evento = @evento,
  Actualizado = @actualizado, AvisadoCampana = @avisadoCampana, AvisadoCorreo = @avisadoCorreo
WHERE Id = @id AND Resuelto IS NULL";

        internal const string SQL_RESOLVER = "UPDATE dbo.PedidosPromesaRiesgo SET Resuelto = @cuando, MotivoResolucion = @motivo, Actualizado = @cuando WHERE Id = @id AND Resuelto IS NULL";

        internal const string SQL_AVISADO_CAMPANA = "UPDATE dbo.PedidosPromesaRiesgo SET AvisadoCampana = @cuando WHERE Id = @id";

        internal const string SQL_AVISADO_CORREO = "UPDATE dbo.PedidosPromesaRiesgo SET AvisadoCorreo = @cuando WHERE Id IN ({0})";

        internal const string SQL_LISTADO = @"SELECT r.Id, RTRIM(r.Empresa) AS Empresa, r.Pedido, r.FechaPrometida, r.FechaNueva, r.Motivo, r.Evento, r.Detectado,
  r.Actualizado, r.AvisadoCampana, r.AvisadoCorreo, r.Resuelto, r.MotivoResolucion,
  RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, RTRIM(cl.Nombre) AS NombreCliente, RTRIM(c.Vendedor) AS Vendedor,
  (SELECT TOP 1 RTRIM(v.Vendedor) FROM dbo.VendedoresPedidoGrupoProducto v WITH (NOLOCK)
   WHERE v.Empresa = c.Empresa AND v.Pedido = c.Número AND v.GrupoProducto = 'PEL') AS VendedorPeluqueria,
  RTRIM(c.Ruta) AS Ruta
FROM dbo.PedidosPromesaRiesgo r
INNER JOIN dbo.CabPedidoVta c WITH (NOLOCK) ON c.Empresa = r.Empresa AND c.Número = r.Pedido
LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
WHERE r.Empresa = @empresa AND r.Resuelto IS NULL
  AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número
              AND l.Estado >= -1 AND l.Estado <= 1 AND l.TipoLinea = 1)
ORDER BY r.FechaPrometida, r.Pedido";

        private static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(5);
        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly Func<NVEntities> crearContexto;

        public RepositorioPromesaRiesgo() : this(null)
        {
        }

        internal RepositorioPromesaRiesgo(Func<NVEntities> crearContexto)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
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
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                ahora = false;
            }
            lock (cerrojo)
            {
                existe = ahora;
                ultimaComprobacion = DateTime.Now;
                return existe;
            }
        }

        public async Task<List<PedidoConPromesa>> LeerCandidatos(string empresa, IEnumerable<string> productos, DateTime hoy)
        {
            var parametros = new List<object> { ParametroEmpresa(empresa), new SqlParameter("@hoy", SqlDbType.DateTime) { Value = hoy.Date } };
            string filtro = string.Empty;
            if (productos != null)
            {
                List<string> lista = Limpios(productos);
                if (!lista.Any())
                {
                    return new List<PedidoConPromesa>();
                }
                filtro = " AND l.Producto IN (" + string.Join(", ", lista.Select((p, i) => "@p" + i)) + ")";
                parametros.AddRange(lista.Select((p, i) => new SqlParameter("@p" + i, SqlDbType.VarChar, 15) { Value = p }));
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<PedidoConPromesa>(string.Format(SQL_CANDIDATOS, filtro), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<RiesgoPromesa>> LeerSinResolver(string empresa, IEnumerable<int> pedidos)
        {
            var parametros = new List<object> { ParametroEmpresa(empresa) };
            string filtro = string.Empty;
            if (pedidos != null)
            {
                List<int> lista = pedidos.Distinct().ToList();
                if (!lista.Any())
                {
                    return new List<RiesgoPromesa>();
                }
                // Enteros: van en el texto sin riesgo de inyección.
                filtro = " AND Pedido IN (" + string.Join(", ", lista) + ")";
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RiesgoPromesa>(string.Format(SQL_SIN_RESOLVER, filtro), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        private class EstadoPedidoSql
        {
            public DateTime? Prometida { get; set; }
            public bool QuedaPendiente { get; set; }
        }

        public async Task<(DateTime? prometida, bool quedaPendiente)> LeerEstadoPedido(string empresa, int pedido)
        {
            using (NVEntities db = crearContexto())
            {
                EstadoPedidoSql estado = await db.Database.SqlQuery<EstadoPedidoSql>(SQL_ESTADO_PEDIDO, ParametroEmpresa(empresa),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido }).FirstOrDefaultAsync().ConfigureAwait(false);
                return estado == null ? ((DateTime?)null, false) : (estado.Prometida, estado.QuedaPendiente);
            }
        }

        public async Task<Dictionary<string, string>> LeerNombresProductos(string empresa, IEnumerable<string> productos)
        {
            List<string> lista = Limpios(productos);
            var nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!lista.Any())
            {
                return nombres;
            }
            using (NVEntities db = crearContexto())
            {
                var filas = await db.Productos.Where(p => p.Empresa == empresa && lista.Contains(p.Número))
                    .Select(p => new { p.Número, p.Nombre }).ToListAsync().ConfigureAwait(false);
                foreach (var fila in filas)
                {
                    nombres[fila.Número.Trim()] = fila.Nombre?.Trim();
                }
            }
            return nombres;
        }

        public async Task<int> Insertar(RiesgoPromesa riesgo)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_INSERTAR,
                    ParametroEmpresa(riesgo.Empresa),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = riesgo.Pedido },
                    Fecha("@prometida", riesgo.FechaPrometida.Date),
                    Fecha("@nueva", riesgo.FechaNueva?.Date),
                    Texto("@motivo", riesgo.Motivo),
                    Texto("@evento", Recortar(riesgo.Evento, 200)),
                    Fecha("@detectado", riesgo.Detectado),
                    Fecha("@actualizado", riesgo.Actualizado),
                    Fecha("@avisadoCampana", riesgo.AvisadoCampana),
                    Fecha("@avisadoCorreo", riesgo.AvisadoCorreo)).FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task Actualizar(RiesgoPromesa riesgo)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_ACTUALIZAR,
                    new SqlParameter("@id", SqlDbType.Int) { Value = riesgo.Id },
                    Fecha("@nueva", riesgo.FechaNueva?.Date),
                    Texto("@motivo", riesgo.Motivo),
                    Texto("@evento", Recortar(riesgo.Evento, 200)),
                    Fecha("@actualizado", riesgo.Actualizado),
                    Fecha("@avisadoCampana", riesgo.AvisadoCampana),
                    Fecha("@avisadoCorreo", riesgo.AvisadoCorreo)).ConfigureAwait(false);
            }
        }

        public async Task Resolver(int id, DateTime cuando, string motivo)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_RESOLVER,
                    new SqlParameter("@id", SqlDbType.Int) { Value = id },
                    Fecha("@cuando", cuando),
                    Texto("@motivo", Recortar(motivo, 200))).ConfigureAwait(false);
            }
        }

        public async Task MarcarAvisadoCampana(int id, DateTime cuando)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_AVISADO_CAMPANA,
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }, Fecha("@cuando", cuando)).ConfigureAwait(false);
            }
        }

        public async Task MarcarAvisadoCorreo(IEnumerable<int> ids, DateTime cuando)
        {
            List<int> lista = (ids ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any())
            {
                return;
            }
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(string.Format(SQL_AVISADO_CORREO, string.Join(", ", lista)),
                    Fecha("@cuando", cuando)).ConfigureAwait(false);
            }
        }

        public async Task<List<RiesgoPromesa>> LeerSinCorreo(string empresa)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RiesgoPromesa>(SQL_SIN_CORREO, ParametroEmpresa(empresa)).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<RiesgoPromesaListado>> LeerListado(string empresa)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RiesgoPromesaListado>(SQL_LISTADO, ParametroEmpresa(empresa)).ToListAsync().ConfigureAwait(false);
            }
        }

        internal static List<string> Limpios(IEnumerable<string> productos) => (productos ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private static string Recortar(string texto, int maximo) =>
            texto == null || texto.Length <= maximo ? texto : texto.Substring(0, maximo);

        // VarChar e Int, no NVarChar: Empresa es char y un parámetro Unicode obligaría a convertir la columna.
        private static SqlParameter ParametroEmpresa(string empresa) =>
            new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() };

        private static SqlParameter Fecha(string nombre, DateTime? valor) =>
            new SqlParameter(nombre, SqlDbType.DateTime) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };

        private static SqlParameter Texto(string nombre, string valor) =>
            new SqlParameter(nombre, SqlDbType.NVarChar, -1) { Value = valor == null ? (object)DBNull.Value : valor };
    }
}
