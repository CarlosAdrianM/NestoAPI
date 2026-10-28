using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnvioDocumentosCorreo
{
    /// <summary>NestoAPI#724: un documento listo para adjuntar, con de quién es y desde qué buzón se manda.</summary>
    public class DocumentoParaCorreo
    {
        public byte[] Pdf { get; set; }
        public string NombreFichero { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>El remitente de la empresa (el de la serie del pedido; administración para el mandato).</summary>
        public MailAddress Remitente { get; set; }
        /// <summary>Firma HTML del departamento que lo manda.</summary>
        public string Firma { get; set; }
    }

    /// <summary>
    /// NestoAPI#724: lo que el envío de documentos necesita de la BD y de los generadores de PDF. Aparte del
    /// gestor para que los tests no toquen ni la BD ni QuestPDF (SkiaSharp).
    /// </summary>
    public interface IProveedorDocumentosCorreo
    {
        /// <summary>El PDF del pedido (el mismo «Descargar PDF» del detalle del pedido). Null si el pedido no existe.</summary>
        Task<DocumentoParaCorreo> PdfPedido(string empresa, int numero, string usuario);
        /// <summary>El PDF del mandato SEPA de esa cuenta (sin cuenta, la del contacto). Null si no existe el cliente o la cuenta.</summary>
        Task<DocumentoParaCorreo> PdfMandato(string empresa, string cliente, string contacto, string ccc, string motor);
        /// <summary>Correos de las personas de contacto activas del cliente/contacto, sin repetir.</summary>
        Task<List<string>> CorreosContacto(string empresa, string cliente, string contacto);
        /// <summary>El correo del usuario (CorreoDefecto de ParametrosUsuario, sin dominio). Null si no tiene.</summary>
        string CorreoUsuario(string usuario);
        /// <summary>Constancia en Modificaciones.</summary>
        Task RegistrarEnvio(string tabla, string documento, string descripcion, string usuario);
    }
}
