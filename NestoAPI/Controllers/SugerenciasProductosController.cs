using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#670 (corte 1 de la épica #669): sugerencias de productos para la plantilla de venta (Nesto y NestoApp), por
    /// reglas, y el registro de lo que se enseña, lo que contesta el vendedor y lo que entra en el pedido.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/SugerenciasProductos")]
    public class SugerenciasProductosController : ApiController
    {
        private readonly Func<IServicioSugerenciasProductos> crearServicio;

        public SugerenciasProductosController() : this(null)
        {
        }

        public SugerenciasProductosController(Func<IServicioSugerenciasProductos> crearServicio)
        {
            this.crearServicio = crearServicio;
        }

        /// <summary>
        /// Las sugerencias para el carrito de la plantilla (3, nunca más de 4). Los clientes la llaman cuando cambia el carrito, con
        /// un retardo. Si la sesión ya tiene sugerencias que siguen valiendo, se mantienen en su hueco. Sin las tablas del script,
        /// la lista va vacía.
        /// </summary>
        [HttpPost]
        [Route("Calcular")]
        [ResponseType(typeof(SugerenciasProductosDTO))]
        public async Task<IHttpActionResult> PostCalcular([FromBody] CalcularSugerenciasDTO peticion)
        {
            try
            {
                SugerenciasProductosDTO resultado = await Usar(s => s.Calcular(peticion, User?.Identity?.Name)).ConfigureAwait(false);
                return Ok(resultado);
            }
            catch (SugerenciasProductosException ex)
            {
                return ex.NoEncontrada ? (IHttpActionResult)NotFound() : BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Lo que contesta el vendedor: «Anadido» (ofrecida y aceptada) o «NoLeInteresa» (ofrecida y rechazada: no se le vuelve a
        /// sugerir a este cliente en 60 días). Con «NoLeInteresa» devuelve la sustituta (en el mismo hueco), si la hay.
        /// </summary>
        [HttpPost]
        [Route("{idSugerencia:int}/Respuesta")]
        [ResponseType(typeof(ResultadoRespuestaSugerenciaDTO))]
        public async Task<IHttpActionResult> PostRespuesta(int idSugerencia, [FromBody] RespuestaSugerenciaDTO respuesta)
        {
            try
            {
                ResultadoRespuestaSugerenciaDTO resultado = await Usar(s => s.Responder(idSugerencia, respuesta)).ConfigureAwait(false);
                return Ok(resultado);
            }
            catch (SugerenciasProductosException ex)
            {
                return ex.NoEncontrada ? Content(System.Net.HttpStatusCode.NotFound, ex.Message) : (IHttpActionResult)BadRequest(ex.Message);
            }
        }

        private async Task<T> Usar<T>(Func<IServicioSugerenciasProductos, Task<T>> accion)
        {
            if (crearServicio != null)
            {
                return await accion(crearServicio()).ConfigureAwait(false);
            }
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return await accion(new ServicioSugerenciasProductos(new RepositorioSugerenciasProductosSql(db))).ConfigureAwait(false);
            }
        }
    }
}
