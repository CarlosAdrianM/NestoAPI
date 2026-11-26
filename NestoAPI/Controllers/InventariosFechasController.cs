using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#883 (corte 1): las fechas de inventario por almacén y las reposiciones que tocan su stock alrededor del recuento. Leer: cualquier
    /// empleado (un cliente de la tienda, 403). Escribir y decidir: Almacén, Dirección e Informática (los del calendario de reposiciones).
    /// Sin Scripts/Issue883_InventariosFechas.sql, 503.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Inventarios/Fechas")]
    public class InventariosFechasController : ApiController
    {
        internal const string MENSAJE_SIN_PERMISO = "Las fechas de inventario las ponen y deciden Almacén, Dirección e Informática.";

        private readonly NVEntities db;
        private ServicioFechasInventario servicio;

        public InventariosFechasController() : this(new NVEntities())
        {
        }

        internal InventariosFechasController(NVEntities db, ServicioFechasInventario servicio = null)
        {
            this.db = db;
            this.servicio = servicio;
        }

        private ServicioFechasInventario Servicio => servicio ?? (servicio = new ServicioFechasInventario(db));

        internal static bool PuedeEscribir(IPrincipal usuario) => ServicioCalendarioReposiciones.PuedeMantener(usuario);

        // GET api/Inventarios/Fechas?almacen=ALG&desde=2026-11-26&empresa=1
        /// <summary>Las fechas de inventario de <paramref name="desde"/> (sin él, hoy) en adelante, cada una con sus reposiciones y su estado.</summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<FechaInventarioDTO>))]
        public async Task<IHttpActionResult> GetFechas(string almacen = null, DateTime? desde = null, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            return Ok(await Servicio.Leer(empresa, almacen, desde).ConfigureAwait(false));
        }

        // POST api/Inventarios/Fechas   { Empresa?, Almacen, Fecha, Comentario? }
        /// <summary>Crea o cambia (por empresa + almacén + día) una fecha de inventario. 400 almacén que no es ALG/REI/ALC o día pasado; 403; 503.</summary>
        [HttpPost]
        [Route("")]
        [ResponseType(typeof(FechaInventarioDTO))]
        public async Task<IHttpActionResult> PostFecha([FromBody] FechaInventarioDTO fecha)
        {
            if (!PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            return Ok(await Servicio.Guardar(fecha, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        // DELETE api/Inventarios/Fechas/5
        /// <summary>Borra una fecha de inventario que aún no ha pasado (sus decisiones también; las excepciones ya puestas se quedan). 403; 404; 503.</summary>
        [HttpDelete]
        [Route("{id:int}")]
        [ResponseType(typeof(FechaInventarioDTO))]
        public async Task<IHttpActionResult> DeleteFecha(int id)
        {
            if (!PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            return Ok(await Servicio.Borrar(id).ConfigureAwait(false));
        }

        // POST api/Inventarios/Fechas/5/Reposiciones   { Origen, Destino, FechaLlegada, Decision: Quitar|Mantener }
        /// <summary>
        /// Decide una reposición que toca el inventario: «Quitar» pone la excepción SinReposicion del calendario (#867: 400 si ya no hay viaje,
        /// 409 si ya ha cerrado) y «Mantener» la confirma (deja de avisar). Devuelve la fecha con sus reposiciones. 403; 404; 503.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/Reposiciones")]
        [ResponseType(typeof(FechaInventarioDTO))]
        public async Task<IHttpActionResult> PostDecision(int id, [FromBody] DecisionReposicionInventarioDTO decision)
        {
            if (!PuedeEscribir(User))
            {
                return Content(HttpStatusCode.Forbidden, new { Message = MENSAJE_SIN_PERMISO });
            }
            return Ok(await Servicio.Decidir(id, decision, UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
