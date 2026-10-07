using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#603 (corte 1): a quién llamar hoy, por prioridad (Máxima/Alta/Media/Baja) y cadencia, con el ritmo del
    /// vendedor. Sustituye a GET api/Clientes/GetClientesProbabilidadVenta, que sigue hasta que Nesto y NestoApp cambien.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Clientes/SugerenciasContacto")]
    public class SugerenciasContactoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO_USO = "El uso de las sugerencias de contacto solo lo pueden consultar Dirección e Informática.";

        private static readonly string[] gruposUso = { Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA };

        private readonly NVEntities db;
        private readonly IServicioSugerenciasContacto servicio;

        public SugerenciasContactoController() : this(new NVEntities())
        {
        }

        internal SugerenciasContactoController(NVEntities db, IServicioSugerenciasContacto servicio = null)
        {
            this.db = db;
            this.servicio = servicio ?? new ServicioSugerenciasContacto(db);
        }

        // GET api/Clientes/SugerenciasContacto?vendedor=MPP&tipoInteraccion=Llamada&numero=20&grupoSubgrupo=
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(SugerenciasContactoDTO))]
        public async Task<IHttpActionResult> GetSugerenciasContacto(string vendedor = null, string tipoInteraccion = "", int numero = 20, string grupoSubgrupo = "")
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                return BadRequest("Hay que indicar el vendedor.");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return Ok(await servicio.Leer(vendedor, tipoInteraccion, numero, grupoSubgrupo, usuario).ConfigureAwait(false));
        }

        // GET api/Clientes/SugerenciasContacto/Uso?desde=2026-10-01&hasta=2026-10-31
        /// <summary>Por vendedor: días con sugerencias, sugeridas, atendidas y rapports totales. Por defecto, el mes en curso.</summary>
        [HttpGet]
        [Route("Uso")]
        [ResponseType(typeof(List<UsoSugerenciasContactoDTO>))]
        public async Task<IHttpActionResult> GetUso(DateTime? desde = null, DateTime? hasta = null)
        {
            if (User == null || !gruposUso.Any(g => User.IsInRoleSinDominio(g)))
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_USO));
            }
            DateTime hoy = DateTime.Today;
            DateTime inicio = desde ?? new DateTime(hoy.Year, hoy.Month, 1);
            DateTime fin = hasta ?? hoy;
            if (fin < inicio)
            {
                return BadRequest("La fecha hasta no puede ser anterior a la fecha desde.");
            }
            return Ok(await servicio.LeerUso(inicio, fin).ConfigureAwait(false));
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
