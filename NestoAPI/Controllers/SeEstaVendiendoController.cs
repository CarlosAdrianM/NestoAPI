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
    [Authorize]
    public class SeEstaVendiendoController : ApiController
    {
        // GET: api/SeEstaVendiendo
        // El usuario se lee del JWT (NestoAPI#307); ?usuario= se mantiene solo como
        // fallback transitorio para clientes antiguos.
        [HttpGet]
        [ResponseType(typeof(List<SeEstaVendiendoModel>))]
        public async Task<IHttpActionResult> GetSeEstaVendiendo(string usuario = null)
        {
            string usuarioExcluido = GestorSeEstaVendiendo.ResolverUsuarioExcluido(User, usuario);
            List<SeEstaVendiendoModel> seEstaVendiendo = await GestorSeEstaVendiendo.ArticulosVendidos(DateTime.Now.AddDays(-1), usuarioExcluido).ConfigureAwait(false);
            return Ok(seEstaVendiendo);
        }
    }
}