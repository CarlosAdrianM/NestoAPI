namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Clase base para todos los mensajes de sincronización
    /// Contiene solo los campos comunes a todas las entidades sincronizadas
    /// </summary>
    public abstract class SyncMessageBase
    {
        /// <summary>
        /// Tabla afectada: "Clientes", "Productos", etc.
        /// </summary>
        public string Tabla { get; set; }

        /// <summary>
        /// Sistema origen: "Nesto", "Odoo", "Prestashop", etc.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Usuario que realizó el cambio
        /// </summary>
        public string Usuario { get; set; }
    }
}
