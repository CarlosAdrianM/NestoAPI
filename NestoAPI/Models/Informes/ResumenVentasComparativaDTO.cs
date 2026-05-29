namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// Vista comparativa (año actual vs. año anterior) del informe Resumen de ventas, derivada de
    /// <see cref="ResumenVentasDTO"/>. No se toca el SP: solo se recolocan los datos que devuelve.
    /// - Año Actual = VtaNV + VtaVC + VtaUL (Visnú y Unión Láser hoy son 0; si no lo fueran se suman aquí).
    /// - Año Anterior = VtaCV.
    /// - Diferencia (€) = VtaTotal tal cual lo devuelve el SP (solo se renombra la etiqueta).
    /// - Diferencia (%) = AñoActual / AñoAnterior - 1 (ratio; ver regla cuando Año Anterior = 0).
    /// </summary>
    public class ResumenVentasComparativaDTO
    {
        public string Grupo { get; set; }
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        public decimal AnnoActual { get; set; }
        public decimal AnnoAnterior { get; set; }
        public decimal DiferenciaEuros { get; set; }

        /// <summary>Ratio de diferencia (0,125 = +12,5%). Ver regla de cálculo cuando Año Anterior = 0.</summary>
        public decimal DiferenciaPorcentaje { get; set; }
    }
}
