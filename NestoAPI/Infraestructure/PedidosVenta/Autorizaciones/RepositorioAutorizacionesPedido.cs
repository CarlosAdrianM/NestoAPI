using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.Autorizaciones
{
    /// <summary>Novedades 1258: dbo.PedidosAutorizacionesSolicitadas (fuera del EDMX, Scripts/PedidosAutorizacionesSolicitadas.sql).</summary>
    public interface IRepositorioAutorizacionesPedido
    {
        bool TablaExiste();
        Task<int> Insertar(FilaAutorizacionPedido fila);
        /// <summary>La Pendiente del mismo solicitante con la misma huella desde <paramref name="desde"/>, o null.</summary>
        Task<int?> PendienteIdentica(string solicitante, string huella, DateTime desde);
        /// <summary>Con el JSON del pedido y el nombre del cliente.</summary>
        Task<FilaAutorizacionPedido> Leer(int id);
        /// <summary>Sin el JSON, con el nombre del cliente, desde <paramref name="desde"/>; la más reciente primero.</summary>
        Task<List<FilaAutorizacionPedido>> Listado(DateTime desde, bool soloPendientes);
        /// <summary>Pasa de Pendiente a <paramref name="estado"/> SOLO si sigue pendiente. True si la ha cogido ESTA llamada.</summary>
        Task<bool> Resolver(int id, string estado, string usuario, DateTime cuando, string motivo, int? pedidoCreado);
        /// <summary>Bloqueo exclusivo (sp_getapplock de sesión) mientras se crea su pedido; null si otro lo tiene.</summary>
        IDisposable BloquearParaAutorizar(int id);
        /// <summary>Fila en Modificaciones (Tabla «PedidoAutorizado»). Nunca lanza.</summary>
        Task Rastro(string tabla, string anterior, string nuevo, string usuario);
    }

    /// <summary>Novedades 1258: a quién avisar de una solicitud (sustituible en los tests).</summary>
    public interface IDestinatariosAutorizacionPedido
    {
        /// <summary>Usuarios con dominio que pueden autorizar ese pedido (<see cref="ReglasAutorizacionPedido.Destinatarios"/>). Nunca lanza.</summary>
        Task<List<string>> Leer(string empresa, IReadOnlyCollection<string> almacenes, string permisoNecesario, string solicitante);
    }

    public class RepositorioAutorizacionesPedido : IRepositorioAutorizacionesPedido
    {
        public const string TABLA = "PedidosAutorizacionesSolicitadas";
        public const string TABLA_RASTRO = "PedidoAutorizado";

        internal const string COLUMNAS = "a.Id, RTRIM(a.Empresa) AS Empresa, RTRIM(a.Cliente) AS Cliente, RTRIM(a.Contacto) AS Contacto, " +
            "RTRIM(a.Solicitante) AS Solicitante, a.FechaSolicitud, a.Motivos, a.Comentario, a.Estado, RTRIM(a.UsuarioResolucion) AS UsuarioResolucion, " +
            "a.FechaResolucion, a.MotivoResolucion, a.PedidoCreado, a.Huella, a.PermisoNecesario, a.Importe, a.NumeroLineas, RTRIM(a.Vendedor) AS Vendedor, " +
            "a.Almacenes, RTRIM(cl.Nombre) AS NombreCliente";

        internal const string JOIN_CLIENTE = " LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = a.Empresa AND cl.[Nº Cliente] = a.Cliente AND cl.Contacto = a.Contacto";

        internal const string SQL_INSERTAR = "INSERT INTO dbo." + TABLA + " (Empresa, Cliente, Contacto, Solicitante, FechaSolicitud, PedidoJson, Motivos, " +
            "Comentario, Estado, Huella, PermisoNecesario, Importe, NumeroLineas, Vendedor, Almacenes) VALUES (@empresa, @cliente, @contacto, @solicitante, " +
            "@fecha, @json, @motivos, @comentario, 'Pendiente', @huella, @permiso, @importe, @lineas, @vendedor, @almacenes); SELECT CAST(SCOPE_IDENTITY() AS int)";

        internal const string SQL_IDENTICA = "SELECT TOP 1 Id FROM dbo." + TABLA + " WHERE Estado = 'Pendiente' AND Solicitante = @solicitante " +
            "AND Huella = @huella AND FechaSolicitud >= @desde ORDER BY Id DESC";

        internal const string SQL_LEER = "SELECT " + COLUMNAS + ", a.PedidoJson FROM dbo." + TABLA + " a" + JOIN_CLIENTE + " WHERE a.Id = @id";

        internal const string SQL_LISTADO = "SELECT " + COLUMNAS + ", CAST(NULL AS nvarchar(max)) AS PedidoJson FROM dbo." + TABLA + " a WITH (NOLOCK)" +
            JOIN_CLIENTE + " WHERE a.FechaSolicitud >= @desde AND (@soloPendientes = 0 OR a.Estado = 'Pendiente') ORDER BY a.FechaSolicitud DESC, a.Id DESC";

        internal const string SQL_RESOLVER = "UPDATE dbo." + TABLA + " SET Estado = @estado, UsuarioResolucion = @usuario, FechaResolucion = @cuando, " +
            "MotivoResolucion = @motivo, PedidoCreado = @pedido WHERE Id = @id AND Estado = 'Pendiente'";

        internal const string SQL_BLOQUEAR = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', " +
            "@LockOwner = 'Session', @LockTimeout = 0; SELECT @r";

        internal const string SQL_SOLTAR = "EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session'";

        internal const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@tabla, @anterior, @nuevo, @usuario)";

        private static readonly object candado = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        private static bool avisadoSinTabla;
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);

        private readonly Func<NVEntities> crearContexto;

        public RepositorioAutorizacionesPedido(Func<NVEntities> crearContexto = null)
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
                    ahora = db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID(N'dbo." + TABLA + "', N'U') IS NULL THEN 0 ELSE 1 END").FirstOrDefault() == 1;
                }
            }
            catch
            {
                ahora = false;
            }
            bool avisar = false;
            lock (candado)
            {
                existe = ahora;
                if (!ahora && !avisadoSinTabla)
                {
                    avisadoSinTabla = true;
                    avisar = true;
                }
            }
            if (avisar)
            {
                ElmahHelper.Log(new Exception("[Autorizaciones de pedidos, Novedades 1258] Falta la tabla dbo." + TABLA +
                    " (Scripts/PedidosAutorizacionesSolicitadas.sql): los endpoints de api/PedidosVenta/Autorizaciones dan 503."));
            }
            return ahora;
        }

        public async Task<int> Insertar(FilaAutorizacionPedido f)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_INSERTAR,
                    Char("@empresa", f.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO, 3),
                    Texto("@cliente", f.Cliente, 10, SqlDbType.VarChar),
                    Texto("@contacto", f.Contacto, 3, SqlDbType.VarChar),
                    Texto("@solicitante", f.Solicitante ?? string.Empty, 50, SqlDbType.VarChar),
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = f.FechaSolicitud },
                    new SqlParameter("@json", SqlDbType.NVarChar, -1) { Value = (object)f.PedidoJson ?? DBNull.Value },
                    new SqlParameter("@motivos", SqlDbType.NVarChar, -1) { Value = (object)f.Motivos ?? DBNull.Value },
                    Texto("@comentario", f.Comentario, ReglasAutorizacionPedido.LARGO_TEXTO, SqlDbType.NVarChar),
                    Texto("@huella", f.Huella, 64, SqlDbType.VarChar),
                    Texto("@permiso", f.PermisoNecesario, 100, SqlDbType.VarChar),
                    new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = f.Importe },
                    new SqlParameter("@lineas", SqlDbType.Int) { Value = f.NumeroLineas },
                    Texto("@vendedor", f.Vendedor, 3, SqlDbType.VarChar),
                    Texto("@almacenes", f.Almacenes, 100, SqlDbType.VarChar))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<int?> PendienteIdentica(string solicitante, string huella, DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                List<int> ids = await db.Database.SqlQuery<int>(SQL_IDENTICA,
                    Texto("@solicitante", solicitante ?? string.Empty, 50, SqlDbType.VarChar),
                    Texto("@huella", huella, 64, SqlDbType.VarChar),
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde })
                    .ToListAsync().ConfigureAwait(false);
                return ids.Count > 0 ? ids[0] : (int?)null;
            }
        }

        public async Task<FilaAutorizacionPedido> Leer(int id)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<FilaAutorizacionPedido>(SQL_LEER, new SqlParameter("@id", SqlDbType.Int) { Value = id })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<FilaAutorizacionPedido>> Listado(DateTime desde, bool soloPendientes)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<FilaAutorizacionPedido>(SQL_LISTADO,
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@soloPendientes", SqlDbType.Bit) { Value = soloPendientes })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> Resolver(int id, string estado, string usuario, DateTime cuando, string motivo, int? pedidoCreado)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_RESOLVER,
                    Texto("@estado", estado, 12, SqlDbType.VarChar),
                    Texto("@usuario", usuario, 50, SqlDbType.VarChar),
                    new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando },
                    Texto("@motivo", motivo, ReglasAutorizacionPedido.LARGO_TEXTO, SqlDbType.NVarChar),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = (object)pedidoCreado ?? DBNull.Value },
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public IDisposable BloquearParaAutorizar(int id)
        {
            NVEntities db = crearContexto();
            try
            {
                // La conexión se abre a mano para que EF no la devuelva al pool a mitad (el bloqueo es de la sesión).
                db.Database.Connection.Open();
                string recurso = "PedidoAutorizacion:" + id;
                int resultado = db.Database.SqlQuery<int>(SQL_BLOQUEAR, new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = recurso }).First();
                if (resultado < 0)
                {
                    db.Dispose();
                    return null;
                }
                return new Bloqueo(db, recurso);
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        public async Task Rastro(string tabla, string anterior, string nuevo, string usuario)
        {
            try
            {
                using (NVEntities db = crearContexto())
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@tabla", SqlDbType.NVarChar, 50) { Value = tabla },
                        new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)UsuarioAuditoriaHelper.ParaAuditoria(usuario) ?? DBNull.Value })
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se ha podido dejar el rastro «{tabla}»: {ex.Message}", ex));
            }
        }

        private sealed class Bloqueo : IDisposable
        {
            private NVEntities db;
            private readonly string recurso;

            public Bloqueo(NVEntities db, string recurso)
            {
                this.db = db;
                this.recurso = recurso;
            }

            public void Dispose()
            {
                if (db == null)
                {
                    return;
                }
                try
                {
                    _ = db.Database.ExecuteSqlCommand(SQL_SOLTAR, new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = recurso });
                }
                catch
                {
                    // Al cerrar la conexión la sesión suelta el bloqueo igualmente
                }
                db.Dispose();
                db = null;
            }
        }

        private static SqlParameter Char(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.Char, largo) { Value = string.IsNullOrWhiteSpace(valor) ? (object)DBNull.Value : valor.Trim() };

        internal static SqlParameter Texto(string nombre, string valor, int largo, SqlDbType tipo)
        {
            string limpio = valor?.Trim();
            if (limpio != null && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            return new SqlParameter(nombre, tipo, largo) { Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio };
        }
    }

    /// <summary>
    /// Novedades 1258: los destinatarios de producción: los grupos del dominio (Dirección, Almacén, Tiendas) y las filas PROPIAS de
    /// ParametrosUsuario de esa empresa (crear con errores, AlmacénPedidoVta y, si lo hay, el permiso propio), en UNA consulta.
    /// </summary>
    public class DestinatariosAutorizacionPedido : IDestinatariosAutorizacionPedido
    {
        internal const string SQL_PARAMETROS = "SELECT RTRIM(Usuario) AS Usuario, RTRIM(Clave) AS Clave, RTRIM(Valor) AS Valor FROM dbo.ParametrosUsuario WITH (NOLOCK) " +
            "WHERE Empresa = @empresa AND Usuario <> '(defecto)' AND Clave IN (@clave1, @clave2, @clave3)";

        private readonly Func<string, List<string>> miembrosGrupo;

        public DestinatariosAutorizacionPedido(Func<string, List<string>> miembrosGrupo = null)
        {
            this.miembrosGrupo = miembrosGrupo ?? MiembrosGrupoDominio.Leer;
        }

        public async Task<List<string>> Leer(string empresa, IReadOnlyCollection<string> almacenes, string permisoNecesario, string solicitante)
        {
            try
            {
                List<ParametroDestinatarioAutorizacion> filas;
                using (var db = new NVEntities())
                {
                    filas = await db.Database.SqlQuery<ParametroDestinatarioAutorizacion>(SQL_PARAMETROS,
                        new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() },
                        new SqlParameter("@clave1", SqlDbType.VarChar, 50) { Value = Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES },
                        new SqlParameter("@clave2", SqlDbType.VarChar, 50) { Value = ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN },
                        new SqlParameter("@clave3", SqlDbType.VarChar, 50) { Value = string.IsNullOrWhiteSpace(permisoNecesario) ? (object)DBNull.Value : permisoNecesario.Trim() })
                        .ToListAsync().ConfigureAwait(false);
                }
                return ReglasAutorizacionPedido.Destinatarios(
                    miembrosGrupo(Constantes.GruposSeguridad.DIRECCION),
                    miembrosGrupo(Constantes.GruposSeguridad.ALMACEN),
                    miembrosGrupo(Constantes.GruposSeguridad.TIENDAS),
                    filas, almacenes, permisoNecesario, solicitante);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] No se han podido leer los que pueden autorizar: {ex.Message}", ex));
                return new List<string>();
            }
        }
    }
}
