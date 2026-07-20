namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// Issue #87: metadata de rectificación pendiente de vincular (la copia se hizo sin facturar
    /// automáticamente; al facturar el pedido a mano se convierte en LinFacturaVtaRectificacion).
    /// Los nombres coinciden con las columnas de la tabla RectificativaPendiente (SqlQuery mapea
    /// por nombre).
    /// </summary>
    public class RectificativaPendienteDTO
    {
        public int NumeroLinea { get; set; }
        public string FacturaOriginalNumero { get; set; }
        public int FacturaOriginalLinea { get; set; }
        public decimal CantidadRectificada { get; set; }
    }
}
