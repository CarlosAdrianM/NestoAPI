using NestoAPI.Infraestructure.ChequesRegalo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>NestoAPI#593 (c4): una línea del pedido ya entregada (o con picking de una pasada anterior).</summary>
    public class LineaEntregadaChequeRegalo
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>
    /// NestoAPI#593 (c4): prepara los pedidos del picking que llevan la línea del cheque regalo: la marca, marca lo que
    /// suma para el mínimo y apunta lo ya entregado. La decisión de si sale la toma
    /// <see cref="GestorReservasStock.ReservarChequesRegalo"/>. Se usan todas las campañas, activas o no: un cheque ya
    /// metido en un pedido sigue la regla aunque la campaña se apague después.
    /// </summary>
    public static class ChequesRegaloPicking
    {
        public static void Rellenar(List<PedidoPicking> pedidos, List<CampanaCanjeChequeRegalo> campanas,
            Func<string, List<string>, Dictionary<string, ProductoParaChequeRegalo>> leerProductos,
            Func<PedidoPicking, List<LineaEntregadaChequeRegalo>> leerLineasYaEntregadas)
        {
            if (pedidos == null || campanas == null || !campanas.Any())
            {
                return;
            }
            foreach (PedidoPicking pedido in pedidos.Where(p => !p.EsNotaEntrega && p.Lineas != null))
            {
                List<LineaPedidoPicking> lineasCheque = pedido.Lineas
                    .Where(l => ReglasCanjeChequeRegalo.EsLineaCheque(l.TipoLinea, l.Producto, campanas))
                    .ToList();
                if (!lineasCheque.Any())
                {
                    continue;
                }
                CampanaCanjeChequeRegalo campana = ReglasCanjeChequeRegalo.CampanaDelProducto(campanas, lineasCheque[0].Producto);
                foreach (LineaPedidoPicking linea in lineasCheque)
                {
                    linea.EsChequeRegalo = true;
                }

                List<LineaEntregadaChequeRegalo> entregadas = leerLineasYaEntregadas(pedido) ?? new List<LineaEntregadaChequeRegalo>();
                List<string> codigos = pedido.Lineas
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim())
                    .Concat(entregadas.Where(e => !string.IsNullOrWhiteSpace(e.Producto)).Select(e => e.Producto.Trim()))
                    .Distinct()
                    .ToList();
                Dictionary<string, ProductoParaChequeRegalo> productos = leerProductos(pedido.Empresa?.Trim(), codigos)
                    ?? new Dictionary<string, ProductoParaChequeRegalo>();

                foreach (LineaPedidoPicking linea in pedido.Lineas)
                {
                    linea.ComputaMinimoChequeRegalo = !linea.EsChequeRegalo
                        && ReglasCanjeChequeRegalo.ComputaParaMinimo(linea.TipoLinea, ReglasCanjeChequeRegalo.Buscar(productos, linea.Producto), campana);
                }

                pedido.ChequeRegalo = new ChequeRegaloPicking
                {
                    Campana = campana.Codigo,
                    MinimoCanje = campana.MinimoCanje,
                    BaseComputableYaEntregada = entregadas
                        .Where(e => ReglasCanjeChequeRegalo.ComputaParaMinimo(Constantes.TiposLineaVenta.PRODUCTO,
                            ReglasCanjeChequeRegalo.Buscar(productos, e.Producto), campana))
                        .Sum(e => e.BaseImponible)
                };
            }
        }
    }
}
