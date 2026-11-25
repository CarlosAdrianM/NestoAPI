using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// Incidencia 1455: el envío de una reposición cortado por el inventario, cuando el original no era de negocio (si lo era, se
    /// devuelve otra <see cref="NestoBusinessException"/> con el mismo código). El original del disparador va dentro para ELMAH.
    /// </summary>
    public class ReposicionInventarioPendienteException : Exception
    {
        public ReposicionInventarioPendienteException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Incidencia 1455 (Paloma, 25/11/26): al ENVIAR una reposición de Alcobendas a Algete (un producto, el 41262), prdExtrProducto
    /// paró en el disparador del inventario y Ariadna enseñó el error en bruto («Error al contabilizar el diario: Se esta realizando
    /// el inventario del producto La transacción terminó en el desencadenador…»). La causa: una fila PENDIENTE del día anterior en
    /// ese almacén, de OTRO producto (24870, de Paloma). LA REGLA (pura, núcleo común con el picking: <see cref="ErrorInventarioPendiente"/>):
    /// se enseñan las filas pendientes del almacén (o almacenes) que mueve la reposición, primero las de sus productos; usuario sin
    /// dominio; 10 como mucho. Sin filas o si la lectura falla, el error de siempre. Solo cambia el texto: el código de estado es el
    /// del error original.
    /// </summary>
    public static class ErrorInventarioPendienteReposicion
    {
        public const int MAXIMO_FILAS = 10;
        public const string INICIO = "No se puede enviar la reposición:";
        /// <summary>Terminar: la reposición sigue en preparación, con lo leído.</summary>
        public const string FINAL_ENVIAR = "vuelva a enviarla; lo leído sigue guardado.";
        /// <summary>Puntual: nace y se termina de una vez; si falla, no queda nada.</summary>
        public const string FINAL_PUNTUAL = "vuelva a mandarla; no se ha mandado nada.";

        /// <summary>Las pendientes de <paramref name="almacenes"/>, primero las de <paramref name="productos"/>, por fecha y número.</summary>
        public static List<FilaInventarioSinContabilizar> Acotar(IEnumerable<FilaInventarioSinContabilizar> pendientes, IEnumerable<string> almacenes,
            IEnumerable<string> productos)
        {
            var delMovimiento = new HashSet<string>((almacenes ?? Enumerable.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()), StringComparer.OrdinalIgnoreCase);
            List<string> listaProductos = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            List<KeyValuePair<string, string>> pares = delMovimiento
                .SelectMany(a => listaProductos.Select(p => new KeyValuePair<string, string>(a, p)))
                .ToList();
            IEnumerable<FilaInventarioSinContabilizar> ordenadas = (pendientes ?? Enumerable.Empty<FilaInventarioSinContabilizar>())
                .Where(f => f != null)
                .OrderBy(f => f.Fecha)
                .ThenBy(f => f.NumeroOrden);
            return ErrorInventarioPendiente.Acotar(ordenadas, f => delMovimiento.Contains(f.Almacen?.Trim() ?? string.Empty), pares,
                soloLasDeLosProductos: false);
        }

        /// <summary>
        /// «No se puede enviar la reposición: en Alcobendas hay inventario sin contabilizar (24870 TANGA CABALLERO NEGRO, del 24/11,
        /// de Paloma). Contabilícelo o borre esa fila en Inventario y vuelva a enviarla; lo leído sigue guardado.» Null sin filas.
        /// </summary>
        public static string Mensaje(IEnumerable<FilaInventarioSinContabilizar> filas, DateTime hoy, string final)
        {
            List<FilaInventarioSinContabilizar> lista = (filas ?? Enumerable.Empty<FilaInventarioSinContabilizar>()).Where(f => f != null).ToList();
            string detalle = ErrorInventarioPendiente.Lista(lista, f => Describir(f, hoy), MAXIMO_FILAS);
            if (detalle == null)
            {
                return null;
            }
            List<string> almacenes = lista
                .Select(f => CalculadoraFechaEntregaAgencia.NombreAlmacen(f.Almacen))
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            string donde = almacenes.Count == 0 ? string.Empty : $" en {Enumerar(almacenes)}";
            string filasTexto = lista.Count == 1 ? "esa fila" : "esas filas";
            return $"{INICIO}{donde} hay inventario sin contabilizar ({detalle}). " +
                $"Contabilícelo o borre {filasTexto} en Inventario y {final ?? FINAL_ENVIAR}";
        }

        /// <summary>
        /// El error para el usuario, o null si hay que dejar el original: no es el del inventario, <paramref name="pendientes"/> es
        /// null (no se ha podido leer) o no hay ninguna fila de esos almacenes. Si el original es de negocio, otra de negocio con su
        /// mismo código de estado (la misma forma de respuesta que recibe Ariadna).
        /// </summary>
        public static Exception Traducir(Exception original, IEnumerable<FilaInventarioSinContabilizar> pendientes, IEnumerable<string> almacenes,
            IEnumerable<string> productos, DateTime hoy, string final)
        {
            if (pendientes == null)
            {
                return null;
            }
            return ErrorInventarioPendiente.Traducir(original,
                () => Mensaje(Acotar(pendientes, almacenes, productos), hoy, final),
                Crear);
        }

        private static Exception Crear(string mensaje, Exception original)
        {
            NestoBusinessException negocio = original as NestoBusinessException;
            if (negocio == null)
            {
                return new ReposicionInventarioPendienteException(mensaje, original);
            }
            return new NestoBusinessException(mensaje, original)
            {
                StatusCode = negocio.StatusCode,
                IsWarning = negocio.IsWarning,
                RegistrarEnLog = negocio.RegistrarEnLog
            };
        }

        /// <summary>«24870 TANGA CABALLERO NEGRO, del 24/11, de Paloma» (con «en 005-002-001» si tiene hueco).</summary>
        internal static string Describir(FilaInventarioSinContabilizar f, DateTime hoy)
        {
            var partes = new List<string>
            {
                $"{f.Producto?.Trim()} {f.Descripcion?.Trim()}".Trim(),
                "del " + CalculadoraFechaEntregaAgencia.DiaCorto(f.Fecha, hoy)
            };
            string hueco = ErrorInventarioPendiente.Hueco(f);
            if (hueco != null)
            {
                partes.Add("en " + hueco);
            }
            string usuario = ErrorInventarioPendiente.SinDominio(f.Usuario);
            if (usuario != null)
            {
                partes.Add("de " + usuario);
            }
            return string.Join(", ", partes.Where(p => !string.IsNullOrEmpty(p)));
        }

        private static string Enumerar(List<string> elementos) =>
            elementos.Count == 1 ? elementos[0] : string.Join(", ", elementos.Take(elementos.Count - 1)) + " y " + elementos.Last();
    }
}
