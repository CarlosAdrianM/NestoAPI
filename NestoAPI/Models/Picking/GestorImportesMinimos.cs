using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorImportesMinimos
    {
        public const decimal IMPORTE_MINIMO = 60;
        public const decimal IMPORTE_MINIMO_URGENTE = 100;
        public const decimal IMPORTE_SIN_PORTES = 150;
        public const decimal IMPORTE_MINIMO_TIENDA_ONLINE = 30;
        public const decimal IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL = 10;

        private decimal importeMinimo;

        private PedidoPicking pedido;
        public GestorImportesMinimos(PedidoPicking pedido)
        {
            this.pedido = pedido;
            if (GestorImportesMinimos.esRutaConPortes(pedido.Ruta))
            {
                if (pedido.EsPrecioPublicoFinal)
                {
                    this.importeMinimo = IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL;
                } else
                {
                    this.importeMinimo = pedido.EsTiendaOnline ? IMPORTE_MINIMO_TIENDA_ONLINE : IMPORTE_MINIMO;
                }                
            } else
            {
                this.importeMinimo = 0;
            }
        }

        public bool LosProductosSobrePedidoLleganAlImporteMinimo()
        {
            if (pedido.EsNotaEntrega)
            {
                return true; // quitamos notas de entrega
            }

            
            return pedido.Lineas.Where(l => l.EsSobrePedido && (l.Cantidad!=0)).Sum(l => l.BaseImponible / l.Cantidad * l.CantidadReservada) >= importeMinimo;
            
        }

        public bool LosProductosDelPedidoOriginalLlegabanAlImporteSinPortes()
        {
            return pedido.ImporteOriginalTotal >= IMPORTE_SIN_PORTES;
        }

        public bool LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo()
        {
            return pedido.ImporteOriginalNoSobrePedido >= importeMinimo ||
                pedido.Lineas.All(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE && l.CantidadReservada < 0);
        }

        public static bool esRutaConPortes(string ruta)
        {
            return (ruta == null || ruta.Trim() == "FW" || ruta.Trim() == "00" || ruta.Trim() == "16" || ruta.Trim() == "AT" || ruta.Trim() == "OT");
        }

        public bool LaEntregaLlegaAlImporteMinimo()
        {
            decimal importeEntrega = pedido.Lineas.Sum(l => l.BaseImponibleEntrega);
            if (importeEntrega >= importeMinimo) {
                return true;
            }
            if (importeEntrega >= 59.40M && pedido.Lineas.Where(l => l.Producto != null && (l.Producto.Trim() == "17404" || l.Producto.Trim() == "17004" || l.Producto.Trim() == "22161" || l.Producto.Trim() == "24490" || l.Producto.Trim() == "24491" || l.Producto.Trim() == "35176")).Sum(l => l.Cantidad) == 12)
            {
                return true;
            }
            if (importeEntrega >= 59.40M && pedido.Lineas.Where(l => l.Producto != null && l.Producto.Trim() == "25401").Sum(l => l.Cantidad) == 18)
            {
                return true;
            }

            return false;
        }
    }
}