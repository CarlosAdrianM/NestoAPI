using NestoAPI.Models.Comisiones;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ComisionesController : ApiController
    {

        // GET: api/Comisiones
        [HttpGet]
        [ResponseType(typeof(ResumenComisionesMes))]
        public async Task<IHttpActionResult> GetComisiones(string vendedor, int anno)
        {
            //string vendedor, DateTime fechaDesde, DateTime fechaHasta, bool incluirAlbaranes
            ServicioComisionesAnualesEstetica servicio = new ServicioComisionesAnualesEstetica();
            VendedorComisionAnual vendedorComision = new VendedorComisionAnual(servicio, vendedor, anno);


            //await Task.Run(() => vendedor = new VendedorComisionAnual(servicio, "PA", 2018));

            return Ok(vendedorComision.ResumenMesActual);
        }
    }

}