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
