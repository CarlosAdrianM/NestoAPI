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

    /// <summary>
    /// Movimiento (efecto) incluido en una remesa de cobro (Nesto#340, Fase 1C.14 slice 3).
    /// DTO ligero con las 7 columnas que pinta el grid, en vez de la entidad ExtractoCliente
    /// entera (mismo criterio que en Alquileres).
    /// </summary>
    public class MovimientoRemesaDTO
    {
        public int Id { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Concepto { get; set; }
        public decimal Importe { get; set; }
        public string Ccc { get; set; }
    }
}
