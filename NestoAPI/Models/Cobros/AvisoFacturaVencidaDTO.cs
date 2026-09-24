using System;

namespace NestoAPI.Models.Cobros
{
    /// <summary>
    /// NestoAPI#534: un efecto de cartera vencido por transferencia candidato al aviso al cliente.
    /// Con <see cref="Motivo"/> a null se le avisaría; con motivo, se queda fuera y el motivo dice
    /// por qué (en el modo sombra se enseñan los dos grupos para que administración valide el criterio).
    /// </summary>
    public class AvisoFacturaVencidaDTO
    {
        /// <summary>Nº de orden del efecto en ExtractoCliente (la clave del efecto).</summary>
        public int NOrden { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Factura { get; set; }
        public string Efecto { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime Vencimiento { get; set; }
        /// <summary>Importe PENDIENTE del efecto (lo que se reclama), no el total de la factura.</summary>
        public decimal Importe { get; set; }
        public int DiasVencida { get; set; }
        /// <summary>Correos a los que iría el aviso, separados por coma. Vacío si la ficha no tiene.</summary>
        public string Destinatarios { get; set; }
        public string Motivo { get; set; }
        public bool SeAvisaria => Motivo == null;
    }
}
