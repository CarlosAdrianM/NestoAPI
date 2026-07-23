using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// NestoAPI#353: datos del informe de remesa (relación de efectos remitidos al banco).
    /// Sustituye al informe antiguo de VB6 imprimiendo IBAN completo en vez de CCC y
    /// agrupando por fecha de cargo (remesas multi-vencimiento #345).
    /// </summary>
    public class RemesaInformeDTO
    {
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Empresa { get; set; }
        public string Banco { get; set; }
        public string IbanAbono { get; set; }
        public List<RemesaInformeEfectoDTO> Efectos { get; set; } = new List<RemesaInformeEfectoDTO>();
    }

    public class RemesaInformeEfectoDTO
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Iban { get; set; }
        public decimal Importe { get; set; }
        /// <summary>FechaVto del pago = fecha en la que el banco carga el efecto.</summary>
        public DateTime? FechaCargo { get; set; }
    }
}
