using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Corrige un bug del ZPL que genera DataTrans (DTX): la etiqueta declara <c>^CI28</c> (modo
    /// UTF-8) pero codifica los caracteres acentuados, en los campos hex de <c>^FH</c>, como un ÚNICO
    /// byte Latin-1/CP1252 (p.ej. "á" = <c>_e1_</c>). Ese byte suelto NO es UTF-8 válido, así que la
    /// Zebra lo descarta y la tilde desaparece ("Rápida" -> "Rpida", "EDÉN" -> "EDN").
    ///
    /// Este normalizador convierte esos bytes altos a su equivalente en hex UTF-8 (<c>_e1_</c> ->
    /// <c>_c3_a1_</c>), manteniendo <c>^CI28</c>, de modo que la Zebra los imprime correctamente.
    ///
    /// Es un WORKAROUND temporal: lo correcto es que el integrador deje el ZPL coherente (UTF-8 real,
    /// o el ^CI del code page que realmente envía). Cuando lo arreglen, esto se puede quitar (será
    /// idempotente igualmente: los bytes ya en UTF-8 son &lt; 0x80 en cada token y no se tocan).
    /// </summary>
    public static class NormalizadorZplDataTrans
    {
        // Token hex de ^FH: separador por defecto '_' + 2 dígitos hexadecimales.
        private static readonly Regex TokenHex = new Regex("_([0-9a-fA-F]{2})", RegexOptions.Compiled);
        private static readonly Encoding Cp1252 = Encoding.GetEncoding(1252);

        public static string CorregirAcentos(string zpl)
        {
            if (string.IsNullOrEmpty(zpl))
            {
                return zpl;
            }
            // Solo si el ZPL está en modo UTF-8 (^CI28): si no, los bytes pueden ser de otro code page
            // y convertirlos los rompería.
            if (zpl.IndexOf("^CI28", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return zpl;
            }

            return TokenHex.Replace(zpl, Convertir);
        }

        private static string Convertir(Match m)
        {
            byte b = byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (b < 0x80)
            {
                return m.Value; // ASCII: ya es UTF-8 válido, se deja tal cual.
            }

            // Byte alto Latin-1/CP1252 -> carácter Unicode -> bytes UTF-8 -> tokens _XX.
            string caracter = Cp1252.GetString(new[] { b });
            byte[] utf8 = Encoding.UTF8.GetBytes(caracter);
            var sb = new StringBuilder(utf8.Length * 3);
            foreach (byte u in utf8)
            {
                sb.Append('_').Append(u.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
