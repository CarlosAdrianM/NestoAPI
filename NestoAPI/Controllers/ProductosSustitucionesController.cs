using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#581: sustitución temporal de referencias («mientras tanto, servid la 45685 en lugar de la 25539»).
    /// Quien mete el pedido (Nesto: plantilla y detalle; NestoApp) pregunta por la vigente al meter el producto y
    /// ofrece cambiarlo; Compras la mantiene desde la ficha del producto. Mismo permiso que el resto del
    /// mantenimiento de productos de la API (usuario identificado); en Nesto la pestaña solo la ve Compras.
    /// </summary>
    [Authorize]
    public class ProductosSustitucionesController : ApiController
    {
        private readonly IServicioSustitucionesProducto servicio;

        // AddControllersAsServices elige el constructor público que puede construir entero: este.
        public ProductosSustitucionesController() : this(new ServicioSustitucionesProducto())
        {
        }

        internal ProductosSustitucionesController(IServicioSustitucionesProducto servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        // GET api/Productos/25539/Sustitucion?empresa=1&cantidad=100
        /// <summary>
        /// La sustitución que hay que avisar AHORA al pedir esa cantidad, o null (200 con cuerpo null) si no hay que
        /// avisar: no tiene, está anulada, ya pasó su fecha o es «mientras no haya stock» y hay disponible suficiente.
        /// </summary>
        [HttpGet]
        [Route("api/Productos/{producto}/Sustitucion")]
        [ResponseType(typeof(SustitucionProductoDTO))]
        public async Task<IHttpActionResult> GetSustitucionVigente(string producto, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, int cantidad = 1)
        {
            SustitucionProductoDTO vigente = await servicio.Vigente(Empresa(empresa), producto, cantidad).ConfigureAwait(false);
            return Ok(vigente);
        }

        // GET api/Productos/25539/Sustituciones?empresa=1
        /// <summary>Todas las del producto (la activa primero, luego de la más nueva a la más vieja), con su Estado.</summary>
        [HttpGet]
        [Route("api/Productos/{producto}/Sustituciones")]
        [ResponseType(typeof(List<SustitucionProductoDTO>))]
        public async Task<IHttpActionResult> GetSustituciones(string producto, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.Listar(Empresa(empresa), producto).ConfigureAwait(false));
        }

        // POST api/Productos/25539/Sustituciones
        /// <summary>
        /// Da de alta la sustitución (201) y anula la que hubiera activa del mismo producto. 400 con el motivo si no
        /// vale (sin «hasta cuándo», fecha pasada, sustituto inexistente o que ya se sustituye por este); 404 si el
        /// producto no existe.
        /// </summary>
        [HttpPost]
        [Route("api/Productos/{producto}/Sustituciones")]
        [ResponseType(typeof(SustitucionProductoDTO))]
        public async Task<IHttpActionResult> PostSustitucion(string producto, [FromBody] NuevaSustitucionProductoDTO nueva)
        {
            ResultadoSustitucion resultado = await servicio.Crear(producto, nueva, Usuario()).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoOperacionSustitucion.Ok:
                    return Content(HttpStatusCode.Created, resultado.Sustitucion);
                case EstadoOperacionSustitucion.NoEncontrado:
                    return Content(HttpStatusCode.NotFound, new { Message = resultado.Mensaje });
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        // DELETE api/Productos/25539/Sustituciones/7?empresa=1
        /// <summary>La anula (no se borra: queda en el historial). 404 si no existe o ya estaba anulada.</summary>
        [HttpDelete]
        [Route("api/Productos/{producto}/Sustituciones/{id:int}")]
        public async Task<IHttpActionResult> DeleteSustitucion(string producto, int id, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            bool anulada = await servicio.Anular(Empresa(empresa), producto, id, Usuario()).ConfigureAwait(false);
            return anulada
                ? (IHttpActionResult)StatusCode(HttpStatusCode.NoContent)
                : Content(HttpStatusCode.NotFound, new { Message = "Esa sustitución no existe o ya estaba anulada." });
        }

        private string Usuario()
        {
            return UsuarioAuditoriaHelper.Resolver(User, null);
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }
    }
}
