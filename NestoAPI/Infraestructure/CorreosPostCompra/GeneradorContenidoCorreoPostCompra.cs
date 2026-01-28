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

        public async Task<string> GenerarContenidoHtml(RecomendacionPostCompraDTO recomendacion)
        {
            if (recomendacion == null || recomendacion.Videos == null || !recomendacion.Videos.Any())
            {
                return null;
            }

            // Obtener URLs de Prestashop para los productos (en paralelo)
            var urlsProductos = await ObtenerUrlsProductosPrestashop(recomendacion).ConfigureAwait(false);

            var systemPrompt = CrearSystemPrompt();
            var userMessage = CrearUserMessage(recomendacion, urlsProductos);

            return await _servicioOpenAI.GenerarCorreoHtmlAsync(systemPrompt, userMessage).ConfigureAwait(false);
        }

        /// <summary>
        /// Obtiene las URLs de Prestashop para todos los productos de los videos.
        /// Se ejecuta en paralelo para minimizar el tiempo de espera.
        /// </summary>
        private async Task<Dictionary<string, string>> ObtenerUrlsProductosPrestashop(RecomendacionPostCompraDTO recomendacion)
        {
            var productosUnicos = recomendacion.Videos
                .SelectMany(v => v.Productos)
                .Select(p => p.ProductoId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var tareas = productosUnicos.Select(async productoId =>
            {
                var url = await ProductoDTO.LeerUrlTiendaOnline(productoId).ConfigureAwait(false);
                return new { ProductoId = productoId, Url = url };
            });

            var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);

            return resultados
                .Where(r => !string.IsNullOrEmpty(r.Url))
                .ToDictionary(r => r.ProductoId, r => r.Url);
        }

        private string CrearSystemPrompt()
        {
            return @"Eres un experto en email marketing para una empresa de productos de estética y peluquería profesional.

Tu objetivo es generar un correo HTML que ayude al cliente a sacar el máximo partido a los productos que acaba de comprar, mostrándole videos tutoriales.

PRINCIPIOS CLAVE (muy importantes):
- El cliente NO debe sentir que le quieres vender nada
- El correo debe parecer una AYUDA genuina, no una promoción
- Primero mostrar valor (videos de lo que YA compró), luego sutilmente mencionar otros productos del video
- NO distinguir explícitamente entre productos que tiene y que no tiene
- Tono amigable y profesional, como un asesor de confianza

ESTRUCTURA DEL CORREO:
1. Saludo breve y personalizado
2. Mensaje de valor: ""Hemos preparado esto para que saques el máximo partido a tu compra""
3. Mostrar el/los video(s) con thumbnail y enlace
4. Mencionar de forma natural qué técnicas/productos verá en el video (sin distinguir si los tiene o no)
5. CTA suave: ""Ver video"" (NO ""Comprar ahora"")
6. Despedida cálida

RESTRICCIONES:
- Máximo 120 palabras de texto (sin contar HTML)
- Diseño limpio, mobile-first
- Colores suaves, sin urgencias ni descuentos
- Un solo botón CTA principal por video
- NO usar palabras como ""oferta"", ""descuento"", ""compra"", ""promoción""

Devuelve SOLO el HTML del cuerpo del correo, sin etiquetas <html>, <head> o <body>.";
        }

        private string CrearUserMessage(RecomendacionPostCompraDTO recomendacion, Dictionary<string, string> urlsProductos)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"CLIENTE: {recomendacion.ClienteNombre}");
            sb.AppendLine();

            // Productos que compró en este pedido
            var productosComprados = recomendacion.Videos
                .SelectMany(v => v.Productos)
                .Where(p => p.EnPedidoActual)
                .Select(p => p.NombreProducto)
                .Distinct()
                .ToList();

            sb.AppendLine("PRODUCTOS QUE ACABA DE COMPRAR:");
            foreach (var producto in productosComprados)
            {
                sb.AppendLine($"- {producto}");
            }
            sb.AppendLine();

            sb.AppendLine("VIDEOS DISPONIBLES:");
            foreach (var video in recomendacion.Videos.Take(2)) // Máximo 2 videos
            {
                sb.AppendLine($"Video: {video.Titulo}");
                sb.AppendLine($"YouTube ID: {video.VideoYoutubeId}");
                sb.AppendLine($"Thumbnail: https://img.youtube.com/vi/{video.VideoYoutubeId}/maxresdefault.jpg");
                sb.AppendLine($"Link video: https://www.youtube.com/watch?v={video.VideoYoutubeId}");
                sb.AppendLine("Productos en el video:");

                foreach (var producto in video.Productos)
                {
                    var marca = producto.YaComprado ? "(tiene)" : "(no tiene)";
                    // Solo informamos internamente, el prompt dice que no debe distinguir en el texto
                    sb.AppendLine($"  - {producto.NombreProducto} {marca}");

                    // Enlace al momento exacto del video donde aparece el producto
                    if (!string.IsNullOrEmpty(producto.EnlaceVideo))
                    {
                        sb.AppendLine($"    Link momento video: {producto.EnlaceVideo}");
                    }

                    // Enlace a la tienda online (de Prestashop, no de VideosProductos)
                    if (!string.IsNullOrWhiteSpace(producto.ProductoId) &&
                        urlsProductos.TryGetValue(producto.ProductoId, out string urlTienda))
                    {
                        sb.AppendLine($"    Link tienda: {urlTienda}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("IMPORTANTE: En el HTML, NO menciones cuáles tiene y cuáles no. Menciona todos los productos del video de forma natural, como si fueran técnicas que aprenderá.");
            sb.AppendLine("IMPORTANTE: Si incluyes enlaces a productos de la tienda, usa SOLO los enlaces proporcionados arriba (Link tienda). NO inventes URLs.");

            return sb.ToString();
        }
    }

    public interface IGeneradorContenidoCorreoPostCompra
    {
        Task<string> GenerarContenidoHtml(RecomendacionPostCompraDTO recomendacion);
    }
}
