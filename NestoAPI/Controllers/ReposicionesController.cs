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
    /// AlmacénPedidoVta, Almacén o Dirección (403 si no). Un origen con control de ubicaciones (Algete) da 400 hasta
    /// NestoAPI#594.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Reposiciones")]
    public class ReposicionesController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioPreparacionReposicion preparacion;

        public ReposicionesController() : this(new NVEntities())
        {
        }

        internal ReposicionesController(NVEntities db) : this(db, new ServicioPreparacionReposicion(db))
        {
        }

        internal ReposicionesController(NVEntities db, IServicioPreparacionReposicion preparacion)
        {
            this.db = db;
            this.preparacion = preparacion;
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
        /// Crea la reposición y la deja en preparación en el diario de salida del origen. Sin líneas, usa la propuesta.
        /// 409 si el origen tiene un inventario en curso o ya tiene una reposición en preparación; 400 si el origen tiene
        /// control de ubicaciones (todavía en Nesto viejo); 403 si quien llama no es de ese almacén.
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
