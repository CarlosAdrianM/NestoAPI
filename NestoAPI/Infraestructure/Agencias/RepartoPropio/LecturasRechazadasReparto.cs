using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>Una fila de dbo.RepartoLecturasRechazadas (Scripts/RepartoLecturasRechazadas.sql).</summary>
    public class LecturaRechazadaReparto
    {
        public int Id { get; set; }
        public DateTime FechaHora { get; set; }
        public string Usuario { get; set; }
        /// <summary>«Cargar», «GetBulto», «Entregar»… (la acción del controlador).</summary>
        public string Endpoint { get; set; }
        /// <summary>El código leído (o el EnvioId de la ruta, si no hay código).</summary>
        public string Codigo { get; set; }
        public int? EnvioId { get; set; }
        public int Status { get; set; }
        /// <summary>NO_CARGADA, EN_FURGONETA… (el Codigo del 409), si lo hay.</summary>
        public string CodigoError { get; set; }
        public string Mensaje { get; set; }
        public string Dispositivo { get; set; }
        public string Version { get; set; }
    }

    /// <summary>
    /// Carlos 20/11/26: hoy, cuando Ariadna lee un código y la API lo rechaza (404 «no existe el envío», 409…), no queda nada. LA REGLA,
    /// pura: toda respuesta 4xx de los endpoints del repartidor (<see cref="ENDPOINTS"/>) se apunta con lo leído, el estado, el código
    /// de error y el mensaje (500). Las 2xx, 5xx y los demás endpoints, no.
    /// </summary>
    public static class ReglaLecturasRechazadas
    {
        public const string TABLA = "RepartoLecturasRechazadas";
        public const int LONGITUD_MENSAJE = 500;
        public const int MAXIMO_FILAS = 500;

        /// <summary>Las acciones de RepartoPropioController que se apuntan (las de lectura y acción del repartidor).</summary>
        public static readonly IReadOnlyCollection<string> ENDPOINTS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GetBulto", "Cargar", "Entregar", "Incidencia", "Recoger", "Descargar", "NoRecogida"
        };

        public static bool SeApunta(string endpoint, int status) => status >= 400 && status < 500 && endpoint != null && ENDPOINTS.Contains(endpoint);

        /// <summary>Null si no se apunta.</summary>
        /// <param name="argumentos">Los de la acción (codigo, envioId, peticion…).</param>
        /// <param name="contenido">El cuerpo de la respuesta ({ Message, Codigo } o { error { code } }).</param>
        public static LecturaRechazadaReparto Construir(string endpoint, IDictionary<string, object> argumentos, int status, object contenido,
            string usuario, string dispositivo, string version, DateTime ahora)
        {
            if (!SeApunta(endpoint, status))
            {
                return null;
            }
            argumentos = argumentos ?? new Dictionary<string, object>();
            string codigo = argumentos.TryGetValue("codigo", out object c) ? c as string : null;
            object peticion = argumentos.TryGetValue("peticion", out object p) ? p : null;
            if (string.IsNullOrWhiteSpace(codigo) && peticion != null)
            {
                codigo = peticion.GetType().GetProperty("Codigo", BindingFlags.Public | BindingFlags.Instance)?.GetValue(peticion) as string;
            }
            int? envioId = argumentos.TryGetValue("envioId", out object e) && e is int n ? n : (int?)null;
            if (string.IsNullOrWhiteSpace(codigo) && envioId.HasValue)
            {
                codigo = envioId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            (string mensaje, string codigoError) = LeerCuerpo(contenido);
            return new LecturaRechazadaReparto
            {
                FechaHora = ahora,
                Usuario = Recortar(usuario, 50) ?? string.Empty,
                Endpoint = Recortar(endpoint, 50),
                Codigo = Recortar(codigo?.Trim(), 100),
                EnvioId = envioId,
                Status = status,
                CodigoError = Recortar(codigoError, 50),
                Mensaje = Recortar(mensaje, LONGITUD_MENSAJE),
                Dispositivo = Recortar(dispositivo, 100),
                Version = Recortar(version, 50)
            };
        }

        /// <summary>El Message y el Codigo (o error.code) del cuerpo, sea un objeto anónimo, un HttpError o un texto.</summary>
        public static (string Mensaje, string Codigo) LeerCuerpo(object contenido)
        {
            if (contenido == null)
            {
                return (null, null);
            }
            if (contenido is string texto)
            {
                return (texto, null);
            }
            try
            {
                JObject json = JObject.FromObject(contenido);
                string mensaje = (string)(json["Message"] ?? json["message"] ?? json["error"]?["message"]);
                string codigo = (string)(json["Codigo"] ?? json["error"]?["code"]);
                return (mensaje, codigo);
            }
            catch (Exception)
            {
                return (contenido.ToString(), null);
            }
        }

        internal static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>Guarda y lee las lecturas rechazadas. Sustituible en los tests.</summary>
    public interface IRepositorioLecturasRechazadas
    {
        bool ScriptAplicado();
        Task Guardar(LecturaRechazadaReparto lectura);
        Task<List<LecturaRechazadaReparto>> Leer(DateTime desde, string usuario, int maximo);
    }

    public class RepositorioLecturasRechazadasSql : IRepositorioLecturasRechazadas
    {
        private const string COLUMNAS = "Id, FechaHora, RTRIM(Usuario) AS Usuario, RTRIM(Endpoint) AS Endpoint, Codigo, EnvioId, Status, CodigoError, Mensaje, Dispositivo, Version";

        private readonly NVEntities db;

        public RepositorioLecturasRechazadasSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, ReglaLecturasRechazadas.TABLA);

        public Task Guardar(LecturaRechazadaReparto l)
            => db.Database.ExecuteSqlCommandAsync(
                $"INSERT INTO dbo.{ReglaLecturasRechazadas.TABLA} (FechaHora, Usuario, Endpoint, Codigo, EnvioId, Status, CodigoError, Mensaje, Dispositivo, Version) " +
                "VALUES (@FechaHora, @Usuario, @Endpoint, @Codigo, @EnvioId, @Status, @CodigoError, @Mensaje, @Dispositivo, @Version)",
                new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = l.FechaHora },
                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = l.Usuario ?? string.Empty },
                new SqlParameter("@Endpoint", SqlDbType.VarChar, 50) { Value = l.Endpoint ?? string.Empty },
                Texto("@Codigo", l.Codigo, 100),
                new SqlParameter("@EnvioId", SqlDbType.Int) { Value = (object)l.EnvioId ?? DBNull.Value },
                new SqlParameter("@Status", SqlDbType.SmallInt) { Value = (short)l.Status },
                Texto("@CodigoError", l.CodigoError, 50),
                Texto("@Mensaje", l.Mensaje, ReglaLecturasRechazadas.LONGITUD_MENSAJE),
                Texto("@Dispositivo", l.Dispositivo, 100),
                Texto("@Version", l.Version, 50));

        public Task<List<LecturaRechazadaReparto>> Leer(DateTime desde, string usuario, int maximo)
            => db.Database.SqlQuery<LecturaRechazadaReparto>(
                $"SELECT TOP (@maximo) {COLUMNAS} FROM dbo.{ReglaLecturasRechazadas.TABLA} " +
                "WHERE FechaHora >= @desde AND (@usuario IS NULL OR Usuario = @usuario OR Usuario LIKE '%\\' + @usuario) " +
                "ORDER BY FechaHora DESC, Id DESC",
                new SqlParameter("@maximo", SqlDbType.Int) { Value = maximo },
                new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = string.IsNullOrWhiteSpace(usuario) ? (object)DBNull.Value : usuario.Trim() })
                .ToListAsync();

        private static SqlParameter Texto(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.NVarChar, largo) { Value = (object)valor ?? DBNull.Value };
    }

    /// <summary>
    /// Apunta en segundo plano, sin romper ni retrasar la respuesta. Sin la tabla, nada (ELMAH una vez). Nunca lanza.
    /// </summary>
    public static class ApuntadorLecturasRechazadas
    {
        private static int avisadoSinTabla;

        /// <summary>Tests: sustituye el repositorio (con su propio contexto en producción).</summary>
        internal static Func<IRepositorioLecturasRechazadas> Repositorio { get; set; }

        /// <summary>Tests: sustituye el «en segundo plano» (en producción, Task.Run).</summary>
        internal static Action<Func<Task>> EnSegundoPlano { get; set; }

        public static void Apuntar(LecturaRechazadaReparto lectura)
        {
            if (lectura == null)
            {
                return;
            }
            try
            {
                Func<Task> trabajo = () => Guardar(lectura);
                if (EnSegundoPlano != null)
                {
                    EnSegundoPlano(trabajo);
                }
                else
                {
                    _ = Task.Run(trabajo);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido apuntar la lectura rechazada de {lectura.Usuario}: {ex.Message}", ex));
            }
        }

        internal static async Task Guardar(LecturaRechazadaReparto lectura)
        {
            NVEntities db = null;
            try
            {
                IRepositorioLecturasRechazadas repositorio;
                if (Repositorio != null)
                {
                    repositorio = Repositorio();
                }
                else
                {
                    db = new NVEntities();
                    repositorio = new RepositorioLecturasRechazadasSql(db);
                }
                if (!repositorio.ScriptAplicado())
                {
                    if (Interlocked.Exchange(ref avisadoSinTabla, 1) == 0)
                    {
                        ElmahHelper.Log(new Exception("[Reparto] Falta Scripts/RepartoLecturasRechazadas.sql: las lecturas rechazadas del reparto no se guardan."));
                    }
                    return;
                }
                await repositorio.Guardar(lectura).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido apuntar la lectura rechazada de {lectura.Usuario} ({lectura.Endpoint} {lectura.Codigo}): {ex.Message}", ex));
            }
            finally
            {
                db?.Dispose();
            }
        }

        internal static void ReiniciarAvisoParaTests() => Interlocked.Exchange(ref avisadoSinTabla, 0);
    }

    /// <summary>
    /// El punto ÚNICO (en RepartoPropioController): tras cada acción del repartidor, si la respuesta es 4xx, se apunta
    /// (<see cref="ReglaLecturasRechazadas"/>, <see cref="ApuntadorLecturasRechazadas"/>).
    /// </summary>
    public class ApuntarLecturasRechazadasAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(HttpActionExecutedContext contexto)
        {
            try
            {
                HttpResponseMessage respuesta = contexto?.Response;
                string endpoint = contexto?.ActionContext?.ActionDescriptor?.ActionName;
                if (respuesta == null || !ReglaLecturasRechazadas.SeApunta(endpoint, (int)respuesta.StatusCode))
                {
                    return;
                }
                object contenido = respuesta.Content is ObjectContent objeto ? objeto.Value : null;
                HttpRequestMessage peticion = contexto.Request;
                string usuario = UsuarioAuditoriaHelper.Resolver(contexto.ActionContext.RequestContext?.Principal, "NestoAPI");
                LecturaRechazadaReparto lectura = ReglaLecturasRechazadas.Construir(endpoint, contexto.ActionContext.ActionArguments, (int)respuesta.StatusCode,
                    contenido, usuario, Cabecera(peticion, Notificaciones.AvisosAlmacen.CABECERA_DISPOSITIVO),
                    Cabecera(peticion, Ariadna.PoliticaVersionMinimaAriadna.CABECERA_VERSION), DateTime.Now);
                ApuntadorLecturasRechazadas.Apuntar(lectura);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido preparar la lectura rechazada: {ex.Message}", ex));
            }
        }

        private static string Cabecera(HttpRequestMessage peticion, string nombre)
            => peticion != null && peticion.Headers.TryGetValues(nombre, out IEnumerable<string> valores)
                ? valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim()
                : null;
    }
}
