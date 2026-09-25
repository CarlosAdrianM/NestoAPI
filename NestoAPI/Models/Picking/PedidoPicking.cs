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
            Lineas = new List<LineaPedidoPicking>();
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
        /// <summary>NestoAPI#488: fecha del pedido (CabPedidoVta.Fecha), decide el importe de los portes provinciales.</summary>
        public System.DateTime? Fecha { get; set; }
        public string Ruta { get; set; }
        public string PlazosPago { get; set; }
        public bool Borrar { get; set; }
        public bool RetenidoPorPrepago { get; private set; }
        public string Iva { get; set; }
        //public bool EsContrareembolso { get; set; }
        public string Usuario { get; set; }
        // NestoAPI#253: casilla "Avisar con importe cuando coja picking" de la cabecera
        public bool AvisarConImporteAlCogerPicking { get; set; }
        public string Vendedor { get; set; }
        public string Cliente { get; set; }
        /// <summary>NestoAPI#362: Clientes.DiasEnServir ("11111" = abre L-V, '0' = cierra ese
        /// día). Null o formato raro = abierto. Lo usa GestorDiasEnServir para no sacar picking
        /// de pedidos cuya entrega caería en día cerrado.</summary>
        public string DiasEnServir { get; set; }
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
       

        /// <summary>NestoAPI#482: modo de servicio informado (null = manda ServirJunto).</summary>
        public byte? ModoServicio { get; set; }

        /// <summary>NestoAPI#482: el pedido ya tuvo una primera entrega (alguna línea en albarán o
        /// factura). En el modo 4 es lo que convierte "ahora lo que hay" en "el resto de una vez".</summary>
        public bool TieneLineasServidas { get; set; }

        /// <summary>NestoAPI#482 (modo 3): lo marca GestorReposicionTiendas tras repartir el stock.
        /// True = a alguna línea le va a llegar stock suyo que aún no está en Algete (en una tienda o
        /// en camino), así que el pedido no sale en esta pasada.</summary>
        public bool EsperaReposicionDeTiendas { get; set; }

        public byte ModoServicioEfectivo => Constantes.Pedidos.ModosServicio.Efectivo(ModoServicio, ServirJunto);

        /// <summary>NestoAPI#542: MantenerJunto de la cabecera (manda cuando ModoFacturacion es null).</summary>
        public bool MantenerJunto { get; set; }
        /// <summary>NestoAPI#542: modo de facturación informado (null = manda MantenerJunto).</summary>
        public byte? ModoFacturacion { get; set; }
        public byte ModoFacturacionEfectivo => Constantes.Pedidos.ModosFacturacion.Efectivo(ModoFacturacion, MantenerJunto);
        /// <summary>NestoAPI#542: «todo ahora, lo pendiente después»: el pedido se factura entero con el primer
        /// albarán y lo que no sale en esta pasada va a Recoger (GestorFacturarTodoAhora).</summary>
        public bool FacturaTodoAhora => Constantes.Pedidos.ModosFacturacion.EsTodoAhora(ModoFacturacionEfectivo);

        /// <summary>
        /// NestoAPI#482: una estrategia por modo, en vez de un if más.
        /// 1: siempre hace falta stock de todo. 2: nunca. 4: solo a partir de la segunda entrega
        /// (la primera saca lo que hay; el resto va de una vez). 3: nunca exige stock de todo, pero
        /// no sale mientras le quede stock suyo por llegar de las tiendas (EsperaReposicionDeTiendas,
        /// que decide GestorReposicionTiendas); cuando ya no queda nada que traer, sale con lo que hay.
        ///
        /// <para>NestoAPI#529: en los modos parciales, si lo único que se quedaría pendiente son
        /// regalos (base 0), no se sirve a medias: se espera a tenerlo todo, como en el 1. Si no,
        /// el regalo acaba saliendo solo en un envío de 0 € (pedido 926923).</para>
        /// </summary>
        public bool ExigeStockDeTodo()
        {
            switch (ModoServicioEfectivo)
            {
                case Constantes.Pedidos.ModosServicio.TODO_JUNTO:
                    return true;
                case Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ:
                    return TieneLineasServidas || new GestorStocksPicking(this).LoPendienteSonSoloRegalos();
                default:
                    return new GestorStocksPicking(this).LoPendienteSonSoloRegalos();
            }
        }

        public bool saleEnPicking()
        {
            if (ModoServicioEfectivo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS && EsperaReposicionDeTiendas)
            {
                return false;
            }
            GestorStocksPicking gestorStocks = new GestorStocksPicking(this);
            bool salePorStock = this.Lineas != null && this.Lineas.Count > 0 && (!ExigeStockDeTodo() || gestorStocks.HayStockDeTodo());
            if (!salePorStock)
            {
                return false;
            }

            return CubiertoPorPrepago();
        }

        /// <summary>
        /// NestoAPI#542: la retención por prepago, separada de <see cref="saleEnPicking"/> para que «todo
        /// ahora» (GestorFacturarTodoAhora) pueda preguntarla ANTES de convertir lo que falta en Recoger:
        /// un pedido retenido por prepago se queda como está. Marca RetenidoPorPrepago si no está cubierto.
        /// </summary>
        public bool CubiertoPorPrepago()
        {
            if (PlazosPago != Constantes.PlazosPago.PREPAGO)
            {
                return true;
            }
            {
                Prepagos = rellenadorPrepagos.Prepagos(Id);
                decimal total = Math.Round(ImporteTotalConIVA, 2, MidpointRounding.AwayFromZero);
                var importePrepagos = Prepagos.Sum(i => i.Importe);

                ExtractosPendientes = rellenadorPrepagos.ExtractosPendientes(Id);
                var extractosValidos = ExtractosPendientes.Where(e => e.estado == null || e.estado == "NRM");

                // Saldo a favor del cliente (importePendiente negativo): siempre se suma
                var saldoAFavor = -extractosValidos.Where(e => e.importePendiente < 0).Sum(e => e.importePendiente);

                // Deuda del cliente (importePendiente positivo): solo se resta si está vencida
                var deudaVencida = extractosValidos
                    .Where(e => e.importePendiente > 0 && e.vencimiento.HasValue && e.vencimiento.Value < DateTime.Today)
                    .Sum(e => e.importePendiente);

                // Sumar prepagos + saldo a favor - deuda vencida
                var importeTotalDisponible = importePrepagos + saldoAFavor - deudaVencida;

                if (importeTotalDisponible >= total - DESCUADRE_PERMITIDO)
                {
                    return true;
                }
                else
                {
                    // NestoAPI#202 (diagnóstico): si un pedido PREPAGO no sale en picking,
                    // dejamos traza en ELMAH con el desglose. Sirve para investigar casos
                    // como pedido 916885 (mayo 2026) donde según el usuario el prepago + un
                    // movimiento pendiente cubrían el total pero el pedido no cogió picking.
                    // Test reproduciendo los datos del ticket pasa, así que cuando vuelva a
                    // ocurrir necesitamos ver los importes reales.
                    System.Diagnostics.Trace.WriteLine(
                        $"[Picking#202] Pedido {Id} retenido por prepago. " +
                        $"Total={total}, Prepagos={importePrepagos}, " +
                        $"SaldoAFavor={saldoAFavor}, DeudaVencida={deudaVencida}, " +
                        $"Disponible={importeTotalDisponible}, " +
                        $"ExtractosPendientesCount={ExtractosPendientes?.Count ?? 0}, " +
                        $"ExtractosValidosCount={extractosValidos.Count()}");
                    RetenidoPorPrepago = true;
                    return false;
                }
            }
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