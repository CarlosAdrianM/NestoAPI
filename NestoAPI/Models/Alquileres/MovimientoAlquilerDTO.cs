using System;

namespace NestoAPI.Models.Alquileres
{
    /// <summary>
    /// Una línea de movimiento (pedido de venta) de un alquiler, para la pestaña "Movimientos".
    /// Solo lectura: se exponen únicamente las columnas clave (Nesto#340, Fase 1C.2).
    /// </summary>
    public class MovimientoAlquilerDTO
    {
        public int NumeroOrden { get; set; }
        public DateTime FechaEntrega { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public short? Cantidad { get; set; }
        public decimal? Precio { get; set; }
        public decimal Total { get; set; }
        public short Estado { get; set; }
    }
}
