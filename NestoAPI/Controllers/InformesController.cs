using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Informes;
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
        [Route("api/Informes/KitsQueSePuedenMontar")]
        [ResponseType(typeof(List<KitsQueSePuedenMontarDTO>))]
        public async Task<IHttpActionResult> GetKitsQueSePuedenMontar(string empresa, string fecha, string almacen, string filtroRutas)
        {
            List<KitsQueSePuedenMontarDTO> lista = await _servicio
                .LeerKitsQueSePuedenMontarAsync(empresa, fecha, almacen, filtroRutas)
                .ConfigureAwait(false);

            return Ok(lista);
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
    }
}
