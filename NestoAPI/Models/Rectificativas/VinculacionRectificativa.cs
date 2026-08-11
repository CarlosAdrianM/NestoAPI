namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// Verifactu #37: vinculación de una línea rectificativa con la línea de la factura
    /// original de la que provienen las unidades. Se persiste en LinFacturaVtaRectificacion.
    /// </summary>
    public class VinculacionRectificativa
    {
        public string FacturaOriginalNumero { get; set; }
        public int FacturaOriginalLinea { get; set; }
        public decimal CantidadRectificada { get; set; }
    }
}
