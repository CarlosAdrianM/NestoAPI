using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// El ZPL de CTT declara <c>^CI28</c> (UTF-8) y escribe los acentos y la eñe como caracteres
    /// Unicode sueltos en los campos <c>^FD</c> ("ESPAÑA"). Nesto manda el ZPL a la Zebra como bytes
    /// ANSI (RawPrinterHelper), así que "Ñ" llegaría como un único byte 0xD1 que en modo UTF-8 no es
    /// válido y la impresora lo descarta. Los campos de CTT llevan <c>^FH\</c> (indicador hex '\'),
    /// así que aquí cada carácter no ASCII de un <c>^FH\^FD…^FS</c> se sustituye por sus bytes UTF-8
    /// en hex ("Ñ" -> <c>\c3\91</c>): el ZPL queda 100 % ASCII y la Zebra imprime la eñe.
    /// Idempotente: un ZPL ya ASCII sale igual.
    /// </summary>
    public static class NormalizadorZplCTT
    {
        // Campo con indicador hex '\' seguido de sus datos hasta ^FS.
        private static readonly Regex CampoHex = new Regex(@"\^FH\\\^FD(.*?)\^FS", RegexOptions.Compiled | RegexOptions.Singleline);

        public static string CodificarNoAscii(string zpl)
        {
            if (string.IsNullOrEmpty(zpl))
            {
                return zpl;
            }
            return CampoHex.Replace(zpl, m => @"^FH\^FD" + Codificar(m.Groups[1].Value) + "^FS");
        }

        private static string Codificar(string datos)
        {
            var sb = new StringBuilder(datos.Length);
            foreach (char c in datos)
            {
                if (c < 0x80)
                {
                    sb.Append(c);
                    continue;
                }
                foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                {
                    sb.Append('\\').Append(b.ToString("x2"));
                }
            }
            return sb.ToString();
        }
    }
}
