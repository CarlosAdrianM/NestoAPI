using NestoAPI.Infraestructure.Direcciones;
using NestoAPI.Models.Direcciones;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#306: autocompletado de direcciones (Google Places) para el alta de clientes.
    /// Los clientes generan un sessionToken (GUID) al empezar a teclear y lo mandan en las dos
    /// llamadas; al mostrar las sugerencias deben incluir la atribución "Powered by Google".
    /// </summary>
    public class DireccionesController : ApiController
    {
        private readonly IServicioDireccionesGoogle servicio;

        public DireccionesController() : this(new ServicioDireccionesGoogle()) { }

        public DireccionesController(IServicioDireccionesGoogle servicio)
        {
            this.servicio = servicio;
        }

        // GET: api/Direcciones/Sugerencias?texto=Avenida Castilla 3&sessionToken=...
        [HttpGet]
        [Route("api/Direcciones/Sugerencias")]
        [ResponseType(typeof(List<SugerenciaDireccionDTO>))]
        public async Task<IHttpActionResult> GetSugerencias(string texto, string sessionToken = null)
        {
            if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 3)
            {
                // Con menos de 3 caracteres no merece la pena preguntar a Google
                return Ok(new List<SugerenciaDireccionDTO>());
            }

            List<SugerenciaDireccionDTO> sugerencias = await servicio.BuscarSugerencias(texto.Trim(), sessionToken);
            return Ok(sugerencias);
        }

        // GET: api/Direcciones/Detalle?placeId=...&sessionToken=...
        [HttpGet]
        [Route("api/Direcciones/Detalle")]
        [ResponseType(typeof(DireccionDetalleDTO))]
        public async Task<IHttpActionResult> GetDetalle(string placeId, string sessionToken = null)
        {
            if (string.IsNullOrWhiteSpace(placeId))
            {
                return BadRequest("El parámetro 'placeId' es obligatorio");
            }

            DireccionDetalleDTO detalle = await servicio.LeerDetalle(placeId, sessionToken);
            return Ok(detalle);
        }
    }
}
