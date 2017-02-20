using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GestorImportesMinimos
    {
        public const decimal IMPORTE_MINIMO = 60;
        public const decimal IMPORTE_SIN_PORTES = 150;
        public const decimal IMPORTE_MINIMO_TIENDA_ONLINE = 30;

        private decimal importeMinimo;

        private PedidoPicking pedido;
        public GestorImportesMinimos(PedidoPicking pedido)
        {
            this.pedido = pedido;
            if (GestorImportesMinimos.esRutaConPortes(pedido.Ruta))
            {
                this.importeMinimo = pedido.EsTiendaOnline ? IMPORTE_MINIMO_TIENDA_ONLINE : IMPORTE_MINIMO;
            } else
            {
                this.importeMinimo = 0;
            }
        }

        public bool LosProductosSobrePedidoLleganAlImporteMinimo()
        {
            if (pedido.EsNotaEntrega || pedido.Lineas.Sum(c => c.Cantidad) == 0)
            {
                return true; // quitamos notas de entrega y evitamos división entre cero
            }

            
            return pedido.Lineas.Where(l => l.EsSobrePedido).Sum(l => l.BaseImponible / l.Cantidad * l.CantidadReservada) >= importeMinimo;
            
        }

        public bool LosProductosSobrePedidoOriginalesLlegabanAlImporteSinPortes()
        {
            return pedido.ImporteOriginalSobrePedido >= IMPORTE_SIN_PORTES;
        }

        public bool LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo()
        {
            return pedido.ImporteOriginalNoSobrePedido >= importeMinimo;
        }

        public static bool esRutaConPortes(string ruta)
        {
            return (ruta == null || ruta.Trim() == "FW" || ruta.Trim() == "00" || ruta.Trim() == "16" || ruta.Trim() == "AT" || ruta.Trim() == "OT");
        }

        public bool LaEntregaLlegaAlImporteMinimo()
        {
            decimal importeEntrega = pedido.Lineas.Sum(l => l.BaseImponibleEntrega);
            return importeEntrega >= importeMinimo;
        }
    }
}