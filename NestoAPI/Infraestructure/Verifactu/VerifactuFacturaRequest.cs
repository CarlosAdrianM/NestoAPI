using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// DTO genérico para enviar una factura a cualquier proveedor de Verifactu.
    /// Contiene los datos necesarios según la normativa, independiente del proveedor.
    /// </summary>
    public class VerifactuFacturaRequest
    {
        /// <summary>
        /// Serie de la factura (ej: "NV", "CV", "RV", "RC")
        /// </summary>
        public string Serie { get; set; }

        /// <summary>
        /// Número de factura
        /// </summary>
        public string Numero { get; set; }

        /// <summary>
        /// Fecha de expedición de la factura
        /// </summary>
        public DateTime FechaExpedicion { get; set; }

        /// <summary>
        /// Tipo de factura: F1 (normal), R1, R3, R4 (rectificativas)
        /// </summary>
        public string TipoFactura { get; set; }

        /// <summary>
        /// Descripción de la operación (máx 500 caracteres)
        /// </summary>
        public string Descripcion { get; set; }

        /// <summary>
        /// NIF/CIF del destinatario
        /// </summary>
        public string NifDestinatario { get; set; }

        /// <summary>
        /// Nombre fiscal del destinatario
        /// </summary>
        public string NombreDestinatario { get; set; }

        /// <summary>
        /// NestoAPI#339: identificación EXTRANJERA del destinatario (pasaporte, etc.). Si va
        /// informada, sustituye al NIF (la AEAT no valida IDOtro contra el censo).
        /// </summary>
        public VerifactuIdOtro IdOtro { get; set; }

        /// <summary>
        /// Desglose por tipos de IVA
        /// </summary>
        public List<VerifactuDesgloseIva> DesgloseIva { get; set; } = new List<VerifactuDesgloseIva>();

        /// <summary>
        /// Importe total de la factura
        /// </summary>
        public decimal ImporteTotal { get; set; }

        /// <summary>
        /// Para rectificativas: tipo de rectificación (S=sustitución, I=diferencia)
        /// Por defecto se usa sustitución.
        /// </summary>
        public string TipoRectificacion { get; set; } = "S";

        /// <summary>
        /// Para rectificativas: lista de facturas originales que se rectifican
        /// </summary>
        public List<VerifactuFacturaRectificada> FacturasRectificadas { get; set; } = new List<VerifactuFacturaRectificada>();
    }

    /// <summary>
    /// Desglose de IVA por tipo impositivo
    /// </summary>
    public class VerifactuDesgloseIva
    {
        /// <summary>
        /// Base imponible
        /// </summary>
        public decimal BaseImponible { get; set; }

        /// <summary>
        /// Tipo de IVA (21, 10, 4, 0)
        /// </summary>
        public decimal TipoIva { get; set; }

        /// <summary>
        /// Cuota de IVA
        /// </summary>
        public decimal CuotaIva { get; set; }

        /// <summary>
        /// Tipo de recargo de equivalencia (5.2, 1.4, 0.5, 0)
        /// </summary>
        public decimal TipoRecargoEquivalencia { get; set; }

        /// <summary>
        /// Cuota de recargo de equivalencia
        /// </summary>
        public decimal CuotaRecargoEquivalencia { get; set; }

        /// <summary>
        /// NestoAPI#347: clave de régimen AEAT del desglose (L8). "17" = operación acogida a
        /// OSS/IOSS (Cap. XI Tít. IX LIVA). Null = régimen general (no se envía).
        /// </summary>
        public string ClaveRegimen { get; set; }

        /// <summary>
        /// NestoAPI#347: calificación de la operación AEAT (L9). "N2" = no sujeta por reglas de
        /// localización (ventas OSS: el IVA extranjero no se declara a la AEAT española).
        /// Null = sujeta y no exenta (S1, el defecto). Con N1/N2 está PROHIBIDO informar
        /// tipo impositivo y cuota (validaciones AEAT §15.4).
        /// </summary>
        public string CalificacionOperacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#339: identificación del destinatario SIN NIF español (catálogo L7 de la AEAT):
    /// 02 NIF-IVA, 03 pasaporte, 04 documento oficial del país, 05 certificado de residencia,
    /// 06 otro documento probatorio, 07 no censado. Con IDOtro no hay validación de censo.
    /// </summary>
    public class VerifactuIdOtro
    {
        /// <summary>País ISO 3166-1 alfa-2 (FR, MA, GB...).</summary>
        public string CodigoPais { get; set; }
        /// <summary>Tipo del catálogo L7 ("03" = pasaporte).</summary>
        public string IdType { get; set; }
        /// <summary>El identificador en sí (el nº de pasaporte, etc.).</summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// Referencia a una factura original que se rectifica
    /// </summary>
    public class VerifactuFacturaRectificada
    {
        /// <summary>
        /// Serie de la factura original
        /// </summary>
        public string Serie { get; set; }

        /// <summary>
        /// Número de la factura original
        /// </summary>
        public string Numero { get; set; }

        /// <summary>
        /// Fecha de expedición de la factura original
        /// </summary>
        public DateTime FechaExpedicion { get; set; }
    }
}
