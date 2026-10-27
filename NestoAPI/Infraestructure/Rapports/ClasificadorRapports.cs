using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>Un rapport con comentario que hay que clasificar (lo mínimo: ni cliente, ni vendedor, ni usuario).</summary>
    public class RapportAClasificar
    {
        public int NumOrden { get; set; }
        public DateTime Fecha { get; set; }
        public string Comentarios { get; set; }
    }

    /// <summary>Una fila de SeguimientoClienteClasificacion.</summary>
    public class ClasificacionRapport
    {
        public int NumOrden { get; set; }
        /// <summary>Código de <see cref="ResultadoRapport"/>.</summary>
        public string Resultado { get; set; }
        public DateTime? ProximoContacto { get; set; }
        /// <summary>0..1.</summary>
        public decimal Confianza { get; set; }
        public string Modelo { get; set; }
    }

    /// <summary>
    /// NestoAPI#716 (2.1, interino): mientras los vendedores no marquen el resultado del rapport, se deduce de los
    /// <c>Comentarios</c> con OpenAI al mismo catálogo (<see cref="ResultadoRapport"/>) y se guarda aparte en
    /// <c>SeguimientoClienteClasificacion</c> (nunca pisa lo que marque el vendedor). Sirve también para rellenar
    /// 2024-2026. Lógica pura: prompt, anonimizado, mensaje por lotes y lectura de la respuesta; la llamada a OpenAI y
    /// la BD están en <see cref="ClasificacionRapportsJobsService"/>.
    /// <para>Datos personales: a OpenAI solo va el texto del comentario, con teléfonos y correos tapados
    /// (<see cref="Anonimizar"/>), más la fecha del rapport (para «la semana que viene»). Ni cliente, ni nombre, ni
    /// vendedor.</para>
    /// </summary>
    public static class ClasificadorRapports
    {
        public const string MODELO = "gpt-4o-mini";
        public const int TAMANO_LOTE = 20;
        /// <summary>Un comentario más largo se recorta: con esto sobra para clasificarlo y acota el coste.</summary>
        public const int MAXIMO_CARACTERES_COMENTARIO = 600;
        /// <summary>~60 tokens por rapport de respuesta; con margen para el lote.</summary>
        public const int MAX_TOKENS_RESPUESTA = 1500;

        public static readonly string PROMPT_SISTEMA =
            "Eres el asistente de un distribuidor de productos de peluquería y estética que vende a salones y centros " +
            "profesionales. Los comerciales anotan en un «rapport» lo que ha pasado en cada llamada o visita a un cliente. " +
            "Tu tarea es clasificar cada rapport en UN resultado del catálogo y, si el texto lo dice, cuándo toca volver a contactar.\n\n" +
            "Catálogo de resultados (usa el código exacto):\n" +
            "- Pedido: el cliente ha hecho pedido o lo va a pasar ya.\n" +
            "- PediraMasTarde: no pide ahora pero pedirá más adelante (tiene stock, espera, «llamar en X semanas»).\n" +
            "- NoNecesita: no necesita nada, no trabaja esos productos o no le interesan.\n" +
            "- Precio: no compra por precio o pide descuento que no se le da.\n" +
            "- Competencia: compra a otro proveedor o distribuidor.\n" +
            "- CerradoTraspaso: el centro ha cerrado, está en traspaso o ha cambiado de dueño.\n" +
            "- SinStock: quería algo que no teníamos o había falta de producto.\n" +
            "- NoDecide: no se ha podido hablar con quien decide (ausente, ocupado, que llamemos otro día sin más).\n" +
            "- SoloInformacion: solo pide información, catálogo, formación o muestras sin intención de pedido.\n" +
            "- Otro: nada de lo anterior (gestiones administrativas, cobros, incidencias, devoluciones, texto sin contenido).\n\n" +
            "Entrada: una línea por rapport con el formato «id | fecha del rapport (yyyy-MM-dd) | comentario».\n" +
            "Salida: SOLO un array JSON, sin texto alrededor ni marcas de código, con un objeto por rapport:\n" +
            "{\"id\": <id>, \"resultado\": \"<código>\", \"proximoContacto\": \"yyyy-MM-dd\" o null, \"confianza\": <0 a 1>}\n" +
            "proximoContacto: solo si el comentario dice cuándo volver a llamar o visitar (fecha o plazo relativo a la fecha " +
            "del rapport); si no, null. confianza: cuánto estás seguro del resultado. Responde en el mismo orden de entrada " +
            "y no inventes ids.";

        private static readonly Regex Correo = new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Teléfonos: 9 o más dígitos seguidos o separados por espacios, puntos o guiones, con prefijo +34 / 0034 opcional.
        private static readonly Regex Telefono = new Regex(@"(\+?\d{1,3}[\s.-]?)?(?<!\d)(\d[\s.-]?){8,}\d(?!\d)", RegexOptions.Compiled);

        /// <summary>Tapa correos y teléfonos. Lo demás (nombres de pila, marcas, productos) se deja: es lo que clasifica.</summary>
        public static string Anonimizar(string comentario)
        {
            if (string.IsNullOrWhiteSpace(comentario))
            {
                return string.Empty;
            }
            string texto = Correo.Replace(comentario, "[correo]");
            texto = Telefono.Replace(texto, m => m.Value.Count(char.IsDigit) >= 9 ? "[teléfono]" : m.Value);
            return Regex.Replace(texto, @"\s+", " ").Trim();
        }

        /// <summary>El mensaje de usuario de un lote: una línea por rapport, comentario anonimizado y recortado.</summary>
        public static string ConstruirMensaje(IEnumerable<RapportAClasificar> lote)
        {
            var sb = new StringBuilder();
            foreach (RapportAClasificar r in lote ?? Enumerable.Empty<RapportAClasificar>())
            {
                string texto = Anonimizar(r.Comentarios).Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
                if (texto.Length > MAXIMO_CARACTERES_COMENTARIO)
                {
                    texto = texto.Substring(0, MAXIMO_CARACTERES_COMENTARIO) + "…";
                }
                _ = sb.Append(r.NumOrden).Append(" | ").Append(r.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" | ").AppendLine(texto);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Lee la respuesta del modelo. Tolera las marcas ```json y texto alrededor del array. Solo devuelve filas con un
        /// id del lote y un resultado del catálogo; una fecha de próximo contacto anterior al rapport o a más de dos años
        /// se descarta (se guarda el resultado sin fecha). Con una respuesta que no es JSON devuelve lista vacía.
        /// </summary>
        public static List<ClasificacionRapport> ParsearRespuesta(string respuesta, IEnumerable<RapportAClasificar> lote, string modelo = MODELO)
        {
            var resultado = new List<ClasificacionRapport>();
            if (string.IsNullOrWhiteSpace(respuesta) || lote == null)
            {
                return resultado;
            }
            Dictionary<int, RapportAClasificar> porId = lote.GroupBy(r => r.NumOrden).ToDictionary(g => g.Key, g => g.First());

            JArray array = ExtraerArray(respuesta);
            if (array == null)
            {
                return resultado;
            }
            var vistos = new HashSet<int>();
            foreach (JToken token in array)
            {
                if (!(token is JObject objeto))
                {
                    continue;
                }
                int? id = LeerEntero(objeto["id"]);
                if (!id.HasValue || !porId.TryGetValue(id.Value, out RapportAClasificar rapport) || !vistos.Add(id.Value))
                {
                    continue;
                }
                string codigo = ResultadoRapport.Normalizar(objeto["resultado"]?.Type == JTokenType.String ? (string)objeto["resultado"] : null);
                if (codigo == null)
                {
                    continue;
                }
                resultado.Add(new ClasificacionRapport
                {
                    NumOrden = id.Value,
                    Resultado = codigo,
                    ProximoContacto = LeerFecha(objeto["proximoContacto"], rapport.Fecha),
                    Confianza = LeerConfianza(objeto["confianza"]),
                    Modelo = modelo
                });
            }
            return resultado;
        }

        private static JArray ExtraerArray(string respuesta)
        {
            string texto = respuesta.Trim();
            int inicio = texto.IndexOf('[');
            int fin = texto.LastIndexOf(']');
            if (inicio < 0 || fin <= inicio)
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<JArray>(texto.Substring(inicio, fin - inicio + 1));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int? LeerEntero(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type == JTokenType.Integer)
            {
                return (int)token;
            }
            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor) ? valor : (int?)null;
        }

        internal static DateTime? LeerFecha(JToken token, DateTime fechaRapport)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            DateTime fecha;
            if (token.Type == JTokenType.Date)
            {
                fecha = (DateTime)token;
            }
            else if (!DateTime.TryParseExact(token.ToString().Trim(), new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            {
                return null;
            }
            fecha = fecha.Date;
            return fecha < fechaRapport.Date || fecha > fechaRapport.Date.AddYears(2) ? (DateTime?)null : fecha;
        }

        internal static decimal LeerConfianza(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0m;
            }
            decimal valor;
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                // JToken.ToString() usa la cultura del hilo («0,9» en español): se lee el número, no el texto.
                valor = Convert.ToDecimal((double)token, CultureInfo.InvariantCulture);
            }
            else if (!decimal.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out valor))
            {
                return 0m;
            }
            if (valor > 1m && valor <= 100m)
            {
                valor /= 100m; // «85» en vez de «0.85»
            }
            return Math.Round(Math.Max(0m, Math.Min(1m, valor)), 3);
        }

        /// <summary>Trocea en lotes de <see cref="TAMANO_LOTE"/>.</summary>
        public static IEnumerable<List<RapportAClasificar>> Lotes(IEnumerable<RapportAClasificar> rapports, int tamano = TAMANO_LOTE)
        {
            var lote = new List<RapportAClasificar>(tamano);
            foreach (RapportAClasificar r in rapports ?? Enumerable.Empty<RapportAClasificar>())
            {
                lote.Add(r);
                if (lote.Count >= tamano)
                {
                    yield return lote;
                    lote = new List<RapportAClasificar>(tamano);
                }
            }
            if (lote.Count > 0)
            {
                yield return lote;
            }
        }
    }
}
