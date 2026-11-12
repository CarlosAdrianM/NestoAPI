using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>Incidencias 1227 y 1057 (12/11/26): una fila pendiente de dbo.Inventarios (Estado 1, sin NºTraspaso, fuera de REI).</summary>
    public class FilaInventarioPendientePicking
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
    /// Incidencias 1227 y 1057: el picking cortado por el inventario. Lleva el mensaje claro; el original del disparador va dentro
    /// (InnerException) para ELMAH. No es <c>NestoBusinessException</c> a propósito: el filtro global la devuelve como INTERNAL_ERROR
    /// y Nesto enseña el mensaje tal cual (sin el «[CÓDIGO]» delante).
    /// </summary>
    public class PickingInventarioPendienteException : Exception
    {
        public PickingInventarioPendienteException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Incidencias 1227 y 1057 (Carlos 12/11/26): los disparadores de Ubicaciones (trdUpdUbicaciones, «…del producto1») y de
    /// ExtractoProducto («…del producto») cortan el picking si un producto que entra en él tiene una fila de inventario pendiente de
    /// otro usuario, sin decir ni cuál ni de quién. Aquí se reconoce ese error y se monta un mensaje con las filas pendientes. Puro: la
    /// lectura de las filas llega como función; si falla o no hay filas, se queda el error original (nunca se empeora).
    /// </summary>
    public static class ErrorInventarioPendientePicking
    {
        public const string TEXTO_DISPARADOR = "realizando el inventario del producto";
        public const string ALMACEN_SIN_CONTROL = "REI";
        public const int MAXIMO_FILAS = 10;

        public const string INICIO = "No se ha podido sacar el picking porque hay inventario sin contabilizar de productos que entran en él.";
        public const string FINAL = "Contabilice ese inventario (Productos > Inventarios) o borre las filas que sobren y vuelva a sacar el picking.";

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
        /// Las filas de los productos (y almacén) del picking; si ninguna casa (p. ej. un componente de kit, o no se sabe qué lleva el
        /// picking), todas las pendientes: mejor de más que ninguna.
        /// </summary>
        public static List<FilaInventarioPendientePicking> Acotar(IEnumerable<FilaInventarioPendientePicking> pendientes,
            IEnumerable<KeyValuePair<string, string>> almacenProductoDelPicking)
        {
            List<FilaInventarioPendientePicking> todas = (pendientes ?? Enumerable.Empty<FilaInventarioPendientePicking>())
                .Where(f => f != null && !string.Equals(f.Almacen?.Trim(), ALMACEN_SIN_CONTROL, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var delPicking = new HashSet<string>((almacenProductoDelPicking ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => Clave(p.Key, p.Value)), StringComparer.OrdinalIgnoreCase);
            List<FilaInventarioPendientePicking> acotadas = todas.Where(f => delPicking.Contains(Clave(f.Almacen, f.Producto))).ToList();
            return acotadas.Count > 0 ? acotadas : todas;
        }

        /// <summary>El mensaje para el usuario, o null si no hay filas que enseñar.</summary>
        public static string Mensaje(IEnumerable<FilaInventarioPendientePicking> filas)
        {
            List<FilaInventarioPendientePicking> lista = (filas ?? Enumerable.Empty<FilaInventarioPendientePicking>())
                .Where(f => f != null)
                .OrderBy(f => f.Fecha)
                .ThenBy(f => f.NumeroOrden)
                .ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            string detalle = string.Join("; ", lista.Take(MAXIMO_FILAS).Select(Describir));
            if (lista.Count > MAXIMO_FILAS)
            {
                detalle += $"; … y {(lista.Count - MAXIMO_FILAS).ToString(CultureInfo.InvariantCulture)} más";
            }
            return $"{INICIO} Filas pendientes: {detalle}. {FINAL}";
        }

        /// <summary>
        /// El error para el usuario, o null si hay que dejar el original (no hay filas, o leerlas ha fallado).
        /// </summary>
        public static Exception Traducir(Exception original, Func<IEnumerable<FilaInventarioPendientePicking>> leerPendientes,
            IEnumerable<KeyValuePair<string, string>> almacenProductoDelPicking)
        {
            if (!EsErrorDeInventario(original) || leerPendientes == null)
            {
                return null;
            }
            try
            {
                string mensaje = Mensaje(Acotar(leerPendientes(), almacenProductoDelPicking));
                return mensaje == null ? null : new PickingInventarioPendienteException(mensaje, original);
            }
            catch (Exception)
            {
                // Explicar es cortesía: si la consulta falla, el error de siempre.
                return null;
            }
        }

        /// <summary>«21075 CARTUCHO VINOTHERAPYA (ALG, 11/11/2026, sin ubicación, contó Alfredo)».</summary>
        internal static string Describir(FilaInventarioPendientePicking f)
        {
            string ubicacion = string.IsNullOrWhiteSpace(f.Pasillo)
                ? "sin ubicación"
                : $"{f.Pasillo.Trim()}-{f.Fila?.Trim()}-{f.Columna?.Trim()}";
            var partes = new List<string> { f.Almacen?.Trim(), f.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), ubicacion };
            string usuario = SinDominio(f.Usuario);
            if (!string.IsNullOrEmpty(usuario))
            {
                partes.Add("contó " + usuario);
            }
            string producto = $"{f.Producto?.Trim()} {f.Descripcion?.Trim()}".Trim();
            return $"{producto} ({string.Join(", ", partes.Where(p => !string.IsNullOrEmpty(p)))})";
        }

        internal static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        private static string Clave(string almacen, string producto) => $"{almacen?.Trim()}#{producto?.Trim()}";
    }
}
