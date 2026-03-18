namespace NestoAPI.Models
{
    public class EnviarNotificacionDTO
    {
        public string Destinatario { get; set; }
        public string TipoDestinatario { get; set; }
        public string Empresa { get; set; }
        public string Aplicacion { get; set; }
        public NotificacionPushDTO Notificacion { get; set; }
    }
}
