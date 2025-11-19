namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Estados posibles para los mensajes de sincronización con reintentos
    /// </summary>
    public enum RetryStatus
    {
        /// <summary>
        /// Aún reintentando (menos de 5 intentos)
        /// </summary>
        Retrying,

        /// <summary>
        /// Límite de intentos alcanzado, pendiente de revisión manual
        /// </summary>
        PoisonPill,

        /// <summary>
        /// Marcado para reprocesar (resetea contador)
        /// </summary>
        Reprocess,

        /// <summary>
        /// Marcado como solucionado manualmente
        /// </summary>
        Resolved,

        /// <summary>
        /// Marcado como fallo permanente (no reprocesar)
        /// </summary>
        PermanentFailure
    }
}
