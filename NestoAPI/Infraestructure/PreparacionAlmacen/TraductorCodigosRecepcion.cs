using NestoAPI.Infraestructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// Sugerencia 778 (Andre, reposición 81153 desde Reina, 26/10/26): al recibir, un código leído que no estaba en la lista se
    /// apuntaba tal cual (el EAN 8435454004144 en vez de la referencia 40945) y al terminar la API decía «El producto
    /// 8435454004144 no existe». Antes de usar lo leído (casar, terminar y las lecturas compartidas en curso), cada código que
    /// sea un código de barras de un producto se cambia por su referencia, con la misma búsqueda de siempre
    /// (<see cref="ServicioCodigosBarras.BuscarPorCodigo"/>: la ficha y ProductosCodigosBarras, solo GTIN válidos y productos
    /// activos). Así vale para Nesto y para Ariadna, y si el producto estaba en la lista cuenta contra su línea.
    /// <para>Lo que no es un GTIN válido (las referencias) no se pregunta. Un código que además es la referencia de un producto
    /// se queda como está. Si el código lo comparten varios productos, el de la lista (si solo hay uno en ella); si no, el
    /// principal. Lo que no es de ningún producto se queda tal cual: el tipo de recepción dará su error de siempre.</para>
    /// </summary>
    public class TraductorCodigosRecepcion
    {
        private readonly IRepositorioCodigosBarras repositorio;
        private readonly ServicioCodigosBarras codigos;

        /// <param name="repositorio">Null: no traduce nada (lo leído sigue tal cual, como antes).</param>
        public TraductorCodigosRecepcion(IRepositorioCodigosBarras repositorio)
        {
            this.repositorio = repositorio;
            codigos = repositorio == null ? null : new ServicioCodigosBarras(repositorio);
        }

        /// <summary>
        /// La referencia de cada código (normalizado: sin espacios y en mayúsculas). Solo vienen los que cambian; los demás se
        /// quedan como están.
        /// </summary>
        /// <param name="esperados">Los productos de la lista, solo para un código que comparten varios productos (se pide entonces).</param>
        public async Task<Dictionary<string, string>> Traducir(string empresa, IEnumerable<string> leidos, Func<Task<IEnumerable<string>>> esperados)
        {
            var traducidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (codigos == null)
            {
                return traducidos;
            }
            HashSet<string> deLaLista = null;
            foreach (string codigo in (leidos ?? Enumerable.Empty<string>())
                .Select(PlanificadorRecepcionCompra.Normalizar)
                .Where(c => !string.IsNullOrEmpty(c) && ValidadorGtin.EsCodigoValido(c))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                List<ProductoPorCodigoBarrasDTO> productos = await codigos.BuscarPorCodigo(empresa, codigo).ConfigureAwait(false)
                    ?? new List<ProductoPorCodigoBarrasDTO>();
                if (productos.Count == 0 || productos.Any(p => Igual(p.Producto, codigo)))
                {
                    continue;
                }
                if (await repositorio.LeerProducto(empresa, codigo).ConfigureAwait(false) != null)
                {
                    continue; // es también la referencia de un producto: lo leído es esa referencia
                }
                string producto;
                if (productos.Count == 1)
                {
                    producto = productos[0].Producto;
                }
                else
                {
                    if (deLaLista == null)
                    {
                        deLaLista = new HashSet<string>(
                            ((esperados == null ? null : await esperados().ConfigureAwait(false)) ?? Enumerable.Empty<string>())
                                .Select(PlanificadorRecepcionCompra.Normalizar)
                                .Where(p => !string.IsNullOrEmpty(p)),
                            StringComparer.OrdinalIgnoreCase);
                    }
                    List<ProductoPorCodigoBarrasDTO> enLaLista = productos.Where(p => deLaLista.Contains(PlanificadorRecepcionCompra.Normalizar(p.Producto))).ToList();
                    producto = enLaLista.Count == 1 ? enLaLista[0].Producto : productos[0].Producto; // BuscarPorCodigo: el principal primero
                }
                traducidos[codigo] = PlanificadorRecepcionCompra.Normalizar(producto);
            }
            return traducidos;
        }

        /// <summary>El producto que toca a lo leído: su traducción o, si no la tiene, lo leído normalizado.</summary>
        public static string Producto(IReadOnlyDictionary<string, string> traducidos, string leido)
        {
            string normalizado = PlanificadorRecepcionCompra.Normalizar(leido);
            return normalizado != null && traducidos != null && traducidos.TryGetValue(normalizado, out string producto) ? producto : normalizado;
        }

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
