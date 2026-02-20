namespace NestoAPI.Models.Pagos
{
    public class ResultadoValidacionNotificacion
    {
        public bool FirmaValida { get; set; }
        public bool PagoAutorizado { get; set; }
        public string CodigoRespuesta { get; set; }
        public string CodigoAutorizacion { get; set; }
        public string NumeroOrden { get; set; }
        public string MensajeError { get; set; }
    }
}
