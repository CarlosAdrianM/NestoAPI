using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class GestorImportesMinimos
    {
        public const decimal IMPORTE_MINIMO = 75;
        public const decimal IMPORTE_MINIMO_PENINSULAR = 100;
        public const decimal IMPORTE_MINIMO_BALEARES = 150;
        public const decimal IMPORTE_MINIMO_CANARIAS = 400;
        public const decimal IMPORTE_MINIMO_URGENTE = 100;
        public const decimal IMPORTE_SIN_PORTES = 150;
        public const decimal IMPORTE_MINIMO_ESPEJO = 150;
        public const decimal IMPORTE_MINIMO_TIENDA_ONLINE = 60;
        public const decimal IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL = 10;

        private decimal importeMinimo;

        private PedidoPicking pedido;
        public GestorImportesMinimos(PedidoPicking pedido)
        {
            this.pedido = pedido;
            if (GestorImportesMinimos.esRutaConPortes(pedido.Ruta))
            {
                if (string.IsNullOrEmpty(pedido.Iva))
                {
                    importeMinimo = IMPORTE_MINIMO_ESPEJO;
                }
                else if (pedido.EsPrecioPublicoFinal)
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

            
            return pedido.Lineas.Where(l => l.EsSobrePedido && (l.Cantidad != 0 || l.CantidadRecogida != 0)).Sum(BaseQueSeFactura) >= importeMinimo;

        }

        /// <summary>
        /// NestoAPI#542: lo que se le va a cobrar al cliente por esta línea en este albarán. Normalmente lo que
        /// sale (prorrateo por lo reservado); en «todo ahora» se factura la línea entera, incluidas las unidades
        /// a recoger, así que el mínimo de portes se mira sobre la base completa (Carlos, 25/09/26).
        /// </summary>
        private decimal BaseQueSeFactura(LineaPedidoPicking linea)
        {
            if (pedido.FacturaTodoAhora && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
            {
                return linea.BaseImponible;
            }
            return linea.BaseImponibleEntrega;
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
            decimal importeEntrega = pedido.Lineas.Sum(BaseQueSeFactura);
            if (importeEntrega >= importeMinimo) {
                return true;
            }
            
            return false;
        }
    }
}