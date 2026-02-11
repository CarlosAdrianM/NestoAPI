using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa un registro de impagado (TipoApunte=4) pendiente en ExtractoCliente.
    /// </summary>
    public class ImpagadoPendiente
    {
        public DateTime FechaVto { get; set; }
        public decimal ImportePendiente { get; set; }
        public bool EsGastos { get; set; }
    }
}
