using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>NestoAPI#698: la tabla PedidosRetenidos y lo poco que hace falta de CabPedidoVta.</summary>
    public interface IRepositorioPedidosRetenidos
    {
        /// <summary>¿Está lanzado Scripts/Issue698_PedidosRetenidos.sql?</summary>
        bool TablaExiste();

        /// <summary>La cabecera con lo que hace falta para decidir; null si el pedido no existe.</summary>
        Task<CabeceraPedidoRetencion> LeerCabecera(string empresa, int pedido);

        /// <summary>La retención viva del pedido (Liberado NULL) o null.</summary>
        Task<RetencionPedidoDTO> LeerActiva(string empresa, int pedido);

        /// <summary>Lo mismo, sin async (GestorPedidosVenta.LeerPedido va dentro de un contexto abierto).</summary>
        RetencionPedidoDTO LeerActivaSincrono(string empresa, int pedido);

        /// <summary>Las retenciones vivas de esos pedidos (para el picking). Vacía si no hay tabla.</summary>
        List<RetencionPedidoDTO> LeerActivasSincrono(string empresa, IEnumerable<int> pedidos);

        /// <summary>Las vivas de la empresa con cliente y vendedor, de la más antigua a la más nueva.</summary>
        Task<List<PedidoRetenidoListadoDTO>> LeerListado(string empresa);

        /// <summary>Las vivas con fecha de revisión ya llegada (HastaFecha &lt;= hoy), de todas las empresas.</summary>
        Task<List<RetencionPedidoDTO>> LeerVencidas(DateTime hoy);

        /// <summary>Las vivas sin fecha de revisión («hasta nuevo aviso»).</summary>
        Task<List<RetencionPedidoDTO>> LeerSinFecha();

        /// <summary>Inserta y devuelve el Id.</summary>
        Task<int> Insertar(RetencionPedidoDTO retencion);

        /// <summary>Cierra la fila (solo si seguía viva). True si la ha cerrado ESTA llamada.</summary>
        Task<bool> Liberar(int id, DateTime cuando, string usuario, string motivo);

        Task MarcarRecordatorio(int id, DateTime cuando);

        /// <summary>
        /// Cambia CabPedidoVta.ModoServicio (solo esa columna: nada de Fecha Modificación, que haría saltar el «el pedido ha
        /// cambiado» de Nesto) y deja constancia en Modificaciones de quién y por qué.
        /// </summary>
        Task CambiarModoServicio(string empresa, int pedido, byte? modo, string usuario, string anterior, string nuevo);
    }

    /// <summary>
    /// NestoAPI#698: SQL a mano, fuera del EDMX, como PedidosPromesaRiesgo (#693): si el script no se ha lanzado, la API no se
    /// cae; Retener contesta que falta la tabla y el picking no retiene nada (el modo 5 tampoco está puesto). Cada operación
    /// abre su propio contexto.
    /// </summary>
    public class RepositorioPedidosRetenidos : IRepositorioPedidosRetenidos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.PedidosRetenidos', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string COLUMNAS = "r.Id, RTRIM(r.Empresa) AS Empresa, r.Pedido, r.Motivo, r.HastaFecha, r.ModoServicioAnterior, RTRIM(r.Usuario) AS Usuario, " +
            "r.Fecha, r.Liberado, RTRIM(r.UsuarioLiberacion) AS UsuarioLiberacion, r.MotivoLiberacion, r.UltimoRecordatorio";

        internal const string SQL_ACTIVA = "SELECT " + COLUMNAS + " FROM dbo.PedidosRetenidos r WITH (NOLOCK) " +
            "WHERE r.Empresa = @empresa AND r.Pedido = @pedido AND r.Liberado IS NULL ORDER BY r.Id DESC";

        internal const string SQL_ACTIVAS = "SELECT " + COLUMNAS + " FROM dbo.PedidosRetenidos r WITH (NOLOCK) " +
            "WHERE r.Empresa = @empresa AND r.Liberado IS NULL AND r.Pedido IN ({0}) ORDER BY r.Id";

        internal const string SQL_LISTADO = "SELECT " + COLUMNAS + ", RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(cl.Nombre) AS NombreCliente, RTRIM(c.Vendedor) AS Vendedor " +
            "FROM dbo.PedidosRetenidos r WITH (NOLOCK) " +
            "LEFT JOIN dbo.CabPedidoVta c WITH (NOLOCK) ON c.Empresa = r.Empresa AND c.Número = r.Pedido " +
            "LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto " +
            "WHERE r.Empresa = @empresa AND r.Liberado IS NULL ORDER BY r.Fecha, r.Id";

        internal const string SQL_VENCIDAS = "SELECT " + COLUMNAS + " FROM dbo.PedidosRetenidos r WITH (NOLOCK) " +
            "WHERE r.Liberado IS NULL AND r.HastaFecha IS NOT NULL AND r.HastaFecha <= @hoy ORDER BY r.Id";

        internal const string SQL_SIN_FECHA = "SELECT " + COLUMNAS + " FROM dbo.PedidosRetenidos r WITH (NOLOCK) " +
            "WHERE r.Liberado IS NULL AND r.HastaFecha IS NULL ORDER BY r.Id";

        internal const string SQL_INSERTAR = "INSERT INTO dbo.PedidosRetenidos (Empresa, Pedido, Motivo, HastaFecha, ModoServicioAnterior, Usuario, Fecha) " +
            "VALUES (@empresa, @pedido, @motivo, @hasta, @modoAnterior, @usuario, @fecha); SELECT CAST(SCOPE_IDENTITY() AS int)";

        internal const string SQL_LIBERAR = "UPDATE dbo.PedidosRetenidos SET Liberado = @cuando, UsuarioLiberacion = @usuario, MotivoLiberacion = @motivo " +
            "WHERE Id = @id AND Liberado IS NULL";

        internal const string SQL_RECORDATORIO = "UPDATE dbo.PedidosRetenidos SET UltimoRecordatorio = @cuando WHERE Id = @id";

        internal const string SQL_CABECERA = @"SELECT RTRIM(c.Empresa) AS Empresa, c.Número AS Pedido, c.ModoServicio, c.ServirJunto, c.NotaEntrega,
       RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(cl.Nombre) AS NombreCliente, RTRIM(c.Vendedor) AS Vendedor,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1)
                 THEN 1 ELSE 0 END AS bit) AS QuedaPendiente,
       (SELECT COUNT(*) FROM dbo.LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado = 1 AND l.Picking > 0) AS LineasEnPicking,
       STUFF((SELECT DISTINCT ', ' + CAST(l.Picking AS varchar(12)) FROM dbo.LinPedidoVta l WITH (NOLOCK)
              WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado = 1 AND l.Picking > 0 FOR XML PATH('')), 1, 2, '') AS PickingsEnCurso
