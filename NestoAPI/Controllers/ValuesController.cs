using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using System.Web.Http.Results;

namespace NestoAPI.Controllers
{
    public class ValuesController : ApiController
    {
        /*
        // GET api/values
        public IEnumerable<string> Get()
        {
            return new string[] { "value1", "value2" };
        }
        */
        // GET api/values/5
        public string Get(int id)
        {
            return "value";
        }

        // POST api/values
        public void Post([FromBody]string value)
        {
        }

        // PUT api/values/5
        public void Put(int id, [FromBody]string value)
        {
        }

        // DELETE api/values/5
        public void Delete(int id)
        {
        }



        private NVEntities db = new NVEntities();

        public ValuesController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        // GET: api/Values
        [ResponseType(typeof(List<RespuestaValidacion>))]
        public async Task<IHttpActionResult> GetPedidosNoValidados()
        {
            List<RespuestaValidacion> listaRespuestas = new List<RespuestaValidacion>();

            PedidosVentaController pedidosVentaController = new PedidosVentaController();
            IQueryable<ResumenPedidoVentaDTO> resumenes = pedidosVentaController.GetPedidosVenta();

            foreach (ResumenPedidoVentaDTO resumen in resumenes.Where(r => r.empresa == "1" || r.empresa == "3"))
            {
                IHttpActionResult actionResult = await pedidosVentaController.GetPedidoVenta(resumen.empresa, resumen.numero);
                var pedido = actionResult as OkNegotiatedContentResult<PedidoVentaDTO>;
                RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido.Content);
                listaRespuestas.Add(respuesta);
            }

            if (listaRespuestas == null)
            {
                return NotFound();
            }

            return Ok(listaRespuestas.Where(l=>!l.ValidacionSuperada));
        }


    }
}
