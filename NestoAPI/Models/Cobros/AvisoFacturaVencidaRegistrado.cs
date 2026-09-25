using System;

namespace NestoAPI.Models.Cobros
{
    /// <summary>
    /// NestoAPI#544: una fila de la tabla AvisosFacturasVencidas (la memoria del aviso). Una por
    /// efecto y aviso mandado de verdad; la sombra la lee pero no la escribe.
    /// </summary>
    public class AvisoFacturaVencidaRegistrado
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>ExtractoCliente.Nº_Orden del efecto avisado.</summary>
        public int NumOrden { get; set; }
        public string Factura { get; set; }
        public int NumeroAviso { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Lo que se reclamaba ese día: si hoy el pendiente es menor, el cliente ha pagado parte y el reloj se reinicia.</summary>
        public decimal ImportePendiente { get; set; }
        public string Destinatarios { get; set; }
    }

    /// <summary>
    /// NestoAPI#544 (b): un apunte con importe pendiente negativo (cobro a cuenta sin liquidar,
    /// abono...) de un cliente al que por eso no se avisa. Administración lo tiene que liquidar.
    /// </summary>
    public class ApunteNegativoClienteDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public int NOrden { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string TipoApunte { get; set; }
        public string Concepto { get; set; }
        public string Documento { get; set; }
    }
}
