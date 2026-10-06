using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#553: lo que una reposición tiene que hacer con los huecos del almacén de ORIGEN. En un almacén sin control
    /// de ubicaciones (las tiendas) no hay nada que hacer. En Algete, Nesto viejo reserva al imprimir (prdUbicarReposicion:
    /// filas de Ubicaciones en estado 4 con NºOrdenRepo = la línea de ENTRADA), devuelve al hueco lo que se baja de la
    /// línea (prdCambiarCantidadReposicion) y al terminar pone esas reservas en -4 con el NºTraspasoRepo. Aquí, el mismo
    /// resultado por la puerta única de Ubicaciones (NestoAPI#594).
    /// </summary>
    public interface IUbicacionesReposicion
    {
        /// <summary>Al dejar la reposición en preparación (en Nesto viejo, al imprimir el listado): reservar en los huecos.</summary>
        Task<ResumenUbicaciones> Reservar(string empresa, string origen, IReadOnlyList<LineaReservaReposicion> lineas, string usuario);

        /// <summary>Al bajar la cantidad de una línea: devolver al hueco las unidades que sobran.</summary>
        Task<ResumenUbicaciones> DevolverSobrante(string empresa, string origen, int numeroOrden, string producto, int cantidadNueva, string usuario);

        /// <summary>Al cerrar la preparación: descontar de los huecos lo que sale, con el número de traspaso.</summary>
        Task<ResumenUbicaciones> DescontarAlTerminar(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario);

        /// <summary>Al anular un traspaso aún sin recoger: lo que salió de los huecos vuelve a ellos.</summary>
        Task<ResumenUbicaciones> AnularSalida(string empresa, string origen, int numeroTraspaso, string usuario);
    }

    /// <summary>El origen no tiene control de ubicaciones (Almacenes.ControlUbicaciones = 0): nada que hacer.</summary>
    public sealed class SinControlUbicaciones : IUbicacionesReposicion
    {
        private static Task<ResumenUbicaciones> Nada() => Task.FromResult(new ResumenUbicaciones());

        public Task<ResumenUbicaciones> Reservar(string empresa, string origen, IReadOnlyList<LineaReservaReposicion> lineas, string usuario) => Nada();

        public Task<ResumenUbicaciones> DevolverSobrante(string empresa, string origen, int numeroOrden, string producto, int cantidadNueva, string usuario) => Nada();

        public Task<ResumenUbicaciones> DescontarAlTerminar(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario) => Nada();

        public Task<ResumenUbicaciones> AnularSalida(string empresa, string origen, int numeroTraspaso, string usuario) => Nada();
    }

    /// <summary>
    /// El origen tiene control de ubicaciones (Algete): todo por la puerta única. Las reservas se buscan en la empresa y en
    /// su espejo, como prdUbicarReposicion (@Espejo).
    ///
    /// <para>Bajar la cantidad de una línea = liberar su reserva entera y volver a reservar la cantidad nueva. Es más
    /// simple que portar el cursor de prdCambiarCantidadReposicion (que además usa system_user y desde la API correría como
    /// la cuenta de máquina) y el resultado en los huecos es el mismo salvo, quizá, de qué hueco sale (vuelve a aplicar
    /// FIFO/LIFO sobre lo libre en ese momento).</para>
    /// </summary>
    public sealed class ConControlUbicaciones : IUbicacionesReposicion
    {
        private readonly IPuertaUbicaciones puerta;
        private readonly string espejo;

        public ConControlUbicaciones(IPuertaUbicaciones puerta, string espejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
        {
            this.puerta = puerta ?? throw new ArgumentNullException(nameof(puerta));
            this.espejo = espejo;
        }

        public Task<ResumenUbicaciones> Reservar(string empresa, string origen, IReadOnlyList<LineaReservaReposicion> lineas, string usuario)
        {
            return puerta.ReservarParaReposicion(empresa, espejo, origen, lineas, usuario);
        }

        public async Task<ResumenUbicaciones> DevolverSobrante(string empresa, string origen, int numeroOrden, string producto, int cantidadNueva, string usuario)
        {
            ResumenUbicaciones liberado = await puerta.LiberarReservaReposicion(empresa, numeroOrden, usuario).ConfigureAwait(false);
            if (cantidadNueva <= 0)
            {
                return liberado;
            }
            ResumenUbicaciones reservado = await puerta.ReservarParaReposicion(empresa, espejo, origen,
                new[] { new LineaReservaReposicion { NumeroOrdenEntrada = numeroOrden, Producto = producto, Cantidad = cantidadNueva } }, usuario).ConfigureAwait(false);
            reservado.FilasTocadas += liberado.FilasTocadas;
            reservado.Movimientos.InsertRange(0, liberado.Movimientos);
            return reservado;
        }

        public Task<ResumenUbicaciones> DescontarAlTerminar(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario)
        {
            return puerta.SalidaDeReposicion(empresa, numerosOrdenEntrada, numeroTraspaso, usuario);
        }

        public Task<ResumenUbicaciones> AnularSalida(string empresa, string origen, int numeroTraspaso, string usuario)
        {
            return puerta.AnularSalidaDeReposicion(empresa, origen, numeroTraspaso, usuario);
        }
    }

    public static class UbicacionesReposicion
    {
        /// <summary>La implementación que corresponde al origen según Almacenes.ControlUbicaciones.</summary>
        public static IUbicacionesReposicion Para(string origen, bool controlUbicaciones, Func<IPuertaUbicaciones> puerta)
        {
            if (!controlUbicaciones)
            {
                return new SinControlUbicaciones();
            }
            return new ConControlUbicaciones((puerta ?? throw new ArgumentNullException(nameof(puerta)))());
        }

        /// <summary>«002/002/004 (3), sin hueco (2)»: dónde está lo reservado de una línea, para enseñarlo.</summary>
        internal static string Huecos(ReservaLineaReposicion reserva)
        {
            if (reserva == null || reserva.Piezas.Count == 0)
            {
                return null;
            }
            List<string> partes = reserva.Piezas
                .GroupBy(p => p.Hueco?.ToString())
                .Where(g => g.Key != null)
                .Select(g => reserva.Piezas.Count == 1 ? g.Key : $"{g.Key} ({g.Sum(p => p.Cantidad)})")
                .ToList();
            return partes.Count == 0 ? null : string.Join(", ", partes);
        }
    }
}
