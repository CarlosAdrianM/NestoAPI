using NestoAPI.Infraestructure.Inventarios;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>Incidencias 1227 y 1057 (12/11/26): una fila pendiente de dbo.Inventarios (Estado 1, sin NºTraspaso, fuera de REI).</summary>
    public class FilaInventarioPendientePicking : FilaInventarioSinContabilizar
    {
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
    /// lectura de las filas llega como función; si falla o no hay filas, se queda el error original (nunca se empeora). El núcleo
    /// (reconocer, acotar, listar, traducir) es común con el envío de una reposición: <see cref="ErrorInventarioPendiente"/>.
    /// </summary>
    public static class ErrorInventarioPendientePicking
    {
        public const string TEXTO_DISPARADOR = ErrorInventarioPendiente.TEXTO_DISPARADOR;
        public const string ALMACEN_SIN_CONTROL = "REI";
        public const int MAXIMO_FILAS = 10;

        public const string INICIO = "No se ha podido sacar el picking porque hay inventario sin contabilizar de productos que entran en él.";
        public const string FINAL = "Contabilice ese inventario (Productos > Inventarios) o borre las filas que sobren y vuelva a sacar el picking.";

        /// <summary>El error (o alguno de dentro) es el del disparador del inventario.</summary>
        public static bool EsErrorDeInventario(Exception ex) => ErrorInventarioPendiente.EsErrorDeInventario(ex);

        /// <summary>
        /// Las filas de los productos (y almacén) del picking; si ninguna casa (p. ej. un componente de kit, o no se sabe qué lleva el
        /// picking), todas las pendientes: mejor de más que ninguna.
        /// </summary>
        public static List<FilaInventarioPendientePicking> Acotar(IEnumerable<FilaInventarioPendientePicking> pendientes,
            IEnumerable<KeyValuePair<string, string>> almacenProductoDelPicking)
        {
            return ErrorInventarioPendiente.Acotar(pendientes,
                f => !string.Equals(f.Almacen?.Trim(), ALMACEN_SIN_CONTROL, StringComparison.OrdinalIgnoreCase),
                almacenProductoDelPicking, soloLasDeLosProductos: true);
        }

        /// <summary>El mensaje para el usuario, o null si no hay filas que enseñar.</summary>
        public static string Mensaje(IEnumerable<FilaInventarioPendientePicking> filas)
        {
            List<FilaInventarioPendientePicking> lista = (filas ?? Enumerable.Empty<FilaInventarioPendientePicking>())
                .Where(f => f != null)
                .OrderBy(f => f.Fecha)
                .ThenBy(f => f.NumeroOrden)
                .ToList();
            string detalle = ErrorInventarioPendiente.Lista(lista, Describir, MAXIMO_FILAS);
            return detalle == null ? null : $"{INICIO} Filas pendientes: {detalle}. {FINAL}";
        }

        /// <summary>
        /// El error para el usuario, o null si hay que dejar el original (no hay filas, o leerlas ha fallado).
        /// </summary>
        public static Exception Traducir(Exception original, Func<IEnumerable<FilaInventarioPendientePicking>> leerPendientes,
            IEnumerable<KeyValuePair<string, string>> almacenProductoDelPicking)
        {
            if (leerPendientes == null)
            {
                return null;
            }
            return ErrorInventarioPendiente.Traducir(original,
                () => Mensaje(Acotar(leerPendientes(), almacenProductoDelPicking)),
                (mensaje, inner) => new PickingInventarioPendienteException(mensaje, inner));
        }

        /// <summary>«21075 CARTUCHO VINOTHERAPYA (ALG, 11/11/2026, sin ubicación, contó Alfredo)».</summary>
        internal static string Describir(FilaInventarioPendientePicking f)
        {
            string ubicacion = ErrorInventarioPendiente.Hueco(f) ?? "sin ubicación";
            var partes = new List<string> { f.Almacen?.Trim(), f.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), ubicacion };
            string usuario = SinDominio(f.Usuario);
            if (!string.IsNullOrEmpty(usuario))
            {
                partes.Add("contó " + usuario);
            }
            string producto = $"{f.Producto?.Trim()} {f.Descripcion?.Trim()}".Trim();
            return $"{producto} ({string.Join(", ", partes.Where(p => !string.IsNullOrEmpty(p)))})";
        }

        internal static string SinDominio(string usuario) => ErrorInventarioPendiente.SinDominio(usuario);
    }
}
