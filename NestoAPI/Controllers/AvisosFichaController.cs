using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Ariadna#8: el mozo informa de un dato mal en la ficha de un producto (foto → Tienda online; lo demás → Compras) y
    /// quien lo recibe lo cierra con «Cambiado» o «Estaba bien», aquí o con el enlace del correo.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/AvisosFicha")]
    public class AvisosFichaController : ApiController
    {
        private readonly IServicioAvisosFicha servicio;

        public AvisosFichaController(IServicioAvisosFicha servicio)
        {
            this.servicio = servicio;
        }

        // POST api/Almacen/AvisosFicha?empresa=1
        /// <summary>El mozo (Almacén o Dirección) avisa de lo que está mal. Si ya había un aviso abierto de eso, se suma.</summary>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ResultadoInformarDatoMalDTO))]
        public async Task<IHttpActionResult> PostInformar([FromBody] InformarDatoMalDTO peticion, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!EscrituraSoloAlmacenAttribute.PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, EscrituraSoloAlmacenAttribute.MENSAJE_SIN_PERMISO);
            }
            ResultadoInformarDatoMal resultado = await servicio.Informar(Empresa(empresa), peticion, User?.Identity?.Name).ConfigureAwait(false);
            if (resultado.Estado != EstadoInformarDatoMal.Guardado)
            {
                return BadRequest(resultado.Mensaje);
            }
            return Ok(new ResultadoInformarDatoMalDTO { Mensaje = resultado.Mensaje, Avisos = resultado.Avisos });
        }

        // POST api/Almacen/AvisosFicha/7/Cerrar   { "Resultado": "Cambiado" | "EstabaBien" }
        /// <summary>Lo cierra quien lo recibe (Tienda online o Compras), Dirección o Admin. Al mozo le llega la respuesta.</summary>
        [HttpPost]
        [Route("{id:int}/Cerrar")]
        public async Task<IHttpActionResult> PostCerrar(int id, [FromBody] CerrarAvisoFichaDTO peticion)
        {
            ResultadoCerrarAvisoFicha resultado = await servicio.Cerrar(id, peticion?.Resultado, User).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoCerrarAvisoFicha.Cerrado:
                    return Ok(resultado.Mensaje);
                case EstadoCerrarAvisoFicha.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, resultado.Mensaje);
                case EstadoCerrarAvisoFicha.YaCerrado:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                case EstadoCerrarAvisoFicha.NoExiste:
                    return NotFound();
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        // GET api/Almacen/AvisosFicha/Enlace/{clave}?resultado=Cambiado
        /// <summary>
        /// Los botones del correo: sin usuario, porque se abren en el navegador. El enlace lleva la clave del aviso, que no
        /// se puede adivinar. Contesta una página corta con lo que ha pasado.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [Route("Enlace/{clave:guid}")]
        public async Task<IHttpActionResult> GetCerrarConEnlace(Guid clave, string resultado)
        {
            ResultadoCerrarAvisoFicha cierre = await servicio.CerrarConEnlace(clave, resultado).ConfigureAwait(false);
            string texto = WebUtility.HtmlEncode(cierre.Mensaje ?? "Hecho.");
            string html = "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
                "<title>Aviso de ficha</title></head><body style=\"font-family:sans-serif;padding:24px\"><p style=\"font-size:20px\">" + texto + "</p></body></html>";
            var respuesta = new HttpResponseMessage(cierre.Estado == EstadoCerrarAvisoFicha.NoExiste ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
            return ResponseMessage(respuesta);
        }

        private static string Empresa(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
