using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#553: reposición de tiendas desde Nesto y Ariadna (sacarla de Nesto viejo), por fases. Fase 1: la propuesta
    /// (lectura) y crear, preparar y terminar la reposición escribiendo lo mismo que Nesto viejo. Leer basta con estar
    /// identificado; escribir (crear, cambiar cantidades, terminar) lo puede hacer quien tiene el almacén de origen en
    /// AlmacénPedidoVta, Almacén o Dirección (403 si no). Desde un origen con control de ubicaciones (Algete), solo
    /// Almacén y Dirección: el POST reserva los huecos (puerta única de Ubicaciones, NestoAPI#594) y la deja cerrada, lista
    /// para recoger en Ariadna; el DELETE la anula mientras nadie la haya empezado a recoger.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Reposiciones")]
    public class ReposicionesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioPreparacionReposicion preparacion;
        private readonly IServicioTransitoReposiciones transito;

        public ReposicionesController() : this(new NVEntities())
        {
        }

        internal ReposicionesController(NVEntities db) : this(db, new ServicioPreparacionReposicion(db))
        {
        }

        internal ReposicionesController(NVEntities db, IServicioPreparacionReposicion preparacion,
            IServicioTransitoReposiciones transito = null)
        {
            this.db = db;
            this.preparacion = preparacion;
            this.transito = transito; // null = el de la BD, creado al usarlo (los tests del resto no pasan db)
        }

        // GET api/Reposiciones/EnTransito?empresa=1&almacen=ALC&productos=45146,45148
        /// <summary>
        /// Nesto#510: por producto, las unidades que viajan HACIA <paramref name="almacen"/> en una reposición: pendientes de
        /// recibir en su diario de entrada (con número de traspaso) o en una reposición en preparación hacia él (Traspaso null).
        /// Solo lectura. Lista vacía si no hay nada en camino.
        /// </summary>
        [HttpGet]
        [Route("EnTransito")]
        [ResponseType(typeof(List<ProductoEnTransitoDTO>))]
        public async Task<IHttpActionResult> GetEnTransito(string almacen, string productos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            IServicioTransitoReposiciones servicio = transito ?? new ServicioTransitoReposiciones(db);
            return Ok(await servicio.LeerEnTransito(empresa, almacen, productos).ConfigureAwait(false));
        }

        // GET api/Reposiciones/Propuesta?origen=ALG&destino=REI&empresa=1
        /// <summary>
        /// Qué habría que mandar de <paramref name="origen"/> a <paramref name="destino"/> (stock máximo, pendientes
        /// y stock de los dos almacenes). 400 si los almacenes no valen o si en el destino hay una reposición
        /// anterior sin contabilizar (el procedimiento no deja mezclarlas).
        /// </summary>
        [HttpGet]
        [Route("Propuesta")]
        [ResponseType(typeof(List<LineaPropuestaReposicionDTO>))]
        public async Task<IHttpActionResult> GetPropuesta(string origen, string destino, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            List<LineaPropuestaReposicionDTO> propuesta = await new ServicioPropuestaReposicion(db)
                .CalcularPropuesta(empresa, origen, destino).ConfigureAwait(false);
            return Ok(propuesta);
        }

        // POST api/Reposiciones   { Empresa, Origen, Destino, Fecha?, Lineas?: [{ Producto, Cantidad }] }
        /// <summary>
        /// Crea la reposición en el diario de salida del origen. Sin líneas, usa la propuesta (prdRellenarReposicionStock).
        /// Desde una tienda la deja en preparación (NumTraspaso null, hasta Terminar). Desde Algete (control de ubicaciones)
        /// reserva los huecos y la cierra sin contabilizar: devuelve NumTraspaso y, por línea, Hueco / SinHueco; sale en
        /// GET api/Almacen/Recogidas (REPO). 409 si el origen tiene un inventario en curso o ya tiene una reposición en
        /// preparación; 403 si quien llama no puede (Algete: Almacén o Dirección).
        /// </summary>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> PostCrear([FromBody] CrearReposicionDTO peticion)
        {
            try
            {
                ReposicionEnPreparacionDTO creada = await preparacion.Crear(peticion, User).ConfigureAwait(false);
                return Content(HttpStatusCode.Created, creada);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // GET api/Reposiciones/EnPreparacion?origen=ALC&empresa=1
        /// <summary>La reposición que el origen tiene en preparación, con sus líneas. 404 si no hay ninguna.</summary>
        [HttpGet]
        [Route("EnPreparacion")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> GetEnPreparacion(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ReposicionEnPreparacionDTO reposicion = await preparacion.LeerEnPreparacion(empresa, origen).ConfigureAwait(false);
            return reposicion == null ? (IHttpActionResult)NotFound() : Ok(reposicion);
        }

        // PUT api/Reposiciones/EnPreparacion/Lineas/561483500?origen=ALC&empresa=1   { Cantidad }
        /// <summary>Baja la cantidad de una línea (0 para no mandarla). 400 si se intenta subir; 404 si la línea no es de esa reposición.</summary>
        [HttpPut]
        [Route("EnPreparacion/Lineas/{numeroOrden:int}")]
        [ResponseType(typeof(ReposicionEnPreparacionDTO))]
        public async Task<IHttpActionResult> PutCantidad(int numeroOrden, [FromBody] CambiarCantidadReposicionDTO cambio, string origen,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (cambio == null)
            {
                return BadRequest("Falta la cantidad.");
            }
            try
            {
                return Ok(await preparacion.CambiarCantidad(empresa, origen, numeroOrden, cambio.Cantidad, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Reposiciones/EnPreparacion/Terminar?origen=ALC&empresa=1
        /// <summary>
        /// Da la reposición por preparada: le pone número de traspaso, contabiliza la salida del origen y deja la entrada
        /// pendiente de recibir en el destino (Almacen/Recepciones REPO). 409 si no hay ninguna en preparación.
        /// </summary>
        [HttpPost]
        [Route("EnPreparacion/Terminar")]
        [ResponseType(typeof(ResultadoTerminarReposicionDTO))]
        public async Task<IHttpActionResult> PostTerminar(string origen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await preparacion.Terminar(empresa, origen, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // DELETE api/Reposiciones/80901?empresa=1
        /// <summary>
        /// Anula una reposición ya cerrada que aún no se ha recogido ni contabilizado (las de Algete creadas por el POST):
        /// devuelve las unidades a sus huecos y borra la salida y la entrada. 409 si ya tiene lecturas en Ariadna o está
        /// contabilizada; 404 si no hay ninguna por salir con ese número; 403 si no es de Almacén o Dirección.
        /// </summary>
        [HttpDelete]
        [Route("{numTraspaso:int}")]
        [ResponseType(typeof(ResultadoAnularReposicionDTO))]
        public async Task<IHttpActionResult> DeleteAnular(int numTraspaso, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            try
            {
                return Ok(await preparacion.Anular(empresa, numTraspaso, User).ConfigureAwait(false));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        /// <summary>Identificado pero sin permiso: 403, no 401 (con 401 el cliente intentaría refrescar el token).</summary>
        private IHttpActionResult Prohibido(UnauthorizedAccessException ex)
        {
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, ex.Message));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (preparacion as IDisposable)?.Dispose();
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
