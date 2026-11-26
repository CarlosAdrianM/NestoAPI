using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Aparatos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#913, corte 1 (Carlos 26/11/26), SOLO LECTURA: la historia de un aparato (producto + número de serie) y el informe de
    /// descuadres por aparato, con lo que ya hay (compras, ventas, registro de Alquileres, inmovilizados). Dirección, Informática,
    /// Administración, Almacén y Compras; un cliente de la tienda, 403.
    /// </summary>
    [Authorize]
    public class AparatosController : ApiController
    {
        internal static readonly string[] GRUPOS =
        {
            Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA, Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.COMPRAS
        };

        internal const string MENSAJE_SIN_PERMISO = "La historia de los aparatos y sus descuadres son de Dirección, Informática, Administración, Almacén y Compras.";

        private readonly NVEntities db;
        private ILectorAparatos lector;

        public AparatosController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        internal AparatosController(ILectorAparatos lector)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
        }

        internal ILectorAparatos Lector => lector ?? (lector = new LectorAparatosSql(db));

        internal static bool PuedeVer(IPrincipal usuario)
            => usuario != null && !ValidadorAccesoCliente.EsClienteTienda(usuario) && GRUPOS.Any(g => usuario.IsInRoleSinDominio(g));

        private IHttpActionResult Prohibido()
        {
            string mensaje = ValidadorAccesoCliente.EsClienteTienda(User) ? ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO : MENSAJE_SIN_PERMISO;
            return Content(HttpStatusCode.Forbidden, new HttpError(mensaje));
        }

        // GET api/Aparatos/Historia?producto=36929&numeroSerie=A32046
        /// <summary>
        /// La historia de un aparato: su situación calculada de los movimientos (EnStock, Vendido, Inmovilizado, Alquilado,
        /// DevueltoAlProveedor, SinRastro) con una frase, su registro de Alquileres y los movimientos por fecha. 400 sin producto o
        /// sin serie; 404 si no hay rastro en ningún sitio.
        /// </summary>
        [HttpGet]
        [Route("api/Aparatos/Historia")]
        [ResponseType(typeof(HistoriaAparatoDTO))]
        public async Task<IHttpActionResult> GetHistoria(string producto = null, string numeroSerie = null)
        {
            if (!PuedeVer(User))
            {
                return Prohibido();
            }
            if (string.IsNullOrWhiteSpace(producto) || string.IsNullOrWhiteSpace(numeroSerie))
            {
                return BadRequest("Indique el producto y el número de serie del aparato.");
            }
            DatosAparatos datos = await Lector.Leer(new FiltroAparatos { Producto = producto.Trim(), NumeroSerie = numeroSerie.Trim() }).ConfigureAwait(false);
            HistoriaAparatoDTO historia = ReglaHistoriaAparato.Construir(producto, numeroSerie, datos);
            if (historia == null)
            {
                return Content(HttpStatusCode.NotFound, new HttpError(
                    $"No hay rastro del aparato {producto.Trim()} con número de serie {numeroSerie.Trim()}: ni compras, ni ventas, ni registro de Alquileres, ni inmovilizados."));
            }
            return Ok(historia);
        }

        // GET api/Aparatos/Descuadres?soloListaAlquileres=false
        /// <summary>
        /// Por aparato (producto + serie), los que la situación calculada de los movimientos no coincide con el registro, con sus
        /// motivos, y el resumen por producto (stock contable por almacén, en stock según el registro y según los movimientos). Como
        /// mucho 2.000 filas (con aviso). Con <paramref name="soloListaAlquileres"/>, solo los productos de la ventana de Alquileres.
        /// </summary>
        [HttpGet]
        [Route("api/Aparatos/Descuadres")]
        [ResponseType(typeof(InformeDescuadresAparatosDTO))]
        public async Task<IHttpActionResult> GetDescuadres(bool soloListaAlquileres = false)
        {
            if (!PuedeVer(User))
            {
                return Prohibido();
            }
            DatosAparatos datos = await Lector.Leer(new FiltroAparatos { SoloListaAlquileres = soloListaAlquileres, ConStock = true }).ConfigureAwait(false);
            return Ok(ReglaDescuadresAparatos.Calcular(datos, soloListaAlquileres));
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
