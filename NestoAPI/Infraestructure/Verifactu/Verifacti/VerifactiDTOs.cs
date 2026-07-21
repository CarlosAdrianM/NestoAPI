using System.Collections.Generic;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Verifactu.Verifacti
{
    /// <summary>
    /// DTOs específicos para la API de Verifacti.
    /// Estos son internos al servicio y mapean exactamente al formato de la API.
    /// </summary>

    /// <summary>
    /// Request para crear factura en Verifacti
    /// </summary>
    internal class VerifactiCreateRequest
    {
        [JsonProperty("serie")]
        public string Serie { get; set; }

        [JsonProperty("numero")]
        public string Numero { get; set; }

        [JsonProperty("fecha_expedicion")]
        public string FechaExpedicion { get; set; } // Formato: DD-MM-YYYY

        [JsonProperty("tipo_factura")]
        public string TipoFactura { get; set; } // F1, R1, R3, R4

        [JsonProperty("descripcion")]
        public string Descripcion { get; set; }

        [JsonProperty("nif")]
        public string Nif { get; set; }

        [JsonProperty("nombre")]
        public string Nombre { get; set; }

        [JsonProperty("lineas")]
        public List<VerifactiLineaRequest> Lineas { get; set; } = new List<VerifactiLineaRequest>();

        [JsonProperty("importe_total")]
        public decimal ImporteTotal { get; set; }

        // Solo para rectificativas
        [JsonProperty("tipo_rectificativa", NullValueHandling = NullValueHandling.Ignore)]
        public string TipoRectificativa { get; set; } // S=sustitución, I=diferencia

        [JsonProperty("facturas_rectificadas", NullValueHandling = NullValueHandling.Ignore)]
        public List<VerifactiFacturaRectificadaRequest> FacturasRectificadas { get; set; }
    }

    /// <summary>
    /// Línea de desglose de IVA para Verifacti.
    /// Nomenclatura SII verificada contra el sandbox real el 17/07/26 (humo Fase A):
    /// la API rechaza los nombres cortos (base/tipo/cuota) con
    /// "El campo base_imponible es requerido para cada linea."
    /// </summary>
    internal class VerifactiLineaRequest
    {
        [JsonProperty("base_imponible")]
        public decimal Base { get; set; }

        [JsonProperty("tipo_impositivo")]
        public decimal Tipo { get; set; } // 21, 10, 4, 0

        [JsonProperty("cuota_repercutida")]
        public decimal Cuota { get; set; }

        // Recargo de equivalencia (opcional)
        [JsonProperty("tipo_recargo_equivalencia", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? TipoRe { get; set; }

        [JsonProperty("cuota_recargo_equivalencia", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? CuotaRe { get; set; }
    }

    /// <summary>
    /// Referencia a factura rectificada para Verifacti
    /// </summary>
    internal class VerifactiFacturaRectificadaRequest
    {
        [JsonProperty("serie")]
        public string Serie { get; set; }

        [JsonProperty("numero")]
        public string Numero { get; set; }

        [JsonProperty("fecha_expedicion")]
        public string FechaExpedicion { get; set; } // Formato: DD-MM-YYYY
    }

    /// <summary>
    /// Respuesta de la API de Verifacti
    /// </summary>
    internal class VerifactiApiResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("uuid")]
        public string Uuid { get; set; }

        [JsonProperty("estado")]
        public string Estado { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("qr")]
        public string Qr { get; set; } // Base64

        [JsonProperty("huella")]
        public string Huella { get; set; } // SHA-256

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("error_code")]
        public string ErrorCode { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        // NestoAPI#329: el endpoint de STATUS devuelve el veredicto de la AEAT con estos
        // nombres (distintos de error/error_code, que son de la propia API de Verifacti).
        [JsonProperty("codigo_error")]
        public string CodigoErrorAeat { get; set; }

        [JsonProperty("mensaje_error")]
        public string MensajeErrorAeat { get; set; }
    }
}
