using Newtonsoft.Json;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

                sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda)}");

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
                        sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda)}");
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

        #region Issue #152: Optimización - una sola plantilla por lote semanal

        internal const int TAMANO_LOTE_SALUDOS = 200;

        /// <summary>
        /// Genera una plantilla HTML reutilizable con placeholders.
        /// Se llama una vez por lote semanal.
        /// </summary>
        public async Task<PlantillaSemanal> GenerarPlantillaSemanalAsync()
        {
            var systemPrompt = CrearSystemPromptPlantilla();
            var userMessage = "Genera la plantilla para el correo de esta semana.";

            var respuesta = await _servicioOpenAI.GenerarCorreoHtmlAsync(systemPrompt, userMessage)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(respuesta))
            {
                return null;
            }

            string asunto = null;
            string html = respuesta;

            if (ExtraerAsuntoYCuerpo(respuesta, out asunto))
            {
                html = respuesta.Substring(respuesta.IndexOf('\n') + 1).TrimStart();
            }

            return new PlantillaSemanal
            {
                Asunto = asunto ?? "Tus vídeos tutoriales de esta semana",
                HtmlPlantilla = html
            };
        }

        /// <summary>
        /// Genera saludos personalizados para una lista de nombres de clientes.
        /// Divide en lotes de TAMANO_LOTE_SALUDOS para evitar respuestas truncadas.
        /// </summary>
        public async Task<Dictionary<string, string>> GenerarSaludosAsync(List<string> nombresClientes)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (nombresClientes == null || !nombresClientes.Any())
            {
                return resultado;
            }

            var lotes = PartirEnLotes(nombresClientes, TAMANO_LOTE_SALUDOS);

            foreach (var lote in lotes)
            {
                var saludosLote = await GenerarSaludosLoteAsync(lote).ConfigureAwait(false);
                foreach (var kvp in saludosLote)
                {
                    resultado[kvp.Key] = kvp.Value;
                }
            }

            return resultado;
        }

        internal async Task<Dictionary<string, string>> GenerarSaludosLoteAsync(List<string> nombres)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var systemPrompt = @"Eres un asistente que genera saludos personalizados para correos de marketing de una empresa de estética profesional.

Para cada nombre de cliente, devuelve un saludo apropiado:
- Si parece una EMPRESA (contiene S.L., S.A., S.C., C.B., SLU, siglas en mayúsculas, palabras como ""centro"", ""peluquería"", ""estética"", ""distribuciones"", etc.), usa un saludo genérico variado: ""Hola"", ""Buenos días"", ""¡Hola!"", etc.
- Si parece una PERSONA, usa el nombre de pila de forma natural: ""Hola María"", ""¡Hola Carlos!"", etc.
- Varía los saludos, no uses siempre el mismo.
- Cada saludo debe ser COMPLETO y AUTOCONTENIDO, listo para insertar directamente en el correo sin añadir nada más alrededor.

