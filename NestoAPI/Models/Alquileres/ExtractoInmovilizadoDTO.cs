using System;

namespace NestoAPI.Models.Alquileres
{
    /// <summary>
    /// Una línea del extracto de inmovilizado de un alquiler, para la pestaña "Inmovilizados".
    /// Solo lectura: se exponen únicamente las columnas clave (Nesto#340, Fase 1C.2).
    /// </summary>
    public class ExtractoInmovilizadoDTO
    {
        public int NumeroOrden { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public string NumeroDocumento { get; set; }
        public decimal Importe { get; set; }
        public decimal ImportePendiente { get; set; }
        public short Estado { get; set; }
    }
}
