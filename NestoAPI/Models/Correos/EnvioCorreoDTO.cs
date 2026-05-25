using System.Collections.Generic;

namespace NestoAPI.Models.Correos
{
    public class EnvioCorreoDTO
    {
        public List<string> Destinatarios { get; set; }
        public List<string> CopiaOculta { get; set; }
        public string Asunto { get; set; }
        public string Cuerpo { get; set; }
        public bool EsHtml { get; set; }
        public string Remitente { get; set; }
        public string NombreRemitente { get; set; }
        public List<AdjuntoCorreoDTO> Adjuntos { get; set; }
    }

    public class EnvioCorreoRespuestaDTO
    {
        public bool Enviado { get; set; }
        public string Mensaje { get; set; }
    }
}
