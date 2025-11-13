namespace NestoAPI.Models
{
    /// <summary>
    /// Representa un registro de la tabla Nesto_sync
    /// </summary>
    public class NestoSyncRecord
    {
        /// <summary>
        /// ID autoincremental del registro
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Nombre de la tabla sincronizada (ej: "Clientes", "Productos")
        /// </summary>
        public string Tabla { get; set; }

        /// <summary>
        /// ID de la entidad modificada (ej: número de cliente, número de producto)
        /// </summary>
        public string ModificadoId { get; set; }

        /// <summary>
        /// Usuario que realizó la modificación
        /// </summary>
        public string Usuario { get; set; }

        /// <summary>
        /// Fecha/hora de sincronización (NULL si pendiente)
        /// </summary>
        public System.DateTime? Sincronizado { get; set; }
    }
}
