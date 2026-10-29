using NestoAPI.Infraestructure.Cupones;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>NestoAPI#613: una línea de producto del pedido ya entregada (o con picking de una pasada anterior).</summary>
    public class LineaEntregadaCupon
    {
        public string Producto { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>NestoAPI#613: el cupón usado en un pedido, con su pedido mínimo guardado (null: el de antes, el doble).</summary>
    public class CuponUsadoPicking
    {
        public decimal Importe { get; set; }
        public decimal? PedidoMinimo { get; set; }
    }

    /// <summary>
    /// NestoAPI#613: prepara los pedidos del picking que llevan la línea de un cupón de evento: la marca, marca lo que suma para
    /// el mínimo y apunta lo ya entregado. Decide <see cref="GestorReservasStock.ReservarCupones"/>.
    /// </summary>
    public static class CuponesPicking
    {
        /// <param name="leerCupon">pedido → el cupón usado en él (importe IVA incluido y pedido mínimo guardado); null si no consta.</param>
        public static void Rellenar(List<PedidoPicking> pedidos, string productoCupon,
            Func<string, List<string>, Dictionary<string, ProductoParaCupon>> leerProductos,
            Func<PedidoPicking, List<LineaEntregadaCupon>> leerLineasYaEntregadas,
            Func<int, CuponUsadoPicking> leerCupon)
        {
            if (pedidos == null || string.IsNullOrWhiteSpace(productoCupon))
            {
                return;
            }
            foreach (PedidoPicking pedido in pedidos.Where(p => !p.EsNotaEntrega && p.Lineas != null))
            {
                List<LineaPedidoPicking> lineasCupon = pedido.Lineas
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && ReglasCupon.EsProductoCupon(l.Producto, productoCupon))
                    .ToList();
                if (!lineasCupon.Any())
                {
                    continue;
                }
                foreach (LineaPedidoPicking linea in lineasCupon)
                {
                    linea.EsCupon = true;
                }
                // Sin el cupón en la tabla (no debería pasar), el importe sale del total de la propia línea (con IVA y RE: el
                // cupón descuenta su importe exacto también con recargo de equivalencia)
                CuponUsadoPicking usado = leerCupon(pedido.Id);
                decimal importe = usado?.Importe
                    ?? Math.Round(Math.Abs(lineasCupon.Sum(l => l.Total)), 2, MidpointRounding.AwayFromZero);

                List<LineaEntregadaCupon> entregadas = leerLineasYaEntregadas(pedido) ?? new List<LineaEntregadaCupon>();
                List<string> codigos = pedido.Lineas
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim())
                    .Concat(entregadas.Where(e => !string.IsNullOrWhiteSpace(e.Producto)).Select(e => e.Producto.Trim()))
                    .Distinct().ToList();
                Dictionary<string, ProductoParaCupon> productos = leerProductos(pedido.Empresa?.Trim(), codigos)
                    ?? new Dictionary<string, ProductoParaCupon>();

                foreach (LineaPedidoPicking linea in pedido.Lineas)
                {
                    linea.ComputaMinimoCupon = !linea.EsCupon && ReglasCupon.ComputaParaMinimo(linea.TipoLinea, ReglasCupon.Buscar(productos, linea.Producto));
                }
                pedido.Cupon = new CuponPicking
                {
                    PedidoMinimo = ReglasCupon.PedidoMinimo(importe, usado?.PedidoMinimo),
                    TotalComputableYaEntregado = entregadas
                        .Where(e => ReglasCupon.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO, ReglasCupon.Buscar(productos, e.Producto)))
                        .Sum(e => e.Total)
                };
            }
        }
    }
}
