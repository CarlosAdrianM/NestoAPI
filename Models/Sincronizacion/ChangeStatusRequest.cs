namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// DTO para solicitar el cambio de estado de un mensaje poison pill
    /// </summary>
    public class ChangeStatusRequest
    {
        /// <summary>
        /// ID del mensaje cuyo estado se va a cambiar
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Nuevo estado: "Reprocess", "Resolved", o "PermanentFailure"
        /// </summary>
        public string NewStatus { get; set; }
    }
}
