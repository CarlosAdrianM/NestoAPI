namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Request para cambiar el estado de un mensaje de sincronización
    /// </summary>
    public class ChangeStatusRequest
    {
        /// <summary>
        /// ID del mensaje a actualizar
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Nuevo estado: "Reprocess", "Resolved", "PermanentFailure"
        /// </summary>
        public string NewStatus { get; set; }
    }
}
