using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Kits
{
    public class ProductoService : IProductoService
    {
        public async Task<ProductoDTO> LeerProducto(string empresa, string id, bool fichaCompleta)
        {
            using (var db = new NVEntities())
            {
                Producto producto = await db.Productos.Include(p => p.Kits).SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == id).ConfigureAwait(false);
                if (producto == null)
                {
                    throw new Exception("Es necesario especificar el código del producto");
                }

                ProductoDTO productoDTO = new ProductoDTO()
                {
                    UrlFoto = fichaCompleta ? await ProductoDTO.RutaImagen(id).ConfigureAwait(false) : null,
                    PrecioPublicoFinal = fichaCompleta ? await ProductoDTO.LeerPrecioPublicoFinal(id, db).ConfigureAwait(false) : 0,
                    UrlEnlace = fichaCompleta ? await ProductoDTO.RutaEnlace(id).ConfigureAwait(false) : null,
                    Producto = producto.Número?.Trim(),
                    Nombre = producto.Nombre?.Trim(),
                    Tamanno = producto.Tamaño,
                    UnidadMedida = producto.UnidadMedida?.Trim(),
                    Familia = producto.Familia1.Descripción?.Trim(),
                    PrecioProfesional = (decimal)producto.PVP,
                    Estado = (short)producto.Estado,
                    Grupo = producto.Grupo,
                    Subgrupo = producto.SubGruposProducto.Descripción?.Trim(),
                    RoturaStockProveedor = producto.RoturaStockProveedor
                };

                foreach (var kit in producto.Kits)
                {
                    productoDTO.ProductosKit.Add(new ProductoKit
                    {
                        ProductoId = kit.NúmeroAsociado.Trim(),
                        Cantidad = kit.Cantidad
                    });
                }
                // Lo dejo medio-hardcoded porque no quiero que los vendedores vean otros almacenes
                if (!producto.Ficticio && fichaCompleta)
                {
                    productoDTO.Stocks.Add(await CalcularStockProducto(id, Constantes.Productos.ALMACEN_POR_DEFECTO));
                    productoDTO.Stocks.Add(await CalcularStockProducto(id, Constantes.Productos.ALMACEN_TIENDA));
                    productoDTO.Stocks.Add(await CalcularStockProducto(id, Constantes.Almacenes.ALCOBENDAS));
                }

                return productoDTO;
            }
        }

        public Task<string> ObtenerRutaImagen(string productoId)
        {
            return ProductoDTO.RutaImagen(productoId);
        }

        public async Task<ProductoDTO.StockProducto> CalcularStockProducto(string producto, string almacen, int? pedidoExcluir = null)
        {
            ProductoDTO.StockProducto stockProducto = await CalcularStockBase(producto, almacen, pedidoExcluir).ConfigureAwait(false);
            stockProducto.CantidadMontable = await CalcularMontablesDesdeComponentes(producto, almacen).ConfigureAwait(false);
            return stockProducto;
        }

        /// <summary>
        /// NestoAPI#412: cuántos kits ADICIONALES se pueden montar con el disponible de los
        /// componentes en ese almacén (min-floor, la fórmula de la consulta legacy de la web).
        /// Devuelve 0 si el producto no es un kit.
        ///
        /// Profundidad 1 deliberada: el disponible de cada componente es su stock BASE, sin
        /// contar a su vez lo montable de kits anidados — igual que el legacy, y evita ciclos.
        /// El disponible del componente ya descuenta sus pendientes de servir, así que un
        /// componente comprometido en pedidos sueltos no se cuenta dos veces aquí. Lo que NO se
        /// descuenta (igual que el legacy) son los pedidos pendientes del propio kit por encima
        /// de su físico: doble conteo asumido, ver el comentario de CantidadMontable.
        /// </summary>
        private async Task<int> CalcularMontablesDesdeComponentes(string producto, string almacen)
        {
            List<ProductoKit> componentes;
            using (var db = new NVEntities())
            {
                componentes = await db.Kits
                    .Where(k => k.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && k.Número == producto)
                    .Select(k => new ProductoKit { ProductoId = k.NúmeroAsociado.Trim(), Cantidad = k.Cantidad })
                    .ToListAsync().ConfigureAwait(false);
            }

            if (componentes.Count == 0)
            {
                return 0;
            }

            Dictionary<string, int> disponibles = new Dictionary<string, int>();
            foreach (ProductoKit componente in componentes)
            {
                if (disponibles.ContainsKey(componente.ProductoId))
                {
                    continue;
                }
                ProductoDTO.StockProducto stockComponente =
                    await CalcularStockBase(componente.ProductoId, almacen, null).ConfigureAwait(false);
                disponibles[componente.ProductoId] = stockComponente.CantidadDisponible;
            }

            return CalcularKitsMontables(componentes, disponibles);
        }

        /// <summary>
        /// La fórmula pura del legacy: montables = MIN sobre los componentes de
        /// FLOOR(disponible / cantidadPorKit). Un componente sin stock registrado cuenta como 0
        /// (bloquea el kit); una cantidad por kit inválida (0 o negativa, dato corrupto) no
        /// limita. A diferencia del legacy, un componente descatalogado NO se ignora: si su
        /// disponible se agota, el kit deja de ser montable, que es lo que pasa en el almacén.
        /// </summary>
        internal static int CalcularKitsMontables(ICollection<ProductoKit> componentes,
            IDictionary<string, int> disponiblePorComponente)
        {
            if (componentes == null || componentes.Count == 0)
            {
                return 0;
            }

            int montables = int.MaxValue;
            foreach (ProductoKit componente in componentes)
            {
                if (componente.Cantidad <= 0)
                {
                    continue;
                }
                int disponible = disponiblePorComponente != null
                    && disponiblePorComponente.TryGetValue(componente.ProductoId, out int valor)
                    ? valor : 0;
                montables = Math.Min(montables, disponible / componente.Cantidad);
            }

            return montables == int.MaxValue ? 0 : montables;
        }

        private static async Task<ProductoDTO.StockProducto> CalcularStockBase(string producto, string almacen, int? pedidoExcluir)
        {
            using (var db = new NVEntities())
            {
                ProductoDTO.StockProducto stockProducto = new ProductoDTO.StockProducto
                {
                    Almacen = almacen,
                    Stock = await db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    // pedidoExcluir (NestoAPI#262): no contar las líneas del propio pedido como pendiente
                    // (si no, la línea que queremos servir cuenta contra sí misma).
                    PendienteEntregar = await db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && (pedidoExcluir == null || e.Número != pedidoExcluir.Value)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    PendienteRecibir = await db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    FechaEstimadaRecepcion = (DateTime)await db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && ((e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true)).Select(e => e.FechaRecepción).DefaultIfEmpty(DateTime.MaxValue).MinAsync(),
                    PendienteReposicion = await db.PreExtrProductos.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync()
                };

                return stockProducto;
            }            
        }
    }
}