FORMATO: Responde SOLO con un JSON array de strings, en el mismo orden que la entrada. Sin explicaciones.
Ejemplo: entrada [""MARIA PELUQUEROS S.L."", ""Rosa Martínez""] → respuesta [""Hola"", ""Hola Rosa""]";

            var nombresJson = JsonConvert.SerializeObject(nombres);

            var respuesta = await _servicioOpenAI.GenerarContenidoAsync(
                systemPrompt, nombresJson, maxTokens: nombres.Count * 15, temperature: 0.5)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(respuesta))
            {
                // Fallback: saludo genérico para todos
                foreach (var nombre in nombres)
                {
                    resultado[nombre] = "Hola";
                }
                return resultado;
            }

            try
            {
                var saludos = JsonConvert.DeserializeObject<List<string>>(respuesta.Trim());
                if (saludos != null && saludos.Count == nombres.Count)
                {
                    for (int i = 0; i < nombres.Count; i++)
                    {
                        resultado[nombres[i]] = saludos[i];
                    }
                    return resultado;
                }
            }
            catch (JsonException) { }

            // Fallback si el parsing falla
            foreach (var nombre in nombres)
            {
                resultado[nombre] = "Hola";
            }
            return resultado;
        }

        /// <summary>
        /// Aplica la plantilla semanal a los datos de un cliente concreto.
        /// Sustituye placeholders por datos reales.
        /// </summary>
        internal static string AplicarPlantilla(string htmlPlantilla, string saludo,
            CorreoPostCompraClienteDTO datos)
        {
            if (string.IsNullOrEmpty(htmlPlantilla) || datos == null)
            {
                return null;
            }

            var html = htmlPlantilla;

            // Sustituir saludo
            html = html.Replace("{{SALUDO}}", saludo ?? "Hola");

            // Sustituir bloque de productos comprados
            html = SustituirBloqueRepetible(html,
                "{{#PRODUCTOS_COMPRADOS}}", "{{/PRODUCTOS_COMPRADOS}}",
                datos.ProductosComprados?.Select(p => new Dictionary<string, string>
                {
                    { "{{NOMBRE_PRODUCTO}}", p.NombreProducto ?? "" },
                    { "{{YOUTUBE_ID}}", p.VideoYoutubeId ?? "" },
                    { "{{VIDEO_TITULO}}", p.VideoTitulo ?? "" },
                    { "{{ENLACE_VIDEO}}", AgregarUtm(p.EnlaceVideoProducto ?? $"https://www.youtube.com/watch?v={p.VideoYoutubeId}") },
                    { "{{ENLACE_TIENDA}}", AgregarUtm(p.EnlaceTienda ?? "") },
                    { "{{THUMBNAIL}}", $"https://img.youtube.com/vi/{p.VideoYoutubeId}/hqdefault.jpg" }
                }).ToList());

            // Sustituir bloque condicional de recomendados
            var recomendadosConEnlace = datos.ProductosRecomendados?
                .Where(p => !string.IsNullOrEmpty(p.EnlaceVideoProducto))
                .ToList();

            if (recomendadosConEnlace != null && recomendadosConEnlace.Any())
            {
                // Mantener el bloque y sustituir los productos
                html = html.Replace("{{#SI_HAY_RECOMENDADOS}}", "").Replace("{{/SI_HAY_RECOMENDADOS}}", "");
                html = SustituirBloqueRepetible(html,
                    "{{#PRODUCTOS_RECOMENDADOS}}", "{{/PRODUCTOS_RECOMENDADOS}}",
                    recomendadosConEnlace.Select(p => new Dictionary<string, string>
                    {
                        { "{{NOMBRE_PRODUCTO}}", p.NombreProducto ?? "" },
                        { "{{ENLACE_VIDEO}}", AgregarUtm(p.EnlaceVideoProducto) },
                        { "{{ENLACE_TIENDA}}", AgregarUtm(p.EnlaceTienda ?? "") }
                    }).ToList());
            }
            else
            {
                // Eliminar todo el bloque condicional
                html = EliminarBloque(html, "{{#SI_HAY_RECOMENDADOS}}", "{{/SI_HAY_RECOMENDADOS}}");
            }

            return html;
        }

        internal static string SustituirBloqueRepetible(string html,
            string marcadorInicio, string marcadorFin,
            List<Dictionary<string, string>> items)
        {
            int inicio = html.IndexOf(marcadorInicio, StringComparison.Ordinal);
            int fin = html.IndexOf(marcadorFin, StringComparison.Ordinal);

            if (inicio < 0 || fin < 0 || fin <= inicio)
            {
                return html;
            }

            string plantillaBloque = html.Substring(
                inicio + marcadorInicio.Length,
                fin - inicio - marcadorInicio.Length);

            var sb = new StringBuilder();

            if (items != null)
            {
                foreach (var item in items)
                {
                    string bloque = plantillaBloque;
                    foreach (var kvp in item)
                    {
                        bloque = bloque.Replace(kvp.Key, kvp.Value);
                    }
                    sb.Append(bloque);
                }
            }

            return html.Substring(0, inicio) + sb.ToString() + html.Substring(fin + marcadorFin.Length);
        }

        internal static string EliminarBloque(string html, string marcadorInicio, string marcadorFin)
        {
            int inicio = html.IndexOf(marcadorInicio, StringComparison.Ordinal);
            int fin = html.IndexOf(marcadorFin, StringComparison.Ordinal);

            if (inicio < 0 || fin < 0)
            {
                return html;
            }

            return html.Substring(0, inicio) + html.Substring(fin + marcadorFin.Length);
        }

        internal static List<List<T>> PartirEnLotes<T>(List<T> lista, int tamanoLote)
        {
            var lotes = new List<List<T>>();
            for (int i = 0; i < lista.Count; i += tamanoLote)
            {
                lotes.Add(lista.GetRange(i, Math.Min(tamanoLote, lista.Count - i)));
            }
            return lotes;
        }

        internal string CrearSystemPromptPlantilla()
        {
            return @"Eres un experto en email marketing para una empresa de productos de estética y peluquería profesional llamada Nueva Visión.

Tu objetivo es generar una PLANTILLA HTML reutilizable para correos post-compra. La plantilla usa placeholders que se sustituirán después con datos reales de cada cliente.

REGLA CRÍTICA SOBRE EL SALUDO:
- El placeholder {{SALUDO}} ya contiene el saludo completo (ej: ""Hola María"", ""¡Hola!"", ""Buenos días"").
- El HTML de la plantilla NO debe incluir ningún saludo, ""Hola"", ""Buenos días"" ni texto introductorio alrededor de {{SALUDO}}.
- Correcto: {{SALUDO}}, te escribimos porque...
- INCORRECTO: ¡Hola {{SALUDO}}! / Hola {{SALUDO}}, te escribimos...

PLACEHOLDERS DISPONIBLES:
- {{SALUDO}} → Se reemplaza por un saludo personalizado completo (ej: ""Hola María"" o ""¡Hola!"")
- {{#PRODUCTOS_COMPRADOS}}...{{/PRODUCTOS_COMPRADOS}} → Bloque que se repite por cada producto (1-3)
  Dentro del bloque:
  - {{NOMBRE_PRODUCTO}} → Nombre del producto
  - {{YOUTUBE_ID}} → ID del vídeo de YouTube
  - {{THUMBNAIL}} → URL del thumbnail (https://img.youtube.com/vi/{{YOUTUBE_ID}}/hqdefault.jpg)
  - {{ENLACE_VIDEO}} → URL completa del vídeo
  - {{VIDEO_TITULO}} → Título del vídeo
  - {{ENLACE_TIENDA}} → URL del producto en la tienda online
- {{#SI_HAY_RECOMENDADOS}}...{{/SI_HAY_RECOMENDADOS}} → Bloque condicional, se elimina si no hay recomendados
  - {{#PRODUCTOS_RECOMENDADOS}}...{{/PRODUCTOS_RECOMENDADOS}} → Bloque que se repite (0-4 productos)
    Dentro: {{NOMBRE_PRODUCTO}}, {{ENLACE_VIDEO}}, {{ENLACE_TIENDA}}

FORMATO DE RESPUESTA:
La PRIMERA línea debe ser ""ASUNTO: [asunto del correo]"". El asunto puede usar {{NOMBRE_PRODUCTO}} del primer producto.
El resto es el HTML del cuerpo.

PRINCIPIOS:
- Tono cercano y profesional, como un asesor de confianza
- El cliente NO debe sentir que le quieres vender nada
- NO usar palabras como ""oferta"", ""descuento"", ""compra"", ""promoción""

RESTRICCIONES TÉCNICAS:
- Máximo 150 palabras de texto (sin contar HTML ni placeholders)
- Diseño limpio, mobile-first, ancho máximo 600px
- Colores suaves (principal: #2C5F7C)
- Botones CTA suaves: ""Ver vídeo"" (NO ""Comprar ahora"")
- Thumbnail como enlace clicable al vídeo
- Cada producto DEBE tener enlace clicable
- Firma: ""El equipo de Nueva Visión"" con teléfono 916 28 19 14 (WhatsApp)

Devuelve ASUNTO en la primera línea y luego el HTML del cuerpo, sin etiquetas <html>, <head> o <body>.";
        }

        #endregion
    }

    public class PlantillaSemanal
    {
        public string Asunto { get; set; }
        public string HtmlPlantilla { get; set; }
    }

    public interface IGeneradorContenidoCorreoPostCompra
    {
        Task<string> GenerarContenidoHtml(CorreoPostCompraClienteDTO correo);
    }
}
