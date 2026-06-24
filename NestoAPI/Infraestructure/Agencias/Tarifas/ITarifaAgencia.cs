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
    /// Tarifa de un servicio de una agencia. El contrato común es AGNÓSTICO de agencia: recibe el
    /// destino CANÓNICO (código postal + país en ISO 3166-1 alpha-2) y devuelve el coste total (porte
    /// con fuel/recargos + reembolso) o <see cref="decimal.MaxValue"/> si la tarifa NO cubre ese destino.
    /// CADA agencia decide su propia zonificación PUERTAS ADENTRO (las nacionales reúsan el helper
    /// común de zonas por CP; GLS internacional usa sus zonas A–E; otra agencia, las suyas), sin
    /// imponer ningún sistema de zonas en lo compartido.
    /// </summary>
    public interface ITarifaAgencia
    {
        int AgenciaId { get; }
        byte ServicioId { get; }
        string NombreServicio { get; }
        byte HorarioDefectoId { get; }

        /// <summary>
        /// Coste total para el destino (CP + país ISO2), con fuel y recargos ya aplicados, o
        /// <see cref="decimal.MaxValue"/> si esta tarifa no cubre el destino.
        /// </summary>
        decimal CalcularCoste(string codigoPostal, string paisIso, decimal peso, decimal reembolso, decimal recargoCombustible);
    }
}
