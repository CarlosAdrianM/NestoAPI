namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Mensaje de sincronización específico para PrestashopProductos
    /// Contiene los campos personalizados de Prestashop (nombre, descripciones, PVP con IVA)
    /// que son independientes del mensaje de Productos (para Odoo)
    /// </summary>
    public class PrestashopProductoSyncMessage : SyncMessageBase
    {
        /// <summary>
        /// ID del producto (Número)
        /// </summary>
        public string Producto { get; set; }

        /// <summary>
        /// Nombre personalizado para Prestashop (puede diferir del nombre en Productos)
        /// </summary>
        public string NombrePersonalizado { get; set; }

        /// <summary>
        /// Descripción completa del producto para Prestashop
        /// </summary>
        public string Descripcion { get; set; }

        /// <summary>
        /// Descripción breve del producto para Prestashop
        /// </summary>
        public string DescripcionBreve { get; set; }

        /// <summary>
        /// Precio de venta al público con IVA incluido
        /// </summary>
        public decimal? PVP_IVA_Incluido { get; set; }
    }
}
