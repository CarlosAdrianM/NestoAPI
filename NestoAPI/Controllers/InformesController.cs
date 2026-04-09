using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;

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
    }
}
