using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#609: las reglas deterministas de la revisión del concepto de un enlace de pago (sin IA ni BD):
    /// la normalización, las guardas que deciden si una propuesta de la IA vale y el cálculo de cambios.
    /// </summary>
    public static class ReglasRevisionConcepto
    {
        /// <summary>
        /// Lo que admite Redsys en Ds_Merchant_ProductDescription (125). PagosTPV.Descripcion es nvarchar(500),
        /// así que el límite real es el de Redsys. Una propuesta que lo pase (y sea más larga que el original) se descarta.
        /// </summary>
        public const int LONGITUD_MAXIMA = 125;

        /// <summary>Series de factura y pedido que existen (CabFacturaVta, 2025-2026).</summary>
        internal static readonly string[] SERIES = { "NV", "GB", "CV", "RV", "EV", "DV", "UL", "RC" };

        private static readonly Regex RegexSerie = new Regex(
            @"(?<![\p{L}\p{N}])(" + string.Join("|", SERIES) + @")(\d{5,9})(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Palabra con al menos una cifra; los signos de puntuación que la rodean no cuentan («29/09,» = «29/09»).
        private static readonly Regex RegexTokenConCifra = new Regex(@"[^\s]*\d[^\s]*", RegexOptions.CultureInvariant);

        private static readonly char[] PuntuacionBorde = { '.', ',', ';', ':', '(', ')', '"', '\'', '¡', '!', '¿', '?', '«', '»' };

        /// <summary>Lo determinista: mayúsculas de <see cref="FormateadorConcepto.Normalizar"/> y series en mayúsculas.</summary>
        public static string AplicarDeterminista(string concepto)
        {
            if (string.IsNullOrWhiteSpace(concepto))
            {
                return concepto?.Trim() ?? string.Empty;
            }
            string normalizado = FormateadorConcepto.Normalizar(concepto);
            return SeriesEnMayusculas(normalizado);
        }

        /// <summary>nv2613646 → NV2613646 (solo las series reales seguidas de cifras).</summary>
        public static string SeriesEnMayusculas(string texto)
        {
            return string.IsNullOrEmpty(texto)
                ? texto
                : RegexSerie.Replace(texto, m => m.Groups[1].Value.ToUpperInvariant() + m.Groups[2].Value);
        }

        /// <summary>Las palabras con cifras (pedidos, facturas, fechas, importes), en orden y sin la puntuación del borde.</summary>
        public static List<string> TokensConCifras(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return new List<string>();
            }
            return RegexTokenConCifra.Matches(texto)
                .Cast<Match>()
                .Select(m => m.Value.Trim(PuntuacionBorde))
                .Where(t => t.Any(char.IsDigit))
                .ToList();
        }

        /// <summary>
        /// La guarda de números: la propuesta solo vale si conserva, en el mismo orden, exactamente las mismas palabras
        /// con cifras que el original (sin distinguir mayúsculas: nv2613646 y NV2613646 son la misma).
        /// </summary>
        public static bool RespetaNumeros(string original, string propuesta)
        {
            List<string> deOriginal = TokensConCifras(original);
            List<string> dePropuesta = TokensConCifras(propuesta);
            return deOriginal.Count == dePropuesta.Count
                && deOriginal.Zip(dePropuesta, (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase)).All(x => x);
        }

        /// <summary>
        /// Si la propuesta de la IA vale: no vacía, mismos números, dentro del límite de longitud y sin añadir ni quitar
        /// información (como mucho dos palabras de diferencia: «Master classn» → «Masterclass»).
        /// </summary>
        public static bool PropuestaValida(string original, string propuesta)
        {
            if (string.IsNullOrWhiteSpace(propuesta))
            {
                return false;
            }
            if (!RespetaNumeros(original, propuesta))
            {
                return false;
            }
            if (propuesta.Length > LONGITUD_MAXIMA && propuesta.Length > (original?.Length ?? 0))
            {
                return false;
            }
            int palabrasOriginal = Palabras(original).Count;
            int palabrasPropuesta = Palabras(propuesta).Count;
            return Math.Abs(palabrasOriginal - palabrasPropuesta) <= 2;
        }

        /// <summary>
        /// Limpia lo que devuelve la IA: primera línea con texto, sin comillas alrededor ni un «Concepto:» delante.
        /// </summary>
        public static string LimpiarRespuestaIA(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return null;
            }
            string linea = respuesta
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
            if (linea == null)
            {
                return null;
            }
            if (linea.StartsWith("Concepto:", StringComparison.OrdinalIgnoreCase))
            {
                linea = linea.Substring("Concepto:".Length).Trim();
            }
            linea = linea.Trim('"', '«', '»', '“', '”', '`').Trim();
            return linea.Length == 0 ? null : linea;
        }

        /// <summary>Diferencias palabra a palabra: los tramos consecutivos que cambian se agrupan en un solo cambio.</summary>
        public static List<CambioConcepto> CalcularCambios(string original, string propuesto)
        {
            List<string> a = Palabras(original);
            List<string> b = Palabras(propuesto);
            int n = a.Count, m = b.Count;
            int[,] lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            var cambios = new List<CambioConcepto>();
            var de = new List<string>();
            var hacia = new List<string>();
            void Cerrar()
            {
                if (de.Count > 0 || hacia.Count > 0)
                {
                    cambios.Add(new CambioConcepto { De = string.Join(" ", de), A = string.Join(" ", hacia) });
                    de.Clear();
                    hacia.Clear();
                }
            }

            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    Cerrar();
                    x++;
                    y++;
                }
                else if (y < m && (x == n || lcs[x, y + 1] >= lcs[x + 1, y]))
                {
                    hacia.Add(b[y++]);
                }
                else
                {
                    de.Add(a[x++]);
                }
            }
            Cerrar();
            return cambios;
        }

        internal static List<string> Palabras(string texto)
        {
            return string.IsNullOrWhiteSpace(texto)
                ? new List<string>()
                : texto.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>Términos del glosario fijo: separados por comas, punto y coma o saltos de línea.</summary>
        public static List<string> ParsearGlosario(IEnumerable<string> valores)
        {
            return (valores ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .SelectMany(v => v.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static readonly HashSet<string> PalabrasVacias = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pago", "pagos", "pedido", "pedidos", "factura", "facturas", "curso", "cursos", "formacion", "formación",
            "reserva", "reservas", "enlace", "cliente", "nueva", "vision", "visión", "primer", "primera", "segundo",
            "cuota", "recibo", "septiembre", "octubre", "noviembre", "diciembre", "enero", "febrero", "marzo", "abril",
            "mayo", "junio", "julio", "agosto", "pendiente", "pendientes", "diferencia", "resto", "señal", "senal"
        };

        /// <summary>
        /// Las palabras del concepto por las que merece la pena buscar productos parecidos: solo letras, de 5 o más,
        /// que no sean palabras de trámite («pago», «pedido», meses…), sin repetir y como mucho <paramref name="maximo"/>.
        /// </summary>
        public static List<string> PalabrasParaBuscarProductos(string concepto, int maximo = 3)
        {
            return Palabras(concepto)
                .Select(p => p.Trim(PuntuacionBorde))
                .SelectMany(p => p.Split(new[] { ',', '/', '-' }, StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 5 && p.All(char.IsLetter) && !PalabrasVacias.Contains(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maximo)
                .ToList();
        }

        public static readonly string PROMPT_SISTEMA = @"Eres un corrector ortográfico de conceptos de pago en español de una empresa de estética profesional (Nueva Visión).
Te paso un concepto escrito por un empleado y un glosario de nombres propios (productos, cursos y términos de la casa).
Reglas ESTRICTAS:
- Corrige SOLO faltas de ortografía, tildes, erratas y nombres propios mal escritos, usando el glosario como referencia de cómo se escriben bien.
- No traduzcas. No cambies el estilo ni el orden de las palabras. No añadas ni quites información.
- NO toques números, fechas, importes, horas ni números de pedido o factura: cópialos exactamente igual.
- Respeta mayúsculas y minúsculas salvo en los nombres propios del glosario, que van como en el glosario.
- No uses el glosario para meter palabras que no estén en el concepto: solo para corregir las que ya están.
- Máximo " + LONGITUD_MAXIMA + @" caracteres.
- Si el concepto ya está bien, devuélvelo exactamente igual.
Devuelve ÚNICAMENTE el concepto corregido, en una sola línea, sin comillas ni explicaciones.";

        public static string MensajeUsuario(string concepto, IEnumerable<string> glosario)
        {
            var sb = new StringBuilder();
            List<string> terminos = (glosario ?? Enumerable.Empty<string>()).ToList();
            if (terminos.Any())
            {
                sb.Append("Glosario: ").AppendLine(string.Join("; ", terminos));
            }
            sb.Append("Concepto: ").Append(concepto);
            return sb.ToString();
        }
    }
}
