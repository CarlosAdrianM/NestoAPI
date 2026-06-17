using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Un tramo de la tabla de precios de un servicio: hasta <see cref="PesoMaximo"/> kilos,
    /// en la zona <see cref="Zona"/>, cuesta <see cref="Precio"/> (antes de fuel).
    /// </summary>
    public class TramoCosteEnvio
    {
        public TramoCosteEnvio(decimal pesoMaximo, ZonasEnvioAgencia zona, decimal precio)
        {
            PesoMaximo = pesoMaximo;
            Zona = zona;
            Precio = precio;
        }

        public decimal PesoMaximo { get; }
        public ZonasEnvioAgencia Zona { get; }
        public decimal Precio { get; }
    }

    /// <summary>
    /// Tarifa de un servicio de una agencia. Portado de Nesto (ITarifaAgencia) al mover el
    /// comparador a NestoAPI. Los precios de <see cref="CosteEnvio"/> y <see cref="CosteKiloAdicional"/>
    /// son ANTES de fuel; el recargo de combustible se aplica en el comparador, por agencia.
    /// </summary>
    public interface ITarifaAgencia
    {
        int AgenciaId { get; }
        byte ServicioId { get; }
        string NombreServicio { get; }
        byte HorarioDefectoId { get; }
        IReadOnlyList<TramoCosteEnvio> CosteEnvio { get; }
        decimal CosteKiloAdicional(ZonasEnvioAgencia zona);
        decimal CosteReembolso(decimal reembolso);
    }
}
