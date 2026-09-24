using NestoAPI.Models.Cobros;
using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// NestoAPI#534: el texto cordial del aviso de factura vencida (propuesta de la issue, en lugar
    /// del «adjunto la factura vencida para que gestionéis el pago»). En el corte 1 solo se usa para
    /// enseñar a administración, en el correo sombra, lo que recibiría el primer cliente; en el
    /// corte 2 será el cuerpo del correo real.
    /// </summary>
    public static class PlantillaAvisoFacturaVencida
    {
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        public static string Asunto(AvisoFacturaVencidaDTO aviso)
            => $"Factura {aviso?.Factura} pendiente de pago";

        /// <summary>Importe en formato español: 1.234,56 €.</summary>
        public static string FormatearImporte(decimal importe) => importe.ToString("N2", ES) + " €";

        public static string FormatearFecha(DateTime? fecha) => fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", ES) : string.Empty;

        /// <summary>Cuerpo en texto plano (el que se revisa en los tests).</summary>
        public static string CuerpoTexto(AvisoFacturaVencidaDTO aviso, string iban)
        {
            if (aviso == null)
            {
                throw new ArgumentNullException(nameof(aviso));
            }
            string saludo = string.IsNullOrWhiteSpace(aviso.Nombre) ? "Hola:" : $"Hola, {aviso.Nombre.Trim()}:";
            string cuenta = string.IsNullOrWhiteSpace(iban) ? "de Nueva Visión que figura en la factura" : iban.Trim();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(saludo);
            sb.AppendLine();
            sb.AppendLine($"Te escribimos porque en nuestros registros la factura {aviso.Factura}, del {FormatearFecha(aviso.FechaFactura)}, " +
                $"por importe de {FormatearImporte(aviso.Importe)}, figura como pendiente de pago desde su vencimiento el " +
                $"{FormatearFecha(aviso.Vencimiento)}. Te la adjuntamos para que la tengas a mano.");
            sb.AppendLine();
            sb.AppendLine("Si ya has hecho la transferencia, no hace falta que hagas nada: seguramente se ha cruzado con este correo " +
                $"y te pedimos disculpas por la molestia. Si no, puedes hacerla a la cuenta {cuenta} indicando el número de " +
                "factura en el concepto.");
            sb.AppendLine();
            sb.AppendLine("Para cualquier duda, o si hay algo en la factura que no te cuadra, responde a este correo y lo vemos.");
            sb.AppendLine();
            sb.AppendLine("Un saludo,");
            sb.Append("Administración, Nueva Visión");
            return sb.ToString();
        }

        /// <summary>El mismo cuerpo en HTML (párrafos), con los datos escapados.</summary>
        public static string CuerpoHtml(AvisoFacturaVencidaDTO aviso, string iban)
        {
            string texto = CuerpoTexto(aviso, iban).Replace("\r\n", "\n");
            StringBuilder sb = new StringBuilder();
            foreach (string parrafo in texto.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append("<p>")
                  .Append(WebUtility.HtmlEncode(parrafo).Replace("\n", "<br/>"))
                  .Append("</p>");
            }
            return sb.ToString();
        }
    }
}
