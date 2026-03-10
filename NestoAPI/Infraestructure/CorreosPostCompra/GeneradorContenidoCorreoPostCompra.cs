using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public class GeneradorContenidoCorreoPostCompra : IGeneradorContenidoCorreoPostCompra
    {
        private readonly IServicioOpenAI _servicioOpenAI;

        public GeneradorContenidoCorreoPostCompra(IServicioOpenAI servicioOpenAI)
        {
            _servicioOpenAI = servicioOpenAI;
        }

        public async Task<string> GenerarContenidoHtml(CorreoPostCompraClienteDTO correo)
        {
            if (correo == null ||
                correo.ProductosComprados == null ||
                !correo.ProductosComprados.Any())
            {
                return null;
            }

            var systemPrompt = CrearSystemPrompt();
            var userMessage = CrearUserMessage(correo);

            return await _servicioOpenAI.GenerarCorreoHtmlAsync(systemPrompt, userMessage).ConfigureAwait(false);
        }

        internal string CrearSystemPrompt()
        {
            return @"Eres un experto en email marketing para una empresa de productos de estética y peluquería profesional llamada Nueva Visión.

Tu objetivo es generar un correo HTML que ayude al cliente a sacar el máximo partido a los productos que ha comprado recientemente, mostrándole vídeos tutoriales.

PRINCIPIOS CLAVE (muy importantes):
- El cliente NO debe sentir que le quieres vender nada
- El correo debe parecer una AYUDA genuina, no una promoción
- Primero mostrar valor (vídeos de lo que YA compró)
- Si hay productos recomendados, mencionarlos de forma sutil como ""otros productos que aparecen en estos vídeos""
- Tono amigable y profesional, como un asesor de confianza

ESTRUCTURA DEL CORREO:
1. Saludo breve y personalizado
2. Mensaje de valor: ""Hemos preparado estos vídeos para que saques el máximo partido a tu compra""
3. Para cada producto comprado (máximo 3): mostrar nombre del producto, thumbnail del vídeo y botón ""Ver vídeo""
4. Si hay productos recomendados: sección ""Otros productos que aparecen en estos vídeos"" con enlaces sutiles
5. Firma: ""El equipo de Nueva Visión"" con teléfono 916 28 19 14 (WhatsApp)

RESTRICCIONES:
- Máximo 150 palabras de texto (sin contar HTML)
- Diseño limpio, mobile-first, ancho máximo 600px
- Colores suaves (principal: #2C5F7C), sin urgencias ni descuentos
- Botones CTA suaves: ""Ver vídeo"" (NO ""Comprar ahora"")
- NO usar palabras como ""oferta"", ""descuento"", ""compra"", ""promoción""
- Thumbnail del vídeo: https://img.youtube.com/vi/{YOUTUBE_ID}/maxresdefault.jpg
- Usar SOLO las URLs proporcionadas, NO inventar enlaces
- Todos los enlaces deben incluir los parámetros UTM que se proporcionan

Devuelve SOLO el HTML del cuerpo del correo, sin etiquetas <html>, <head> o <body>.";
        }

        internal string CrearUserMessage(CorreoPostCompraClienteDTO correo)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"CLIENTE: {correo.ClienteNombre}");
            sb.AppendLine();

            sb.AppendLine("PRODUCTOS COMPRADOS RECIENTEMENTE (mostrar con vídeo tutorial):");
            foreach (var producto in correo.ProductosComprados)
            {
                sb.AppendLine($"- Producto: {producto.NombreProducto}");
                sb.AppendLine($"  Vídeo: {producto.VideoTitulo}");
                sb.AppendLine($"  YouTube ID: {producto.VideoYoutubeId}");
                sb.AppendLine($"  Thumbnail: https://img.youtube.com/vi/{producto.VideoYoutubeId}/maxresdefault.jpg");

                if (!string.IsNullOrEmpty(producto.EnlaceVideoProducto))
                {
                    sb.AppendLine($"  Link vídeo: {AgregarUtm(producto.EnlaceVideoProducto)}");
                }
                else
                {
                    sb.AppendLine($"  Link vídeo: {AgregarUtm($"https://www.youtube.com/watch?v={producto.VideoYoutubeId}")}");
                }

                if (!string.IsNullOrEmpty(producto.EnlaceTienda))
                {
                    sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda)}");
                }

                sb.AppendLine();
            }

            if (correo.ProductosRecomendados != null && correo.ProductosRecomendados.Any())
            {
                sb.AppendLine("PRODUCTOS RECOMENDADOS (aparecen en los mismos vídeos, el cliente NO los ha comprado):");
                foreach (var producto in correo.ProductosRecomendados)
                {
                    sb.AppendLine($"- Producto: {producto.NombreProducto}");

                    if (!string.IsNullOrEmpty(producto.EnlaceVideoProducto))
                    {
                        sb.AppendLine($"  Link vídeo: {AgregarUtm(producto.EnlaceVideoProducto)}");
                    }

                    if (!string.IsNullOrEmpty(producto.EnlaceTienda))
                    {
                        sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda)}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("IMPORTANTE: NO distinguir entre productos comprados y recomendados en el texto visible. Menciona los recomendados de forma natural como \"otros productos que aparecen en estos vídeos\".");
            sb.AppendLine("IMPORTANTE: Usa SOLO los enlaces proporcionados. NO inventes URLs.");

            return sb.ToString();
        }

        internal static string AgregarUtm(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var separator = url.Contains("?") ? "&" : "?";
            return $"{url}{separator}utm_source=correo_postcompra&utm_medium=email&utm_campaign=tutoriales_postcompra";
        }
    }

    public interface IGeneradorContenidoCorreoPostCompra
    {
        Task<string> GenerarContenidoHtml(CorreoPostCompraClienteDTO correo);
    }
}
