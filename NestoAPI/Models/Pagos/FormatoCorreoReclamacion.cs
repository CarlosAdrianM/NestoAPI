using System.Web;

namespace NestoAPI.Models.Pagos
{
    public class FormatoCorreoReclamacion
    {
        public string nombreComprador { get; set; }
        public string direccionComprador { get; set; }
        public string subjectMailCliente { get; set; }
        public string textoLibre1 { get; set; }

        public string ToXML()
        {
            string resultado = "<![CDATA[";

            if (!string.IsNullOrWhiteSpace(nombreComprador))
            {
                resultado += "<nombreComprador>" + HttpUtility.HtmlEncode(nombreComprador) + "</nombreComprador>";
            }

            if (!string.IsNullOrWhiteSpace(direccionComprador))
            {
                resultado += "<direccionComprador>" + HttpUtility.HtmlEncode(direccionComprador) + "</direccionComprador>";
            }

            if (!string.IsNullOrWhiteSpace(subjectMailCliente))
            {
                resultado += "<subjectMailCliente>" + HttpUtility.HtmlEncode(subjectMailCliente) + "</subjectMailCliente>";
            }

            if (!string.IsNullOrWhiteSpace(textoLibre1))
            {
                resultado += "<textoLibre1>" + HttpUtility.HtmlEncode(textoLibre1) + "</textoLibre1>";
            }

            resultado += "]]>";

            return resultado;
        }
    }
}
