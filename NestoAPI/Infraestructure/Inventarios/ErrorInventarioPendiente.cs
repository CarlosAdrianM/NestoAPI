using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>Una fila pendiente de dbo.Inventarios (Estado 1, sin NºTraspaso): la que hace saltar los disparadores del inventario.</summary>
    public class FilaInventarioSinContabilizar
    {
        public int NumeroOrden { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>
    /// Incidencias 1227 y 1057 (picking, 12/11/26) e incidencia 1455 (enviar una reposición, Paloma, 25/11/26): los disparadores de
    /// Ubicaciones y de ExtractoProducto cortan el movimiento con «Se esta realizando el inventario del producto» si hay inventario
    /// sin contabilizar, sin decir ni de qué fila ni de quién. El NÚCLEO común: reconocer ese error, elegir las filas que se enseñan,
    /// listarlas con un tope y cambiar el error solo si se puede explicar (si no hay filas o la lectura falla, el original).
    /// Cada camino (picking, reposición) pone su frase.
    /// </summary>
    public static class ErrorInventarioPendiente
    {
        public const string TEXTO_DISPARADOR = "realizando el inventario del producto";

        /// <summary>El error (o alguno de dentro) es el del disparador del inventario.</summary>
        public static bool EsErrorDeInventario(Exception ex)
        {
            for (Exception actual = ex; actual != null; actual = actual.InnerException)
            {
                if (actual.Message != null && actual.Message.IndexOf(TEXTO_DISPARADOR, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// De las <paramref name="pendientes"/> que cumplen <paramref name="candidata"/>, las de los (almacén, producto) dados. Con
        /// <paramref name="soloLasDeLosProductos"/> (picking) solo esas, y todas las candidatas si ninguna casa; sin él (reposición),
        /// esas primero y luego las demás candidatas. Conserva el orden de entrada dentro de cada grupo.
        /// </summary>
        public static List<T> Acotar<T>(IEnumerable<T> pendientes, Func<T, bool> candidata,
            IEnumerable<KeyValuePair<string, string>> almacenProducto, bool soloLasDeLosProductos) where T : FilaInventarioSinContabilizar
        {
            List<T> todas = (pendientes ?? Enumerable.Empty<T>())
                .Where(f => f != null && (candidata == null || candidata(f)))
                .ToList();
            var buscadas = new HashSet<string>((almacenProducto ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => Clave(p.Key, p.Value)), StringComparer.OrdinalIgnoreCase);
            List<T> casan = todas.Where(f => buscadas.Contains(Clave(f.Almacen, f.Producto))).ToList();
            if (soloLasDeLosProductos)
            {
                return casan.Count > 0 ? casan : todas;
            }
            return casan.Concat(todas.Where(f => !casan.Contains(f))).ToList();
        }

        /// <summary>«A; B; C; … y 3 más» (hasta <paramref name="maximo"/>), o null si no hay filas.</summary>
        public static string Lista<T>(IReadOnlyCollection<T> filas, Func<T, string> describir, int maximo)
        {
            if (filas == null || filas.Count == 0)
            {
                return null;
            }
            string detalle = string.Join("; ", filas.Take(maximo).Select(describir));
            if (filas.Count > maximo)
            {
                detalle += $"; … y {(filas.Count - maximo).ToString(CultureInfo.InvariantCulture)} más";
            }
            return detalle;
        }

        /// <summary>
        /// El error para el usuario, o null si hay que dejar el original: no es el del inventario, el mensaje sale null (no hay filas
        /// que enseñar) o montarlo falla (p. ej. la lectura de las filas). Explicar es cortesía: nunca se empeora el error.
        /// </summary>
        public static Exception Traducir(Exception original, Func<string> mensaje, Func<string, Exception, Exception> crear)
        {
            if (!EsErrorDeInventario(original) || mensaje == null || crear == null)
            {
                return null;
            }
            try
            {
                string texto = mensaje();
                return texto == null ? null : crear(texto, original);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>«NUEVAVISION\Paloma» → «Paloma»; null si no hay usuario.</summary>
        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>«005-002-001», o null si la fila no tiene hueco.</summary>
        public static string Hueco(FilaInventarioSinContabilizar f)
        {
            return f == null || string.IsNullOrWhiteSpace(f.Pasillo)
                ? null
                : $"{f.Pasillo.Trim()}-{f.Fila?.Trim()}-{f.Columna?.Trim()}";
        }

        private static string Clave(string almacen, string producto) => $"{almacen?.Trim()}#{producto?.Trim()}";
    }
}
