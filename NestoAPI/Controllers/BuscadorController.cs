using NestoAPI.Infraestructure.Buscador;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    public class BuscadorController : ApiController
    {
        [HttpPost]
        [Route("api/buscador/indexar")]
        public IHttpActionResult Indexar()
        {
            LuceneBuscador.IndexarTodo();
            return Ok("Indexación completada.");
        }

        [HttpGet]
        [Route("api/buscador")]
        public IHttpActionResult Buscar(string q, string tipo = null)
        {
            System.Collections.Generic.List<dynamic> resultados = LuceneBuscador.Buscar(q, tipo);
            return Ok(resultados);
        }
    }
}
