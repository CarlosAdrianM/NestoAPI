namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Estados posibles para el procesamiento de mensajes de sincronización
    /// </summary>
    public enum RetryStatus
    {
        /// <summary>
        /// Aún reintentando (attemptCount menor que MaxAttempts)
        /// </summary>
        Retrying,

        /// <summary>
        /// Llegó al límite de reintentos, pendiente de revisión manual
        /// </summary>
        PoisonPill,

        /// <summary>
        /// Marcado manualmente para reprocesar (resetea contador de intentos)
        /// </summary>
        Reprocess,

        /// <summary>
        /// Marcado manualmente como solucionado exitosamente
        /// </summary>
        Resolved,

        /// <summary>
        /// Marcado manualmente como fallo permanente (no reintentar)
        /// </summary>
        PermanentFailure
    }
}
