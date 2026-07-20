using System;

namespace NestoAPI.Models.Remesas
{
    /// <summary>
    /// Remesa de cobros ligera para el grid de RemesasViewModel (Nesto#340, Fase 1C.14 slice 2).
    /// Nombres ASCII: el cliente bindea Numero directamente.
    /// </summary>
    public class RemesaDTO
    {
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string Banco { get; set; }
    }
}
