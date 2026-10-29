using FakeItEasy;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Stocks
{
    /// <summary>
    /// NestoAPI#730: para los tests de la fecha de entrega a la agencia escritos con la regla de antes («stock − pendientes de los
    /// demás»), un <see cref="ILectorRepartoStock"/> falso que sale de los MISMOS datos que devuelve el repositorio falso
    /// (<see cref="IRepositorioFechaEntregaAgencia.LeerStock"/>, la última llamada): el stock de Algete, tienda y Alcobendas, y lo
    /// pendiente de los demás en Algete como UNA línea MÁS ANTIGUA que todas las del pedido. Con eso el reparto por
    /// antigüedad da exactamente lo mismo que la regla de antes (todos los demás van delante), y esos tests siguen probando lo
    /// que probaban (tiendas, kits, proveedor, textos). Los casos en los que la antigüedad cambia el resultado van aparte
    /// (<see cref="RepartoStockPorAntiguedadTests"/>).
    /// </summary>
    internal static class RepartoStockFalso
    {
        public static IRepartoStockPorAntiguedad DesdeSombra(IRepositorioFechaEntregaAgencia repositorio)
        {
            return new RepartoStockPorAntiguedad(LectorDesdeSombra(() => Fake.GetCalls(repositorio)
                .Where(c => c.Method.Name == nameof(IRepositorioFechaEntregaAgencia.LeerStock))
                .Select(c => c.ReturnValue as DatosSombraModoServicio)
                .LastOrDefault()));
        }

        public static ILectorRepartoStock LectorDesdeSombra(Func<DatosSombraModoServicio> datos)
        {
            ILectorRepartoStock lector = A.Fake<ILectorRepartoStock>();
            A.CallTo(() => lector.LeerStocks(A<IEnumerable<string>>._)).ReturnsLazily((IEnumerable<string> productos) =>
            {
                ResumenStocksProductos resumen = (datos() ?? new DatosSombraModoServicio()).Resumen;
                return productos.Select(p => new StockProducto
                {
                    Producto = p,
                    StockDisponible = ResumenStocksProductos.Valor(resumen.StockAlmacen, ResumenStocksProductos.Clave(p, Constantes.Almacenes.ALGETE)),
                    StockTienda = ResumenStocksProductos.Valor(resumen.StockAlmacen, ResumenStocksProductos.Clave(p, Constantes.Almacenes.REINA))
                        + ResumenStocksProductos.Valor(resumen.StockAlmacen, ResumenStocksProductos.Clave(p, Constantes.Almacenes.ALCOBENDAS))
                }).ToList();
            });
            A.CallTo(() => lector.LeerLineasPendientes(A<IEnumerable<string>>._)).ReturnsLazily((IEnumerable<string> productos) =>
            {
                ResumenStocksProductos resumen = (datos() ?? new DatosSombraModoServicio()).Resumen;
                int id = 0;
                return productos
                    .Select(p => new LineaPedidoPicking
                    {
                        Id = int.MinValue + (++id),
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        NumeroPedido = -1,
                        TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = p,
                        Almacen = Constantes.Almacenes.ALGETE,
                        Cantidad = ResumenStocksProductos.Valor(resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(p, Constantes.Almacenes.ALGETE)),
                        FechaModificacion = DateTime.MinValue
                    })
                    .Where(l => l.Cantidad != 0)
                    .ToList();
            });
            return lector;
        }
    }
}
