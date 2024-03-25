using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    
    public class PedidoPicking
    {
        private const string PREFIJO_PORTES = "624";
        private const decimal DESCUADRE_PERMITIDO = .25M;
        public IRellenadorPrepagosService rellenadorPrepagos { get; set; }

        public PedidoPicking()
        {
            rellenadorPrepagos = new RellenadorPrepagosService();
        }

        public PedidoPicking(IRellenadorPrepagosService rellenadorPrepagosService)
        {
            this.rellenadorPrepagos = rellenadorPrepagosService;
        }
                
        public string Empresa { get; set; }
        public int Id { get; set; }
        public bool ServirJunto { get; set; }
        public bool EsTiendaOnline { get; set; }
        public bool EsPrecioPublicoFinal { get; set; }
        public bool EsNotaEntrega { get; set; }
        public bool EsProductoYaFacturado { get; set; }
        public decimal ImporteOriginalSobrePedido { get; set; }
        public decimal ImporteOriginalNoSobrePedido { get; set; }
        public string CodigoPostal { get; set; }
        public string Ruta { get; set; }
        public string PlazosPago { get; set; }
        public bool Borrar { get; set; }
        public bool RetenidoPorPrepago { get; private set; }
        public string Usuario { get; set; }
        public List<PrepagoDTO> Prepagos { get; set; }
        public List<ExtractoClienteDTO> ExtractosPendientes { get; set; }
        public decimal ImporteTotalConIVA
        {
            get
            {
                return Lineas.Sum(l => l.Total);
            }
        }
        public decimal ImporteOriginalTotal
        {
            get
            {
                return ImporteOriginalNoSobrePedido + ImporteOriginalSobrePedido;
            }
        }
        
        public List<LineaPedidoPicking> Lineas { get; set; }
       

        public bool saleEnPicking()
        {
            GestorStocksPicking gestorStocks = new GestorStocksPicking(this);
            bool salePorStock = this.Lineas != null && this.Lineas.Count > 0 && (gestorStocks.HayStockDeTodo() || !this.ServirJunto);
            if (!salePorStock)
            {
                return false;
            }

            if (PlazosPago == Constantes.PlazosPago.PREPAGO)
            {
                Prepagos = rellenadorPrepagos.Prepagos(Id);
                decimal total = Math.Round(ImporteTotalConIVA, 2, MidpointRounding.AwayFromZero);
                var importePrepagos = Prepagos.Sum(i => i.Importe);
                if (importePrepagos >= total - DESCUADRE_PERMITIDO)
                {
                    return true;
                }
                ExtractosPendientes = rellenadorPrepagos.ExtractosPendientes(Id);
                var importePendiente = -ExtractosPendientes.Where(e => e.estado == null || e.estado == "NRM").Sum(e => e.importePendiente);
                bool estaPagado = importePendiente >= total - DESCUADRE_PERMITIDO;
                if (estaPagado)
                {
                    return true;
                } else
                {
                    RetenidoPorPrepago = true;
                    return false;
                }
            }

            return true;
        }

        public bool hayQueSumarPortes()
        {
            if (EsTiendaOnline)
            {
                return false;
            }

            bool yaLlevaPortes = this.Lineas.Any(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE && l.Producto != null && l.Producto.StartsWith(PREFIJO_PORTES));

            if (yaLlevaPortes || EsProductoYaFacturado)
            {
                return false;
            }
            
            GestorStocksPicking gestorStocks = new GestorStocksPicking(this);
            GestorImportesMinimos gestorImportesMinimos = new GestorImportesMinimos(this);
            if (!gestorStocks.HayStockDeAlgo())
            {
                return false;
            }

            if (gestorImportesMinimos.LaEntregaLlegaAlImporteMinimo())
            {
                return false;
            }

            if (gestorImportesMinimos.LosProductosDelPedidoOriginalLlegabanAlImporteSinPortes())
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
        public string CorreoUsuarioPedido
        {
            get => rellenadorPrepagos.CorreoUsuario(Usuario);
        }
    }
}