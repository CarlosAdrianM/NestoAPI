using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857 (fase 3): un remitente del paquete que Compras ha dicho de qué proveedor es (dbo.ComprasRemitentesPaquete).</summary>
    public class RemitentePaqueteRecordado
    {
        public int Id { get; set; }
        /// <summary>Normalizado con <see cref="ExtractorRemitentePaquete.Normalizar"/>.</summary>
        public string RemitentePaquete { get; set; }
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        public string Usuario { get; set; }
        public System.DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#857 (fase 3): QUIÉN EXPIDE el paquete según el aviso de la agencia, que no siempre es el proveedor (caso real de UPS,
    /// 12/11/26: «¡Tu paquete está de camino!» / «De MBE STORE C/O MULTIBOXES S.L»: una tienda de Mail Boxes que envía por un
    /// proveedor). PURA. Por este orden: (1) tras una etiqueta «Remitente:», «Expedidor:», «Enviado por:» o «Shipper:» en cualquier
    /// línea; (2) la línea «De X» (sin dos puntos) justo tras el titular, en las primeras <see cref="LINEAS_TITULAR"/> líneas (UPS);
    /// (3) tras «From:» al principio de una línea. Nunca nosotros (Nueva Visión) ni el nombre de una agencia a secas («Enviado por:
    /// SEUR» es la agencia, no el remitente). Se devuelve normalizado (<see cref="Normalizar"/>), que es también la clave de la memoria.
    /// </summary>
    public static class ExtractorRemitentePaquete
    {
        public const int LONGITUD = 100;
        public const int LINEAS_TITULAR = 6;
        private const int MINIMO_LETRAS = 3;

        // Por línea: el valor va detrás de la etiqueta o, si no hay nada, en la siguiente línea no vacía («Remitente:\nWeelko Barcelona S. L.», MBE)
        private static readonly Regex Etiqueta = new Regex(
            @"(?<![\p{L}\p{N}])(?:remitente|expedidor|enviado\s+por|shipper)\s*\*?\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EtiquetaFrom = new Regex(@"^\s*\*?\s*from\s*\*?\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>«De MBE STORE C/O MULTIBOXES S.L»: «De»/«From» y el nombre, sin dos puntos en la línea.</summary>
        private static readonly Regex LineaDe = new Regex(@"^\s*(?:de|from)\s+(\p{L}[^:]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Lo que viene detrás en la misma línea («… Destinatario: NUEVA VISION»): se corta antes de la siguiente etiqueta.</summary>
        private static readonly Regex SiguienteEtiqueta = new Regex(@"\s+\p{L}+\s*:", RegexOptions.Compiled);

        private static readonly Regex Direccion = new Regex(@"<?[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}>?", RegexOptions.Compiled);
        private static readonly Regex Espacios = new Regex(@"\s+", RegexOptions.Compiled);

        public static string Extraer(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            List<string> lineas = texto.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            string porEtiqueta = TrasEtiqueta(lineas, Etiqueta);
            if (porEtiqueta != null)
            {
                return porEtiqueta;
            }

            for (int i = 1; i < lineas.Count && i < LINEAS_TITULAR; i++)
            {
                Match m = LineaDe.Match(lineas[i]);
                if (m.Success)
                {
                    string valor = Valido(m.Groups[1].Value);
                    if (valor != null)
                    {
                        return valor;
                    }
                }
            }

            return TrasEtiqueta(lineas, EtiquetaFrom);
        }

        /// <summary>El primer valor válido tras la etiqueta: en la misma línea o, si está vacía, en la siguiente (ya sin líneas vacías).</summary>
        private static string TrasEtiqueta(List<string> lineas, Regex etiqueta)
        {
            for (int i = 0; i < lineas.Count; i++)
            {
                Match m = etiqueta.Match(lineas[i]);
                if (!m.Success)
                {
                    continue;
                }
                string bruto = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(bruto) && i + 1 < lineas.Count && !lineas[i + 1].Contains(":"))
                {
                    bruto = lineas[i + 1];
                }
                string valor = Valido(bruto);
                if (valor != null)
                {
                    return valor;
                }
            }
            return null;
        }

        private static string Valido(string bruto)
        {
            if (string.IsNullOrWhiteSpace(bruto))
            {
                return null;
            }
            Match siguiente = SiguienteEtiqueta.Match(bruto);
            string valor = siguiente.Success ? bruto.Substring(0, siguiente.Index) : bruto;
            string normalizado = Normalizar(valor);
            if (normalizado == null || normalizado.Count(char.IsLetter) < MINIMO_LETRAS)
            {
                return null;
            }
            if (Regex.IsMatch(normalizado, @"(?<![a-z])nueva\s+vision(?![a-z])"))
            {
                return null;
            }
            // El nombre de una agencia a secas es la agencia, no quien expide
            string comparable = TextoCorreo.Normalizar(normalizado);
            bool esAgencia = AgenciasEntradaRegistradas.Todas.OfType<AgenciaEntradaPorPatrones>()
                .SelectMany(a => (a.NombresEnTexto ?? new string[0]).Concat(new[] { a.Nombre }))
                .Any(n => TextoCorreo.Normalizar(n) == comparable);
            return esAgencia ? null : normalizado;
        }

        /// <summary>
        /// La forma en que se guarda y se compara: sin direcciones de correo, sin tildes, en minúsculas, con un solo espacio y sin signos
        /// al principio ni al final; como mucho <see cref="LONGITUD"/> caracteres. «  MBE Store C/O  Multiboxes S.L. » → «mbe store c/o
        /// multiboxes s.l». Null si no queda nada.
        /// </summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string sinCorreos = Direccion.Replace(texto, " ");
            string descompuesto = sinCorreos.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            string resultado = Espacios.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ")
                .Trim(' ', '.', ',', ';', ':', '-', '*', '"', '\'', '(', ')', '[', ']', '<', '>', '|', '¡', '!', '¿', '?');
            if (resultado.Length > LONGITUD)
            {
                resultado = resultado.Substring(0, LONGITUD).TrimEnd();
            }
            return resultado.Length == 0 ? null : resultado;
        }
    }
}
