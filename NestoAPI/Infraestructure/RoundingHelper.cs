using System;

namespace NestoAPI.Infraestructure
{
    public static class RoundingHelper
    {
        /// <summary>
        /// Controla el modo de redondeo usado en toda la aplicación.
        /// - true: AwayFromZero (redondeo comercial, cumple legislación española)
        /// - false: ToEven (Banker's rounding, compatible con VB6/Nesto viejo)
        ///
        /// IMPORTANTE: Si se usa AwayFromZero, el procedimiento almacenado de facturación
        /// debe usar dbo.RoundAwayFromZero(valor, 2) en lugar de ROUND(valor, 2).
        ///
        /// Para volver al comportamiento anterior, cambiar a false.
        /// </summary>
        public static bool UsarAwayFromZero { get; set; } = true;

        /// <summary>
        /// Redondea un valor decimal a 2 decimales.
        /// El modo de redondeo depende de <see cref="UsarAwayFromZero"/>.
        /// </summary>
        public static decimal DosDecimalesRound(decimal value)
        {
            return Round(value, 2);
        }

        /// <summary>
        /// Redondea un valor decimal al número de decimales especificado.
        /// El modo de redondeo depende de <see cref="UsarAwayFromZero"/>.
        /// </summary>
        public static decimal Round(decimal value, int decimals)
        {
            if (UsarAwayFromZero)
            {
                return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            }
            else
            {
                return Vb6Round(value, decimals);
            }
        }

        /// <summary>
        /// Redondeo al estilo VB6 (Banker's rounding / ToEven).
        /// Mantenido para compatibilidad con Nesto viejo si es necesario.
        /// </summary>
        private static decimal Vb6Round(decimal value, int decimals)
        {
            double d = (double)value;
            double factor = Math.Pow(10, decimals);
            return (decimal)(Math.Round(d * factor, 0, MidpointRounding.ToEven) / factor);
        }
    }
}
