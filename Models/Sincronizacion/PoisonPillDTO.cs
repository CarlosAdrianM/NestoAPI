using System;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// DTO para visualizar poison pills en el endpoint de gestión
    /// </summary>
    public class PoisonPillDTO
    {
        /// <summary>
        /// ID único del mensaje de Pub/Sub
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Tabla afectada (Clientes, Productos, etc.)
        /// </summary>
        public string Tabla { get; set; }

        /// <summary>
        /// ID de la entidad afectada
        /// </summary>
        public string EntityId { get; set; }

        /// <summary>
        /// Origen del mensaje (Odoo, Prestashop, etc.)
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Número de intentos realizados
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// Fecha del primer intento
        /// </summary>
        public DateTime FirstAttemptDate { get; set; }

        /// <summary>
        /// Fecha del último intento
        /// </summary>
        public DateTime LastAttemptDate { get; set; }

        /// <summary>
        /// Último error registrado
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Estado actual del mensaje
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Datos del mensaje original (JSON serializado)
        /// </summary>
        public string MessageData { get; set; }

        /// <summary>
        /// Tiempo transcurrido desde el primer intento (formato legible)
        /// </summary>
        public string TimeSinceFirstAttempt { get; set; }

        /// <summary>
        /// Tiempo transcurrido desde el último intento (formato legible)
        /// </summary>
        public string TimeSinceLastAttempt { get; set; }
    }
}
