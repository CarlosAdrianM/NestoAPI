using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Caso 931007: los estados de una solicitud a Almacén (columna Estado de dbo.AmpliacionesPedidoEnPreparacion).</summary>
    public static class EstadosAmpliacionPedido
    {
        public const string PENDIENTE = "Pendiente";
        /// <summary>Almacén ha dicho que sí y se ha sacado el picking de lo añadido.</summary>
        public const string ENTRA = "Entra";
        /// <summary>Almacén ha dicho que no (o el picking no se ha podido sacar): lo añadido pasa a un pedido nuevo.</summary>
        public const string NO_ENTRA = "NoEntra";
        /// <summary>Nadie ha contestado en <see cref="AmpliacionPedidoEnPreparacion.ESPERA_RESPUESTA_ALMACEN"/>: lo añadido pasa a un pedido nuevo.</summary>
        public const string SIN_RESPUESTA = "SinRespuesta";
    }

    /// <summary>Caso 931007: una solicitud a Almacén de meter en el envío de hoy lo añadido a un pedido en preparación.</summary>
    public class AmpliacionPedidoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Los Nº de orden de las líneas añadidas, separados por comas.</summary>
        public string Lineas { get; set; }
        /// <summary>«39284 (1 ud.)…», para los avisos.</summary>
        public string Productos { get; set; }
        public string Pickings { get; set; }
        public int? Envio { get; set; }
        /// <summary>Quien lo pidió (el usuario del PUT).</summary>
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public string Estado { get; set; }
        public DateTime? Respondida { get; set; }
        public string UsuarioRespuesta { get; set; }
        public int? PickingNuevo { get; set; }
        public int? PedidoNuevo { get; set; }
        public string Motivo { get; set; }
        /// <summary>Solo en el listado: el cliente y su nombre.</summary>
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }

        public List<int> NumerosOrden() => LeerEnteros(Lineas);

        public List<int> NumerosPicking() => LeerEnteros(Pickings);

        internal static List<int> LeerEnteros(string texto)
        {
            return (texto ?? string.Empty).Split(',')
                .Select(t => int.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0)
                .Where(n => n > 0).Distinct().ToList();
        }

        internal static string Enteros(IEnumerable<int> numeros)
            => string.Join(",", (numeros ?? Enumerable.Empty<int>()).Where(n => n > 0).Distinct().Select(n => n.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Caso 931007: lo que se lee del pedido para decidir (<see cref="AmpliacionPedidoEnPreparacion.Evaluar"/>). Sustituible en los tests.</summary>
    public interface ILectorAmpliacionPedido
    {
        /// <summary>Las líneas guardadas del pedido (sin seguimiento) y si es una nota de entrega.</summary>
        Task<List<LinPedidoVta>> LeerLineas(string empresa, int pedido);

        /// <summary>Los envíos del pedido con sus pickings (incidencia 1328), su reembolso y si están cargados en la furgoneta.</summary>
        Task<List<EnvioAmpliacionPedido>> LeerEnvios(string empresa, int pedido);

        /// <summary>Por qué hoy no sale (retenido, aplazado, retenido por falta) o null: <see cref="GuardiaPedidoRetenido"/>.</summary>
        Task<string> MotivoNoSaleHoy(string empresa, int pedido);
    }

    /// <summary>Caso 931007: la tabla dbo.AmpliacionesPedidoEnPreparacion (fuera del EDMX, Scripts/AmpliacionesPedidoEnPreparacion.sql).</summary>
    public interface IRepositorioAmpliacionesPedido
    {
        bool TablaExiste();
        Task<int> Insertar(AmpliacionPedidoDTO solicitud);
        Task<AmpliacionPedidoDTO> Leer(int id);
        /// <summary>Las pendientes (con <paramref name="soloPendientes"/>) o también las contestadas de los últimos días, con el cliente.</summary>
        Task<List<AmpliacionPedidoDTO>> Listado(bool soloPendientes);
        /// <summary>Las pendientes de un pedido (para el correo).</summary>
        Task<List<AmpliacionPedidoDTO>> PendientesDelPedido(string empresa, int pedido);
        /// <summary>Pasa de Pendiente a <paramref name="estado"/> SOLO si sigue pendiente. True si la ha cogido ESTA llamada.</summary>
        Task<bool> Reclamar(int id, string estado, string usuario, DateTime cuando);
        /// <summary>Apunta el desenlace (estado final, picking o pedido nuevo y el motivo).</summary>
        Task Cerrar(int id, string estado, int? pickingNuevo, int? pedidoNuevo, string motivo);
    }

    public class RepositorioAmpliacionesPedido : IRepositorioAmpliacionesPedido
    {
        public const string TABLA = "AmpliacionesPedidoEnPreparacion";
        public const int DIAS_CONTESTADAS = 7;

        internal const string COLUMNAS = "a.Id, RTRIM(a.Empresa) AS Empresa, a.Pedido, a.Lineas, a.Productos, a.Pickings, a.Envio, RTRIM(a.Usuario) AS Usuario, " +
            "a.Fecha, a.Estado, a.Respondida, RTRIM(a.UsuarioRespuesta) AS UsuarioRespuesta, a.PickingNuevo, a.PedidoNuevo, a.Motivo";

        internal const string SQL_INSERTAR = "INSERT INTO dbo." + TABLA + " (Empresa, Pedido, Lineas, Productos, Pickings, Envio, Usuario, Fecha, Estado) " +
            "VALUES (@empresa, @pedido, @lineas, @productos, @pickings, @envio, @usuario, @fecha, 'Pendiente'); SELECT CAST(SCOPE_IDENTITY() AS int)";

        internal const string SQL_LEER = "SELECT " + COLUMNAS + ", CAST(NULL AS varchar(10)) AS Cliente, CAST(NULL AS varchar(3)) AS Contacto, " +
            "CAST(NULL AS nvarchar(100)) AS NombreCliente FROM dbo." + TABLA + " a WHERE a.Id = @id";

        internal const string SQL_LISTADO = "SELECT " + COLUMNAS + ", RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, RTRIM(cl.Nombre) AS NombreCliente " +
            "FROM dbo." + TABLA + " a WITH (NOLOCK) " +
            "LEFT JOIN dbo.CabPedidoVta c WITH (NOLOCK) ON c.Empresa = a.Empresa AND c.Número = a.Pedido " +
            "LEFT JOIN dbo.Clientes cl WITH (NOLOCK) ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto " +
            "WHERE a.Estado = 'Pendiente' OR (@soloPendientes = 0 AND a.Fecha >= @desde) " +
            "ORDER BY CASE WHEN a.Estado = 'Pendiente' THEN 0 ELSE 1 END, a.Fecha, a.Id";

        internal const string SQL_PENDIENTES_PEDIDO = "SELECT " + COLUMNAS + ", CAST(NULL AS varchar(10)) AS Cliente, CAST(NULL AS varchar(3)) AS Contacto, " +
            "CAST(NULL AS nvarchar(100)) AS NombreCliente FROM dbo." + TABLA + " a WITH (NOLOCK) " +
            "WHERE a.Empresa = @empresa AND a.Pedido = @pedido AND a.Estado = 'Pendiente' ORDER BY a.Id";

        internal const string SQL_RECLAMAR = "UPDATE dbo." + TABLA + " SET Estado = @estado, Respondida = @cuando, UsuarioRespuesta = @usuario " +
            "WHERE Id = @id AND Estado = 'Pendiente'";

        internal const string SQL_CERRAR = "UPDATE dbo." + TABLA + " SET Estado = @estado, PickingNuevo = @picking, PedidoNuevo = @pedidoNuevo, Motivo = @motivo " +
            "WHERE Id = @id";

        private static readonly object candado = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);

        private readonly Func<NVEntities> crearContexto;

        public RepositorioAmpliacionesPedido(Func<NVEntities> crearContexto = null)
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
            lock (candado)
            {
                existe = ahora;
            }
            return ahora;
        }

        public async Task<int> Insertar(AmpliacionPedidoDTO solicitud)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<int>(SQL_INSERTAR,
                    Empresa(solicitud.Empresa),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = solicitud.Pedido },
                    Texto("@lineas", solicitud.Lineas, 200, SqlDbType.VarChar),
                    Texto("@productos", solicitud.Productos, 500, SqlDbType.NVarChar),
                    Texto("@pickings", solicitud.Pickings, 100, SqlDbType.VarChar),
                    new SqlParameter("@envio", SqlDbType.Int) { Value = (object)solicitud.Envio ?? DBNull.Value },
                    Texto("@usuario", solicitud.Usuario ?? string.Empty, 50, SqlDbType.VarChar),
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = solicitud.Fecha })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<AmpliacionPedidoDTO> Leer(int id)
        {
            if (!TablaExiste())
            {
                return null;
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<AmpliacionPedidoDTO>(SQL_LEER, new SqlParameter("@id", SqlDbType.Int) { Value = id })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<AmpliacionPedidoDTO>> Listado(bool soloPendientes)
        {
            if (!TablaExiste())
            {
                return new List<AmpliacionPedidoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<AmpliacionPedidoDTO>(SQL_LISTADO,
                    new SqlParameter("@soloPendientes", SqlDbType.Bit) { Value = soloPendientes },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = DateTime.Today.AddDays(-DIAS_CONTESTADAS) })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<AmpliacionPedidoDTO>> PendientesDelPedido(string empresa, int pedido)
        {
            if (!TablaExiste())
            {
                return new List<AmpliacionPedidoDTO>();
            }
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<AmpliacionPedidoDTO>(SQL_PENDIENTES_PEDIDO, Empresa(empresa),
                    new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> Reclamar(int id, string estado, string usuario, DateTime cuando)
        {
            using (NVEntities db = crearContexto())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_RECLAMAR,
                    Texto("@estado", estado, 12, SqlDbType.VarChar),
                    new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando },
                    Texto("@usuario", usuario, 50, SqlDbType.VarChar),
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
                return filas > 0;
            }
        }

        public async Task Cerrar(int id, string estado, int? pickingNuevo, int? pedidoNuevo, string motivo)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                    Texto("@estado", estado, 12, SqlDbType.VarChar),
                    new SqlParameter("@picking", SqlDbType.Int) { Value = (object)pickingNuevo ?? DBNull.Value },
                    new SqlParameter("@pedidoNuevo", SqlDbType.Int) { Value = (object)pedidoNuevo ?? DBNull.Value },
                    Texto("@motivo", motivo, 500, SqlDbType.NVarChar),
                    new SqlParameter("@id", SqlDbType.Int) { Value = id }).ConfigureAwait(false);
            }
        }

        private static SqlParameter Empresa(string empresa)
            => new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() };

        private static SqlParameter Texto(string nombre, string valor, int largo, SqlDbType tipo)
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
    /// Caso 931007: el lector de producción. Usa el contexto del PUT (dentro del TransactionScope de UnirPedidos se alista en él, como el
    /// resto del PUT). Las tablas del reparto y de los pickings del envío, solo si existen (sin ellas, «sin datos», como en sus reglas).
    /// </summary>
    public class LectorAmpliacionPedido : ILectorAmpliacionPedido
    {
        private readonly NVEntities db;

        public LectorAmpliacionPedido(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<List<LinPedidoVta>> LeerLineas(string empresa, int pedido)
        {
            return db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresa && l.Número == pedido)
                .ToListAsync();
        }

        public async Task<List<EnvioAmpliacionPedido>> LeerEnvios(string empresa, int pedido)
        {
            List<EnvioAmpliacionPedido> envios = await db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Empresa == empresa && e.Pedido == pedido)
                .Select(e => new EnvioAmpliacionPedido { Numero = e.Numero, Estado = e.Estado, Agencia = e.Agencia, Reembolso = e.Reembolso })
                .ToListAsync().ConfigureAwait(false);
            if (envios.Count == 0)
            {
                return envios;
            }
            try
            {
                Dictionary<int, List<int>> pickings = await new RepositorioPickingsEnvio(db).LeerAsync(envios.Select(e => e.Numero)).ConfigureAwait(false);
                foreach (EnvioAmpliacionPedido envio in envios)
                {
                    envio.Pickings = pickings != null && pickings.TryGetValue(envio.Numero, out List<int> suyos) ? suyos : null;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] Pedido {pedido}: no se han podido leer los pickings de sus envíos: {ex.Message}", ex));
            }
            foreach (EnvioAmpliacionPedido envio in envios.Where(e => e.EsRepartoPropioPorEntregar))
            {
                envio.CargadoEnFurgoneta = await Hay(RepositorioCargaFurgoneta.TABLA, envio.Numero).ConfigureAwait(false)
                    || await Hay(RepositorioFurgoneta.TABLA, envio.Numero).ConfigureAwait(false);
            }
            return envios;
        }

        public Task<string> MotivoNoSaleHoy(string empresa, int pedido) => GuardiaPedidoRetenido.Motivo(empresa, pedido);

        private async Task<bool> Hay(string tabla, int envio)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, tabla))
            {
                return false;
            }
            List<int> filas = await db.Database.SqlQuery<int>($"SELECT TOP 1 1 FROM dbo.{tabla} WHERE EnvioId = @p0", envio)
                .ToListAsync().ConfigureAwait(false);
            return filas.Count > 0;
        }
    }
}
