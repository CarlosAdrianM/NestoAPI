namespace NestoAPI.Models
{
    public class ReclamacionDeuda
    {
        public string Correo { get; set; }
        public string Movil { get; set; }
        public decimal Importe { get; set; }
        public string Asunto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string TextoSMS { get; set; }
        public string Cliente { get; set; }


        public bool TramitadoOK { get; set; }
        public string Enlace { get; set; }
    }
}