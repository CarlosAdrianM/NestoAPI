using NestoAPI.Infraestructure.Picking;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class PickingController : ApiController
    {
        private GestorPicking gestorPicking;
        // GET: api/Picking?empresa=1&numeroPedido=654321[&ignorarCierreCliente=true]
        /// <param name="ignorarCierreCliente">El usuario ha confirmado en Nesto «¿Aún así quieres asignarle picking?»
        /// tras el aviso de que el cliente cierra el día de la entrega (error PICKING_CLIENTE_CERRADO).</param>
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking(string empresa, int numeroPedido, bool ignorarCierreCliente = false)
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking(empresa, numeroPedido, ignorarCierreCliente));

            return Ok(gestorPicking.PedidosEnPicking());
        }

        // GET: api/Picking
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking()
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking());

            return Ok(gestorPicking.PedidosEnPicking());
        }

        // GET: api/Picking/15191
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking(string cliente)
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking(cliente));

            return Ok(gestorPicking.PedidosEnPicking());
        }

        /// <summary>
        /// NestoAPI#361: disparo MANUAL del picking de CIERRE del día (el de las 11h).
        ///
        /// Su ejecución normal ya no pasa por aquí: desde la migración a Hangfire la lanza el job
        /// recurrente <c>picking-cierre-diario</c>. Este endpoint se mantiene para poder relanzarlo
        /// a mano si hiciera falta, y delega en EL MISMO método que el job, para que no haya dos
        /// implementaciones del picking de cierre que puedan divergir.
        ///
        /// A diferencia de <see cref="SacarPicking()"/>, declara su horizonte de entrega (hoy) en
        /// vez de deducirlo del reloj, y avisa por correo al almacén del resultado.
        /// </summary>
        [HttpGet]
        [Route("api/Picking/Automatico")]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPickingAutomatico()
        {
            List<PedidoPicking> pedidos = await Task.Run(
                () => Infraestructure.Picking.PickingJobsService.SacarPickingDeCierre());

            return Ok(pedidos);
        }

        public PickingController()
        {
            CrearGestorSugerenciasRuta = () => new GestorSugerenciasRuta(new RepositorioSugerenciasRutaSql(Db));
        }

        // Revisión 13/10/26: el contexto que crea el controlador se libera en Dispose, como en el resto de controladores
        // (antes se creaba un NVEntities por petición de sugerencias de ruta y nadie lo liberaba). Se crea solo si hace
        // falta: los endpoints de sacar picking no lo usan.
        private NVEntities db;

        /// <summary>Revisión 13/10/26: cómo se crea el contexto del controlador. Sustituible en los tests.</summary>
        internal Func<NVEntities> CrearContexto { get; set; } = () => new NVEntities();

        private NVEntities Db => db ?? (db = CrearContexto());

        /// <summary>Sugerencia 575: el gestor de las sugerencias de ruta propia. Sustituible en los tests.</summary>
        internal Func<GestorSugerenciasRuta> CrearGestorSugerenciasRuta { get; set; }

        /// <summary>Sugerencia 575: el horizonte del picking interactivo (el mismo que usará el picking). Sustituible en los tests.</summary>
        internal Func<DateTime> FechaPicking { get; set; } =
            () => GestorPicking.CalcularFechaPicking(DateTime.Now, HoraCortePicking.Leer(Constantes.Empresas.EMPRESA_POR_DEFECTO));

        /// <summary>
        /// Sugerencia 575 (Alfredo, almacén): antes de sacar el picking, los pedidos que van por OnTime y que seguramente
        /// saldrán por nuestra ruta (AT/16), con la ruta propuesta y el motivo. Lo pide el popup del picking de Nesto con el
        /// mismo alcance que el picking: un pedido (empresa + numeroPedido), un cliente o, sin nada, el de rutas.
        /// Solo para almacén: quien mete el pedido no ve esta sugerencia en ningún sitio.
        /// GET api/Picking/SugerenciasRuta[?empresa=1&amp;numeroPedido=928241 | ?cliente=5825]
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Picking/SugerenciasRuta")]
        [ResponseType(typeof(List<SugerenciaRutaPedidoDTO>))]
        public async Task<IHttpActionResult> GetSugerenciasRuta(string empresa = null, int numeroPedido = 0, string cliente = null)
        {
            if (!EsDeAlmacen())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            List<SugerenciaRutaPedidoDTO> sugerencias = await CrearGestorSugerenciasRuta()
                .LeerSugerencias(empresa, numeroPedido > 0 ? numeroPedido : (int?)null, cliente, FechaPicking()).ConfigureAwait(false);
            return Ok(sugerencias);
        }

        /// <summary>
        /// Sugerencia 575: almacén pasa a nuestra ruta (AT o 16) los pedidos que ha marcado en el aviso del picking. Solo se
        /// cambia si el pedido sigue con la ruta que se vio; queda en LogModificaciones quién y cuándo (usuario del JWT).
        /// POST api/Picking/CambiarRuta  { "Cambios": [ { "Empresa": "3", "Pedido": 928241, "RutaAnterior": "FW", "RutaNueva": "16" } ] }
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Picking/CambiarRuta")]
        [ResponseType(typeof(List<ResultadoCambioRutaDTO>))]
        public async Task<IHttpActionResult> PostCambiarRuta([FromBody] CambiarRutaPedidosRequest peticion)
        {
            if (!EsDeAlmacen())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (peticion?.Cambios == null || !peticion.Cambios.Any())
            {
                return BadRequest("No hay ningún pedido al que cambiar la ruta.");
            }
            string usuario = Infraestructure.UsuarioAuditoriaHelper.Resolver(User, peticion.Usuario);
            List<ResultadoCambioRutaDTO> resultados = await CrearGestorSugerenciasRuta()
                .CambiarRutas(peticion.Cambios, usuario).ConfigureAwait(false);
            return Ok(resultados);
        }

        // Sugerencia 575 (revisión 13/10/26): solo almacén (o dirección) ve la propuesta y cambia rutas. Antes bastaba con estar
        // identificado: cualquier vendedor de NestoApp podía pasar a AT/16 la ruta de un pedido ajeno.
        private bool EsDeAlmacen() => !EsClienteDeTienda() && Infraestructure.PreparacionAlmacen.EscrituraSoloAlmacenAttribute.PuedeEscribir(User);

        // Un cliente de la tienda (JWT con claim "cliente") no ve ni cambia rutas de pedidos.
        private bool EsClienteDeTienda()
        {
            return (User?.Identity as System.Security.Claims.ClaimsIdentity)?.FindFirst("cliente") != null;
        }

        private void crearModulos()
        {
            ModulosPicking modulos = new ModulosPicking();
            modulos.rellenadorPicking = new RellenadorPickingService();
            modulos.rellenadorStocks = new RellenadorStocksService();
            modulos.rellenadorUbicaciones = new RellenadorUbicacionesService();
            modulos.finalizador = new FinalizadorPicking();

            gestorPicking = new GestorPicking(modulos);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
                db = null;
            }
            base.Dispose(disposing);
        }
    }

}