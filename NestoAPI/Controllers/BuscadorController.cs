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

        /// <summary>
        /// Busca productos y vídeos. Con <paramref name="incluirAnulados"/> los productos anulados
        /// se devuelven detrás de los activos, marcados con Anulado = true, para que la tienda los
        /// muestre etiquetados en vez de ocultarlos (TiendasNuevaVision#38).
        /// </summary>
        [HttpGet]
        [Route("api/buscador")]
        public IHttpActionResult Buscar(string q, string tipo = null, bool incluirAnulados = false)
        {
            System.Collections.Generic.List<dynamic> resultados = LuceneBuscador.Buscar(q, tipo, incluirAnulados: incluirAnulados);
            return Ok(resultados);
        }
    }
}
