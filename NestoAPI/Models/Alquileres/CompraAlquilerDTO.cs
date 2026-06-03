using System;

namespace NestoAPI.Models.Alquileres
{
    /// <summary>
    /// Una línea de compra (pedido de compra) asociada a un producto/número de serie
    /// de alquiler, para la pestaña "Compra".
    /// Solo lectura: se exponen únicamente las columnas clave (Nesto#340, Fase 1C.2).
    /// </summary>
    public class CompraAlquilerDTO
    {
        public int NumeroOrden { get; set; }
        public int NumeroPedido { get; set; }
        public string Proveedor { get; set; }
        public DateTime? FechaRecepcion { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public short? Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Total { get; set; }
        public short Estado { get; set; }
        public string NumSerie { get; set; }
    }
}
