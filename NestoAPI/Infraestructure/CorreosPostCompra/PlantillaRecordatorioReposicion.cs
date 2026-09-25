using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: texto del recordatorio de reposición que recibiría el cliente. Tono de servicio
    /// («por si te viene bien tenerlo a mano»), nunca de reproche. Sin OpenAI: el mismo texto para todos.
    /// En este corte solo se usa como muestra dentro del correo sombra.
    /// </summary>
    public static class PlantillaRecordatorioReposicion
    {
        public const string UTM = "utm_source=correo_reposicion&utm_medium=email&utm_campaign=reposicion";
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        public static string Asunto(RecordatorioReposicionClienteDTO correo)
        {
            string primero = correo?.Productos?.FirstOrDefault()?.NombreProducto?.Trim();
            if (string.IsNullOrWhiteSpace(primero))
            {
                return "Por si te viene bien reponer";
            }
            return correo.Productos.Count > 1
                ? $"Por si te viene bien reponer {primero} y algo más"
                : $"Por si te viene bien reponer {primero}";
        }

        public static string CuerpoHtml(RecordatorioReposicionClienteDTO correo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div style='font-family: Arial, sans-serif; font-size: 14px; color: #333; max-width: 600px;'>");
            sb.AppendLine("<p>Hola:</p>");
            sb.AppendLine("<p>Por lo que sueles pedirnos, puede que dentro de poco te toque reponer " +
                (correo.Productos.Count > 1 ? "alguno de estos productos" : "este producto") +
                ". Te lo dejamos aquí por si te viene bien tenerlo a mano:</p>");
            sb.AppendLine("<ul>");
            foreach (CandidatoReposicionDTO producto in correo.Productos)
            {
                string nombre = WebUtility.HtmlEncode(producto.NombreProducto ?? producto.Producto);
                string enlace = ConUtm(producto.EnlaceTienda);
                sb.Append("<li style='margin-bottom: 8px;'>");
                sb.Append(string.IsNullOrWhiteSpace(enlace)
                    ? $"<b>{nombre}</b>"
                    : $"<a href='{WebUtility.HtmlEncode(enlace)}' style='color: #007bff;'><b>{nombre}</b></a>");
                sb.Append($" <span style='color: #888;'>(la última vez, el {producto.UltimaCompra.ToString("d 'de' MMMM", ES)})</span>");
                sb.AppendLine("</li>");
            }
            sb.AppendLine("</ul>");
            if (!string.IsNullOrWhiteSpace(correo.VendedorNombre))
            {
                sb.AppendLine($"<p>Si lo prefieres, pídeselo a {WebUtility.HtmlEncode(correo.VendedorNombre)}, tu comercial, como siempre.</p>");
            }
            sb.AppendLine("<p>Y si ya lo tienes cubierto, no hace falta que hagas nada.</p>");
            sb.AppendLine("<p>Un saludo,<br/>El equipo de Nueva Visión</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        internal static string ConUtm(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            string limpia = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(url.Trim());
            return limpia + (limpia.Contains("?") ? "&" : "?") + UTM;
        }
    }
}
