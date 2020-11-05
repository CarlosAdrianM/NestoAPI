using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class ReclamacionDeudaController : ApiController
    {
        // GET api/<controller>
        public IEnumerable<ReclamacionDeuda> Get()
        {
            throw new NotImplementedException();
        }

        // GET api/<controller>/5
        public string Get(int id)
        {
            throw new NotImplementedException();
        }

        // POST api/<controller>
        [ResponseType(typeof(ReclamacionDeuda))]
        public async Task<IHttpActionResult> Post([FromBody] ReclamacionDeuda reclamacion)
        {
            ServicioReclamacionDeuda servicio = new ServicioReclamacionDeuda();
            ReclamacionDeuda respuesta = await servicio.ProcesarReclamacionDeuda(reclamacion).ConfigureAwait(false);
            return Ok(respuesta);
        }

        // PUT api/<controller>/5
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<controller>/5
        public void Delete(int id)
        {
        }
    }
}