FROM dbo.CabPedidoVta c WITH (NOLOCK)
LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
WHERE c.Empresa = @empresa AND c.Número = @pedido";

        internal const string SQL_CAMBIAR_MODO = "UPDATE dbo.CabPedidoVta SET ModoServicio = @modo WHERE Empresa = @empresa AND Número = @pedido";

        internal const string SQL_MODIFICACION = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES ('CabPedidoVta', @anterior, @nuevo, @usuario)";

        private static readonly object candado = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);

        private readonly Func<NVEntities> crearContexto;

        public RepositorioPedidosRetenidos(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        public bool TablaExiste()
        {
            lock (candado)
            {
                if (existe || DateTime.Now - ultimaComprobacion < cadaCuanto)
                {
                    return existe;
                }
                ultimaComprobacion = DateTime.Now;
            }
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(SQL_EXISTE).FirstOrDefault();
                }
            }
            catch
            {
                ahora = false;
            }
            lock (candado)
            {
                existe = ahora;
            }
            return ahora;
        }

        public async Task<CabeceraPedidoRetencion> LeerCabecera(string empresa, int pedido)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<CabeceraPedidoRetencion>(SQL_CABECERA, ParametroEmpresa(empresa), ParametroPedido(pedido))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<RetencionPedidoDTO> LeerActiva(string empresa, int pedido)
        {
            if (!TablaExiste())
            {
                return null;
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RetencionPedidoDTO>(SQL_ACTIVA, ParametroEmpresa(empresa), ParametroPedido(pedido))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public RetencionPedidoDTO LeerActivaSincrono(string empresa, int pedido)
        {
            if (!TablaExiste())
            {
                return null;
            }
            using (NVEntities db = crearContexto())
            {
                return db.Database.SqlQuery<RetencionPedidoDTO>(SQL_ACTIVA, ParametroEmpresa(empresa), ParametroPedido(pedido)).FirstOrDefault();
            }
        }

        public List<RetencionPedidoDTO> LeerActivasSincrono(string empresa, IEnumerable<int> pedidos)
        {
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any() || !TablaExiste())
            {
                return new List<RetencionPedidoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                // Enteros: van en el texto sin riesgo de inyección.
                return db.Database.SqlQuery<RetencionPedidoDTO>(string.Format(SQL_ACTIVAS, string.Join(", ", lista)), ParametroEmpresa(empresa)).ToList();
            }
        }

        public async Task<List<PedidoRetenidoListadoDTO>> LeerListado(string empresa)
        {
            if (!TablaExiste())
            {
                return new List<PedidoRetenidoListadoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<PedidoRetenidoListadoDTO>(SQL_LISTADO, ParametroEmpresa(empresa)).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<RetencionPedidoDTO>> LeerVencidas(DateTime hoy)
        {
            if (!TablaExiste())
            {
                return new List<RetencionPedidoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RetencionPedidoDTO>(SQL_VENCIDAS, new SqlParameter("@hoy", SqlDbType.DateTime) { Value = hoy.Date })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<RetencionPedidoDTO>> LeerSinFecha()
        {
            if (!TablaExiste())
            {
                return new List<RetencionPedidoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<RetencionPedidoDTO>(SQL_SIN_FECHA).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<int> Insertar(RetencionPedidoDTO retencion)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_INSERTAR,
                    ParametroEmpresa(retencion.Empresa),
                    ParametroPedido(retencion.Pedido),
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 500) { Value = retencion.Motivo ?? string.Empty },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = (object)retencion.HastaFecha ?? DBNull.Value },
                    new SqlParameter("@modoAnterior", SqlDbType.TinyInt) { Value = (object)retencion.ModoServicioAnterior ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = retencion.Usuario ?? string.Empty },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = retencion.Fecha })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> Liberar(int id, DateTime cuando, string usuario, string motivo)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_LIBERAR,
                    new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = usuario ?? string.Empty },
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 200) { Value = (object)motivo ?? DBNull.Value },
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task MarcarRecordatorio(int id, DateTime cuando)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_RECORDATORIO,
                    new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando },
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
            }
        }

        public async Task CambiarModoServicio(string empresa, int pedido, byte? modo, string usuario, string anterior, string nuevo)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_CAMBIAR_MODO,
                    new SqlParameter("@modo", SqlDbType.TinyInt) { Value = (object)modo ?? DBNull.Value },
                    ParametroEmpresa(empresa), ParametroPedido(pedido)).ConfigureAwait(false);
                // Auditoría en Modificaciones con el usuario de verdad. Con EF no se puede: en el EDMX Usuario es Computed y la
                // columna cae en su DEFAULT suser_sname() (la cuenta del servidor), que es justo lo que la issue #698 echa en cara
                // al historial de hoy. Fecha tiene DEFAULT getdate().
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_MODIFICACION,
                    new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                    new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }).ConfigureAwait(false);
            }
        }

        private static SqlParameter ParametroEmpresa(string empresa)
            => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = (string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim()) };

        private static SqlParameter ParametroPedido(int pedido) => new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido };
    }
}
