using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#693: los pedidos con la fecha prometida en riesgo (lo registrado sin resolver), para la pantalla del corte 2
    /// (Nesto y NestoApp). Controlador propio para no tocar PedidosVentaController.
    /// </summary>
    [Authorize]
    public class PedidosVentaPromesaRiesgoController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo puede ver los pedidos de su vendedor o de los vendedores de su equipo.";

        private readonly NVEntities db;
        private readonly IRepositorioPromesaRiesgo repositorio;
        private readonly IServicioVendedoresVisibles visibles;
        private readonly IServicioVendedores vendedores;

        public PedidosVentaPromesaRiesgoController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            repositorio = new RepositorioPromesaRiesgo();
            vendedores = new ServicioVendedores();
            visibles = new ServicioVendedoresVisibles(db, vendedores);
        }

        internal PedidosVentaPromesaRiesgoController(IRepositorioPromesaRiesgo repositorio, IServicioVendedoresVisibles visibles, IServicioVendedores vendedores)
        {
            this.repositorio = repositorio;
            this.visibles = visibles;
            this.vendedores = vendedores;
        }

        // GET api/PedidosVenta/PromesaEnRiesgo?empresa=1&vendedor=JGP
        /// <summary>
        /// NestoAPI#693: los pedidos registrados en PedidosPromesaRiesgo sin resolver y con algo pendiente, de la fecha prometida
        /// más próxima a la más lejana. Como los demás listados de pedidos del vendedor: con <paramref name="vendedor"/>, los
        /// suyos y, si es jefe de equipo, los de su equipo (403 si el usuario no puede verlo); sin vendedor, Dirección ve todos y
        /// el resto, los de su vendedor y su equipo. Cuenta el vendedor de la cabecera y el de peluquería del pedido. Lista vacía
        /// si aún no se ha lanzado Scripts/Issue693_PedidosPromesaRiesgo.sql.
        /// </summary>
        [HttpGet]
        [Route("api/PedidosVenta/PromesaEnRiesgo")]
        [ResponseType(typeof(List<PedidoPromesaRiesgoDTO>))]
        public async Task<IHttpActionResult> GetPromesaEnRiesgo(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string vendedor = null)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            if ((User?.Identity as ClaimsIdentity)?.FindFirst("cliente") != null)
            {
                return Prohibido();
            }

            HashSet<string> filtro;
            if (!string.IsNullOrWhiteSpace(vendedor))
            {
                if (!await visibles.PuedeVer(User, empresa, vendedor).ConfigureAwait(false))
                {
                    return Prohibido();
                }
                List<string> equipo = await vendedores.VendedoresEquipoString(empresa, vendedor.Trim()).ConfigureAwait(false) ?? new List<string>();
                filtro = new HashSet<string>(equipo.Select(v => v?.Trim()).Where(v => !string.IsNullOrEmpty(v)), StringComparer.OrdinalIgnoreCase)
                {
                    vendedor.Trim()
                };
            }
            else if (ServicioVendedoresVisibles.EsDireccion(User))
            {
                filtro = null;
            }
            else
            {
                List<VendedorVisibleDTO> suyos = await visibles.LeerVisibles(User, empresa).ConfigureAwait(false) ?? new List<VendedorVisibleDTO>();
                filtro = new HashSet<string>(suyos.Select(v => v.Vendedor?.Trim()).Where(v => !string.IsNullOrEmpty(v)), StringComparer.OrdinalIgnoreCase);
            }

            if (filtro != null && !filtro.Any())
            {
                return Ok(new List<PedidoPromesaRiesgoDTO>());
            }
            if (!repositorio.TablaExiste())
            {
                return Ok(new List<PedidoPromesaRiesgoDTO>());
            }
            List<RiesgoPromesaListado> filas = await repositorio.LeerListado(empresa).ConfigureAwait(false) ?? new List<RiesgoPromesaListado>();
            return Ok(Filtrar(filas, filtro).Select(PedidoPromesaRiesgoDTO.De).ToList());
        }

        /// <summary>Con <paramref name="vendedores"/> null, todos; si no, los del vendedor de la cabecera o el de peluquería.</summary>
        internal static IEnumerable<RiesgoPromesaListado> Filtrar(IEnumerable<RiesgoPromesaListado> filas, ISet<string> vendedores)
        {
            return (filas ?? Enumerable.Empty<RiesgoPromesaListado>())
                .Where(f => vendedores == null
                    || (!string.IsNullOrWhiteSpace(f.Vendedor) && vendedores.Contains(f.Vendedor.Trim()))
                    || (!string.IsNullOrWhiteSpace(f.VendedorPeluqueria) && vendedores.Contains(f.VendedorPeluqueria.Trim())));
        }

        private IHttpActionResult Prohibido() =>
            ResponseMessage(Request != null
                ? Request.CreateErrorResponse(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO)
                : new HttpResponseMessage(HttpStatusCode.Forbidden));

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
