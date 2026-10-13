using System;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Fallo de comunicación con la plataforma de una agencia (no un rechazo de negocio, que viaja
    /// dentro de la respuesta): no se pudo conectar, HTTP 5xx, timeout, respuesta que no se puede
    /// interpretar. Base común de <see cref="Innovatrans.DataTransException"/> y de los errores de
    /// CTT (NestoAPI#493), para que la política de reintentos y los controladores traten a todas las
    /// agencias igual.
    /// </summary>
    public class AgenciaRemotaException : Exception
    {
        public AgenciaRemotaException(string message) : base(message) { }
        public AgenciaRemotaException(string message, Exception inner) : base(message, inner) { }

        /// <summary>
        /// True si el fallo es de transporte y puede desaparecer reintentando en segundos (no se
        /// pudo conectar, HTTP 5xx). False para errores estables (respuesta no interpretable):
        /// reintentarlos solo repite el mismo error. Lo usa la política de reintentos de
        /// PoliticasAgenciasRemotas (NestoAPI#288).
        /// </summary>
        public bool EsTransitoria { get; set; }
    }

    /// <summary>
    /// 23/09/26: la agencia ha cortado por CUPO de llamadas (CTT: HTTP 429 «Quota has been exceeded»).
    /// NO es transitoria para la política de reintentos (#288): reintentar a los pocos segundos solo
    /// gasta más cupo. Quien la recibe deja de llamar a esa agencia hasta la siguiente pasada.
    /// </summary>
    public class CupoAgenciaAgotadoException : AgenciaRemotaException
    {
        public CupoAgenciaAgotadoException(string message, System.TimeSpan? reintentarTras = null) : base(message)
        {
            ReintentarTras = reintentarTras;
        }

        /// <summary>Lo que la agencia pide esperar (Retry-After), si lo dice.</summary>
        public System.TimeSpan? ReintentarTras { get; }
    }

    /// <summary>
    /// Sugerencia 507 (Alfredo, 06/10/26): CTT estuvo un rato contestando HTTP 500 «CONNECTIVITY» y en Nesto solo se veía
    /// «HTTP 500». Distingue el fallo «la agencia no responde» (su servidor caído o lento) del resto, para decirle al
    /// usuario lo que puede hacer: reintentar en unos minutos o sacarlo por otra agencia.
    /// </summary>
    public static class FallosAgenciaRemota
    {
        /// <summary>«Codigo» del error (en el cuerpo del 502 o en el ErrorCode de la NestoBusinessException) cuando la agencia no responde.</summary>
        public const string CODIGO_NO_RESPONDE = "AGENCIA_NO_RESPONDE";

        /// <summary>
        /// True si el fallo es de la agencia que no responde: HTTP 5xx, timeout o no se pudo conectar (las
        /// <see cref="AgenciaRemotaException"/> transitorias, y WebException / HttpRequestException /
        /// TaskCanceledException sueltas o envueltas). El cupo agotado (429) NO: tiene su propio aviso (#602).
        /// Un rechazo de negocio o una respuesta que no se entiende, tampoco.
        /// </summary>
        public static bool NoResponde(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is CupoAgenciaAgotadoException)
                {
                    return false;
                }
                if (actual is AgenciaRemotaException remota && remota.EsTransitoria)
                {
                    return true;
                }
                if (actual is System.Net.WebException
                    || actual is System.Net.Http.HttpRequestException
                    || actual is System.Threading.Tasks.TaskCanceledException
                    || actual is TimeoutException)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Lo que lee el usuario cuando la agencia no responde: «CTT no responde ahora mismo (error de su servidor).
        /// Vuelve a intentarlo en unos minutos o, si es urgente, sácalo por GLS.» Sin nombre, «La agencia…»; si la que
        /// falla es GLS, la alternativa es «otra agencia».
        /// </summary>
        public static string MensajeNoResponde(string agencia) => MensajeNoResponde(agencia, puedeHaberseCreado: false);

        /// <summary>
        /// Como <see cref="MensajeNoResponde(string)"/>; con <paramref name="puedeHaberseCreado"/> (timeout en el alta del
        /// envío: la agencia pudo crearlo aunque no llegara la respuesta) añade «Antes de reintentar, comprueba en la web
        /// de CTT que no se ha creado ya.», para no duplicarlo.
        /// </summary>
        public static string MensajeNoResponde(string agencia, bool puedeHaberseCreado)
        {
            string nombre = string.IsNullOrWhiteSpace(agencia) ? "La agencia" : agencia.Trim();
            string alternativa = nombre.IndexOf("GLS", StringComparison.OrdinalIgnoreCase) >= 0 ? "otra agencia" : "GLS";
            string mensaje = $"{nombre} no responde ahora mismo (error de su servidor). " +
                $"Vuelve a intentarlo en unos minutos o, si es urgente, sácalo por {alternativa}.";
            if (puedeHaberseCreado)
            {
                string web = string.IsNullOrWhiteSpace(agencia) ? "la agencia" : nombre;
                mensaje += $" Antes de reintentar, comprueba en la web de {web} que no se ha creado ya.";
            }
            return mensaje;
        }

        /// <summary>
        /// True si el fallo es un TIMEOUT (la petición salió y no llegó la respuesta a tiempo): TaskCanceledException
        /// (timeout del HttpClient), TimeoutException o WebException con estado Timeout, sueltas o envueltas. En el alta
        /// del envío es el único «no responde» en el que la agencia pudo crearlo: con un 5xx o sin conexión, no.
        /// </summary>
        public static bool EsTimeout(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual is System.Threading.Tasks.TaskCanceledException || actual is TimeoutException)
                {
                    return true;
                }
                if (actual is System.Net.WebException web && web.Status == System.Net.WebExceptionStatus.Timeout)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
