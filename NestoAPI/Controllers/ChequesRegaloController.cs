using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Models;
using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#593 (c4): consulta del cheque regalo de un cliente, para que Nesto y NestoApp avisen en la plantilla y
    /// sepan qué línea mandar. El canje en sí va en POST y PUT de api/PedidosVenta.
    /// </summary>
    [Authorize]
    public class ChequesRegaloController : ApiController
    {
        private readonly NVEntities db;

        public ChequesRegaloController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
        }

        public ChequesRegaloController(NVEntities db)
        {
            this.db = db;
        }

        private IServicioCanjeChequesRegalo servicioCanje;
        internal IServicioCanjeChequesRegalo ServicioCanje
        {
            get => servicioCanje ?? (servicioCanje = new ServicioCanjeChequesRegalo(new RepositorioCanjeChequesRegalo(db)));
            set => servicioCanje = value;
        }

        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        /// <summary>
        /// GET api/ChequesRegalo/Cliente?empresa=1&amp;cliente=15191 → el cheque del cliente en una campaña activa (el que
        /// se puede usar o ya está en un pedido, si hay varios), o 404 si no tiene ninguno. Un cliente de la tienda (JWT
        /// con claim «cliente») solo puede consultar el suyo.
        /// </summary>
        [HttpGet]
        [Route("api/ChequesRegalo/Cliente")]
        [ResponseType(typeof(ChequeRegaloClienteDTO))]
        public async Task<IHttpActionResult> GetChequeCliente(string cliente, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente");
            }
            string clienteDelToken = (User?.Identity as ClaimsIdentity)?.FindFirst("cliente")?.Value;
            if (clienteDelToken != null && clienteDelToken.Trim() != cliente.Trim())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            ChequeRegaloClienteDTO cheque = await ServicioCanje
                .LeerChequeVigente(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(), cliente.Trim(), Ahora())
                .ConfigureAwait(false);
            if (cheque == null)
            {
                return NotFound();
            }
            return Ok(cheque);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
