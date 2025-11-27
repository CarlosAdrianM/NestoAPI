using System.Collections.Generic;
using NestoAPI.Models.Facturas.SeriesFactura;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Registro centralizado de series de factura que tramitan por Verifactu.
    /// Solo las series incluidas en este diccionario enviarán facturas a Verifacti.
    /// Series como GB (interna) o las eliminadas (EV, UL, VC, DV) no están aquí.
    /// </summary>
    public static class RegistroSeriesVerifactu
    {
        private static readonly Dictionary<string, ISerieFacturaVerifactu> _series =
            new Dictionary<string, ISerieFacturaVerifactu>
            {
                { "NV", new SerieNV() },
                { "CV", new SerieCV() },
                { "RV", new SerieRV() },
                { "RC", new SerieRC() },
            };

        /// <summary>
        /// Obtiene la configuración de una serie si está registrada para Verifactu.
        /// </summary>
        /// <param name="codigoSerie">Código de la serie (ej: "NV", "CV")</param>
        /// <returns>La serie si existe y tramita Verifactu, null en caso contrario</returns>
        public static ISerieFacturaVerifactu ObtenerSerie(string codigoSerie)
        {
            if (string.IsNullOrWhiteSpace(codigoSerie))
                return null;

            var codigo = codigoSerie.Trim().ToUpperInvariant();
            return _series.TryGetValue(codigo, out var serie) ? serie : null;
        }

        /// <summary>
        /// Indica si una serie debe tramitarse por Verifactu.
        /// </summary>
        /// <param name="codigoSerie">Código de la serie</param>
        /// <returns>True si la serie está registrada y tramita Verifactu</returns>
        public static bool TramitaVerifactu(string codigoSerie)
        {
            var serie = ObtenerSerie(codigoSerie);
            return serie?.TramitaVerifactu ?? false;
        }

        /// <summary>
        /// Indica si una serie es para facturas rectificativas.
        /// </summary>
        /// <param name="codigoSerie">Código de la serie</param>
        /// <returns>True si es una serie rectificativa</returns>
        public static bool EsSerieRectificativa(string codigoSerie)
        {
            var serie = ObtenerSerie(codigoSerie);
            return serie?.EsRectificativa ?? false;
        }

        /// <summary>
        /// Obtiene el código de la serie rectificativa asociada a una serie normal.
        /// </summary>
        /// <param name="codigoSerie">Código de la serie normal (ej: "NV")</param>
        /// <returns>Código de la serie rectificativa (ej: "RV") o null</returns>
        public static string ObtenerSerieRectificativa(string codigoSerie)
        {
            var serie = ObtenerSerie(codigoSerie);
            return serie?.SerieRectificativaAsociada;
        }

        /// <summary>
        /// Registra una nueva serie en el diccionario.
        /// Útil para tests o para añadir series dinámicamente.
        /// </summary>
        internal static void RegistrarSerie(string codigoSerie, ISerieFacturaVerifactu serie)
        {
            var codigo = codigoSerie.Trim().ToUpperInvariant();
            _series[codigo] = serie;
        }
    }
}
