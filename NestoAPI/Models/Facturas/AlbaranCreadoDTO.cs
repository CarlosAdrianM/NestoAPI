namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Representa un albarán creado durante la facturación de rutas.
    /// Hereda de DocumentoImprimibleDTO las propiedades comunes (Empresa, NumeroPedido, Cliente, etc.) y DatosImpresion.
    /// </summary>
    public class AlbaranCreadoDTO : DocumentoImprimibleDTO
    {
        /// <summary>
        /// Número de albarán creado
        /// </summary>
        public int NumeroAlbaran { get; set; }
    }
}
