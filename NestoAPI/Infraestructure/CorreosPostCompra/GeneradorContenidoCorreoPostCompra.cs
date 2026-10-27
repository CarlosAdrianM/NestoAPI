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
                    sb.AppendLine($"  Link vídeo: {AgregarUtm(producto.EnlaceVideoProducto, UTM_CONTENT_COMPRADO, producto.ProductoId)}");
                }
                else
                {
                    sb.AppendLine($"  Link vídeo: {AgregarUtm($"https://www.youtube.com/watch?v={producto.VideoYoutubeId}", UTM_CONTENT_COMPRADO, producto.ProductoId)}");
                }

                sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda, UTM_CONTENT_COMPRADO, producto.ProductoId)}");

                sb.AppendLine();
            }

            if (correo.ProductosRecomendados != null && correo.ProductosRecomendados.Any())
            {
                // Solo incluir recomendados que tengan enlace al vídeo y a la tienda (NestoAPI#706)
                var recomendadosConEnlace = RecomendadosConEnlaces(correo.ProductosRecomendados);

                if (recomendadosConEnlace.Any())
                {
                    sb.AppendLine("PRODUCTOS RECOMENDADOS (aparecen en los mismos vídeos, el cliente NO los ha comprado):");
                    sb.AppendLine("CADA producto DEBE mostrarse como enlace <a href> clicable al vídeo. NUNCA como texto plano.");
                    foreach (var producto in recomendadosConEnlace)
                    {
                        sb.AppendLine($"- Producto: {producto.NombreProducto}");
                        sb.AppendLine($"  ENLACE OBLIGATORIO al vídeo: {AgregarUtm(producto.EnlaceVideoProducto, UTM_CONTENT_RECOMENDADO, producto.ProductoId)}");
                        sb.AppendLine($"  Link tienda: {AgregarUtm(producto.EnlaceTienda, UTM_CONTENT_RECOMENDADO, producto.ProductoId)}");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("IMPORTANTE: TODOS los productos mencionados DEBEN tener enlace clicable. Si no tienes enlace para un producto, NO lo menciones.");
            sb.AppendLine("IMPORTANTE: Usa SOLO los enlaces proporcionados. NO inventes URLs.");

            return sb.ToString();
        }

        internal const string UTM_SOURCE = "correo_postcompra";
        internal const string UTM_CAMPAIGN = "tutoriales_postcompra";
        /// <summary>NestoAPI#706: utm_content distingue el producto comprado del recomendado; utm_term lleva su referencia.</summary>
        internal const string UTM_CONTENT_COMPRADO = "comprado";
        internal const string UTM_CONTENT_RECOMENDADO = "recomendado";

        /// <summary>
        /// La URL con las UTM del correo post-compra (EnlacesTienda: quita las que traiga y no duplica). Null si no hay
        /// URL válida. <paramref name="content"/> (comprado|recomendado) y <paramref name="term"/> (la referencia del
        /// producto) sirven para medir qué enlaces producen ventas.
        /// </summary>
        internal static string AgregarUtm(string url, string content = null, string term = null)
            => EnlacesTienda.ConUtm(url, UTM_SOURCE, EnlacesTienda.MEDIUM_EMAIL, UTM_CAMPAIGN, content, term);

        /// <summary>
        /// NestoAPI#706: un recomendado solo sale si tiene enlace al vídeo Y a la tienda (antes salía con href="").
        /// </summary>
        internal static List<ProductoRecomendadoDTO> RecomendadosConEnlaces(IEnumerable<ProductoRecomendadoDTO> recomendados)
            => recomendados?
                .Where(p => !string.IsNullOrEmpty(p.EnlaceVideoProducto) && EnlacesTienda.UrlValida(p.EnlaceTienda) != null)
                .ToList();

        #region Issue #152: Optimización - una sola plantilla por lote semanal

        /// <summary>
        /// NestoAPI#484: con 200 iban TODOS los nombres de la semana en una llamada y, a partir de
        /// 30-49 nombres, gpt-4o-mini dejaba de aplicar la regla de las empresas ("Hola The Look
        /// Getafe SL"). Los lotes de hasta 12 nunca fallaron; 20 deja margen sin multiplicar llamadas.
        /// </summary>
        internal const int TAMANO_LOTE_SALUDOS = 20;

        internal const string SALUDO_GENERICO = "Hola";

        /// <summary>Un saludo a una persona lleva como mucho un nombre de pila compuesto ("Hola María José").</summary>
        private const int MAX_PALABRAS_DE_NOMBRE_EN_SALUDO = 2;

        // Siglas de sociedad, con o sin puntos y con o sin espacios: S.L., SL, S.L.U., SLU, SLL,
        // S.A., SAU, S.R.L., SRL, S.C., SCP, C.B., S.COOP., SLNE, LTDA, LTD, GMBH, INC, LLC
        private static readonly Regex SiglasDeSociedad = new Regex(
            @"(?<![\p{L}\d])(S\.?\s?L\.?(\s?[UL]\.?)?|S\.?\s?A\.?(\s?U\.?)?|S\.?\s?R\.?\s?L\.?|S\.?\s?C\.?(\s?P\.?)?|C\.?\s?B\.?|S\.?\s?COOP\.?|SLNE|LTDA?|GMBH|INC\.?|LLC)(?![\p{L}\d])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> FormulasDeSaludo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hola", "buenos", "buenas", "días", "dias", "tardes", "noches", "qué", "que", "tal",
            "saludos", "bienvenido", "bienvenida", "estimado", "estimada", "querido", "querida", "hey", "de", "nuevo",
        };

        /// <summary>
        /// NestoAPI#484: guarda determinista sobre lo que devuelve el modelo. La instrucción de
        /// "si es una empresa, saludo genérico" no se puede fiar al prompt: con lotes grandes la
        /// ignoraba y salían "Hola The Look Getafe SL" o "Hola Eurobeauty Canarias, SRL". Si el
        /// saludo (o el nombre del cliente) lleva siglas de sociedad, o el saludo lleva más de dos
        /// palabras aparte de la fórmula, se sustituye por el genérico.
        /// </summary>
        internal static string SanearSaludo(string nombreCliente, string saludo)
        {
            if (string.IsNullOrWhiteSpace(saludo))
            {
                return SALUDO_GENERICO;
            }
            string limpio = saludo.Trim();
            if (SiglasDeSociedad.IsMatch(limpio) || (nombreCliente != null && SiglasDeSociedad.IsMatch(nombreCliente)))
            {
                return SALUDO_GENERICO;
            }
            int palabrasDeNombre = Regex.Replace(limpio, @"[¡!¿?,.:;()""']", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(p => !FormulasDeSaludo.Contains(p));
            return palabrasDeNombre > MAX_PALABRAS_DE_NOMBRE_EN_SALUDO ? SALUDO_GENERICO : limpio;
        }

        /// <summary>
        /// NestoAPI#544 (d): el nombre de pila que lleva un saludo ya saneado ("¡Hola Carlos!" →
        /// "Carlos"; "Buenos días" → ""). Sirve para componer otra fórmula (el aviso de facturas
        /// vencidas saluda con «Buenos días, Carlos:») reutilizando la decisión persona/empresa que
        /// toma <see cref="GenerarSaludosAsync"/> y la guarda de <see cref="SanearSaludo"/>.
        /// </summary>
        internal static string NombreDelSaludo(string saludo)
        {
            if (string.IsNullOrWhiteSpace(saludo))
            {
                return string.Empty;
            }
            IEnumerable<string> palabras = Regex.Replace(saludo, @"[¡!¿?,.:;()""']", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !FormulasDeSaludo.Contains(p));
            return string.Join(" ", palabras).Trim();
        }

        /// <summary>
        /// Genera una plantilla HTML reutilizable con placeholders.
        /// Se llama una vez por lote semanal.
        /// </summary>
        public async Task<PlantillaSemanal> GenerarPlantillaSemanalAsync()
        {
            // NestoAPI#706: la plantilla tiene que llevar {{ENLACE_TIENDA}} en los dos bloques de productos (si no, esos
            // enlaces, que son los que se miden con UTM, desaparecen). Si el modelo no lo pone, se le pide otra vez y,
            // si tampoco, se usa la plantilla fija. Si OpenAI no responde la primera vez, null como siempre (no se manda).
            PlantillaSemanal sinEnlaces = null;
            for (int intento = 1; intento <= INTENTOS_PLANTILLA; intento++)
            {
                PlantillaSemanal leida = await PedirPlantillaSemanalAsync().ConfigureAwait(false);
                if (leida == null)
                {
                    break;
                }
                if (PlantillaTieneEnlacesTienda(leida.HtmlPlantilla))
                {
                    return leida;
                }
                sinEnlaces = leida;
            }
            return sinEnlaces == null ? null : PlantillaSemanal.Fija();
        }

        /// <summary>NestoAPI#706: cuántas veces se le pide la plantilla al modelo antes de usar la fija.</summary>
        internal const int INTENTOS_PLANTILLA = 2;
        internal const string MARCA_ENLACE_TIENDA = "{{ENLACE_TIENDA}}";

        /// <summary>
        /// NestoAPI#706: true si la plantilla lleva {{ENLACE_TIENDA}} dentro del bloque de comprados y dentro del de
        /// recomendados.
        /// </summary>
        internal static bool PlantillaTieneEnlacesTienda(string html)
            => BloqueContiene(html, "{{#PRODUCTOS_COMPRADOS}}", "{{/PRODUCTOS_COMPRADOS}}", MARCA_ENLACE_TIENDA)
                && BloqueContiene(html, "{{#PRODUCTOS_RECOMENDADOS}}", "{{/PRODUCTOS_RECOMENDADOS}}", MARCA_ENLACE_TIENDA);

        /// <summary>
        /// NestoAPI#706: la plantilla que se aplica de verdad. Si la que llega no lleva los enlaces a la tienda (p. ej.
        /// una generada antes de este cambio que ya estaba programada en Hangfire), la fija.
        /// </summary>
        internal static PlantillaSemanal AsegurarEnlacesTienda(PlantillaSemanal plantilla)
            => plantilla == null || PlantillaTieneEnlacesTienda(plantilla.HtmlPlantilla) ? plantilla : PlantillaSemanal.Fija();

        private static bool BloqueContiene(string html, string marcadorInicio, string marcadorFin, string buscado)
        {
            if (string.IsNullOrEmpty(html))
            {
                return false;
            }
            int inicio = html.IndexOf(marcadorInicio, StringComparison.Ordinal);
            int fin = html.IndexOf(marcadorFin, StringComparison.Ordinal);
            return inicio >= 0 && fin > inicio
                && html.IndexOf(buscado, inicio, fin - inicio, StringComparison.Ordinal) >= 0;
        }

        private async Task<PlantillaSemanal> PedirPlantillaSemanalAsync()
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
                        resultado[nombres[i]] = SanearSaludo(nombres[i], saludos[i]);
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
                    { "{{ENLACE_VIDEO}}", AgregarUtm(p.EnlaceVideoProducto ?? $"https://www.youtube.com/watch?v={p.VideoYoutubeId}", UTM_CONTENT_COMPRADO, p.ProductoId) ?? "" },
                    { "{{ENLACE_TIENDA}}", AgregarUtm(p.EnlaceTienda, UTM_CONTENT_COMPRADO, p.ProductoId) ?? EnlacesTienda.ConUtm(EnlacesTienda.URL_TIENDA, UTM_SOURCE, EnlacesTienda.MEDIUM_EMAIL, UTM_CAMPAIGN, UTM_CONTENT_COMPRADO, p.ProductoId) },
                    { "{{THUMBNAIL}}", $"https://img.youtube.com/vi/{p.VideoYoutubeId}/hqdefault.jpg" }
                }).ToList());

            // Sustituir bloque condicional de recomendados
            var recomendadosConEnlace = RecomendadosConEnlaces(datos.ProductosRecomendados);

            if (recomendadosConEnlace != null && recomendadosConEnlace.Any())
            {
                // Mantener el bloque y sustituir los productos
                html = html.Replace("{{#SI_HAY_RECOMENDADOS}}", "").Replace("{{/SI_HAY_RECOMENDADOS}}", "");
                html = SustituirBloqueRepetible(html,
                    "{{#PRODUCTOS_RECOMENDADOS}}", "{{/PRODUCTOS_RECOMENDADOS}}",
                    recomendadosConEnlace.Select(p => new Dictionary<string, string>
                    {
                        { "{{NOMBRE_PRODUCTO}}", p.NombreProducto ?? "" },
                        { "{{ENLACE_VIDEO}}", AgregarUtm(p.EnlaceVideoProducto, UTM_CONTENT_RECOMENDADO, p.ProductoId) ?? "" },
                        { "{{ENLACE_TIENDA}}", AgregarUtm(p.EnlaceTienda, UTM_CONTENT_RECOMENDADO, p.ProductoId) }
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
- OBLIGATORIO: {{ENLACE_TIENDA}} tiene que aparecer como enlace (<a href=""{{ENLACE_TIENDA}}"">) DENTRO del bloque {{#PRODUCTOS_COMPRADOS}} y DENTRO del bloque {{#PRODUCTOS_RECOMENDADOS}} (por ejemplo, un enlace discreto «Ver en la tienda»)
- Firma: ""El equipo de Nueva Visión"" con teléfono 916 28 19 14 (WhatsApp)

Devuelve ASUNTO en la primera línea y luego el HTML del cuerpo, sin etiquetas <html>, <head> o <body>.";
        }

        #endregion
    }

    public class PlantillaSemanal
    {
        public string Asunto { get; set; }
        public string HtmlPlantilla { get; set; }

        internal const string ASUNTO_FIJO = "Sus vídeos tutoriales de esta semana";

        /// <summary>
        /// NestoAPI#706: la plantilla de reserva, de usted, cuando la del modelo no trae los enlaces a la tienda. Mismos
        /// marcadores y mismo estilo que se le piden al modelo (600 px, #2C5F7C, botón «Ver vídeo»).
        /// </summary>
        public static PlantillaSemanal Fija() => new PlantillaSemanal
        {
            Asunto = ASUNTO_FIJO,
            HtmlPlantilla = HTML_FIJO
        };

        internal const string HTML_FIJO = @"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""max-width:600px;margin:0 auto;font-family:Arial,Helvetica,sans-serif;color:#333333;"">
<tr><td style=""padding:16px 8px;font-size:15px;line-height:1.5;"">
<p style=""margin:0 0 12px 0;"">{{SALUDO}}</p>
<p style=""margin:0 0 16px 0;"">Le dejamos los vídeos tutoriales de los productos que ha recibido estos días. Esperamos que le resulten útiles en su día a día.</p>
</td></tr>
{{#PRODUCTOS_COMPRADOS}}<tr><td style=""padding:8px;border-top:1px solid #e5e5e5;"">
<p style=""margin:0 0 8px 0;font-size:16px;font-weight:bold;color:#2C5F7C;"">{{NOMBRE_PRODUCTO}}</p>
<a href=""{{ENLACE_VIDEO}}"" target=""_blank""><img src=""{{THUMBNAIL}}"" alt=""Vídeo: {{VIDEO_TITULO}}"" width=""320"" style=""width:320px;max-width:100%;height:auto;border:0;display:block;""></a>
<p style=""margin:12px 0 4px 0;""><a href=""{{ENLACE_VIDEO}}"" target=""_blank"" style=""display:inline-block;padding:10px 20px;background-color:#2C5F7C;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;"">Ver vídeo</a></p>
<p style=""margin:4px 0 8px 0;font-size:13px;""><a href=""{{ENLACE_TIENDA}}"" target=""_blank"" style=""color:#2C5F7C;"">Ver el producto en la tienda online</a></p>
</td></tr>{{/PRODUCTOS_COMPRADOS}}
{{#SI_HAY_RECOMENDADOS}}<tr><td style=""padding:16px 8px 8px 8px;border-top:1px solid #e5e5e5;font-size:14px;line-height:1.5;"">
<p style=""margin:0 0 8px 0;"">En esos mismos vídeos aparecen también estos productos, por si le interesan:</p>
<ul style=""margin:0;padding-left:20px;"">
{{#PRODUCTOS_RECOMENDADOS}}<li style=""margin:0 0 6px 0;""><a href=""{{ENLACE_VIDEO}}"" target=""_blank"" style=""color:#2C5F7C;"">{{NOMBRE_PRODUCTO}}</a> · <a href=""{{ENLACE_TIENDA}}"" target=""_blank"" style=""color:#2C5F7C;font-size:13px;"">ver en la tienda</a></li>{{/PRODUCTOS_RECOMENDADOS}}
</ul>
</td></tr>{{/SI_HAY_RECOMENDADOS}}
<tr><td style=""padding:16px 8px;font-size:14px;line-height:1.5;"">
<p style=""margin:0;"">El equipo de Nueva Visión<br/>916 28 19 14 (WhatsApp)</p>
</td></tr>
</table>";
    }

    public interface IGeneradorContenidoCorreoPostCompra
    {
        Task<string> GenerarContenidoHtml(CorreoPostCompraClienteDTO correo);
    }
}
