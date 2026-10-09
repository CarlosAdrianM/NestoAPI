using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System.Net.Http;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Description;
using System.Web.Http;
using System;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#593: el cheque regalo de cara a los clientes.
    /// <para>(c4) GET Cliente: el cheque de un cliente, para que Nesto y NestoApp avisen en la plantilla y sepan qué línea
    /// mandar; el canje en sí va en POST y PUT de api/PedidosVenta.</para>
    /// <para>(aviso) Ver y probar el correo del cheque antes de lanzar la campaña (aunque esté inactiva). Solo Dirección e
    /// Informática.</para>
    /// </summary>
    [RoutePrefix("api/ChequesRegalo")]
    public class ChequesRegaloController : ApiController
    {
        private readonly NVEntities db;
        private readonly IAvisadorChequesRegalo avisador;

        public ChequesRegaloController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            avisador = AvisadorChequesRegalo.Crear(db);
        }

        public ChequesRegaloController(NVEntities db)
        {
            this.db = db;
        }

        internal ChequesRegaloController(IAvisadorChequesRegalo avisador)
        {
            this.avisador = avisador;
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
        [Authorize]
        [Route("Cliente")]
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


        /// <summary>
        /// GET api/ChequesRegalo/PrevisualizarCorreo?campana=CHEQUE50_OCT_2026&amp;cliente=15000 — el HTML del correo, con
        /// la imagen dentro (data URI) para verlo en el navegador. Sin cliente, con «Nombre del cliente». No manda nada.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("PrevisualizarCorreo")]
        public async Task<IHttpActionResult> PrevisualizarCorreo(string campana, string cliente = null)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(campana))
            {
                return BadRequest("Falta la campaña (campana=CHEQUE50_OCT_2026)");
            }
            CorreoChequeRegalo correo = await avisador.Previsualizar(campana, cliente);
            if (correo == null)
            {
                return NotFound();
            }
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(correo.Html, Encoding.UTF8, "text/html")
            };
            return ResponseMessage(respuesta);
        }

        /// <summary>
        /// POST api/ChequesRegalo/EnviarCorreoPrueba?campana=CHEQUE50_OCT_2026&amp;correo=alguien@nuevavision.es&amp;cliente=15000
        /// — manda el correo (asunto con «[PRUEBA]») a esa dirección. No marca ningún cheque.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("EnviarCorreoPrueba")]
        public async Task<IHttpActionResult> EnviarCorreoPrueba(string campana, string correo, string cliente = null)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(campana))
            {
                return BadRequest("Falta la campaña (campana=CHEQUE50_OCT_2026)");
            }
            if (!EsCorreoValido(correo))
            {
                return BadRequest("Falta un correo válido (correo=alguien@nuevavision.es)");
            }
            bool? enviado = await avisador.EnviarPrueba(campana, correo.Trim(), cliente);
            if (enviado == null)
            {
                return NotFound();
            }
            if (enviado == false)
            {
                return Content(HttpStatusCode.BadGateway, "No se ha podido mandar el correo (SMTP)");
            }
            return Ok(new { Enviado = true, Correo = correo.Trim() });
        }

        private static bool EsCorreoValido(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo))
            {
                return false;
            }
            try
            {
                return string.Equals(new MailAddress(correo.Trim()).Address, correo.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool EsDireccionOInformatica()
        {
            return User != null && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
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
