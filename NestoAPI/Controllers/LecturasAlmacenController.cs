using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Ariadna#43: lo que Ariadna apunta de las lecturas: las que no casan (con el código leído, sin molestar al mozo) y por qué
    /// se ha tecleado un producto que tiene un código válido (una sola vez por producto; «No se lee» = código que se lee mal).
    ///
    /// <para>Va en un controlador propio, SIN el filtro EscrituraSoloAlmacen del resto de api/Almacen (como EtiquetasProducto):
    /// las tiendas también leen y teclean. Escriben los mismos que gestionan los códigos de barras (Compras, Almacén, Tiendas,
    /// Dirección e Informática); el resto, 403.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/Lecturas")]
    public class LecturasAlmacenController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO =
            "Para apuntar lecturas hay que ser de Compras, Almacén, Tiendas, Dirección o Informática.";

        private readonly IServicioLecturasAlmacen servicio;

        public LecturasAlmacenController(IServicioLecturasAlmacen servicio)
        {
            this.servicio = servicio;
        }

        // POST api/Almacen/Lecturas/NoCasadas?empresa=1   [ { "Codigo": "8412345678905", "ProductoEsperado": "17404", ... } ]
        /// <summary>Apunta las lecturas que no han casado. Las que no traen código (o uno de más de 50) se descartan sin error.</summary>
        [HttpPost]
        [Route("NoCasadas")]
        [ResponseType(typeof(ResultadoLecturasNoCasadasDTO))]
        public async Task<IHttpActionResult> PostNoCasadas([FromBody] List<LecturaNoCasadaDTO> lecturas,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                return Ok(await servicio.RegistrarNoCasadas(Empresa(empresa), lecturas, User?.Identity?.Name).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // GET api/Almacen/Lecturas/MotivoTecleo?producto=17404&empresa=1
        /// <summary>Si ya se ha preguntado por qué se teclea este producto (Preguntado) y qué se contestó.</summary>
        [HttpGet]
        [Route("MotivoTecleo")]
        [ResponseType(typeof(EstadoMotivoTecleoDTO))]
        public async Task<IHttpActionResult> GetMotivoTecleo(string producto, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await servicio.LeerMotivo(Empresa(empresa), producto).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // POST api/Almacen/Lecturas/MotivoTecleo?empresa=1   { "Producto": "17404", "Motivo": "NOSELEE", "Codigo": "8412345678905" }
        /// <summary>
        /// Por qué se ha tecleado: NOSELEE (el producto queda como «código que se lee mal» para el informe de los lunes a Compras,
        /// NestoAPI#674), SINCODIGO u OTRO. 400 si el motivo no vale o el producto no existe.
        /// </summary>
        [HttpPost]
        [Route("MotivoTecleo")]
        [ResponseType(typeof(EstadoMotivoTecleoDTO))]
        public async Task<IHttpActionResult> PostMotivoTecleo([FromBody] MotivoTecleoDTO motivo, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (!ServicioCodigosBarras.PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            try
            {
                return Ok(await servicio.GuardarMotivo(Empresa(empresa), motivo, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
