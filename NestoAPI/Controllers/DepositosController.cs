using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Depositos;
using NestoAPI.Models.Depositos;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    public class DepositosController : ApiController
    {
        // GET api/<controller>
        public async Task<List<DepositoCorreoProveedor>> Get()
        {
            IServicioDeposito servicio = new ServicioDeposito();
            IServicioGestorStocks servicioGestorStocks = new ServicioGestorStocks();
            GestorDepositos gestor = new GestorDepositos(servicio, servicioGestorStocks);

            var lista = await gestor.EnviarCorreos().ConfigureAwait(false);

            return lista;
        }

        /*
        // GET api/<controller>/5
        public string Get(int id)
        {
            return "value";
        }

        // POST api/<controller>
        public void Post([FromBody] string value)
        {
        }

        // PUT api/<controller>/5
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/<controller>/5
        public void Delete(int id)
        {
        }
        */
    }
}