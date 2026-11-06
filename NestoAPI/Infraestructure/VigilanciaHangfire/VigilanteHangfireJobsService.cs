using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.VigilanciaHangfire
{
    /// <summary>NestoAPI#820: lo que el vigilante lee de las tablas de Hangfire.</summary>
    public interface IRepositorioVigilanciaHangfire
    {
        Task<List<JobEnCurso>> LeerEnCurso();
        Task<int> ContarEncolados();
    }

    /// <summary>
    /// NestoAPI#820: SQL directo (sin EF) a las tablas de Hangfire (esquema HangFire de NestoConnection). Solo lectura, NOLOCK y
    /// 30 s de timeout: el vigilante no debe bloquear a Hangfire ni quedarse colgado él.
    /// </summary>
    public class RepositorioVigilanciaHangfire : IRepositorioVigilanciaHangfire
    {
        // El estado Processing guarda en CreatedAt cuándo lo cogió el worker. Mientras se procesa, SqlServerStorage (con
        // SlidingInvisibilityTimeout) mantiene su fila en JobQueue con FetchedAt: de ahí sale la cola.
        internal const string SQL_EN_CURSO = @"
SELECT j.Id, j.InvocationData, s.CreatedAt AS EmpezoUtc, q.Queue
FROM HangFire.Job j WITH (NOLOCK)
JOIN HangFire.State s WITH (NOLOCK) ON s.Id = j.StateId AND s.JobId = j.Id
LEFT JOIN HangFire.JobQueue q WITH (NOLOCK) ON q.JobId = j.Id
WHERE j.StateName = 'Processing'";

        internal const string SQL_ENCOLADOS = "SELECT COUNT(*) FROM HangFire.JobQueue WITH (NOLOCK) WHERE FetchedAt IS NULL";

        private readonly string cadenaConexion;

        public RepositorioVigilanciaHangfire() : this(ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString)
        {
        }

        public RepositorioVigilanciaHangfire(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        public async Task<List<JobEnCurso>> LeerEnCurso()
        {
            var jobs = new List<JobEnCurso>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_EN_CURSO, conexion) { CommandTimeout = 30 })
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        jobs.Add(new JobEnCurso
                        {
                            Id = Convert.ToInt64(lector["Id"]),
                            Nombre = NombreDesdeInvocationData(lector["InvocationData"] as string),
                            EmpezoUtc = DateTime.SpecifyKind((DateTime)lector["EmpezoUtc"], DateTimeKind.Utc),
                            Cola = lector["Queue"] as string
                        });
                    }
                }
            }
            return jobs;
        }

        public async Task<int> ContarEncolados()
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_ENCOLADOS, conexion) { CommandTimeout = 30 })
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                return Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false));
            }
        }

        private static readonly Regex TIPO = new Regex("\"t\"\\s*:\\s*\"([^\",]+)", RegexOptions.Compiled);
        private static readonly Regex METODO = new Regex("\"m\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// De <c>{"t":"NestoAPI.Infraestructure.SincronizacionJobsService, NestoAPI","m":"SincronizarProductos"}</c> saca
        /// «SincronizacionJobsService.SincronizarProductos». Si no lo entiende, lo devuelve tal cual (recortado).
        /// </summary>
        internal static string NombreDesdeInvocationData(string invocationData)
        {
            if (string.IsNullOrWhiteSpace(invocationData))
            {
                return null;
            }
            Match tipo = TIPO.Match(invocationData);
            Match metodo = METODO.Match(invocationData);
            if (!tipo.Success || !metodo.Success)
            {
                return invocationData.Length > 120 ? invocationData.Substring(0, 120) : invocationData;
            }
            string clase = tipo.Groups[1].Value.Trim();
            int corte = Math.Max(clase.LastIndexOf('.'), clase.LastIndexOf('+'));
            return (corte >= 0 ? clase.Substring(corte + 1) : clase) + "." + metodo.Groups[1].Value;
        }
    }

    /// <summary>
    /// NestoAPI#820 (incidente 04-05/11/26): job <c>hangfire-vigilante</c>, cada 10 minutos. Mira si hay un job en marcha más de
    /// 15 minutos (NestoAPI#835: o de su umbral, <see cref="VigilanteHangfire.MaximoEnMarcha(string, string)"/>) o más de 100 esperando y lo avisa UNA vez por episodio en ELMAH y en la campana de Nesto del grupo Informática.
    ///
    /// Va en su PROPIA cola («vigilante», con su servidor de un hilo en Startup), no en «default»: si fuera en «default» se
    /// quedaría esperando detrás del job colgado que tiene que avisar, justo lo que pasó el 04/11. Tampoco en «entrenamiento»: ahí
    /// los reentrenamientos ocupan el único hilo durante horas y el aviso llegaría tarde. El programador de recurrentes de Hangfire
    /// corre aparte de los workers, así que la cola «vigilante» se sigue llenando y atendiendo aunque «default» esté parada.
    /// </summary>
    public class VigilanteHangfireJobsService
    {
        public const string JOB_ID = "hangfire-vigilante";
        public const string CRON = "*/10 * * * *";
        public const string COLA = "vigilante";
        public const string TIPO_NOTIFICACION = "HangfireAtascado";
        internal const string USUARIO_SISTEMA = "Sistema (vigilante de Hangfire)";

        private readonly IRepositorioVigilanciaHangfire repositorio;
        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<string, List<string>> miembros;
        private readonly Func<DateTime> ahoraUtc;
        private readonly Action<Exception> elmah;

        internal VigilanteHangfireJobsService(IRepositorioVigilanciaHangfire repositorio, Func<IServicioNotificacionesPush> notificaciones,
            Func<string, List<string>> miembros, Func<DateTime> ahoraUtc = null, Action<Exception> elmah = null)
        {
            this.repositorio = repositorio;
            this.notificaciones = notificaciones;
            this.miembros = miembros;
            this.ahoraUtc = ahoraUtc ?? (() => DateTime.UtcNow);
            this.elmah = elmah ?? (ex => ElmahHelper.Log(ex, USUARIO_SISTEMA));
        }

        /// <summary>Punto de entrada de Hangfire. Sin reintentos: la próxima pasada es en 10 minutos.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0)]
        public static Task Vigilar()
        {
            return new VigilanteHangfireJobsService(new RepositorioVigilanciaHangfire(), () => new ServicioNotificacionesPush(),
                MiembrosGrupoDominio.Leer).VigilarUnaVez();
        }

        /// <returns>Los avisos nuevos que se han dado en esta pasada.</returns>
        internal async Task<List<AvisoHangfire>> VigilarUnaVez()
        {
            List<AvisoHangfire> nuevos;
            try
            {
                List<JobEnCurso> enCurso = await repositorio.LeerEnCurso().ConfigureAwait(false);
                int encolados = await repositorio.ContarEncolados().ConfigureAwait(false);
                nuevos = DeduplicadorAvisosHangfire.Nuevos(VigilanteHangfire.Evaluar(enCurso, encolados, ahoraUtc()));
            }
            catch (Exception ex)
            {
                elmah(new Exception($"[Vigilante Hangfire #820] No se han podido leer las tablas de Hangfire: {ex.Message}", ex));
                return new List<AvisoHangfire>();
            }
            foreach (AvisoHangfire aviso in nuevos)
            {
                elmah(new Exception($"[Vigilante Hangfire #820] {aviso.Titulo}. {aviso.Texto}"));
                await AvisarInformatica(aviso).ConfigureAwait(false);
            }
            return nuevos;
        }

        private async Task AvisarInformatica(AvisoHangfire aviso)
        {
            List<string> usuarios;
            try
            {
                usuarios = miembros(Constantes.GruposSeguridad.INFORMATICA) ?? new List<string>();
            }
            catch (Exception ex)
            {
                elmah(new Exception($"[Vigilante Hangfire #820] No se pudieron leer los miembros de {Constantes.GruposSeguridad.INFORMATICA}: {ex.Message}", ex));
                return;
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = aviso.Titulo,
                Cuerpo = aviso.Texto,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["clave"] = aviso.Clave
                }
            };
            IServicioNotificacionesPush servicio = notificaciones();
            var conDominio = new HashSet<string>(usuarios.Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(Agencias.RepartoPropio.AvisosFurgonetaSinDescargar.ConDominio), StringComparer.OrdinalIgnoreCase);
            foreach (string usuario in conDominio)
            {
                try
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    elmah(new Exception($"[Vigilante Hangfire #820] No se pudo avisar a {usuario}: {ex.Message}", ex));
                }
            }
        }
    }
}
