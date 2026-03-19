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

            var respuesta = await _servicioOpenAI.GenerarCorreoHtmlAsync(systemPrompt, userMessage).ConfigureAwait(false);

            if (ExtraerAsuntoYCuerpo(respuesta, out _))
            {
                return respuesta.Substring(respuesta.IndexOf('\n') + 1).TrimStart();
            }

            return respuesta;
        }

        /// <summary>
        /// Genera el contenido HTML y extrae el asunto generado por OpenAI.
        /// </summary>
        public async Task<(string Asunto, string Html)> GenerarContenidoConAsunto(CorreoPostCompraClienteDTO correo)
        {
            if (correo == null ||
                correo.ProductosComprados == null ||
                !correo.ProductosComprados.Any())
            {
                return (null, null);
            }

            var systemPrompt = CrearSystemPrompt();
            var userMessage = CrearUserMessage(correo);

            var respuesta = await _servicioOpenAI.GenerarCorreoHtmlAsync(systemPrompt, userMessage).ConfigureAwait(false);

            if (string.IsNullOrEmpty(respuesta))
            {
                return (null, null);
            }

            if (ExtraerAsuntoYCuerpo(respuesta, out string asunto))
            {
                var html = respuesta.Substring(respuesta.IndexOf('\n') + 1).TrimStart();
                return (asunto, html);
            }

            return (null, respuesta);
        }

        /// <summary>
        /// Extrae el asunto de la primera línea si tiene el formato "ASUNTO: ..."
        /// </summary>
        internal static bool ExtraerAsuntoYCuerpo(string respuesta, out string asunto)
        {
            asunto = null;
            if (string.IsNullOrEmpty(respuesta))
            {
                return false;
            }

            var primeraLinea = respuesta.Split('\n')[0].Trim();
            if (primeraLinea.StartsWith("ASUNTO:", System.StringComparison.OrdinalIgnoreCase))
            {
                asunto = primeraLinea.Substring("ASUNTO:".Length).Trim();
                return true;
            }

            return false;
        }

        internal string CrearSystemPrompt()
        {
            return @"Eres un experto en email marketing para una empresa de productos de estética y peluquería profesional llamada Nueva Visión.

Tu objetivo es generar un correo HTML personalizado que ayude al cliente a sacar partido a los productos que ha comprado recientemente, mostrándole vídeos tutoriales.

PRINCIPIOS CLAVE:
- El correo debe parecer escrito por una persona REAL, no generado automáticamente
- CADA correo debe ser DIFERENTE: varía el saludo, el tono, las frases, la estructura. Sé creativo
- Si el nombre del cliente parece una empresa (contiene S.L., S.A., siglas, etc.), NO uses el nombre en el saludo. Usa algo genérico como ""Hola"", ""Buenos días"", etc.
- Si el nombre parece de persona, úsalo de forma natural (solo el nombre de pila, no apellidos)
- El cliente NO debe sentir que le quieres vender nada
- Tono cercano y profesional, como un asesor de confianza que escribe un email personal
- NO repitas fórmulas: evita usar siempre ""sacar el máximo partido"" u otras muletillas

FORMATO DE RESPUESTA:
La PRIMERA línea debe ser el asunto del correo, precedida por ""ASUNTO: "". El asunto debe ser natural, variado y personalizado. NO uses siempre la misma fórmula. Ejemplos de variación:
- ""Tus vídeos de esta semana""
- ""Mira lo que puedes hacer con [producto]""
- ""Te dejamos unos tutoriales que te van a encantar""
- ""[Nombre], estos vídeos son para ti""
El resto es el HTML del cuerpo del correo.

CONTENIDO:
1. Saludo personalizado y variado
2. Breve intro explicando por qué le escribes (que le envías vídeos de los productos que ha comprado). Varía la redacción en cada correo
3. Para cada producto comprado: nombre del producto, thumbnail del vídeo (como enlace clicable) y botón ""Ver vídeo""
4. Si hay productos recomendados: mostrarlos con ENLACES CLICABLES al vídeo donde aparecen. NUNCA mencionar un producto sin enlace. Cada producto recomendado debe ser un enlace <a href> al vídeo correspondiente
5. Firma: ""El equipo de Nueva Visión"" con teléfono 916 28 19 14 (WhatsApp)

REGLA CRÍTICA SOBRE ENLACES:
- TODOS los productos mencionados en el correo DEBEN tener un enlace clicable
- NUNCA menciones un producto como texto plano sin enlace
- Los productos recomendados deben enlazar al vídeo donde aparecen (usa el ""Link vídeo"" proporcionado)
- Si un producto no tiene enlace en los datos, NO lo menciones en el correo

RESTRICCIONES TÉCNICAS:
- Máximo 150 palabras de texto (sin contar HTML)
- Diseño limpio, mobile-first, ancho máximo 600px
- Colores suaves (principal: #2C5F7C), sin urgencias ni descuentos
- Botones CTA suaves: ""Ver vídeo"" (NO ""Comprar ahora"")
- NO usar palabras como ""oferta"", ""descuento"", ""compra"", ""promoción""
- Thumbnail del vídeo: https://img.youtube.com/vi/{YOUTUBE_ID}/hqdefault.jpg (enlazar a la URL del vídeo)
- Usar SOLO las URLs proporcionadas, NO inventar enlaces
- Todos los enlaces deben incluir los parámetros UTM proporcionados
- Las imágenes deben tener siempre alt descriptivo y un ancho fijo en píxeles (no %)

Devuelve ASUNTO en la primera línea y luego el HTML del cuerpo, sin etiquetas <html>, <head> o <body>.";
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
                sb.AppendLine($"  Thumbnail: https://img.youtube.com/vi/{producto.VideoYoutubeId}/hqdefault.jpg");

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
                // Solo incluir recomendados que tengan enlace al vídeo
                var recomendadosConEnlace = correo.ProductosRecomendados
                    .Where(p => !string.IsNullOrEmpty(p.EnlaceVideoProducto))
                    .ToList();

                if (recomendadosConEnlace.Any())
                {
                    sb.AppendLine("PRODUCTOS RECOMENDADOS (aparecen en los mismos vídeos, el cliente NO los ha comprado):");
                    sb.AppendLine("CADA producto DEBE mostrarse como enlace <a href> clicable al vídeo. NUNCA como texto plano.");
                    foreach (var producto in recomendadosConEnlace)
                    {
                        sb.AppendLine($"- Producto: {producto.NombreProducto}");
                        sb.AppendLine($"  ENLACE OBLIGATORIO al vídeo: {AgregarUtm(producto.EnlaceVideoProducto)}");

                        if (!string.IsNullOrEmpty(producto.EnlaceTienda))
                        {
                            sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda)}");
                        }
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("IMPORTANTE: TODOS los productos mencionados DEBEN tener enlace clicable. Si no tienes enlace para un producto, NO lo menciones.");
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
