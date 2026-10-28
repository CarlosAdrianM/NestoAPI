using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que sale de pedir la ficha: la ficha, los productos entre los que elegir, o nada.</summary>
    public class ResultadoFichaProductoAlmacen
    {
        public FichaProductoAlmacenDTO Ficha { get; set; }
        /// <summary>Más de un producto lleva el código: la app pregunta cuál y vuelve a pedir la ficha con su referencia.</summary>
        public List<ProductoCandidatoFichaDTO> Candidatos { get; set; } = new List<ProductoCandidatoFichaDTO>();
        public bool NoExiste => Ficha == null && Candidatos.Count == 0;
    }

    public interface IServicioFichaProductoAlmacen
    {
        /// <summary>La ficha por la referencia o por cualquiera de sus códigos de barras (el principal o uno alternativo).</summary>
        Task<ResultadoFichaProductoAlmacen> Leer(string empresa, string codigo);
    }

    /// <summary>
    /// Sugerencia 824 (Andre): la ficha de un producto para Ariadna, montada con lo que ya hay. Sin SQL propio: el producto y sus
    /// huecos los busca <see cref="IServicioUbicacionesAlmacen.BuscarProducto"/> (lo mismo que «buscar» al ubicar, una vez por
    /// almacén), la familia, la foto y la referencia del proveedor, <see cref="IFichasProductoAlmacen"/>; los códigos con sus
    /// unidades, <see cref="IServicioCodigosBarras.Listar"/>, y el stock y lo pendiente, <see cref="IProductoService.CalcularStockProducto"/>
    /// (el mismo cálculo que la ficha de Nesto y NestoApp).
    /// </summary>
    public class ServicioFichaProductoAlmacen : IServicioFichaProductoAlmacen
    {
        /// <summary>Los almacenes de la ficha, en este orden.</summary>
        public static readonly IReadOnlyList<string> AlmacenesFicha = new[]
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        private readonly IServicioUbicacionesAlmacen ubicaciones;
        private readonly IFichasProductoAlmacen fichas;
        private readonly IServicioCodigosBarras codigos;
        private readonly IProductoService stocks;

        public ServicioFichaProductoAlmacen(IServicioUbicacionesAlmacen ubicaciones, IFichasProductoAlmacen fichas,
            IServicioCodigosBarras codigos, IProductoService stocks)
        {
            this.ubicaciones = ubicaciones ?? throw new ArgumentNullException(nameof(ubicaciones));
            this.fichas = fichas;
            this.codigos = codigos;
            this.stocks = stocks ?? throw new ArgumentNullException(nameof(stocks));
        }

        public async Task<ResultadoFichaProductoAlmacen> Leer(string empresa, string codigo)
        {
            var resultado = new ResultadoFichaProductoAlmacen();
            string leido = codigo?.Trim();
            if (string.IsNullOrEmpty(leido))
            {
                return resultado;
            }

            string primero = AlmacenesFicha[0];
            List<ProductoAlmacenDTO> encontrados = (await ubicaciones.BuscarProducto(empresa, primero, leido).ConfigureAwait(false)
                ?? new List<ProductoAlmacenDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p?.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            // La referencia tecleada manda sobre un código de barras que pudiera coincidir
            ProductoAlmacenDTO producto = encontrados.Count == 1
                ? encontrados[0]
                : encontrados.FirstOrDefault(p => string.Equals(p.Producto.Trim(), leido, StringComparison.OrdinalIgnoreCase));
            if (producto == null)
            {
                resultado.Candidatos = encontrados
                    .Select(p => new ProductoCandidatoFichaDTO { Producto = p.Producto.Trim(), Nombre = p.Descripcion?.Trim() })
                    .ToList();
                return resultado;
            }

            string numero = producto.Producto.Trim();
            var ficha = new FichaProductoAlmacenDTO
            {
                Producto = numero,
                Nombre = string.IsNullOrWhiteSpace(producto.Descripcion) ? numero : producto.Descripcion.Trim(),
                CodigoBarras = producto.CodigoBarras?.Trim()
            };

            var porAlmacen = new Dictionary<string, ProductoAlmacenDTO>(StringComparer.OrdinalIgnoreCase) { [primero] = producto };
            foreach (string almacen in AlmacenesFicha.Skip(1))
            {
                List<ProductoAlmacenDTO> enAlmacen = await ubicaciones.BuscarProducto(empresa, almacen, numero).ConfigureAwait(false);
                porAlmacen[almacen] = enAlmacen?.FirstOrDefault(p => string.Equals(p?.Producto?.Trim(), numero, StringComparison.OrdinalIgnoreCase));
            }

            foreach (string almacen in AlmacenesFicha)
            {
                ProductoDTO.StockProducto stock = await stocks.CalcularStockProducto(numero, almacen).ConfigureAwait(false);
                porAlmacen.TryGetValue(almacen, out ProductoAlmacenDTO huecos);
                ficha.Almacenes.Add(new AlmacenFichaProductoDTO
                {
                    Almacen = almacen,
                    Stock = stock?.Stock ?? 0,
                    PendienteEntregar = stock?.PendienteEntregar ?? 0,
                    PendienteRecibir = stock?.PendienteRecibir ?? 0,
                    PendienteReposicion = stock?.PendienteReposicion ?? 0,
                    Disponible = stock?.CantidadDisponible ?? 0,
                    PendienteDeUbicar = huecos?.PendienteDeUbicar ?? 0,
                    Ubicaciones = huecos?.Ubicaciones ?? new List<UbicacionAlmacenDTO>()
                });
            }

            ficha.CodigosBarras = await LeerCodigos(empresa, numero, ficha.CodigoBarras).ConfigureAwait(false);
            if (fichas != null)
            {
                await fichas.Completar(empresa, new[] { ficha }).ConfigureAwait(false);
            }
            resultado.Ficha = ficha;
            return resultado;
        }

        /// <summary>
        /// Los códigos activos con sus unidades, el principal el primero. El de la ficha (Productos.CodBarras) va siempre, aunque
        /// no esté en ProductosCodigosBarras (los productos de antes de NestoAPI#605).
        /// </summary>
        private async Task<List<CodigoBarrasFichaDTO>> LeerCodigos(string empresa, string producto, string principal)
        {
            List<CodigoBarrasProductoDTO> filas = codigos == null
                ? new List<CodigoBarrasProductoDTO>()
                : await codigos.Listar(empresa, producto).ConfigureAwait(false) ?? new List<CodigoBarrasProductoDTO>();
            List<CodigoBarrasFichaDTO> lista = filas
                .Where(f => f != null && f.Activo && !string.IsNullOrWhiteSpace(f.Codigo))
                .GroupBy(f => f.Codigo.Trim())
                .Select(g => g.OrderByDescending(f => f.Principal).First())
                .Select(f => new CodigoBarrasFichaDTO { Codigo = f.Codigo.Trim(), Cantidad = f.Cantidad > 0 ? f.Cantidad : 1, Principal = f.Principal })
                .ToList();
            if (!string.IsNullOrWhiteSpace(principal))
            {
                CodigoBarrasFichaDTO deLaFicha = lista.FirstOrDefault(c => c.Codigo == principal.Trim());
                if (deLaFicha == null)
                {
                    lista.Insert(0, new CodigoBarrasFichaDTO { Codigo = principal.Trim(), Cantidad = 1, Principal = true });
                }
                else if (!lista.Any(c => c.Principal))
                {
                    deLaFicha.Principal = true;
                }
            }
            return lista.OrderByDescending(c => c.Principal).ToList();
        }
    }
}
