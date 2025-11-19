using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Contexto adicional para excepciones de negocio.
    /// Almacena información útil para debugging y logging.
    /// </summary>
    public class ErrorContext
    {
        /// <summary>
        /// Código de error único que identifica el tipo de problema.
        /// Ejemplo: "FACTURACION_IVA_FALTANTE", "PEDIDO_SIN_LINEAS"
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Empresa en la que ocurrió el error
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Número de pedido relacionado con el error
        /// </summary>
        public int? Pedido { get; set; }

        /// <summary>
        /// Cliente relacionado con el error
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Usuario que ejecutó la operación
        /// </summary>
        public string Usuario { get; set; }

        /// <summary>
        /// Número de factura relacionado con el error
        /// </summary>
        public string Factura { get; set; }

        /// <summary>
        /// Datos adicionales específicos del error
        /// </summary>
        public Dictionary<string, object> AdditionalData { get; set; }

        /// <summary>
        /// Timestamp del error
        /// </summary>
        public DateTime Timestamp { get; set; }

        public ErrorContext()
        {
            AdditionalData = new Dictionary<string, object>();
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Agrega un dato adicional al contexto
        /// </summary>
        public ErrorContext WithData(string key, object value)
        {
            AdditionalData[key] = value;
            return this;
        }

        /// <summary>
        /// Obtiene una representación de texto del contexto para logging
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(ErrorCode))
                parts.Add($"ErrorCode={ErrorCode}");

            if (!string.IsNullOrEmpty(Empresa))
                parts.Add($"Empresa={Empresa}");

            if (Pedido.HasValue)
                parts.Add($"Pedido={Pedido}");

            if (!string.IsNullOrEmpty(Cliente))
                parts.Add($"Cliente={Cliente}");

            if (!string.IsNullOrEmpty(Usuario))
                parts.Add($"Usuario={Usuario}");

            if (!string.IsNullOrEmpty(Factura))
                parts.Add($"Factura={Factura}");

            if (AdditionalData != null && AdditionalData.Count > 0)
            {
                foreach (var kvp in AdditionalData)
                {
                    parts.Add($"{kvp.Key}={kvp.Value}");
                }
            }

            return string.Join(", ", parts);
        }
    }
}
