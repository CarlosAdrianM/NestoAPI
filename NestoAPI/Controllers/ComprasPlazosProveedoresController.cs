using Hangfire;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#853: respuesta de POST api/Compras/PlazosProveedores/Recalcular.</summary>
    public class RecalculoPlazosProveedoresDTO
    {
        public string JobId { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#853 (corte 2): lo que suele tardar cada proveedor (dbo.ProveedoresPlazosEntrega, que rellena de noche el job
    /// <see cref="PlazosProveedoresJobsService.JOB_ID"/>). El GET solo lee la tabla; el recálculo se encola en Hangfire.
    /// </summary>
    public class ComprasPlazosProveedoresController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Compras, Dirección o Informática pueden ver los plazos de los proveedores.";
        public const string MENSAJE_SIN_PERMISO_RECALCULAR = "Solo Dirección o Informática pueden recalcular los plazos de los proveedores.";

        private readonly Func<ServicioPlazosProveedores> crearServicio;
        private readonly Func<string> encolar;

        public ComprasPlazosProveedoresController()
            : this(() => new ServicioPlazosProveedores(RepositorioPlazosProveedores.DesdeConfiguracion()),
                  () => BackgroundJob.Enqueue(() => PlazosProveedoresJobsService.Recalcular()))
        {
        }

        internal ComprasPlazosProveedoresController(Func<ServicioPlazosProveedores> crearServicio, Func<string> encolar)
        {
            this.crearServicio = crearServicio ?? throw new ArgumentNullException(nameof(crearServicio));
            this.encolar = encolar ?? (() => "0");
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        internal static bool PuedeRecalcular(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Compras/PlazosProveedores
        /// <summary>Las filas de la tabla (la genérica, «*», al final). 503 sin el script.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/PlazosProveedores")]
        [ResponseType(typeof(List<PlazoProveedor>))]
        public IHttpActionResult GetPlazos(string empresa = null)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            ServicioPlazosProveedores servicio = crearServicio();
            if (!servicio.TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioPlazosProveedores.MENSAJE_SIN_TABLA);
            }
            return Ok(servicio.Leer(empresa));
        }

        // POST api/Compras/PlazosProveedores/Recalcular
        /// <summary>Encola el recálculo (202). 503 sin el script.</summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Recalcular")]
        [ResponseType(typeof(RecalculoPlazosProveedoresDTO))]
        public IHttpActionResult PostRecalcular()
        {
            if (!PuedeRecalcular(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_RECALCULAR);
            }
            if (!crearServicio().TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioPlazosProveedores.MENSAJE_SIN_TABLA);
            }
            string id = encolar();
            return Content(HttpStatusCode.Accepted, new RecalculoPlazosProveedoresDTO
            {
                JobId = id,
                Mensaje = "Recálculo de los plazos de los proveedores encolado: en unos minutos estará en GET api/Compras/PlazosProveedores."
            });
        }
    }
}
