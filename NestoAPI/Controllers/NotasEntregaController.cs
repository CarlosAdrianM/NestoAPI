using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#757 (corte 1 de #755): la cabecera de una nota de entrega (tipo Entrega / Recogida, bultos y factura original).
    /// La usan Almacén, Administración, Dirección e Informática (los mismos que api/RepartoPropio); el resto, 403.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/NotasEntrega")]
    public class NotasEntregaController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioNotasEntregaCabecera servicio;

        public NotasEntregaController() : this(new NVEntities())
        {
        }

        internal NotasEntregaController(NVEntities db) : this(db, new ServicioNotasEntregaCabecera(new RepositorioNotasEntregaCabecera(db)))
        {
        }

        internal NotasEntregaController(NVEntities db, IServicioNotasEntregaCabecera servicio)
        {
            this.db = db;
            this.servicio = servicio;
        }

        // GET api/NotasEntrega/1/930123
        /// <summary>La cabecera de la nota. 404 si el pedido no es una nota de entrega. Sin fila: Entrega, 1 bulto.</summary>
        [HttpGet]
        [Route("{empresa}/{pedido:int}")]
        [ResponseType(typeof(NotaEntregaCabeceraDTO))]
        public async Task<IHttpActionResult> GetNotaEntrega(string empresa, int pedido)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Leer(empresa, pedido));
        }

        // PUT api/NotasEntrega/1/930123/Cabecera { Tipo, Bultos, FacturaOriginal }
        /// <summary>
        /// Cambia tipo, bultos y factura original y deja fila en Modificaciones. 400 (de usted) si el pedido no es nota, el tipo
        /// no es Entrega/Recogida, una Recogida no lleva bultos (≥ 1) o la factura no existe o es de otro cliente; 404 si el
        /// pedido no existe; 409 si su envío ya está Entregado. En Entrega, Bultos no se valida (se guarda 1) y FacturaOriginal
        /// se ignora (se guarda NULL).
        /// </summary>
        [HttpPut]
        [Route("{empresa}/{pedido:int}/Cabecera")]
        [ResponseType(typeof(NotaEntregaCabeceraDTO))]
        public async Task<IHttpActionResult> PutCabecera(string empresa, int pedido, [FromBody] ActualizarNotaEntregaCabeceraDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await servicio.Actualizar(empresa, pedido, peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        private IHttpActionResult Responder(ResultadoNotaEntregaCabecera resultado)
        {
            switch (resultado.Tipo)
            {
                case TipoResultadoNotaEntrega.Ok:
                    return Ok(resultado.Contenido);
                case TipoResultadoNotaEntrega.NoEncontrado:
                    return Content(HttpStatusCode.NotFound, new { Message = resultado.Mensaje });
                case TipoResultadoNotaEntrega.Conflicto:
                    return Content(HttpStatusCode.Conflict, new { Message = resultado.Mensaje });
                default:
                    return Content(HttpStatusCode.BadRequest, new { Message = resultado.Mensaje });
            }
        }

        private IHttpActionResult Prohibido()
            => Content(HttpStatusCode.Forbidden, new { Message = "Su usuario no tiene permiso para las notas de entrega." });

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
