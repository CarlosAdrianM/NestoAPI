using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models.Inventarios;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Sugerencia 1185 (Alfredo): dejar una referencia a cero en todas sus ubicaciones de una vez, por el inventario de siempre (filas
    /// pendientes de hoy); se contabiliza después con «Contabilizar inventario». Lógica en <see cref="ServicioInventarioACero"/>.
    /// </summary>
    public partial class InventariosController
    {
        private ServicioInventarioACero servicioACero;

        /// <summary>Para los tests; por defecto, el de verdad sobre esta misma BD.</summary>
        internal ServicioInventarioACero ServicioACero
        {
            get => servicioACero ?? (servicioACero = new ServicioInventarioACero(new RepositorioInventarioACeroSql(db)));
            set => servicioACero = value;
        }

        // POST: api/Inventarios/DejarACero { "Empresa": "1", "Almacen": "ALG", "Producto": "44901", "Simular": true }
        /// <summary>
        /// Sugerencia 1185: crea (o pone a 0) las filas pendientes de HOY del producto en el almacén, una por ubicación con stock
        /// (teórico = lo ubicado, contado 0); sin ubicaciones (tiendas, o producto que no se ubica), una sola con el stock del almacén.
        /// No contabiliza. Con Simular, dice lo que haría sin tocar nada (para la confirmación). Permisos: los de contar ese almacén.
        /// 409 {Message, Codigo, PuedeContarOtraVez, Lineas}: YaContabilizado (se repite con OtraVez), FilasDuplicadas y
        /// FilasSinUbicacion (filas del producto sin ubicación en un almacén con ubicaciones: se borran con QuitarFilasSinUbicacion).
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Inventarios/DejarACero")]
        [ResponseType(typeof(DejarACeroInventarioRespuestaDTO))]
        public async Task<IHttpActionResult> PostDejarACero(DejarACeroInventarioDTO peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return BadRequest("Falta el almacén.");
            }
            if (string.IsNullOrWhiteSpace(peticion.Producto))
            {
                return BadRequest("Falta el producto.");
            }
            string empresa = EmpresaONula(peticion.Empresa);
            string motivo = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(empresa), peticion.Almacen.Trim());
            if (motivo != null)
            {
                return Content(HttpStatusCode.Forbidden, new HttpError(motivo));
            }

            ResultadoProductoLeido leido = await lector.Leer(empresa, peticion.Producto).ConfigureAwait(false);
            if (leido.Producto == null)
            {
                return RespuestaSinProducto(leido);
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            ResultadoInventarioACero resultado = await ServicioACero
                .DejarACero(peticion, leido.Producto.Número?.Trim(), usuario, Hoy()).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoInventarioACero.Ok:
                    return Ok(resultado.Respuesta);
                case EstadoInventarioACero.YaContabilizado:
                    return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                    {
                        Message = resultado.Mensaje,
                        Codigo = CodigosConflictoInventario.YA_CONTABILIZADO,
                        PuedeContarOtraVez = true,
                        Lineas = resultado.Lineas
                    });
                case EstadoInventarioACero.FilasDuplicadas:
                    return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                    {
                        Message = resultado.Mensaje,
                        Codigo = CodigosConflictoInventario.FILAS_DUPLICADAS,
                        Lineas = resultado.Lineas
                    });
                case EstadoInventarioACero.FilasSinUbicacion:
                    return Content(HttpStatusCode.Conflict, new YaRellenadoInventarioDTO
                    {
                        Message = resultado.Mensaje,
                        Codigo = CodigosConflictoInventario.FILAS_SIN_UBICACION,
                        Lineas = resultado.Lineas
                    });
                case EstadoInventarioACero.NoExisteAlmacen:
                case EstadoInventarioACero.NoExisteProducto:
                    return Content(HttpStatusCode.NotFound, new HttpError(resultado.Mensaje));
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }
    }
}
