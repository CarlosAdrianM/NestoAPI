using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: las reglas de la preparación con lector, sin base de datos. Casar lo leído con
    /// lo que había que preparar, resolver un código de barras y ordenar el recorrido del picking.
    ///
    /// <para>Es el mismo núcleo para el packing, la recepción de una reposición de tienda (#553) y la
    /// entrada de mercancía de proveedor (#559): en los tres hay una lista de «lo esperado» y una
    /// de «lo leído», y hay que decir qué falta, qué sobra y qué no pinta nada ahí.</para>
    /// </summary>
    public static class CasadorEscaneos
    {
        public const string FASE_PICKING = "PICK";
        public const string FASE_PACKING = "PACK";
        public const string METODO_LECTOR = "SCAN";
        public const string METODO_MANUAL = "MANUAL";
        public const string METODO_FALTA = "FALTA";

        /// <summary>Un producto con su cantidad: sirve para lo esperado y para lo leído.</summary>
        public class Cantidad
        {
            public string Producto { get; set; }
            public string Descripcion { get; set; }
            public int Unidades { get; set; }
        }

        public enum TipoResolucion
        {
            /// <summary>El código es de un solo producto de la lista.</summary>
            Unico,
            /// <summary>Varios productos de la lista comparten el código: el mozo elige entre ellos.</summary>
            Duplicado,
            /// <summary>Ningún producto de la lista tiene ese código.</summary>
            Ajeno
        }

        public class Resolucion
        {
            public TipoResolucion Tipo { get; set; }
            public List<string> Productos { get; set; } = new List<string>();
        }

        /// <summary>
        /// Lo esperado frente a lo leído, producto a producto. Los productos leídos que no estaban
        /// en lo esperado salen marcados como ajenos. Una falta (método FALTA) no cuenta como leída:
        /// quien llama pasa aquí solo las lecturas y los toques.
        /// </summary>
        public static List<DiferenciaPreparacionDTO> Casar(IEnumerable<Cantidad> esperado, IEnumerable<Cantidad> leido)
        {
            var resultado = new Dictionary<string, DiferenciaPreparacionDTO>(StringComparer.OrdinalIgnoreCase);

            foreach (Cantidad e in esperado ?? Enumerable.Empty<Cantidad>())
            {
                string producto = e.Producto?.Trim() ?? string.Empty;
                if (!resultado.TryGetValue(producto, out DiferenciaPreparacionDTO fila))
                {
                    fila = new DiferenciaPreparacionDTO { Producto = producto, Descripcion = e.Descripcion?.Trim() };
                    resultado[producto] = fila;
                }
                fila.Esperado += e.Unidades;
            }

            foreach (Cantidad l in leido ?? Enumerable.Empty<Cantidad>())
            {
                string producto = l.Producto?.Trim() ?? string.Empty;
                if (!resultado.TryGetValue(producto, out DiferenciaPreparacionDTO fila))
                {
                    fila = new DiferenciaPreparacionDTO { Producto = producto, Descripcion = l.Descripcion?.Trim(), Ajeno = true };
                    resultado[producto] = fila;
                }
                fila.Leido += l.Unidades;
            }

            // Un ajeno cuyas lecturas se han deshecho (suma 0) ya no es nada
            return resultado.Values
                .Where(f => !(f.Ajeno && f.Leido == 0))
                .OrderBy(f => f.Ajeno)
                .ThenBy(f => f.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool EstaCompleto(IEnumerable<DiferenciaPreparacionDTO> diferencias)
        {
            List<DiferenciaPreparacionDTO> lista = (diferencias ?? Enumerable.Empty<DiferenciaPreparacionDTO>()).ToList();
            return lista.Any() && lista.All(d => !d.Ajeno && d.Diferencia == 0);
        }

        /// <summary>
        /// A qué producto de la lista corresponde un código leído. La lista es la del picking o la
        /// del pedido que se está preparando: un código duplicado en el catálogo solo molesta si los
        /// dos productos están en lo que se prepara.
        /// </summary>
        public static Resolucion ResolverCodigo(string codigoLeido, IEnumerable<KeyValuePair<string, string>> productosConCodigo)
        {
            string codigo = codigoLeido?.Trim();
            var resolucion = new Resolucion { Tipo = TipoResolucion.Ajeno };
            if (string.IsNullOrEmpty(codigo))
            {
                return resolucion;
            }

            resolucion.Productos = (productosConCodigo ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(p => string.Equals(p.Value?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Key?.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            resolucion.Tipo = resolucion.Productos.Count == 0
                ? TipoResolucion.Ajeno
                : resolucion.Productos.Count == 1 ? TipoResolucion.Unico : TipoResolucion.Duplicado;
            return resolucion;
        }

        /// <summary>
        /// Los códigos de barras que comparten dos o más productos distintos de la lista.
        /// </summary>
        public static HashSet<string> CodigosDuplicados(IEnumerable<KeyValuePair<string, string>> productosConCodigo)
        {
            return new HashSet<string>(
                (productosConCodigo ?? Enumerable.Empty<KeyValuePair<string, string>>())
                    .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                    .GroupBy(p => p.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Select(p => p.Key?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>«001/002/004»: pasillo/fila/columna, como se enseña siempre. Null sin ubicación.</summary>
        public static string TextoUbicacion(string pasillo, string fila, string columna)
        {
            return string.IsNullOrWhiteSpace(pasillo)
                ? null
                : $"{pasillo.Trim()}/{fila?.Trim()}/{columna?.Trim()}";
        }

        /// <summary>
        /// El recorrido del picking. La ubicación se ENSEÑA pasillo/fila/columna, pero se RECORRE por
        /// pasillo, columna y fila: así se avanza columna a columna dentro del pasillo y se anda menos
        /// (Carlos, 30/09/26; es también el orden del papel de hoy). Lo que no tiene ubicación va al
        /// final. Deja puestos Orden, Ubicacion, SinCodigo y CodigoDuplicado.
        /// </summary>
        public static List<LineaPickingAlmacenDTO> OrdenarRecorrido(IEnumerable<LineaPickingAlmacenDTO> lineas)
        {
            List<LineaPickingAlmacenDTO> ordenadas = (lineas ?? Enumerable.Empty<LineaPickingAlmacenDTO>())
                .OrderBy(l => string.IsNullOrWhiteSpace(l.Pasillo))
                .ThenBy(l => l.Pasillo?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Columna?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Fila?.Trim(), StringComparer.Ordinal)
                .ThenBy(l => l.Producto?.Trim(), StringComparer.Ordinal)
                .ToList();

            HashSet<string> duplicados = CodigosDuplicados(
                ordenadas.Select(l => new KeyValuePair<string, string>(l.Producto, l.CodigoBarras)));

            int orden = 0;
            foreach (LineaPickingAlmacenDTO linea in ordenadas)
            {
                linea.Orden = ++orden;
                linea.CodigoBarras = string.IsNullOrWhiteSpace(linea.CodigoBarras) ? null : linea.CodigoBarras.Trim();
                linea.SinCodigo = linea.CodigoBarras == null;
                linea.CodigoDuplicado = linea.CodigoBarras != null && duplicados.Contains(linea.CodigoBarras);
                linea.Ubicacion = TextoUbicacion(linea.Pasillo, linea.Fila, linea.Columna);
            }
            return ordenadas;
        }

        /// <summary>
        /// Por qué no se puede guardar un escaneo, o null si está bien. Se valida uno a uno para que
        /// un escaneo malo no tire el lote entero de la cola del móvil.
        /// </summary>
        public static string MotivoDeRechazo(EscaneoAlmacenDTO escaneo)
        {
            if (escaneo == null)
            {
                return "El escaneo llega vacío.";
            }
            if (escaneo.IdCliente == Guid.Empty)
            {
                return "Falta el identificador del escaneo (IdCliente).";
            }
            if (escaneo.Picking <= 0)
            {
                return "Falta el número de picking.";
            }
            if (string.IsNullOrWhiteSpace(escaneo.Producto))
            {
                return "Falta el producto.";
            }
            if (escaneo.Producto.Trim().Length > 15)
            {
                return "El producto tiene más de 15 caracteres.";
            }
            string fase = escaneo.Fase?.Trim().ToUpperInvariant();
            if (fase != FASE_PICKING && fase != FASE_PACKING)
            {
                return "La fase tiene que ser PICK o PACK.";
            }
            string metodo = escaneo.Metodo?.Trim().ToUpperInvariant();
            if (metodo != METODO_LECTOR && metodo != METODO_MANUAL && metodo != METODO_FALTA)
            {
                return "El método tiene que ser SCAN, MANUAL o FALTA.";
            }
            if (escaneo.Cantidad == 0 || escaneo.Cantidad < short.MinValue || escaneo.Cantidad > short.MaxValue)
            {
                return "La cantidad no es válida.";
            }
            if (fase == FASE_PACKING && !escaneo.Pedido.HasValue)
            {
                return "En el packing hay que decir de qué pedido es el escaneo.";
            }
            if (escaneo.Bulto.HasValue && (escaneo.Bulto <= 0 || escaneo.Bulto > short.MaxValue))
            {
                return "El número de bulto no es válido.";
            }
            if (escaneo.FechaEscaneo.Year < 2026)
            {
                return "La fecha del escaneo no es válida.";
            }
            return null;
        }
    }
}
