using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infrastructure;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Pagos")]
    public class PagosController : ApiController
    {
        private readonly IServicioPagos _servicioPagos;

        public PagosController(IServicioPagos servicioPagos)
        {
            _servicioPagos = servicioPagos;
        }

        [HttpPost]
        [Route("")]
        [Authorize]
        public async Task<IHttpActionResult> IniciarPago([FromBody] SolicitudPagoTPV solicitud)
        {
            if (solicitud == null)
            {
                return BadRequest("La solicitud de pago es obligatoria");
            }

            // NestoAPI#436: el pedido que se cobra NO se acepta de fuera. Un cobro con pedido entra
            // como Prepago, y dejarlo abierto permitiria a cualquier autenticado meter un prepago
            // en el pedido de otro (o pagar 1 EUR por uno de 100). Lo pone el servidor, llamando al
            // servicio directamente desde PedidosClienteController con el pedido que acaba de crear.
            solicitud.Pedido = null;

            if (solicitud.Efectos != null && solicitud.Efectos.Any())
            {
                decimal sumaEfectos = solicitud.Efectos.Sum(e => e.Importe);
                if (sumaEfectos != solicitud.Importe)
                {
                    return BadRequest($"La suma de los efectos ({sumaEfectos}) no coincide con el importe total ({solicitud.Importe})");
                }
            }

            string usuario = User?.Identity?.Name ?? "Desconocido";
            try
            {
                RespuestaIniciarPago respuesta = await _servicioPagos.IniciarPago(solicitud, usuario).ConfigureAwait(false);
                return Ok(respuesta);
            }
            catch (ArgumentException ex)
            {
                // #295: las validaciones del servicio (importe, concepto genérico sin efectos)
                // son errores del llamante → 400 con mensaje claro, no 500.
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("NotificacionRedsys")]
        [AllowAnonymous]
        public async Task<IHttpActionResult> NotificacionRedsys()
        {
            // Redsys envia la notificacion como application/x-www-form-urlencoded
            NameValueCollection formData = await Request.Content.ReadAsFormDataAsync().ConfigureAwait(false);

            if (formData == null)
            {
                return Ok();
            }

            var notificacion = new NotificacionRedsys
            {
                Ds_SignatureVersion = formData["Ds_SignatureVersion"],
                Ds_MerchantParameters = formData["Ds_MerchantParameters"],
                Ds_Signature = formData["Ds_Signature"]
            };

            // Siempre devolver 200 para que Redsys no reintente
            await _servicioPagos.ProcesarNotificacion(notificacion).ConfigureAwait(false);

            return Ok();
        }

        [HttpGet]
        [Route("{idPago:int}")]
        [Authorize]
        public async Task<IHttpActionResult> ConsultarPago(int idPago)
        {
            PagoTPVDTO pago = await _servicioPagos.ConsultarPago(idPago).ConfigureAwait(false);

            if (pago == null)
            {
                return NotFound();
            }

            return Ok(pago);
        }

        /// <summary>
        /// Nesto#261: GET api/Pagos/Auditoria?fechaDesde=2026-09-01&amp;fechaHasta=2026-09-29&amp;cliente=15191&amp;usuario=Sancho&amp;estado=Pendiente&amp;numeroOrden=B9BC22C32366&amp;limite=500
        /// Auditoría de enlaces de pago: quién y cuándo los creó, cliente, importe y adónde se enviaron.
        /// Solo lectura. Solo Administración y Dirección (403 al resto).
        /// Si se indica numeroOrden, se ignoran las fechas.
        /// </summary>
        [HttpGet]
        [Route("Auditoria")]
        [Authorize]
        public async Task<IHttpActionResult> BuscarAuditoria(DateTime? fechaDesde = null, DateTime? fechaHasta = null,
            string cliente = null, string usuario = null, string estado = null, string numeroOrden = null, int? limite = null)
        {
            if (!PuedeConsultarAuditoria(User))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (fechaDesde.HasValue && fechaHasta.HasValue && fechaDesde.Value.Date > fechaHasta.Value.Date)
            {
                return BadRequest("La fecha desde no puede ser posterior a la fecha hasta");
            }

            var filtro = new FiltroAuditoriaPagosTPV
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                Cliente = cliente,
                Usuario = usuario,
                Estado = estado,
                NumeroOrden = numeroOrden,
                Limite = limite
            };
            List<PagoTPVAuditoriaDTO> pagos = await _servicioPagos.BuscarAuditoria(filtro).ConfigureAwait(false);
            return Ok(pagos);
        }

        /// <summary>Nesto#261: la auditoría de enlaces de pago es de Administración y Dirección.</summary>
        internal static bool PuedeConsultarAuditoria(IPrincipal usuario)
        {
            return usuario != null
                && (usuario.IsInRoleSinDominio(GruposSeguridad.ADMINISTRACION)
                    || usuario.IsInRoleSinDominio(GruposSeguridad.DIRECCION));
        }

        [HttpGet]
        [Route("Auditoria/{numeroOrden}")]
        [Authorize]
        public async Task<IHttpActionResult> ConsultarAuditoria(string numeroOrden)
        {
            PagoTPVDTO pago = await _servicioPagos.ConsultarAuditoria(numeroOrden).ConfigureAwait(false);

            if (pago == null)
            {
                return NotFound();
            }

            return Ok(pago);
        }

        /// <summary>
        /// Obtiene el historial de pagos TPV de un cliente.
        /// Issue Nesto#322: Mostrar histórico de enlaces de pago NestoPago en ventana de Clientes.
        /// </summary>
        [HttpGet]
        [Route("Cliente/{empresa}/{cliente}")]
        [Authorize]
        public async Task<IHttpActionResult> ListarPorCliente(string empresa, string cliente, int limite = 20)
        {
            List<PagoTPVDTO> pagos = await _servicioPagos.ListarPorCliente(empresa, cliente, limite).ConfigureAwait(false);
            return Ok(pagos);
        }
    }
}
