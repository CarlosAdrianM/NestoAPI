using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Videos
{
    /// <summary>
    /// NestoAPI#454: en el buscador de la tienda (nestobuscador) un vídeo sale porque dentro se habla
    /// de un producto... en el minuto 37. El enlace del vídeo entero llevaba al minuto 0. Esto elige,
    /// entre los productos de un vídeo, el que casa con lo que se buscó y devuelve su momento, para
    /// que el listado pueda dar el enlace con <c>&amp;t=</c>. Reglas puras.
    ///
    /// <para>Decisiones (las tres que dejó el módulo en la issue): si el vídeo casa por título o
    /// transcripción y no por un producto, no se inventa nada (null); si casan varios productos se
    /// da el MÁS TEMPRANO, para no adelantar al usuario más allá de lo que buscaba; un producto
    /// repetido con varias referencias en el mismo momento es un solo momento.</para>
    /// </summary>
    public static class CoincidenciaProductoVideo
    {
        public class ProductoEnVideo
        {
            public string Nombre { get; set; }
            public string TiempoAparicion { get; set; }
        }

        public class Coincidencia
        {
            public string Producto { get; set; }
            public int Segundos { get; set; }
        }

        /// <summary>
        /// El producto del vídeo cuyo nombre contiene TODAS las palabras de la búsqueda (sin
        /// mayúsculas ni acentos; se ignoran las de una letra), el más temprano si hay varios.
        /// Null si ninguno casa o si el que casa no tiene un momento legible.
        /// </summary>
        public static Coincidencia Elegir(string query, IEnumerable<ProductoEnVideo> productos)
        {
            List<string> palabras = Palabras(query);
            if (palabras.Count == 0 || productos == null)
            {
                return null;
            }
            return productos
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Nombre))
                .Select(p => new { p.Nombre, Segundos = Segundos(p.TiempoAparicion) })
                .Where(p => p.Segundos.HasValue)
                .Where(p =>
                {
                    string nombre = Normalizar(p.Nombre);
                    return palabras.All(nombre.Contains);
                })
                .OrderBy(p => p.Segundos.Value)
                .Select(p => new Coincidencia { Producto = p.Nombre.Trim(), Segundos = p.Segundos.Value })
                .FirstOrDefault();
        }

        /// <summary>
        /// VideosProductos.TiempoAparicion guarda casi siempre segundos ("2260"); unas pocas filas
        /// llevan "hh:mm:ss". Null si no se puede leer: mejor sin momento que un momento inventado.
        /// </summary>
        public static int? Segundos(string tiempoAparicion)
        {
            if (string.IsNullOrWhiteSpace(tiempoAparicion))
            {
                return null;
            }
            string texto = tiempoAparicion.Trim();
            if (texto.Contains(":"))
            {
                return TimeSpan.TryParse(texto, CultureInfo.InvariantCulture, out TimeSpan ts) && ts.TotalSeconds >= 0
                    ? (int?)(int)Math.Floor(ts.TotalSeconds)
                    : null;
            }
            return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int segundos) && segundos >= 0
                ? (int?)segundos
                : null;
        }

        private static List<string> Palabras(string query)
        {
            return Normalizar(query)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.Length >= 2)
                .Distinct()
                .ToList();
        }

        private static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return sb.ToString();
        }
    }
}
