using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Fechas de entrega fiables (corte 1, en sombra, 11/11/26): el error (fecha real del albarán − fecha prometida, en días) de lo que
    /// no salía del stock de Algete, por origen (tienda, en camino, proveedor, sin stock) y proveedor. Solo Dirección e Informática.
    /// </summary>
    [Authorize]
    public class FechasEntregaErrorController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Dirección o Informática pueden ver el error de las fechas de entrega.";
        public const int MESES_MAXIMOS = 24;

        private readonly IRepositorioFotosFechaEntrega repositorio;

        public FechasEntregaErrorController() : this(new RepositorioFotosFechaEntrega())
        {
        }

        internal FechasEntregaErrorController(IRepositorioFotosFechaEntrega repositorio)
        {
            this.repositorio = repositorio;
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/PedidosVenta/FechasEntrega/Error?meses=3
        [HttpGet]
        [Route("api/PedidosVenta/FechasEntrega/Error")]
        [ResponseType(typeof(List<ResumenErrorFechaEntregaDTO>))]
        public async Task<IHttpActionResult> GetError(int meses = 3)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (meses < 1 || meses > MESES_MAXIMOS)
            {
                return BadRequest($"Los meses van de 1 a {MESES_MAXIMOS}.");
            }
            if (!repositorio.TablaExiste())
            {
                return Ok(new List<ResumenErrorFechaEntregaDTO>());
            }
            List<ErrorFechaEntregaFila> filas = await repositorio.LeerErrores(DateTime.Today.AddMonths(-meses)).ConfigureAwait(false);
            return Ok(ReglasFotosFechaEntrega.Resumir(filas));
        }
    }
}
