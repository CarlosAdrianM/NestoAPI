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
        /// <summary>NestoAPI#574: recoger es lo mismo venga de un picking de pedidos o de una reposición a tienda.</summary>
        public const string ORIGEN_PICKING = "PICK";
        public const string ORIGEN_REPOSICION = "REPO";

        public const string DESTINO_PICKING = "Mesa de packing";

        /// <summary>PICK o REPO, como se escriba. Null si no es ninguno de los dos.</summary>
        public static string NormalizarTipoOrigen(string tipo)
        {
            string limpio = tipo?.Trim().ToUpperInvariant();
            return limpio == ORIGEN_PICKING || limpio == ORIGEN_REPOSICION ? limpio : null;
        }

        /// <summary>
        /// Reparte lo ya resuelto de cada producto (cogido o dado por falta) entre sus paradas, en el
        /// orden del recorrido: los escaneos dicen el producto, no el hueco, y un producto puede estar
        /// en varios. Lo leído de más no se apunta a ninguna parada (lo dice el estado).
        /// </summary>
        public static List<LineaRecogidaDTO> RepartirLoResuelto(IEnumerable<LineaPickingAlmacenDTO> recorrido, IEnumerable<Cantidad> resuelto)
        {
            Dictionary<string, int> quedan = (resuelto ?? Enumerable.Empty<Cantidad>())
                .GroupBy(r => r.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Math.Max(0, g.Sum(r => r.Unidades)), StringComparer.OrdinalIgnoreCase);

            var lineas = new List<LineaRecogidaDTO>();
            foreach (LineaPickingAlmacenDTO parada in (recorrido ?? Enumerable.Empty<LineaPickingAlmacenDTO>()).OrderBy(l => l.Orden))
            {
                string producto = parada.Producto?.Trim() ?? string.Empty;
                int disponible = quedan.TryGetValue(producto, out int unidades) ? unidades : 0;
                int hecho = Math.Min(disponible, Math.Max(0, parada.Cantidad));
                quedan[producto] = disponible - hecho;
                lineas.Add(new LineaRecogidaDTO
                {
                    Orden = parada.Orden,
                    Producto = parada.Producto,
                    Descripcion = parada.Descripcion,
                    CodigoBarras = parada.CodigoBarras,
                    CodigosBarras = parada.CodigosBarras ?? new List<string>(),
                    SinCodigo = parada.SinCodigo,
                    CodigoDuplicado = parada.CodigoDuplicado,
                    Cantidad = parada.Cantidad,
                    Tamano = parada.Tamano,
                    UnidadMedida = parada.UnidadMedida,
                    Pasillo = parada.Pasillo,
                    Fila = parada.Fila,
                    Columna = parada.Columna,
                    Ubicacion = parada.Ubicacion,
                    Resuelto = hecho
                });
            }
            return lineas;
        }

        /// <summary>El tipo de origen de un escaneo. Si no lo dice, es un picking.</summary>
        public static string TipoOrigenDe(EscaneoAlmacenDTO escaneo)
        {
            string tipo = escaneo?.TipoOrigen?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(tipo) ? ORIGEN_PICKING : tipo;
        }

        /// <summary>El número del picking o del traspaso. Admite el atajo de mandar solo <c>Picking</c>.</summary>
        public static int NumeroOrigenDe(EscaneoAlmacenDTO escaneo)
        {
            if (escaneo == null)
            {
                return 0;
            }
            return escaneo.NumeroOrigen > 0 || TipoOrigenDe(escaneo) != ORIGEN_PICKING ? escaneo.NumeroOrigen : escaneo.Picking;
        }

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
        /// NestoAPI#605: lo mismo, casando por CUALQUIER código activo de cada línea (CodigosBarras, además del principal).
        /// Un código que esté en varios productos del catálogo se resuelve por el contexto: solo cuentan los productos de
        /// esta lista; si quedan dos, es Duplicado (el mozo elige), como siempre.
        /// </summary>
        public static Resolucion ResolverCodigo(string codigoLeido, IEnumerable<IConCodigosBarras> lineas)
        {
            return ResolverCodigo(codigoLeido, ParesProductoCodigo(lineas));
        }

        /// <summary>NestoAPI#605: un par (producto, código) por cada código de cada línea: el principal y los alternativos.</summary>
        public static IEnumerable<KeyValuePair<string, string>> ParesProductoCodigo(IEnumerable<IConCodigosBarras> lineas)
        {
            foreach (IConCodigosBarras linea in lineas ?? Enumerable.Empty<IConCodigosBarras>())
            {
                if (linea == null)
                {
                    continue;
                }
                yield return new KeyValuePair<string, string>(linea.Producto, linea.CodigoBarras);
                foreach (string codigo in linea.CodigosBarras ?? new List<string>())
                {
                    yield return new KeyValuePair<string, string>(linea.Producto, codigo);
                }
            }
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
            string tipoOrigen = TipoOrigenDe(escaneo);
            if (tipoOrigen != ORIGEN_PICKING && tipoOrigen != ORIGEN_REPOSICION)
            {
                return "El tipo de origen tiene que ser PICK o REPO.";
            }
            if (escaneo.Picking > 0 && (tipoOrigen != ORIGEN_PICKING || (escaneo.NumeroOrigen > 0 && escaneo.NumeroOrigen != escaneo.Picking)))
            {
                return "El picking y el origen del escaneo no dicen lo mismo.";
            }
            if (NumeroOrigenDe(escaneo) <= 0)
            {
                return tipoOrigen == ORIGEN_PICKING ? "Falta el número de picking." : "Falta el número de la reposición.";
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
            if (tipoOrigen == ORIGEN_REPOSICION && (fase != FASE_PICKING || escaneo.Pedido.HasValue))
            {
                return "Una reposición solo se recoge (fase PICK) y no lleva pedido.";
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
