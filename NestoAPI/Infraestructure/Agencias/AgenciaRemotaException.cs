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
}
