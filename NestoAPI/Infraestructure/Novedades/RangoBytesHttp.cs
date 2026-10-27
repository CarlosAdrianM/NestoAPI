using System.Globalization;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>NestoAPI#711: qué se contesta a una cabecera Range sobre un fichero de una longitud dada.</summary>
    public enum TipoRespuestaRango
    {
        /// <summary>200 con el fichero entero (sin Range, Range que no se entiende o con varios tramos).</summary>
        Completo,
        /// <summary>206 con el tramo [Desde, Hasta] (ambos incluidos).</summary>
        Parcial,
        /// <summary>416: el tramo empieza después del final del fichero.</summary>
        NoSatisfacible
    }

    public class ResultadoRango
    {
        public TipoRespuestaRango Tipo { get; set; }
        public long Desde { get; set; }
        public long Hasta { get; set; }
        public long Longitud => Hasta - Desde + 1;

        /// <summary>El valor de Content-Range: «bytes 0-99/1000», o «bytes */1000» en el 416.</summary>
        public string ContentRange(long longitudTotal) =>
            Tipo == TipoRespuestaRango.NoSatisfacible
                ? $"bytes */{longitudTotal}"
                : $"bytes {Desde}-{Hasta}/{longitudTotal}";
    }

    /// <summary>
    /// NestoAPI#711: la cabecera Range de HTTP (RFC 7233), solo en bytes y con UN tramo, que es lo que piden los
    /// reproductores (el &lt;video&gt; de la app y del navegador) para saltar adelante y atrás. Lo que no se entiende
    /// se ignora y se sirve entero (lo permite la RFC), en vez de dar error.
    /// </summary>
    public static class RangoBytesHttp
    {
        public static ResultadoRango Calcular(string cabeceraRange, long longitudTotal)
        {
            var completo = new ResultadoRango { Tipo = TipoRespuestaRango.Completo, Desde = 0, Hasta = longitudTotal - 1 };
            if (string.IsNullOrWhiteSpace(cabeceraRange) || longitudTotal <= 0)
            {
                return completo;
            }
            string valor = cabeceraRange.Trim();
            const string UNIDAD = "bytes=";
            if (!valor.StartsWith(UNIDAD, System.StringComparison.OrdinalIgnoreCase))
            {
                return completo;
            }
            string tramo = valor.Substring(UNIDAD.Length).Trim();
            if (tramo.Length == 0 || tramo.Contains(","))
            {
                return completo; // varios tramos (multipart/byteranges): mejor el fichero entero
            }
            int guion = tramo.IndexOf('-');
            if (guion < 0)
            {
                return completo;
            }
            string inicio = tramo.Substring(0, guion).Trim();
            string fin = tramo.Substring(guion + 1).Trim();

            if (inicio.Length == 0)
            {
                // «bytes=-500»: los últimos 500
                if (!EsNumero(fin, out long sufijo))
                {
                    return completo;
                }
                if (sufijo == 0)
                {
                    return NoSatisfacible(); // «bytes=-0»: cero bytes, no hay nada que servir
                }
                long desdeSufijo = sufijo >= longitudTotal ? 0 : longitudTotal - sufijo;
                return new ResultadoRango { Tipo = TipoRespuestaRango.Parcial, Desde = desdeSufijo, Hasta = longitudTotal - 1 };
            }

            if (!EsNumero(inicio, out long desde))
            {
                return completo;
            }
            long hasta;
            if (fin.Length == 0)
            {
                hasta = longitudTotal - 1; // «bytes=100-»
            }
            else if (!EsNumero(fin, out hasta) || hasta < desde)
            {
                return completo; // sintácticamente inválido: se ignora
            }
            if (desde >= longitudTotal)
            {
                return NoSatisfacible();
            }
            if (hasta >= longitudTotal)
            {
                hasta = longitudTotal - 1;
            }
            return new ResultadoRango { Tipo = TipoRespuestaRango.Parcial, Desde = desde, Hasta = hasta };
        }

        private static ResultadoRango NoSatisfacible() => new ResultadoRango { Tipo = TipoRespuestaRango.NoSatisfacible };

        private static bool EsNumero(string texto, out long numero)
        {
            numero = 0;
            return texto.Length > 0 && long.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out numero);
        }
    }
}
