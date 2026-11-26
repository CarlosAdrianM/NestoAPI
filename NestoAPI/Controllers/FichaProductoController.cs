using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Sugerencia 1482 (Manuel, Compras; OK de Carlos 26/11/26): la referencia que el proveedor da al producto (línea de la ficha) y el
    /// extracto del producto (ventana aparte, como el extracto de cliente; hoy solo en Nesto viejo). Solo lectura; aparte de GET api/Productos,
    /// como la próxima entrada (#869) y los vecinos.
    /// </summary>
    public class FichaProductoController : ApiController
    {
        public const string MENSAJE_SIN_PRODUCTO = "Falta el producto.";
        public const string MENSAJE_FECHAS = "La fecha «desde» no puede ser posterior a «hasta».";

        private readonly Func<ILectorFichaProducto> crearLector;
        private readonly Func<DateTime> hoy;

        public FichaProductoController()
            : this(() => LectorFichaProductoSql.DesdeConfiguracion(),
                () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date)
        {
        }

        internal FichaProductoController(Func<ILectorFichaProducto> crearLector, Func<DateTime> hoy)
        {
            this.crearLector = crearLector ?? throw new ArgumentNullException(nameof(crearLector));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        // GET api/Productos/ReferenciasProveedor?producto=17404&empresa=1
        /// <summary>
        /// [{Proveedor, NombreProveedor, ReferenciaProveedor, Orden, EsHabitual}] (el habitual, el de menor Orden; lista vacía si no tiene).
        /// Solo Compras, Dirección e Informática (403 a los demás); 400 sin producto; 404 si no existe.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Productos/ReferenciasProveedor")]
        [ResponseType(typeof(List<ReferenciaProveedorProductoDTO>))]
        public IHttpActionResult GetReferenciasProveedor(string producto, string empresa = null)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO);
            }
            if (!PermisosFichaProducto.PuedeVerProveedoresYCostes(User))
            {
                return Content(HttpStatusCode.Forbidden, PermisosFichaProducto.MENSAJE_SIN_PERMISO_PROVEEDORES);
            }
            if (string.IsNullOrWhiteSpace(producto))
            {
                return BadRequest(MENSAJE_SIN_PRODUCTO);
            }
            List<ReferenciaProveedorProductoDTO> filas = crearLector().LeerReferenciasProveedor(Empresa(empresa), producto.Trim());
            if (filas == null)
            {
                return NotFound();
            }
            return Ok(ReglaExtractoProducto.MarcarHabitual(filas));
        }

        // GET api/Productos/Extracto?producto=17404&empresa=1&almacen=ALG&desde=2025-11-26&hasta=2026-11-26&pagina=1&tamano=200
        /// <summary>
        /// Los movimientos de ExtractoProducto de la empresa y su espejo (1 y 3), del más nuevo al más antiguo, paginados (≤ 500), con su saldo
        /// acumulado. Sin fechas, los últimos 12 meses. Importes y proveedor solo para Compras, Dirección e Informática (a los demás, null).
        /// 403 a quien no es de Compras, Almacén, Administración, Dirección, Informática ni Tiendas; 400 sin producto o fechas al revés; 404.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Productos/Extracto")]
        [ResponseType(typeof(ExtractoProductoFichaDTO))]
        public IHttpActionResult GetExtracto(string producto, string empresa = null, string almacen = null, DateTime? desde = null, DateTime? hasta = null,
            int? pagina = null, int? tamano = null)
        {
            if (ValidadorAccesoCliente.EsClienteTienda(User))
            {
                return Content(HttpStatusCode.Forbidden, ValidadorAccesoCliente.MENSAJE_SOLO_INTERNO);
            }
            if (!PermisosFichaProducto.PuedeVerExtracto(User))
            {
                return Content(HttpStatusCode.Forbidden, PermisosFichaProducto.MENSAJE_SIN_PERMISO_EXTRACTO);
            }
            if (string.IsNullOrWhiteSpace(producto))
            {
                return BadRequest(MENSAJE_SIN_PRODUCTO);
            }
            (DateTime inicio, DateTime fin) = ReglaExtractoProducto.Periodo(desde, hasta, hoy());
            if (inicio > fin)
            {
                return BadRequest(MENSAJE_FECHAS);
            }
            int paginaPedida = ReglaExtractoProducto.Pagina(pagina);
            int tamanoPedido = ReglaExtractoProducto.Tamano(tamano);
            string almacenFiltro = string.IsNullOrWhiteSpace(almacen) ? null : almacen.Trim().ToUpperInvariant();

            PaginaExtractoProducto leido = crearLector().LeerExtracto(Empresa(empresa), producto.Trim(), almacenFiltro, inicio, fin, paginaPedida, tamanoPedido);
            if (leido == null)
            {
                return NotFound();
            }
            return Ok(Construir(leido, almacenFiltro, inicio, fin, paginaPedida, tamanoPedido, PermisosFichaProducto.PuedeVerProveedoresYCostes(User)));
        }

        internal static ExtractoProductoFichaDTO Construir(PaginaExtractoProducto leido, string almacen, DateTime desde, DateTime hasta,
            int pagina, int tamano, bool veProveedoresYCostes)
        {
            ReglaExtractoProducto.CalcularSaldos(leido.SaldoInicial, leido.SumaPeriodo, leido.SumaMasNuevos, leido.Movimientos);
            if (!veProveedoresYCostes)
            {
                foreach (MovimientoExtractoProductoDTO movimiento in leido.Movimientos)
                {
                    movimiento.Importe = null;
                    movimiento.Proveedor = null;
                    movimiento.NombreProveedor = null;
                }
            }
            return new ExtractoProductoFichaDTO
            {
                Producto = leido.Producto,
                Nombre = leido.Nombre,
                Almacen = almacen,
                Desde = desde,
                Hasta = hasta,
                SaldoInicial = leido.SaldoInicial,
                SaldoFinal = leido.SaldoInicial + leido.SumaPeriodo,
                Total = leido.Total,
                Pagina = pagina,
                Tamano = tamano,
                Movimientos = leido.Movimientos
            };
        }

        private static string Empresa(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
