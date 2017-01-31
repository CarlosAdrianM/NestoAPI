using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    
    public class PedidoPicking
    {
        private const string PREFIJO_PORTES = "624";
        public string Empresa { get; set; }
        public int Id { get; set; }
        public bool ServirJunto { get; set; }
        public bool EsTiendaOnline { get; set; }
        public bool EsNotaEntrega { get; set; }
        public bool EsProductoYaFacturado { get; set; }
        public decimal ImporteOriginalSobrePedido { get; set; }
        public decimal ImporteOriginalNoSobrePedido { get; set; }
        public string CodigoPostal { get; set; }
        public string Ruta { get; set; }
        public bool Borrar { get; set; } = false;
        public decimal ImporteOriginalTotal()
        {
            return ImporteOriginalNoSobrePedido + ImporteOriginalSobrePedido;
        }
        
        public List<LineaPedidoPicking> Lineas { get; set; }

        public bool saleEnPicking()
        {
            GestorStocks gestorStocks = new GestorStocks(this);
            return (this.Lineas != null && this.Lineas.Count > 0 && (gestorStocks.HayStockDeTodo() || !this.ServirJunto));
        }

        public bool hayQueSumarPortes()
        {
            bool yaLlevaPortes = this.Lineas.FirstOrDefault(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE && l.Producto.StartsWith(PREFIJO_PORTES)) != null;

            if (yaLlevaPortes || EsProductoYaFacturado)
            {
                return false;
            }
            
            GestorStocks gestorStocks = new GestorStocks(this);
            GestorImportesMinimos gestorImportesMinimos = new GestorImportesMinimos(this);
            if (!gestorStocks.HayStockDeAlgo())
            {
                return false;
            }

            if (!gestorStocks.TodoLoQueTieneStockEsSobrePedido() && !gestorImportesMinimos.LosProductosNoSobrePedidoOriginalesLlegabanAlImporteMinimo())
            {
                return true;
            }

            if (gestorStocks.TodoLoQueTieneStockEsSobrePedido() && !gestorImportesMinimos.LosProductosSobrePedidoLleganAlImporteMinimo())
            {
                return true;
            }

            return false;
            
        }
    }
}