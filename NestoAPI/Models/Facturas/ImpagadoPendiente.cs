using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa un registro de impagado (TipoApunte=4) pendiente en ExtractoCliente.
    /// </summary>
    public class ImpagadoPendiente
    {
        public DateTime FechaVto { get; set; }
        /// <summary>
        /// El número de efecto del extracto. Es lo que casa el impagado con su vencimiento: la
        /// fecha no vale, porque el impagado se apunta con la del cargo de la remesa (NestoAPI#572).
        /// </summary>
        public string Efecto { get; set; }
        public decimal ImportePendiente { get; set; }
        public bool EsGastos { get; set; }
    }
}
