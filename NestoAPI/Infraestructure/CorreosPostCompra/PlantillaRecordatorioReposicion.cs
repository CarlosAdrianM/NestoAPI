using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: el correo de reposición que recibe el cliente. De usted y de ayuda (nunca de venta ni dando a entender
    /// que miramos lo que pide). Las piezas que cambian cada semana (asunto, entrada y cierre) vienen en un
    /// <see cref="TextoRecordatorioReposicion"/> (OpenAI o la fija); el saludo, la lista de productos con su foto y enlace,
    /// la frase de baja y la firma las pone siempre este código.
    /// </summary>
    public static class PlantillaRecordatorioReposicion
    {
        public const string UTM = "utm_source=correo_reposicion&utm_medium=email&utm_campaign=reposicion";
        /// <summary>NestoAPI#532 (bajas por tipo): la baja es solo de este aviso; el resto de correos le siguen llegando.</summary>
        public const string FRASE_BAJA = "Si prefiere no recibir estos avisos de reposición, responda a este correo y dejaremos de enviárselos. " +
            "Es solo para este aviso: el resto de nuestros correos le seguirán llegando como siempre.";
        internal const string SIN_VENDEDOR = "nuestro equipo";
        internal const int TAMANO_FOTO = 64;
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        public static string Asunto(RecordatorioReposicionClienteDTO correo, TextoRecordatorioReposicion texto = null)
        {
            texto = texto ?? TextoRecordatorioReposicion.Fija();
            string primero = RecordatorioReposicionPushJobsService.NombreNatural(
                correo?.Productos?.FirstOrDefault()?.NombreProducto ?? correo?.Productos?.FirstOrDefault()?.Producto);
            if (string.IsNullOrWhiteSpace(primero))
            {
                primero = "ningún producto";
            }
            string producto = correo.Productos.Count > 1 ? primero + " y algo más" : primero;
            return texto.Asunto.Replace(TextoRecordatorioReposicion.MARCA_PRODUCTO, producto);
        }

        public static string CuerpoHtml(RecordatorioReposicionClienteDTO correo, TextoRecordatorioReposicion texto = null,
            DateTime? hoy = null)
        {
            texto = texto ?? TextoRecordatorioReposicion.Fija();
            int año = (hoy ?? DateTime.Today).Year;
            string vendedor = string.IsNullOrWhiteSpace(correo.VendedorNombre) ? SIN_VENDEDOR : correo.VendedorNombre.Trim();
            string productos = correo.Productos.Count > 1 ? "alguno de estos productos" : "este producto";

            var sb = new StringBuilder();
            sb.AppendLine("<div style='font-family: Arial, sans-serif; font-size: 15px; line-height: 1.45; color: #333; max-width: 600px;'>");
            sb.AppendLine("<p>Hola:</p>");
            sb.AppendLine("<p>" + WebUtility.HtmlEncode(texto.Entrada.Replace(TextoRecordatorioReposicion.MARCA_PRODUCTOS, productos)) + "</p>");
            sb.AppendLine("<table role='presentation' cellspacing='0' cellpadding='0' border='0' width='100%' style='max-width: 600px; margin: 4px 0 12px 0;'>");
            foreach (CandidatoReposicionDTO producto in correo.Productos)
            {
                AppendProducto(sb, producto, año);
            }
            sb.AppendLine("</table>");
            sb.AppendLine("<p>" + WebUtility.HtmlEncode(texto.Cierre.Replace(TextoRecordatorioReposicion.MARCA_VENDEDOR, vendedor)) + "</p>");
            sb.AppendLine($"<p>{WebUtility.HtmlEncode(FRASE_BAJA)}</p>");
            sb.AppendLine("<p>Un saludo,<br/>El equipo de Nueva Visión</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Una fila por producto: la miniatura (si la hay) y el nombre, los dos enlazados a la ficha de la tienda con su UTM,
        /// y debajo cuándo lo pidió por última vez. Celdas de ancho fijo para la foto y el resto fluido: se ve bien en el móvil.
        /// </summary>
        private static void AppendProducto(StringBuilder sb, CandidatoReposicionDTO producto, int año)
        {
            string nombre = WebUtility.HtmlEncode(producto.NombreProducto ?? producto.Producto);
            string enlace = ConUtm(producto.EnlaceTienda);
            string foto = NormalizarUrlFoto(producto.UrlFoto);
            string fecha = producto.UltimaCompra.ToString(producto.UltimaCompra.Year == año ? "d 'de' MMMM" : "d 'de' MMMM 'de' yyyy", ES);

            sb.Append("<tr>");
            if (foto != null)
            {
                string img = $"<img src='{WebUtility.HtmlEncode(foto)}' width='{TAMANO_FOTO}' height='{TAMANO_FOTO}' alt='{nombre}' " +
                    $"style='display: block; width: {TAMANO_FOTO}px; height: {TAMANO_FOTO}px; border: 1px solid #eee; border-radius: 6px;'/>";
                sb.Append($"<td width='{TAMANO_FOTO + 12}' valign='middle' style='width: {TAMANO_FOTO + 12}px; padding: 6px 12px 6px 0;'>");
                sb.Append(enlace == null ? img : $"<a href='{WebUtility.HtmlEncode(enlace)}' style='text-decoration: none;'>{img}</a>");
                sb.Append("</td>");
            }
            sb.Append("<td valign='middle' style='padding: 6px 0;'>");
            sb.Append(enlace == null
                ? $"<b>{nombre}</b>"
                : $"<a href='{WebUtility.HtmlEncode(enlace)}' style='color: #007bff;'><b>{nombre}</b></a>");
            sb.Append($"<br/><span style='color: #888; font-size: 13px;'>(lo pidió por última vez el {fecha})</span>");
            sb.AppendLine("</td></tr>");
        }

        /// <summary>
        /// La URL de la foto si sirve; si no, null. ProductoDTO.RutaImagen devuelve «https://» a secas cuando el producto no
        /// tiene foto, y eso no es una foto.
        /// </summary>
        public static string NormalizarUrlFoto(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }
            string limpia = url.Trim();
            if (!Uri.TryCreate(limpia, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || string.IsNullOrWhiteSpace(uri.Host) || !uri.Host.Contains(".")
                || uri.AbsolutePath.Length <= 1)
            {
                return null;
            }
            return limpia;
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
