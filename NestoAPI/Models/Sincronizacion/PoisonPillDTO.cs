using System;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// DTO para visualizar poison pills (mensajes que fallaron repetidamente)
    /// </summary>
    public class PoisonPillDTO
    {
        /// <summary>
        /// ID del mensaje de Pub/Sub
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Tabla de la entidad (ej: "Clientes", "Productos")
        /// </summary>
        public string Tabla { get; set; }

        /// <summary>
        /// ID de la entidad afectada
        /// </summary>
        public string EntityId { get; set; }

        /// <summary>
        /// Sistema de origen (ej: "Odoo", "Prestashop")
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Número de intentos de procesamiento
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
        /// Estado actual: "PoisonPill", "Retrying", "Reprocess", "Resolved", "PermanentFailure"
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Datos del mensaje en JSON (para debugging)
        /// </summary>
        public string MessageData { get; set; }

        /// <summary>
        /// Tiempo transcurrido desde el primer intento
        /// </summary>
        public string TimeSinceFirstAttempt { get; set; }

        /// <summary>
        /// Tiempo transcurrido desde el último intento
        /// </summary>
        public string TimeSinceLastAttempt { get; set; }
    }
}
