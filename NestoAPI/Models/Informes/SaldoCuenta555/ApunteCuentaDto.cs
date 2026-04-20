using System;

namespace NestoAPI.Models.Informes.SaldoCuenta555
{
    public class ApunteCuentaDto
    {
        public long NumeroOrden { get; set; }
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        public decimal Debe { get; set; }
        public decimal Haber { get; set; }
        public string NumeroDocumento { get; set; }
        public string Diario { get; set; }
        public int TipoApunte { get; set; }

        public decimal ImporteNeto => Debe - Haber;
    }
}
