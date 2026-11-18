using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#869: la próxima entrada de compra de un producto para la ficha (Nesto). Solo lectura. Va aparte de GET api/Productos
    /// (ProductoDTO también viaja por Nesto_sync y lo pide NestoApp): así la ficha no espera a los plazos y nada más cambia.
    /// </summary>
    public class ProximaEntradaProductoController : ApiController
    {
        public const string MENSAJE_SIN_PRODUCTO = "Falta el producto.";

        private readonly Func<ServicioProximaEntradaProducto> crearServicio;

        public ProximaEntradaProductoController()
            : this(() => new ServicioProximaEntradaProducto(RepositorioProximaEntradaSql.DesdeConfiguracion(),
                new LectorCertezaFechaEntregaSql(),
                () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date))
        {
        }

        internal ProximaEntradaProductoController(Func<ServicioProximaEntradaProducto> crearServicio)
        {
            this.crearServicio = crearServicio ?? throw new ArgumentNullException(nameof(crearServicio));
        }

        // GET api/Productos/ProximaEntrada?producto=17404&almacen=ALG&empresa=1
        /// <summary>
        /// Pedido en el aire con su fecha; si no, la orientativa (#853); si no, Tipo «SinDatos» con el motivo. Siempre 200 salvo producto que
        /// no existe (404) o sin producto (400).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Productos/ProximaEntrada")]
        [ResponseType(typeof(ProximaEntradaProductoDTO))]
        public IHttpActionResult GetProximaEntrada(string producto, string almacen = null, string empresa = null)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return BadRequest(MENSAJE_SIN_PRODUCTO);
            }
            ProximaEntradaProductoDTO resultado = crearServicio().Calcular(empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO, producto, almacen);
            if (resultado == null)
            {
                return NotFound();
            }
            return Ok(SinProveedorSiNoPuedeVerlo(resultado, User));
        }

        /// <summary>
        /// Carlos 18/11/26: a quién se compra cada producto solo lo ven Compras, Dirección e Informática. A los demás el código y el
        /// nombre del proveedor (del pedido y el habitual) les llegan vacíos; las fechas y el texto, igual. El texto no lo nombra nunca.
        /// </summary>
        internal static ProximaEntradaProductoDTO SinProveedorSiNoPuedeVerlo(ProximaEntradaProductoDTO resultado, System.Security.Principal.IPrincipal usuario)
        {
            if (resultado != null && !ComprasPlazosProveedoresController.PuedeVer(usuario))
            {
                resultado.ProveedorPedido = null;
                resultado.NombreProveedorPedido = null;
                resultado.ProveedorHabitual = null;
                resultado.NombreProveedorHabitual = null;
            }
            return resultado;
        }
    }
}
