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

        public async Task<ProductoDTO.StockProducto> CalcularStockProducto(string producto, string almacen)
        {
            using (var db = new NVEntities())
            {
                ProductoDTO.StockProducto stockProducto = new ProductoDTO.StockProducto
                {
                    Almacen = almacen,
                    Stock = await db.ExtractosProducto.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Número == producto).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    PendienteEntregar = await db.LinPedidoVtas.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE)).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    PendienteRecibir = await db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && (e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync(),
                    FechaEstimadaRecepcion = (DateTime)await db.LinPedidoCmps.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto == producto && ((e.Estado == Constantes.EstadosLineaVenta.EN_CURSO || e.Estado == Constantes.EstadosLineaVenta.PENDIENTE) && e.Enviado == true)).Select(e => e.FechaRecepción).DefaultIfEmpty(DateTime.MaxValue).MinAsync(),
                    PendienteReposicion = await db.PreExtrProductos.Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO) && e.Almacén == almacen && e.Producto.Número == producto && e.NºTraspaso != null && e.NºTraspaso > 0).Select(e => (int)e.Cantidad).DefaultIfEmpty(0).SumAsync()
                };

                return stockProducto;
            }            
        }
    }
}