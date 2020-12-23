using NestoAPI.Infraestructure.SeEstaVendiendo;
using NestoAPI.Models;
using NestoAPI.Models.SeEstaVendiendo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class SeEstaVendiendoController : ApiController
    {
        // GET: api/SeEstaVendiendo
        [HttpGet]
        [ResponseType(typeof(List<SeEstaVendiendoModel>))]
        public async Task<IHttpActionResult> GetSeEstaVendiendo(string usuario)
        {
            List<SeEstaVendiendoModel> seEstaVendiendo = await GestorSeEstaVendiendo.ArticulosVendidos(DateTime.Now.AddDays(-1), usuario).ConfigureAwait(false);
            return Ok(seEstaVendiendo);
        }
    }
}