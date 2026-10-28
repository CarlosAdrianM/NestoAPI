using System.Collections.Generic;

namespace NestoAPI.Models
{
    /// <summary>
    /// NestoAPI#724 (sugerencia 831): mandar al cliente por correo un documento (pedido o mandato SEPA) sin descargarlo.
    /// Sin destinatarios, van a las personas de contacto del cliente que tienen correo. Asunto y texto, opcionales.
    /// </summary>
    public class EnvioDocumentoCorreoDTO
    {
        /// <summary>Uno o varios correos (se admiten varios en una cadena, separados por «;» o «,»).</summary>
        public List<string> Destinatarios { get; set; }
        public string Asunto { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>NestoAPI#724: el mandato SEPA es de una cuenta (CCC) del contacto; sin ella, la cuenta activa del contacto.</summary>
    public class EnvioMandatoCorreoDTO : EnvioDocumentoCorreoDTO
    {
        public string Ccc { get; set; }
        /// <summary>«QuestPDF» o «RDLC» (el parámetro MotorPdfMandatoSepa del usuario); sin él, QuestPDF.</summary>
        public string Motor { get; set; }
    }

    public class ResultadoEnvioDocumentoCorreoDTO
    {
        public List<string> Destinatarios { get; set; } = new List<string>();
        /// <summary>El nombre del PDF que va adjunto.</summary>
        public string Adjunto { get; set; }
        public string Mensaje { get; set; }
    }
}
