using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;
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
            bool puedeContar = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(empresa), almacen) == null;
            // Novedad 1364: sin el permiso de borrar, sus propias filas de días anteriores sí (ReglaBorrarFilaInventario.PuedeBorrarEsta).
            ReglaBorrarFilaInventario.Marcar(comprobaciones, puedeContar && PuedeBorrarFilas(), hoy, puedeContar, User?.Identity?.Name);
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

        /// <summary>
        /// 25/11/26: <see cref="Inventario.SePuedeBorrar"/> en las filas del GET, con la regla del DELETE. Los permisos se leen una vez
        /// y las comprobaciones de hoy (para «sobra») solo si hay alguna fila de hoy que lo necesite, una vez por tanda. Nunca rompe el GET.
        /// </summary>
        private async Task MarcarSePuedeBorrar(List<Inventario> filas, string empresa, string almacen)
        {
            if (filas == null || filas.Count == 0)
            {
                return;
            }
            try
            {
                DateTime hoy = Hoy();
                bool puedeContar = PermisosInventario.MotivoNoPuedeContar(User, AlmacenDeQuienPide(EmpresaONula(empresa)), almacen) == null;
                bool puedeBorrar = puedeContar && PuedeBorrarFilas();
                await MarcarSePuedeBorrar(filas, puedeContar, puedeBorrar, UsuarioAuditoriaHelper.Resolver(User, null), hoy,
                    () => ServicioContabilizar.Comprobar(EmpresaONula(empresa), almacen?.Trim(), hoy.Date)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Inventario] No se ha podido calcular SePuedeBorrar: {ex.Message}", ex), "Sistema (inventario)");
            }
        }

        /// <summary>El cálculo de <see cref="MarcarSePuedeBorrar(List{Inventario}, string, string)"/>, sin el controlador (para los tests).</summary>
        internal static async Task MarcarSePuedeBorrar(IEnumerable<Inventario> filas, bool puedeContar, bool puedeBorrar, string quienPregunta,
            DateTime hoy, Func<Task<ComprobacionesInventarioDTO>> comprobacionesDeHoy)
        {
            ComprobacionesInventarioDTO comprobaciones = null;
            bool leidas = false;
            foreach (Inventario fila in filas ?? Enumerable.Empty<Inventario>())
            {
                bool pendiente = fila.Estado == ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR && !fila.NºTraspaso.HasValue;
                // «Sobra» solo hace falta para las de hoy que pasan lo demás: las comprobaciones se piden la primera vez que hace falta
                bool necesitaSobra = puedeContar && pendiente && fila.Fecha.Date == hoy.Date
                    && ReglaBorrarFilaInventario.PuedeBorrarEsta(puedeBorrar, fila.Fecha, fila.Usuario, quienPregunta, hoy);
                if (necesitaSobra && !leidas)
                {
                    comprobaciones = comprobacionesDeHoy == null ? null : await comprobacionesDeHoy().ConfigureAwait(false);
                    leidas = true;
                }
                fila.SePuedeBorrar = ReglaBorrarFilaInventario.SePuedeBorrarFila(puedeContar, puedeBorrar, pendiente, fila.Fecha, fila.Usuario,
                    quienPregunta, hoy, () => ReglaBorrarFilaInventario.EsBorrable(comprobaciones, fila.NºOrden));
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
