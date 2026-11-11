using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inventarios;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Épica #784 (incidencia 1186, 11/11/26): borrar una fila pendiente del inventario desde Nesto y Ariadna (antes, solo por SQL).
    /// Lógica en <see cref="ServicioBorrarFilaInventario"/>.
    /// </summary>
    public partial class InventariosController
    {
        private ServicioBorrarFilaInventario servicioBorrarFila;

        /// <summary>Para los tests; por defecto, el de verdad sobre esta misma BD.</summary>
        internal ServicioBorrarFilaInventario ServicioBorrarFila
        {
            get => servicioBorrarFila ?? (servicioBorrarFila = new ServicioBorrarFilaInventario(new RepositorioBorrarFilaInventarioSql(db)));
            set => servicioBorrarFila = value;
        }

        // DELETE: api/Inventarios/Filas/948787
        /// <summary>
        /// Épica #784: borra una fila pendiente (Estado 1) del inventario. 204 si la borra; 404 si no existe; 409 {Message} si ya está
        /// contabilizada (nunca se borran); 403 {Message} sin permiso de contar en el almacén de la fila, o si es de otro día y quien
        /// pide no es de Administración, Dirección o Informática. Deja rastro en Modificaciones.
        /// </summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Inventarios/Filas/{numeroOrden:int}")]
        public async Task<IHttpActionResult> DeleteFila(int numeroOrden)
        {
            ResultadoBorrarFilaInventario resultado = await ServicioBorrarFila.Borrar(
                numeroOrden,
                (empresa, almacen) => PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(EmpresaONula(empresa)), almacen),
                PermisosInventario.EsGlobal(User),
                UsuarioAuditoriaHelper.Resolver(User, null),
                Hoy()).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoBorrarFilaInventario.Borrada:
                    return StatusCode(HttpStatusCode.NoContent);
                case EstadoBorrarFilaInventario.NoExiste:
                    return Content(HttpStatusCode.NotFound, new HttpError(resultado.Mensaje));
                case EstadoBorrarFilaInventario.SinPermiso:
                    return Content(HttpStatusCode.Forbidden, new HttpError(resultado.Mensaje));
                default:
                    return Content(HttpStatusCode.Conflict, new HttpError(resultado.Mensaje));
            }
        }
    }
}
