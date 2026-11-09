using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#790 (épica #784, inventario del 21/11/26): contabilizar el inventario de un almacén desde Nesto, con las comprobaciones
    /// de antes (las consultas que Carlos lanzaba en SSMS) y las diferencias valoradas a PVP (tiendas) o a coste (el resto).
    /// Permisos en <see cref="PermisosInventario"/>; la lógica en <see cref="ServicioContabilizarInventario"/>.
    /// </summary>
    public partial class InventariosController
    {
        private IServicioContabilizarInventario servicioContabilizar;
        private Func<string, string, string> almacenDelUsuario;

        /// <summary>Para los tests; por defecto, el de verdad sobre esta misma BD.</summary>
        internal IServicioContabilizarInventario ServicioContabilizar
        {
            get => servicioContabilizar ?? (servicioContabilizar = new ServicioContabilizarInventario(new RepositorioContabilizarInventarioSql(db)));
            set => servicioContabilizar = value;
        }

        /// <summary>(empresa, usuario sin dominio) → su tienda: AlmacenInventario y, si no lo tiene, AlmacénPedidoVta.</summary>
        internal Func<string, string, string> AlmacenDelUsuario
        {
            get => almacenDelUsuario ?? (almacenDelUsuario = LeerAlmacenDelUsuario);
            set => almacenDelUsuario = value;
        }

        // GET: api/Inventarios/Comprobaciones?empresa=1&almacen=REI&fecha=2026-11-21
        /// <summary>
        /// NestoAPI#790: lo que se comprueba antes de contabilizar las filas pendientes del almacén ese día: bloqueantes (no deja
        /// contabilizar), avisos y quién ha contado. Solo vienen las comprobaciones que tienen filas.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Inventarios/Comprobaciones")]
        [ResponseType(typeof(ComprobacionesInventarioDTO))]
        public async Task<IHttpActionResult> GetComprobaciones(string almacen, DateTime fecha, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            empresa = EmpresaONula(empresa);
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            almacen = almacen.Trim();
            string motivo = PermisosInventario.MotivoNoPuedeConsultar(User, AlmacenDeQuienPide(empresa), almacen);
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            ComprobacionesInventarioDTO comprobaciones = await ServicioContabilizar.Comprobar(empresa, almacen, fecha.Date).ConfigureAwait(false);
            if (comprobaciones == null)
            {
                return Content(HttpStatusCode.NotFound, new HttpError(AlmacenNoExiste(empresa, almacen)));
            }
            return Ok(comprobaciones);
        }

        // POST: api/Inventarios/Contabilizar { "Empresa": "1", "Almacen": "REI", "Fecha": "2026-11-21" }
        /// <summary>
        /// NestoAPI#790: comprueba y, si no hay bloqueantes, contabiliza (prdContabilizarInventario) las filas pendientes del almacén ese
        /// día, un traspaso por usuario. Con bloqueantes, 409 con las comprobaciones y no se toca nada. Si el procedimiento falla
        /// para un usuario, va en Errores y se sigue con los demás.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Inventarios/Contabilizar")]
        [ResponseType(typeof(ContabilizacionInventarioDTO))]
        public async Task<IHttpActionResult> PostContabilizar([FromBody] ContabilizarInventarioParametrosDTO parametros)
        {
            if (parametros == null || string.IsNullOrWhiteSpace(parametros.Almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            if (parametros.Fecha == default)
            {
                return BadRequest("Falta la fecha del inventario.");
            }
            string empresa = EmpresaONula(parametros.Empresa);
            string almacen = parametros.Almacen.Trim();
            DateTime fecha = parametros.Fecha.Date;
            string motivo = PermisosInventario.MotivoNoPuedeContabilizar(User, AlmacenDeQuienPide(empresa), almacen);
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            ResultadoContabilizarInventario resultado = await ServicioContabilizar.Contabilizar(empresa, almacen, fecha, parametros.ConfirmarContadoCero).ConfigureAwait(false);
            if (resultado == null)
            {
                return Content(HttpStatusCode.NotFound, new HttpError(AlmacenNoExiste(empresa, almacen)));
            }
            if (resultado.Bloqueado != null)
            {
                return Content(HttpStatusCode.Conflict, resultado.Bloqueado);
            }
            if (!resultado.Contabilizacion.Contabilizados.Any() && !resultado.Contabilizacion.Errores.Any())
            {
                return BadRequest($"No hay filas pendientes de contabilizar en {almacen} el {fecha:dd/MM/yyyy}.");
            }
            return Ok(resultado.Contabilizacion);
        }

        // GET: api/Inventarios/Diferencias?empresa=1&almacen=REI&fecha=2026-11-21&valoracion=pvp
        // GET: api/Inventarios/Diferencias?almacen=REI&traspaso=77263&traspaso=77264
        /// <summary>
        /// NestoAPI#790: las filas con StockReal ≠ StockCalculado, valoradas. Con <paramref name="traspaso"/> (uno o varios), las
        /// contabilizadas en esos traspasos; sin él, las pendientes de <paramref name="fecha"/>. <paramref name="valoracion"/> «pvp» o
        /// «coste»: sin ella, coste para Compras, Almacén, Administración, Dirección e Informática y PVP para las tiendas (que no pueden
        /// pedir coste: 403).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Inventarios/Diferencias")]
        [ResponseType(typeof(DiferenciasInventarioDTO))]
        public async Task<IHttpActionResult> GetDiferencias(string almacen, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
            DateTime? fecha = null, [FromUri] int[] traspaso = null, string valoracion = null)
        {
            empresa = EmpresaONula(empresa);
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            almacen = almacen.Trim();
            int[] traspasos = (traspaso ?? new int[0]).Where(t => t > 0).Distinct().ToArray();
            if (!fecha.HasValue && traspasos.Length == 0)
            {
                return BadRequest("Indique la fecha del inventario o el traspaso.");
            }
            string motivo = PermisosInventario.MotivoNoPuedeConsultar(User, AlmacenDeQuienPide(empresa), almacen);
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }
            string conQueValora = PermisosInventario.Valoracion(User, valoracion, out motivo, out bool peticionIncorrecta);
            if (conQueValora == null)
            {
                return peticionIncorrecta
                    ? BadRequest(motivo)
                    : (IHttpActionResult)Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            DiferenciasInventarioDTO diferencias = await ServicioContabilizar
                .Diferencias(empresa, almacen, fecha?.Date, traspasos.ToList(), conQueValora).ConfigureAwait(false);
            return Ok(diferencias);
        }

        private static string EmpresaONula(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private static string AlmacenNoExiste(string empresa, string almacen) => $"El almacén {almacen} no existe en la empresa {empresa}.";

        /// <summary>Solo hace falta (y solo se lee) para quien es de Tiendas.</summary>
        private string AlmacenDeQuienPide(string empresa)
        {
            if (!PermisosInventario.EsTienda(User))
            {
                return null;
            }
            string usuario = PermisosEventos.SinDominio(User?.Identity?.Name);
            return string.IsNullOrEmpty(usuario) ? null : AlmacenDelUsuario(empresa, usuario);
        }

        private static string LeerAlmacenDelUsuario(string empresa, string usuario)
        {
            string almacen = ParametrosUsuarioController.LeerParametroSinDefecto(empresa, usuario, PermisosInventario.CLAVE_ALMACEN_INVENTARIO);
            return string.IsNullOrWhiteSpace(almacen)
                ? ParametrosUsuarioController.LeerParametro(empresa, usuario, PermisosInventario.CLAVE_ALMACEN_PEDIDO_VTA)?.Trim()
                : almacen.Trim();
        }
    }
}
