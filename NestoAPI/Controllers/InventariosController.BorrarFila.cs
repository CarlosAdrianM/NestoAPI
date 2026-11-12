using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
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

        private Func<string, string> parametroBorrarFilas;

        /// <summary>(usuario sin dominio) → su ParametrosUsuario «InventarioBorrarFilas» (solo su fila, sin crearla). Para los tests.</summary>
        internal Func<string, string> ParametroBorrarFilas
        {
            get => parametroBorrarFilas ?? (parametroBorrarFilas = usuario =>
                ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario, ReglaBorrarFilaInventario.PARAMETRO_USUARIO));
            set => parametroBorrarFilas = value;
        }

        /// <summary>Épica #784: Administración, Dirección e Informática, o el parámetro «InventarioBorrarFilas» = "1".</summary>
        private bool PuedeBorrarFilas()
        {
            if (PermisosInventario.EsGlobal(User))
            {
                return true;
            }
            string usuario = PermisosEventos.SinDominio(User?.Identity?.Name);
            if (string.IsNullOrEmpty(usuario))
            {
                return false;
            }
            try
            {
                return ReglaBorrarFilaInventario.PuedeBorrar(false, ParametroBorrarFilas(usuario));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Inventario #784] No se ha podido leer el permiso de borrar filas: {ex.Message}", ex), "Sistema (inventario)");
                return false;
            }
        }

        /// <summary>
        /// Épica #784: SePuedeBorrar en cada fila (si quien pregunta puede contar en el almacén y borrar) y el aviso «Filas borradas hoy
        /// del recuento».
        /// </summary>
        private async Task CompletarComprobaciones(ComprobacionesInventarioDTO comprobaciones, string empresa, string almacen)
        {
            if (comprobaciones == null)
            {
                return;
            }
            DateTime hoy = Hoy();
            bool puede = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(empresa), almacen) == null && PuedeBorrarFilas();
            ReglaBorrarFilaInventario.Marcar(comprobaciones, puede, hoy);
            try
            {
                ComprobacionInventarioDTO borradas = ReglaBorrarFilaInventario.AvisoBorradas(
                    await ServicioBorrarFila.Borradas(comprobaciones.Fecha).ConfigureAwait(false), empresa, almacen, comprobaciones.Fecha);
                if (borradas != null)
                {
                    comprobaciones.Avisos.Add(borradas);
                }
            }
            catch (Exception ex)
            {
                // El aviso de lo borrado nunca impide ver las comprobaciones.
                ElmahHelper.Log(new Exception($"[Inventario #784] No se han podido añadir las filas borradas: {ex.Message}", ex), "Sistema (inventario)");
            }
        }

        // DELETE: api/Inventarios/Filas/948787
        /// <summary>
        /// Épica #784: borra una fila pendiente de HOY que sobra (<see cref="ReglaBorrarFilaInventario"/>: está ahora en el bloqueante
        /// SinUbicacion, FilasDuplicadas o TeoricoConReservas) o (12/11/26) cualquier fila pendiente de un día ANTERIOR a hoy. 204 si la
        /// borra; 404 {Message} si no existe; 409 {Message} si está contabilizada, es de un día posterior a hoy o (de hoy) no es una
        /// fila que sobre; 403 {Message} sin permiso de contar en el almacén o sin permiso de borrar (Administración,
        /// Dirección, Informática o ParametrosUsuario «InventarioBorrarFilas» = "1"). Deja rastro en Modificaciones.
        /// </summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Inventarios/Filas/{numeroOrden:int}")]
        public async Task<IHttpActionResult> DeleteFila(int numeroOrden)
        {
            DateTime hoy = Hoy();
            ResultadoBorrarFilaInventario resultado = await ServicioBorrarFila.Borrar(
                numeroOrden,
                (empresa, almacen) => PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(EmpresaONula(empresa)), almacen),
                PuedeBorrarFilas(),
                async fila => ReglaBorrarFilaInventario.EsBorrable(
                    await ServicioContabilizar.Comprobar(EmpresaONula(fila.Empresa), fila.Almacen?.Trim(), hoy.Date).ConfigureAwait(false), fila.NumeroOrden),
                UsuarioAuditoriaHelper.Resolver(User, null),
                hoy).ConfigureAwait(false);
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
