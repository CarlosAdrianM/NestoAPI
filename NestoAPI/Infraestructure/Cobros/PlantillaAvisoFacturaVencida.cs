using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>
    /// NestoAPI#534: el texto cordial del aviso de factura vencida (propuesta de la issue, en lugar
    /// del «adjunto la factura vencida para que gestionéis el pago»).
    /// NestoAPI#544: un aviso por cliente con TODAS sus facturas vencidas (tabla y total), saludo
    /// «Buenos días / Buenas tardes» con nombre de pila, bloque para copiar y pegar (IBAN, titular y
    /// concepto «Cliente NNNNN – NV…, NV…») y la petición de transferencia inmediata.
    /// </summary>
    public static class PlantillaAvisoFacturaVencida
    {
        private static readonly CultureInfo ES = new CultureInfo("es-ES");
        public const string FIRMA = "Administración, Nueva Visión";
        public const string FRASE_INMEDIATA = "Si puedes y no te supone coste, haz la transferencia inmediata: llega en el momento y evitamos avisos innecesarios.";

        public static string Asunto(AvisoClienteFacturasVencidasDTO aviso)
        {
            List<string> facturas = aviso?.NumerosFactura ?? new List<string>();
            string asunto = facturas.Count == 1
                ? $"Factura {facturas[0]} pendiente de pago"
                : $"Facturas {string.Join(", ", facturas)} pendientes de pago";
            return (aviso?.NumeroAviso ?? 1) >= 2 ? "Recordatorio: " + asunto : asunto;
        }

        /// <summary>
        /// Escapa lo que rompe el HTML (&amp; &lt; &gt; ") y NADA más: WebUtility.HtmlEncode convierte
        /// las tildes en entidades numéricas (&amp;#237;), feo de leer en el fuente del correo y en los
        /// tests. El cuerpo va en UTF-8.
        /// </summary>
        public static string Html(string texto)
            => texto == null ? string.Empty
                : texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        /// <summary>Importe en formato español: 1.234,56 €.</summary>
        public static string FormatearImporte(decimal importe) => importe.ToString("N2", ES) + " €";

        public static string FormatearFecha(DateTime? fecha) => fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy", ES) : string.Empty;

        /// <summary>
        /// NestoAPI#544 (d): «Buenos días» hasta las 14:00 y «Buenas tardes» después, más el nombre
        /// de pila si lo hay: «Buenos días, Susana:» / «Buenas tardes:».
        /// </summary>
        public static string Saludo(string nombrePila, DateTime horaEnvio)
        {
            string formula = horaEnvio.Hour < 14 ? "Buenos días" : "Buenas tardes";
            return string.IsNullOrWhiteSpace(nombrePila) ? formula + ":" : $"{formula}, {nombrePila.Trim()}:";
        }

        /// <summary>Concepto propuesto para la transferencia: «Cliente 27120 – NV2615541, NV2615600».</summary>
        public static string Concepto(AvisoClienteFacturasVencidasDTO aviso)
            => $"Cliente {aviso?.Cliente?.Trim()} – {string.Join(", ", aviso?.NumerosFactura ?? new List<string>())}";

        /// <summary>Cuerpo en texto plano (el que se revisa en los tests y el que se enseña en la sombra).</summary>
        public static string CuerpoTexto(AvisoClienteFacturasVencidasDTO aviso, DatosPagoAviso datos)
        {
            if (aviso == null)
            {
                throw new ArgumentNullException(nameof(aviso));
            }
            List<AvisoFacturaVencidaDTO> facturas = FacturasOrdenadas(aviso);
            bool varias = facturas.Count > 1;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(aviso.Saludo ?? Saludo(null, DateTime.Now));
            sb.AppendLine();
            sb.AppendLine(Introduccion(aviso.NumeroAviso, varias));
            sb.AppendLine();
            sb.AppendLine("Factura      Fecha        Vencimiento  Importe");
            foreach (AvisoFacturaVencidaDTO f in facturas)
            {
                sb.AppendLine($"{(f.Factura ?? string.Empty).PadRight(12)} {FormatearFecha(f.FechaFactura).PadRight(12)} " +
                    $"{FormatearFecha(f.Vencimiento).PadRight(12)} {FormatearImporte(f.Importe)}");
            }
            if (varias)
            {
                sb.AppendLine($"Total pendiente: {FormatearImporte(aviso.Total)}");
            }
            sb.AppendLine();
            sb.AppendLine(ParrafoCruce(datos));
            sb.AppendLine();
            foreach ((string etiqueta, string valor) in BloqueCopiar(aviso, datos))
            {
                sb.AppendLine($"{etiqueta}: {valor}");
            }
            sb.AppendLine();
            sb.AppendLine(FRASE_INMEDIATA);
            sb.AppendLine();
            sb.AppendLine(ParrafoDudas(varias));
            sb.AppendLine();
            sb.AppendLine("Un saludo,");
            sb.Append(FIRMA);
            return sb.ToString();
        }

        /// <summary>El mismo aviso en HTML: tabla de facturas y bloque para copiar y pegar, con los datos escapados.</summary>
        public static string CuerpoHtml(AvisoClienteFacturasVencidasDTO aviso, DatosPagoAviso datos)
        {
            if (aviso == null)
            {
                throw new ArgumentNullException(nameof(aviso));
            }
            List<AvisoFacturaVencidaDTO> facturas = FacturasOrdenadas(aviso);
            bool varias = facturas.Count > 1;
            StringBuilder sb = new StringBuilder();
            sb.Append("<div style='font-family: Arial, sans-serif; font-size: 14px; color: #222;'>");
            sb.Append("<p>").Append(PlantillaAvisoFacturaVencida.Html(aviso.Saludo ?? Saludo(null, DateTime.Now))).Append("</p>");
            sb.Append("<p>").Append(PlantillaAvisoFacturaVencida.Html(Introduccion(aviso.NumeroAviso, varias))).Append("</p>");
            sb.Append("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse: collapse; font-size: 13px;'>");
            sb.Append("<tr style='background-color: #f2f2f2;'><th align='left'>Factura</th><th align='left'>Fecha</th><th align='left'>Vencimiento</th><th align='right'>Importe</th></tr>");
            foreach (AvisoFacturaVencidaDTO f in facturas)
            {
                sb.Append("<tr>")
                  .Append("<td>").Append(PlantillaAvisoFacturaVencida.Html(f.Factura)).Append("</td>")
                  .Append("<td>").Append(FormatearFecha(f.FechaFactura)).Append("</td>")
                  .Append("<td>").Append(FormatearFecha(f.Vencimiento)).Append("</td>")
                  .Append("<td align='right'>").Append(FormatearImporte(f.Importe)).Append("</td>")
                  .Append("</tr>");
            }
            if (varias)
            {
                sb.Append("<tr style='font-weight: bold;'><td colspan='3'>Total pendiente</td><td align='right'>")
                  .Append(FormatearImporte(aviso.Total)).Append("</td></tr>");
            }
            sb.Append("</table>");
            sb.Append("<p>").Append(PlantillaAvisoFacturaVencida.Html(ParrafoCruce(datos))).Append("</p>");
            sb.Append("<div style='border: 1px solid #bbb; background-color: #fafafa; padding: 10px 14px; font-family: Consolas, monospace; font-size: 13px;'>");
            foreach ((string etiqueta, string valor) in BloqueCopiar(aviso, datos))
            {
                sb.Append("<div><b>").Append(PlantillaAvisoFacturaVencida.Html(etiqueta)).Append(":</b> ")
                  .Append(PlantillaAvisoFacturaVencida.Html(valor)).Append("</div>");
            }
            sb.Append("</div>");
            sb.Append("<p>").Append(PlantillaAvisoFacturaVencida.Html(FRASE_INMEDIATA)).Append("</p>");
            sb.Append("<p>").Append(PlantillaAvisoFacturaVencida.Html(ParrafoDudas(varias))).Append("</p>");
            sb.Append("<p>Un saludo,<br/>").Append(PlantillaAvisoFacturaVencida.Html(FIRMA)).Append("</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static List<AvisoFacturaVencidaDTO> FacturasOrdenadas(AvisoClienteFacturasVencidasDTO aviso)
            => (aviso.Facturas ?? new List<AvisoFacturaVencidaDTO>()).OrderBy(f => f.Vencimiento).ThenBy(f => f.Factura).ToList();

        private static string Introduccion(int numeroAviso, bool varias)
        {
            string arranque = numeroAviso >= 2 ? "Te volvemos a escribir porque" : "Te escribimos porque";
            return varias
                ? $"{arranque} en nuestros registros figuran como pendientes de pago estas facturas, ya vencidas. Te las adjuntamos para que las tengas a mano."
                : $"{arranque} en nuestros registros figura como pendiente de pago esta factura, ya vencida. Te la adjuntamos para que la tengas a mano.";
        }

        private static string ParrafoCruce(DatosPagoAviso datos)
        {
            string cierre = string.IsNullOrWhiteSpace(datos?.Iban)
                ? "Si no, puedes hacerla a la cuenta de Nueva Visión que figura en la factura, con el concepto de abajo."
                : "Si no, aquí tienes los datos para hacerla (puedes copiarlos tal cual):";
            return "Si ya has hecho la transferencia, no hace falta que hagas nada: seguramente se ha cruzado con este correo " +
                "y te pedimos disculpas por la molestia. " + cierre;
        }

        private static string ParrafoDudas(bool varias)
            => varias
                ? "Para cualquier duda, o si hay algo en alguna factura que no te cuadra, responde a este correo y lo vemos."
                : "Para cualquier duda, o si hay algo en la factura que no te cuadra, responde a este correo y lo vemos.";

        private static IEnumerable<(string, string)> BloqueCopiar(AvisoClienteFacturasVencidasDTO aviso, DatosPagoAviso datos)
        {
            if (!string.IsNullOrWhiteSpace(datos?.Iban))
            {
                yield return ("IBAN", datos.Iban.Trim());
            }
            if (!string.IsNullOrWhiteSpace(datos?.Titular))
            {
                yield return ("Titular", datos.Titular.Trim());
            }
            yield return ("Concepto", Concepto(aviso));
        }
    }
}
