using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models.Informes;
using NestoAPI.Models.Informes.SaldoCuenta555;

namespace NestoAPI.Controllers
{
    [Authorize]
    public class InformesController : ApiController
    {
        private readonly IInformesService _servicio;

        public InformesController(IInformesService servicio)
        {
            _servicio = servicio;
        }

        [HttpGet]
        [Route("api/Informes/ResumenVentas")]
        [ResponseType(typeof(List<ResumenVentasDTO>))]
        public async Task<IHttpActionResult> GetResumenVentas(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            List<ResumenVentasDTO> lista = await _servicio
                .LeerResumenVentasAsync(fechaDesde, fechaHasta, soloFacturas)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        /// <summary>
        /// Render del informe Resumen de ventas en PDF (QuestPDF), vista comparativa Año Actual vs.
        /// Año Anterior, para que Nesto lo descargue en vez de renderizar el RDLC en local.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/ResumenVentas/Pdf")]
        public async Task<HttpResponseMessage> GetResumenVentasPdf(DateTime fechaDesde, DateTime fechaHasta, bool soloFacturas)
        {
            List<ResumenVentasDTO> datos = await _servicio
                .LeerResumenVentasAsync(fechaDesde, fechaHasta, soloFacturas)
                .ConfigureAwait(false);

            GeneradorPdfResumenVentas generador = new GeneradorPdfResumenVentas();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(datos, fechaDesde, fechaHasta, soloFacturas)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        [HttpGet]
        [Route("api/Informes/ControlPedidos")]
        [ResponseType(typeof(List<ControlPedidosDTO>))]
        public async Task<IHttpActionResult> GetControlPedidos()
        {
            List<ControlPedidosDTO> lista = await _servicio
                .LeerControlPedidosAsync()
                .ConfigureAwait(false);

            return Ok(lista);
        }

        /// <summary>
        /// Render del informe Control de pedidos en PDF (QuestPDF), para que Nesto lo descargue
        /// en vez de renderizar el RDLC ControlPedidos.rdlc en local.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/ControlPedidos/Pdf")]
        public async Task<HttpResponseMessage> GetControlPedidosPdf()
        {
            List<ControlPedidosDTO> lineas = await _servicio
                .LeerControlPedidosAsync()
                .ConfigureAwait(false);

            GeneradorPdfControlPedidos generador = new GeneradorPdfControlPedidos();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(lineas)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        [HttpGet]
        [Route("api/Informes/DetalleRapports")]
        [ResponseType(typeof(List<DetalleRapportsDTO>))]
        public async Task<IHttpActionResult> GetDetalleRapports(DateTime fechaDesde, DateTime fechaHasta, string listaVendedores = "")
        {
            List<DetalleRapportsDTO> lista = await _servicio
                .LeerDetalleRapportsAsync(fechaDesde, fechaHasta, listaVendedores)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/ExtractoContable")]
        [ResponseType(typeof(List<ExtractoContableDTO>))]
        public async Task<IHttpActionResult> GetExtractoContable(string empresa, string cuenta, DateTime fechaDesde, DateTime fechaHasta)
        {
            List<ExtractoContableDTO> lista = await _servicio
                .LeerExtractoContableAsync(empresa, cuenta, fechaDesde, fechaHasta)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        /// <summary>
        /// Render del "EXTRACTO CONTABLE" (libro mayor de una cuenta) en PDF (QuestPDF), para que Nesto
        /// lo descargue en vez de renderizar el RDLC ExtractoContable.rdlc en local (Nesto#340, Fase 2).
        /// </summary>
        [HttpGet]
        [Route("api/Informes/ExtractoContable/Pdf")]
        public async Task<HttpResponseMessage> GetExtractoContablePdf(string empresa, string cuenta, DateTime fechaDesde, DateTime fechaHasta)
        {
            List<ExtractoContableDTO> lineas = await _servicio
                .LeerExtractoContableAsync(empresa, cuenta, fechaDesde, fechaHasta)
                .ConfigureAwait(false);

            GeneradorPdfExtractoContable generador = new GeneradorPdfExtractoContable();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(cuenta, fechaDesde, fechaHasta, lineas)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        /// <summary>
        /// Issue Nesto#349 Fase 2a: apuntes del extracto contable de un proveedor concreto
        /// (p. ej. 999 = Amazon) en el rango indicado. Pensado para alimentar el Cuadre
        /// Canales Externos en el lado Nesto.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/ExtractoProveedor")]
        [ResponseType(typeof(List<ExtractoProveedorDTO>))]
        public async Task<IHttpActionResult> GetExtractoProveedor(string empresa, string proveedor, DateTime fechaDesde, DateTime fechaHasta)
        {
            List<ExtractoProveedorDTO> lista = await _servicio
                .LeerExtractoProveedorAsync(empresa, proveedor, fechaDesde, fechaHasta)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        /// <summary>
        /// Issue NestoAPI#164 (Nesto#349 Fase 4): saldo acumulado de una cuenta 555 a una
        /// fecha de corte, con identificación de movimientos abiertos (no compensados) y
        /// su antigüedad. Algoritmo en 3 pasadas: AmazonOrderId / NumeroDocumento / FIFO.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/SaldoCuenta555")]
        [ResponseType(typeof(SaldoCuenta555ResultadoDto))]
        public async Task<IHttpActionResult> GetSaldoCuenta555(string empresa, string cuenta, DateTime fechaCorte)
        {
            SaldoCuenta555ResultadoDto resultado = await _servicio
                .LeerSaldoCuenta555Async(empresa, cuenta, fechaCorte)
                .ConfigureAwait(false);

            return Ok(resultado);
        }

        [HttpGet]
        [Route("api/Informes/UbicacionesInventario")]
        [ResponseType(typeof(List<UbicacionesInventarioDTO>))]
        public async Task<IHttpActionResult> GetUbicacionesInventario(string empresa = "1")
        {
            List<UbicacionesInventarioDTO> lista = await _servicio
                .LeerUbicacionesInventarioAsync(empresa)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/UbicacionesInventario/Pdf")]
        public async Task<HttpResponseMessage> GetUbicacionesInventarioPdf(string empresa = "1")
        {
            List<UbicacionesInventarioDTO> lista = await _servicio
                .LeerUbicacionesInventarioAsync(empresa)
                .ConfigureAwait(false);

            GeneradorPdfUbicacionesInventario generador = new GeneradorPdfUbicacionesInventario();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(lista)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        [HttpGet]
        [Route("api/Informes/KitsQueSePuedenMontar")]
        [ResponseType(typeof(List<KitsQueSePuedenMontarDTO>))]
        public async Task<IHttpActionResult> GetKitsQueSePuedenMontar(string empresa, string fecha, string almacen, string filtroRutas)
        {
            List<KitsQueSePuedenMontarDTO> lista = await _servicio
                .LeerKitsQueSePuedenMontarAsync(empresa, fecha, almacen, filtroRutas)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        /// <summary>
        /// Render del informe "Kits que se pueden montar o desmontar" en PDF (QuestPDF), para que
        /// Nesto lo descargue en vez de renderizar el RDLC KitsQueSePuedenMontar.rdlc en local.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/KitsQueSePuedenMontar/Pdf")]
        public async Task<HttpResponseMessage> GetKitsQueSePuedenMontarPdf(string empresa, string fecha, string almacen, string filtroRutas)
        {
            List<KitsQueSePuedenMontarDTO> kits = await _servicio
                .LeerKitsQueSePuedenMontarAsync(empresa, fecha, almacen, filtroRutas)
                .ConfigureAwait(false);

            GeneradorPdfKitsQueSePuedenMontar generador = new GeneradorPdfKitsQueSePuedenMontar();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(kits)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        [HttpGet]
        [Route("api/Informes/MontarKitProductos")]
        [ResponseType(typeof(List<MontarKitProductosDTO>))]
        public async Task<IHttpActionResult> GetMontarKitProductos(int traspaso)
        {
            List<MontarKitProductosDTO> lista = await _servicio
                .LeerMontarKitProductosAsync(traspaso)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/Picking")]
        [ResponseType(typeof(List<PickingDTO>))]
        public async Task<IHttpActionResult> GetPicking(int picking, string empresa = "1", int personas = 1)
        {
            List<PickingDTO> lista = await _servicio
                .LeerPickingAsync(picking, empresa, personas)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/UltimoPicking")]
        [ResponseType(typeof(int))]
        public async Task<IHttpActionResult> GetUltimoPicking()
        {
            int ultimo = await _servicio
                .LeerUltimoPickingAsync()
                .ConfigureAwait(false);

            return Ok(ultimo);
        }

        [HttpGet]
        [Route("api/Informes/Packing")]
        [ResponseType(typeof(List<PackingDTO>))]
        public async Task<IHttpActionResult> GetPacking(int picking, int personas = 1)
        {
            List<PackingDTO> lista = await _servicio
                .LeerPackingAsync(picking, personas)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/ManifiestoAgencia")]
        [ResponseType(typeof(List<ManifiestoAgenciaDTO>))]
        public async Task<IHttpActionResult> GetManifiestoAgencia(string empresa, int agencia, DateTime fecha)
        {
            List<ManifiestoAgenciaDTO> lista = await _servicio
                .LeerManifiestoAgenciaAsync(empresa, agencia, fecha)
                .ConfigureAwait(false);

            return Ok(lista);
        }

        [HttpGet]
        [Route("api/Informes/PedidoCompra")]
        [ResponseType(typeof(PedidoCompraInformeDTO))]
        public async Task<IHttpActionResult> GetPedidoCompra(string empresa, int pedido)
        {
            PedidoCompraInformeDTO resultado = await _servicio
                .LeerPedidoCompraAsync(empresa, pedido)
                .ConfigureAwait(false);

            if (resultado == null) return NotFound();
            return Ok(resultado);
        }

        /// <summary>
        /// Render de la "ORDEN DE COMPRA" a proveedor en PDF (QuestPDF), para que Nesto lo descargue
        /// en vez de renderizar el RDLC PedidoCompra.rdlc en local (Nesto#340/#386). Lleva el sello
        /// Madrid Excelente (NestoAPI#244). Si el pedido no existe devuelve 404.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/PedidoCompra/Pdf")]
        public async Task<HttpResponseMessage> GetPedidoCompraPdf(string empresa, int pedido)
        {
            PedidoCompraInformeDTO resultado = await _servicio
                .LeerPedidoCompraAsync(empresa, pedido)
                .ConfigureAwait(false);

            if (resultado == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            GeneradorPdfPedidoCompra generador = new GeneradorPdfPedidoCompra();
            HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarPdf(resultado)
            };
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return result;
        }

        /// <summary>
        /// Render de la "ORDEN DE COMPRA" a proveedor en Excel (.xlsx, ClosedXML), mismo contenido que
        /// el PDF. Algunos proveedores prefieren el Excel; objetivo: eliminar el RDLC que hoy lo genera.
        /// Si el pedido no existe devuelve 404.
        /// </summary>
        [HttpGet]
        [Route("api/Informes/PedidoCompra/Excel")]
        public async Task<HttpResponseMessage> GetPedidoCompraExcel(string empresa, int pedido)
        {
            PedidoCompraInformeDTO resultado = await _servicio
                .LeerPedidoCompraAsync(empresa, pedido)
                .ConfigureAwait(false);

            if (resultado == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            GeneradorExcelPedidoCompra generador = new GeneradorExcelPedidoCompra();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = generador.GenerarExcel(resultado)
            };
        }

        [HttpGet]
        [Route("api/Informes/EtiquetasTienda")]
        [ResponseType(typeof(List<EtiquetasTiendaDTO>))]
        public async Task<IHttpActionResult> GetEtiquetasTienda(string productos)
        {
            var lista = (productos ?? string.Empty)
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            List<EtiquetasTiendaDTO> resultado = await _servicio
                .LeerEtiquetasTiendaAsync(lista)
                .ConfigureAwait(false);

            return Ok(resultado);
        }
    }
}
