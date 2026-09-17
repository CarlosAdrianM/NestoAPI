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
}
