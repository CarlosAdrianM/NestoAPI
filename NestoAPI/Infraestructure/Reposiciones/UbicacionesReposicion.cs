using NestoAPI.Infraestructure.Exceptions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#553: lo que una reposición en preparación tiene que hacer con los huecos del almacén de ORIGEN. En un
    /// almacén sin control de ubicaciones (las tiendas) no hay nada que hacer. En Algete, Nesto viejo reserva al imprimir
    /// (filas de Ubicaciones en estado 4 con NºOrdenRepo), devuelve al hueco lo que se baja de la línea
    /// (prdCambiarCantidadReposicion) y al terminar pone esas reservas en -4 con el NºTraspasoRepo.
    ///
    /// <para>Ese resultado NO se copia de Nesto viejo: se conseguirá con la clase centralizada de Ubicaciones
    /// (NestoAPI#594, una sola puerta con registro de movimientos). Hasta entonces, un origen con control de ubicaciones
    /// se rechaza con un mensaje claro (ver <see cref="UbicacionesReposicion.Para"/>).</para>
    /// </summary>
    public interface IUbicacionesReposicion
    {
        /// <summary>Al dejar la reposición en preparación (en Nesto viejo, al imprimir el listado): reservar en los huecos.</summary>
        Task ReservarAlImprimir(string empresa, string diario, string destino);

        /// <summary>Al bajar la cantidad de una línea: devolver al hueco las unidades que sobran.</summary>
        Task DevolverSobrante(string empresa, int numeroOrden, int cantidadNueva);

        /// <summary>Al terminar: descontar de los huecos lo que sale, con el número de traspaso.</summary>
        Task DescontarAlTerminar(string empresa, string diario, string destino, int numeroTraspaso);
    }

    /// <summary>El origen no tiene control de ubicaciones (Almacenes.ControlUbicaciones = 0): nada que hacer.</summary>
    public sealed class SinControlUbicaciones : IUbicacionesReposicion
    {
        public Task ReservarAlImprimir(string empresa, string diario, string destino) => Task.CompletedTask;

        public Task DevolverSobrante(string empresa, int numeroOrden, int cantidadNueva) => Task.CompletedTask;

        public Task DescontarAlTerminar(string empresa, string diario, string destino, int numeroTraspaso) => Task.CompletedTask;
    }

    public static class UbicacionesReposicion
    {
        public const string MENSAJE_NO_DISPONIBLE =
            "Las reposiciones desde {0} todavía se hacen en Nesto viejo: ese almacén tiene control de ubicaciones y " +
            "la API aún no reserva ni descuenta los huecos (NestoAPI#594).";

        /// <summary>
        /// La implementación que corresponde al origen. TODO NestoAPI#594: cuando exista la puerta única de Ubicaciones,
        /// devolver aquí su implementación para los almacenes con control, en vez de rechazarlos.
        /// </summary>
        /// <exception cref="NestoBusinessException">400 si el origen tiene control de ubicaciones.</exception>
        public static IUbicacionesReposicion Para(string origen, bool controlUbicaciones)
        {
            if (!controlUbicaciones)
            {
                return new SinControlUbicaciones();
            }
            throw new NestoBusinessException(string.Format(MENSAJE_NO_DISPONIBLE, origen?.Trim()));
        }
    }
}
