namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Clase base abstracta para documentos que se pueden imprimir (facturas, albaranes).
    /// Extiende DocumentoCreadoDTO añadiendo datos de impresión opcionales.
    /// </summary>
    public abstract class DocumentoImprimibleDTO : DocumentoCreadoDTO
    {
        /// <summary>
        /// Datos de impresión opcionales (bytes del PDF, número de copias, bandeja).
        /// Si es null, el documento no debe imprimirse.
        /// Si tiene valor, el documento debe enviarse a impresora.
        /// </summary>
        public DocumentoParaImprimir DatosImpresion { get; set; }
    }
}
