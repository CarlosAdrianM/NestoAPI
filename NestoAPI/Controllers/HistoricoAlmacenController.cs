using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
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
    /// Ariadna#63 (sugerencias 872 y 873): el histórico de pickings y packings hechos en Ariadna. Solo lectura. Lo ven quien
    /// tenga VerHistoricoAlmacen = "1" en su propia fila de ParametrosUsuario (Alfredo y Santiago, Scripts/Ariadna63_VerHistoricoAlmacen.sql),
    /// Dirección e Informática; los demás, 403. Aparte de AlmacenController para no tocar su constructor.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/Historico")]
    public class HistoricoAlmacenController : ApiController
    {
        private readonly IServicioHistoricoAlmacen servicio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<DateTime> hoy;
        private IDisposable desechable;

        // AddControllersAsServices elige el constructor público que puede construir entero: este.
        public HistoricoAlmacenController(IServicioPreparacionAlmacen preparacion, ILectorParametrosUsuario parametros)
            : this(RepositorioHistoricoAlmacenSql.ConContextoPropio(), preparacion, parametros)
        {
        }

        private HistoricoAlmacenController(RepositorioHistoricoAlmacenSql repositorio, IServicioPreparacionAlmacen preparacion, ILectorParametrosUsuario parametros)
            : this(new ServicioHistoricoAlmacen(repositorio, pedido => preparacion.LeerBultos(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido)), parametros, null)
        {
            desechable = repositorio;
        }

        internal HistoricoAlmacenController(IServicioHistoricoAlmacen servicio, ILectorParametrosUsuario parametros, Func<DateTime> hoy)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        // GET api/Almacen/Historico/Pickings?desde=2026-10-01&hasta=2026-10-29&pedido=&producto=
        /// <summary>Los pickings con algo hecho en Ariadna en esas fechas (máx. 31 días), el más reciente primero.</summary>
        [HttpGet]
        [Route("Pickings")]
        [ResponseType(typeof(List<PickingHistoricoDTO>))]
        public async Task<IHttpActionResult> GetPickings(DateTime? desde = null, DateTime? hasta = null, int? pedido = null, string producto = null)
        {
            if (!PermisosHistoricoAlmacen.PuedeVer(User, parametros))
            {
                return Prohibido();
            }
            string error = ServicioHistoricoAlmacen.ValidarFechas(desde, hasta, hoy(), out DateTime inicio, out DateTime fin);
            if (error != null)
            {
                return Content(HttpStatusCode.BadRequest, new { Message = error });
            }
            return Ok(await servicio.LeerPickings(Constantes.Empresas.EMPRESA_POR_DEFECTO, inicio, fin, pedido > 0 ? pedido : null, producto)
                .ConfigureAwait(false));
        }

        // GET api/Almacen/Historico/Pickings/99757
        [HttpGet]
        [Route("Pickings/{picking:int}")]
        [ResponseType(typeof(PickingHistoricoDetalleDTO))]
        public async Task<IHttpActionResult> GetPicking(int picking)
        {
            if (!PermisosHistoricoAlmacen.PuedeVer(User, parametros))
            {
                return Prohibido();
            }
            PickingHistoricoDetalleDTO detalle = await servicio.LeerPicking(Constantes.Empresas.EMPRESA_POR_DEFECTO, picking).ConfigureAwait(false);
            return detalle == null
                ? Content(HttpStatusCode.NotFound, new { Message = $"No hay nada del picking {picking}." })
                : (IHttpActionResult)Ok(detalle);
        }

        // GET api/Almacen/Historico/Packings?desde=2026-10-01&hasta=2026-10-29&pedido=
        /// <summary>Los pedidos con cajas o lecturas de packing en Ariadna en esas fechas (máx. 31 días), el más reciente primero.</summary>
        [HttpGet]
        [Route("Packings")]
        [ResponseType(typeof(List<PackingHistoricoDTO>))]
        public async Task<IHttpActionResult> GetPackings(DateTime? desde = null, DateTime? hasta = null, int? pedido = null)
        {
            if (!PermisosHistoricoAlmacen.PuedeVer(User, parametros))
            {
                return Prohibido();
            }
            string error = ServicioHistoricoAlmacen.ValidarFechas(desde, hasta, hoy(), out DateTime inicio, out DateTime fin);
            if (error != null)
            {
                return Content(HttpStatusCode.BadRequest, new { Message = error });
            }
            return Ok(await servicio.LeerPackings(Constantes.Empresas.EMPRESA_POR_DEFECTO, inicio, fin, pedido > 0 ? pedido : null).ConfigureAwait(false));
        }

        // GET api/Almacen/Historico/Packings/929262
        [HttpGet]
        [Route("Packings/{pedido:int}")]
        [ResponseType(typeof(PackingHistoricoDetalleDTO))]
        public async Task<IHttpActionResult> GetPacking(int pedido)
        {
            if (!PermisosHistoricoAlmacen.PuedeVer(User, parametros))
            {
                return Prohibido();
            }
            PackingHistoricoDetalleDTO detalle = await servicio.LeerPacking(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido).ConfigureAwait(false);
            return detalle == null
                ? Content(HttpStatusCode.NotFound, new { Message = $"El pedido {pedido} no tiene nada empaquetado en Ariadna." })
                : (IHttpActionResult)Ok(detalle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                desechable?.Dispose();
                desechable = null;
            }
            base.Dispose(disposing);
        }

        private IHttpActionResult Prohibido() => Content(HttpStatusCode.Forbidden, new { Message = ServicioHistoricoAlmacen.MENSAJE_SIN_PERMISO });
    }
}
