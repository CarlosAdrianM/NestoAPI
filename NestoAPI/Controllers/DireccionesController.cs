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

        // GET: api/Direcciones/Sugerencias?texto=Avenida Castilla 3&sessionToken=...&pais=IT
        // Nesto#436: pais es el ISO alpha-2 donde buscar; sin él se busca en España (como siempre).
        [HttpGet]
        [Route("api/Direcciones/Sugerencias")]
        [ResponseType(typeof(List<SugerenciaDireccionDTO>))]
        public async Task<IHttpActionResult> GetSugerencias(string texto, string sessionToken = null, string pais = null)
        {
            if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < 3)
            {
                // Con menos de 3 caracteres no merece la pena preguntar a Google
                return Ok(new List<SugerenciaDireccionDTO>());
            }

            try
            {
                List<SugerenciaDireccionDTO> sugerencias = await servicio.BuscarSugerencias(texto.Trim(), sessionToken, pais);
                return Ok(sugerencias);
            }
            catch (System.ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
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

        // GET: api/Direcciones/DesdeCoordenadas?latitud=40.5605&longitud=-3.4702
        /// <summary>
        /// NestoAPI#578: la dirección de la ubicación del móvil, para que NestoApp deje el plugin
        /// nativegeocoder (alta de cliente y códigos postales de Rapports). 404 si Google no encuentra
        /// nada; 400 si faltan las coordenadas o están fuera de rango; si Google falla, 502 con el motivo
        /// (la app lo trata como «no se ha podido» y deja los campos como estaban).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Direcciones/DesdeCoordenadas")]
        [ResponseType(typeof(DireccionDetalleDTO))]
        public async Task<IHttpActionResult> GetDesdeCoordenadas(double? latitud = null, double? longitud = null)
        {
            if (!latitud.HasValue || !longitud.HasValue)
            {
                return BadRequest("Faltan la latitud y la longitud");
            }
            if (latitud < -90 || latitud > 90 || longitud < -180 || longitud > 180)
            {
                return BadRequest("Las coordenadas están fuera de rango (latitud -90..90, longitud -180..180)");
            }

            DireccionDetalleDTO detalle;
            try
            {
                detalle = await servicio.LeerDesdeCoordenadas(latitud.Value, longitud.Value);
            }
            catch (System.Exception ex)
            {
                return Content(System.Net.HttpStatusCode.BadGateway, "No se ha podido sacar la dirección de esas coordenadas: " + ex.Message);
            }
            return detalle == null ? (IHttpActionResult)NotFound() : Ok(detalle);
        }
    }
}
