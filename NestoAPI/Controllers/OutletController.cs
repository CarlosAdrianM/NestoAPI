using NestoAPI.Infraestructure.Outlet;
using NestoAPI.Infrastructure;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#643: el Outlet de la tienda = los productos en estado 4 que están en la tienda. La API mantiene las
    /// categorías Outlet con el job nocturno «outlet-cuadre-nocturno» (detrás del interruptor
    /// Outlet:SincronizarConEstado); aquí se puede ver en cualquier momento lo que haría.
    /// </summary>
    [RoutePrefix("api/Outlet")]
    public class OutletController : ApiController
    {
        private readonly Func<ServicioOutlet> crearServicio;

        public OutletController() : this(null)
        {
        }

        internal OutletController(Func<ServicioOutlet> crearServicio)
        {
            this.crearServicio = crearServicio ?? (() => new ServicioOutlet());
        }

        private bool PuedeConsultar() => User != null
            && (User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(GruposSeguridad.COMPRAS));

        /// <summary>
        /// Lo que haría ahora el cuadre del Outlet, sin escribir nada: qué productos entran, salen o cambian de grupo,
        /// con su motivo. Solo Dirección y Compras.
        /// GET api/Outlet/Previsualizar
        /// </summary>
        [HttpGet]
        [Route("Previsualizar")]
        [Authorize]
        [ResponseType(typeof(ResultadoCuadreOutlet))]
        public async Task<IHttpActionResult> Previsualizar()
        {
            if (!PuedeConsultar())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            ServicioOutlet servicio = crearServicio();
            ResultadoCuadreOutlet resultado = await servicio.Previsualizar().ConfigureAwait(false);
            resultado.InterruptorEncendido = servicio.InterruptorEncendido();
            return Ok(resultado);
        }
    }
}
