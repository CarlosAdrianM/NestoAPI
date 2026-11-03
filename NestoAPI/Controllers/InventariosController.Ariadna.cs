using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#795 (épica #784, corte 5): lo que necesita la pantalla de inventario de Ariadna (Ariadna#79). Algete ubicación a ubicación
    /// (siguientes ubicaciones y rellenar una con su teórico), los evolutivos de las tiendas (rellenar una familia o una referencia) y lo
    /// contado. Contar con el GET/POST/PUT de siempre (#788). Permisos en <see cref="PermisosInventario"/>: cuentan Almacén, Tiendas (la
    /// suya), Administración, Dirección e Informática; lo contado lo consultan además Compras.
    /// </summary>
    public partial class InventariosController
    {
        private IServicioInventarioUbicaciones servicioUbicaciones;
        private Func<DateTime> hoy;

        /// <summary>Para los tests; por defecto, el de verdad sobre esta misma BD.</summary>
        internal IServicioInventarioUbicaciones ServicioUbicaciones
        {
            get => servicioUbicaciones ?? (servicioUbicaciones = new ServicioInventarioUbicaciones(new RepositorioInventarioUbicacionesSql(db)));
            set => servicioUbicaciones = value;
        }

        /// <summary>Para los tests; por defecto, el día de hoy del servidor.</summary>
        internal Func<DateTime> Hoy
        {
            get => hoy ?? (() => DateTime.Today);
            set => hoy = value;
        }

        // GET: api/Inventarios/Ubicaciones/Siguientes?empresa=1&almacen=ALG&numero=15
        /// <summary>
        /// NestoAPI#795: las ubicaciones que tocan contar, por prioridad (prdUbicacionesParaInventario), con cuántos productos tienen en
        /// Ubicaciones y si ya se han rellenado hoy. Solo almacenes con control de ubicaciones (400 si no).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Inventarios/Ubicaciones/Siguientes")]
        [ResponseType(typeof(List<UbicacionParaInventarioDTO>))]
        public async Task<IHttpActionResult> GetUbicacionesSiguientes(string almacen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            int numero = ServicioInventarioUbicaciones.NUMERO_POR_DEFECTO)
        {
            empresa = EmpresaONula(empresa);
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            string motivo = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(empresa), almacen.Trim());
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>> resultado =
                await ServicioUbicaciones.Siguientes(empresa, almacen.Trim(), numero, Hoy()).ConfigureAwait(false);
            return Respuesta(resultado);
        }

        // POST: api/Inventarios/RellenarUbicacion { "Empresa": "1", "Almacen": "ALG", "Fecha": "2026-11-21", "Pasillo": "003", "Fila": "001", "Columna": "002" }
        /// <summary>
        /// NestoAPI#795: rellena una ubicación con su teórico, como la Ariadna vieja: una fila por producto de Ubicaciones (estado ≥ 0) con
        /// StockCalculado su cantidad, StockReal 0, Estado 1, la ubicación, Aplicacion «NestoAPI» y el usuario del token. Una ubicación vacía
        /// se rellena con 0 filas (200) para añadir lo que se encuentre. 409 con Message y Lineas si ya tiene filas pendientes ese día.
        /// Solo almacenes con control de ubicaciones.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Inventarios/RellenarUbicacion")]
        [ResponseType(typeof(RellenarInventarioFilasRespuestaDTO))]
        public async Task<IHttpActionResult> PostRellenarUbicacion(RellenarUbicacionInventarioDTO peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            string motivo = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(EmpresaONula(peticion.Empresa)), peticion.Almacen.Trim());
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return Respuesta(await ServicioUbicaciones.RellenarUbicacion(peticion, usuario).ConfigureAwait(false));
        }

        // POST: api/Inventarios/RellenarParcial { "Empresa": "1", "Almacen": "REI", "Fecha": "2026-11-21", "Familia": "Lisap" }
        // POST: api/Inventarios/RellenarParcial { "Empresa": "1", "Almacen": "REI", "Producto": "8435454004144" }
        /// <summary>
        /// NestoAPI#795: el evolutivo de una tienda: como Rellenar (#789) pero solo la familia (no ficticios con stock distinto de cero) o
        /// solo la referencia (su fila aunque el stock sea 0; Producto puede ser un código de barras, se lee como en la recepción: 404/409
        /// como el GET). 409 con Message y Lineas si ya hay filas pendientes de esa familia o referencia ese día. Solo almacenes sin
        /// control de ubicaciones.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Inventarios/RellenarParcial")]
        [ResponseType(typeof(RellenarInventarioFilasRespuestaDTO))]
        public async Task<IHttpActionResult> PostRellenarParcial(RellenarParcialInventarioDTO peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            string empresa = EmpresaONula(peticion.Empresa);
            string motivo = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(empresa), peticion.Almacen.Trim());
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            string referencia = null;
            if (!string.IsNullOrWhiteSpace(peticion.Producto))
            {
                if (!string.IsNullOrWhiteSpace(peticion.Familia))
                {
                    return BadRequest("Indique la familia o la referencia del evolutivo (una de las dos, no ambas).");
                }
                ResultadoProductoLeido leido = await lector.Leer(empresa, peticion.Producto).ConfigureAwait(false);
                if (leido.Producto == null)
                {
                    return RespuestaSinProducto(leido);
                }
                referencia = leido.Producto.Número?.Trim();
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return Respuesta(await ServicioUbicaciones.RellenarParcial(peticion, referencia, usuario).ConfigureAwait(false));
        }

        // GET: api/Inventarios/Contado?empresa=1&almacen=ALG&fecha=2026-11-21                       → resumen por ubicación
        // GET: api/Inventarios/Contado?empresa=1&almacen=ALG&fecha=2026-11-21&pasillo=003&fila=001&columna=002 → las filas de la ubicación
        // GET: api/Inventarios/Contado?empresa=1&almacen=REI&fecha=2026-11-21&producto=8435454004144  → las filas del producto
        /// <summary>
        /// NestoAPI#795: lo ya metido ese día (Estado 1, sin traspaso). Con ubicación (cualquiera de pasillo, fila y columna filtra) o
        /// producto (referencia o código: 404/409 como el GET), las filas en Lineas; sin filtro, el resumen por ubicación en Ubicaciones
        /// (en las tiendas, una sola sin pasillo). Sin fecha, hoy.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Inventarios/Contado")]
        [ResponseType(typeof(InventarioContadoDTO))]
        public async Task<IHttpActionResult> GetContado(string almacen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, DateTime? fecha = null,
            string pasillo = null, string fila = null, string columna = null, string producto = null)
        {
            empresa = EmpresaONula(empresa);
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            string motivo = PermisosInventario.MotivoNoPuedeConsultar(User, AlmacenDeQuienPide(empresa), almacen.Trim());
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            string referencia = null;
            if (!string.IsNullOrWhiteSpace(producto))
            {
                ResultadoProductoLeido leido = await lector.Leer(empresa, producto).ConfigureAwait(false);
                if (leido.Producto == null)
                {
                    return RespuestaSinProducto(leido);
                }
                referencia = leido.Producto.Número?.Trim();
            }

            var filtro = new FiltroInventarioContado { Pasillo = pasillo, Fila = fila, Columna = columna, Producto = referencia };
            return Respuesta(await ServicioUbicaciones.Contado(empresa, almacen.Trim(), (fecha ?? Hoy()).Date, filtro).ConfigureAwait(false));
        }

        private IHttpActionResult Respuesta<T>(ResultadoInventarioUbicaciones<T> resultado)
        {
            switch (resultado.Estado)
            {
                case EstadoInventarioUbicaciones.Ok:
                    return Ok(resultado.Respuesta);
                case EstadoInventarioUbicaciones.YaRellenado:
                    return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO { Message = resultado.Mensaje, Lineas = resultado.Lineas });
                case EstadoInventarioUbicaciones.NoExisteAlmacen:
                    return Content(HttpStatusCode.NotFound, new HttpError(resultado.Mensaje));
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }
    }
}
