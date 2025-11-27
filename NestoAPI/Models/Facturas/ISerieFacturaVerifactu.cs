using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Interfaz extendida para series de factura con soporte Verifactu.
    /// Las series que implementen esta interfaz y estén registradas en RegistroSeriesVerifactu
    /// serán tramitadas a través de la API de Verifacti.
    /// </summary>
    public interface ISerieFacturaVerifactu : ISerieFactura
    {
        /// <summary>
        /// Indica si las facturas de esta serie deben enviarse a Verifactu.
        /// </summary>
        bool TramitaVerifactu { get; }

        /// <summary>
        /// Tipo de factura para Verifactu: F1 (normal), R1, R3, R4 (rectificativas).
        /// Para series rectificativas, este valor se usa como tipo por defecto
        /// si no se especifica otro en el pedido.
        /// </summary>
        string TipoFacturaVerifactuPorDefecto { get; }

        /// <summary>
        /// Indica si esta serie es para facturas rectificativas.
        /// </summary>
        bool EsRectificativa { get; }

        /// <summary>
        /// Descripción que se enviará a Verifactu en el campo obligatorio 'descripcion'.
        /// Máximo 500 caracteres.
        /// </summary>
        string DescripcionVerifactu { get; }

        /// <summary>
        /// Código de la serie de rectificativas asociada.
        /// Por ejemplo, para NV sería "RV", para CV sería "RC".
        /// Null si esta serie ya es rectificativa o no tiene asociada.
        /// </summary>
        string SerieRectificativaAsociada { get; }
    }
}
