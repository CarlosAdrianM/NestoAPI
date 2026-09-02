using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#178: las tarjetas guardadas de un cliente final (TiendasNuevaVision). El cliente
    /// sale SIEMPRE del claim <c>cliente</c> del JWT, igual que en <see cref="PedidosClienteController"/>:
    /// cada cliente solo ve y toca las suyas. El token de Redsys no sale nunca por aquí: la API
    /// devuelve solo lo que hace falta para pintarlas (últimos dígitos, marca, caducidad).
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Tarjetas")]
    public class TarjetasController : ApiController
    {
        private readonly ITarjetaClienteStore store;
        private readonly IServicioPagos servicioPagos;

        public TarjetasController()
            : this(new TarjetaClienteStore(),
                   new ServicioPagos(new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario()))
        {
        }

        public TarjetasController(ITarjetaClienteStore store, IServicioPagos servicioPagos = null)
        {
            this.store = store;
            this.servicioPagos = servicioPagos;
        }

        /// <summary>
        /// NestoAPI#178: cómo se cobra hoy con una tarjeta guardada, para que la app sepa si el
        /// cobro es directo (sin pasarela) o si el cliente tiene que confirmarlo en la pasarela
        /// (plan B mientras el terminal no permita MIT). Ver <see cref="ModoCobroTarjetaGuardada"/>.
        /// </summary>
        [HttpGet]
        [Route("Capacidades")]
        [ResponseType(typeof(CapacidadesTarjetasDTO))]
        public IHttpActionResult GetCapacidades()
        {
            return Ok(new CapacidadesTarjetasDTO { CobroDirecto = ModoCobroTarjetaGuardada.EsCobroDirecto });
        }

        // GET: api/Tarjetas
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<TarjetaClienteDTO>))]
        public IHttpActionResult GetTarjetas()
        {
            string cliente = ClienteDelJwt();
            if (cliente == null)
            {
                return Unauthorized();
            }

            List<TarjetaClienteDTO> tarjetas = store
                .ListarActivas(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente)
                .Select(TarjetaClienteDTO.Desde)
                .ToList();

            return Ok(tarjetas);
        }

        /// <summary>
        /// NestoAPI#178: arranca el alta de una tarjeta SIN cobro — la app abre la pasarela con
        /// una autorización de 0 EUR y, cuando Redsys confirma, el token queda guardado. Con la
        /// tarjeta dada de alta, TODOS los pedidos (el primero incluido) van por el flujo
        /// cobrar-primero: OK = pedido creado, KO = ni pedido ni cargo.
        /// </summary>
        // POST: api/Tarjetas/Alta
        [HttpPost]
        [Route("Alta")]
        [ResponseType(typeof(RespuestaIniciarPago))]
        public async Task<IHttpActionResult> PostAltaTarjeta(AltaTarjetaRequest peticion)
        {
            string cliente = ClienteDelJwt();
            if (cliente == null)
            {
                return Unauthorized();
            }

            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            RespuestaIniciarPago respuesta = await servicioPagos.IniciarAltaTarjeta(new SolicitudAltaTarjeta
            {
                Cliente = cliente,
                Correo = identity?.FindFirst(ClaimTypes.Email)?.Value,
                UrlOk = peticion?.UrlOk,
                UrlKo = peticion?.UrlKo
            }, cliente);

            return Ok(respuesta);
        }

        // DELETE: api/Tarjetas/5
        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult DeleteTarjeta(int id)
        {
            string cliente = ClienteDelJwt();
            if (cliente == null)
            {
                return Unauthorized();
            }

            TarjetaCliente tarjeta = store.ObtenerPorId(id);
            if (tarjeta == null || !string.Equals(tarjeta.Cliente?.Trim(), cliente, System.StringComparison.OrdinalIgnoreCase))
            {
                // La misma respuesta si no existe que si es de otro cliente: no se filtran ids
                return NotFound();
            }

            if (tarjeta.Activa)
            {
                store.Desactivar(id, "Eliminada por el cliente desde la app");
            }

            return Ok();
        }

        /// <summary>
        /// NestoAPI#178: lo único que la app puede decir del alta de tarjeta — a dónde volver.
        /// El cliente y el correo salen del JWT.
        /// </summary>
        public class AltaTarjetaRequest
        {
            public string UrlOk { get; set; }
            public string UrlKo { get; set; }
        }

        /// <summary>
        /// El cliente del JWT, con las mismas reglas de acceso que el resto del canal app.
        /// Null cuando el token no es de un cliente final o no pasa la validación.
        /// </summary>
        private string ClienteDelJwt()
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return null;
            }
            ValidadorAccesoCliente.ResultadoValidacion acceso = ValidadorAccesoCliente.ValidarAcceso(identity, cliente);
            return acceso.Autorizado ? cliente : null;
        }
    }
}
