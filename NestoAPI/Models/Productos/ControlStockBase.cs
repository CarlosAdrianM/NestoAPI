using System;
using System.Globalization;

namespace NestoAPI.Models.Productos
{
    public abstract class ControlStockBase
    {
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        public int ConsumoAnual { get; set; } // Cargamos el consumo de dos años
        public decimal ConsumoMedioDiario => ConsumoMedioMensual / 30;
        public decimal ConsumoMedioMensual => ConsumoAnual / MesesAntiguedad;
        public int DiasReaprovisionamiento { get; set; }
        public int DiasStockSeguridad { get; set; }

        private decimal mesesAntiguedad;
        public decimal MesesAntiguedad
        {
            get => mesesAntiguedad;
            set
            {
                if (value < 1)
                {
                    value = 1;
                }
                else if (value > 24) // 24 porque el ConsumoAnual es de dos años
                {
                    value = 24;
                }
                mesesAntiguedad = value;
            }
        }

        private int? stockMaximoFijado;

        /// <summary>
        /// Media de 24 meses × (seguridad + 2 × días en servir). NestoAPI#628 (ficha = Excel): en ALG lo fija el motor de las
        /// propuestas de stock (<see cref="FijarStockMaximoCalculado"/>), para que la ficha y el Excel sugieran lo mismo.
        /// </summary>
        public int StockMaximoCalculado => stockMaximoFijado ?? StockMaximoFormulaClasica;

        internal int StockMaximoFormulaClasica => (int)Math.Ceiling(ConsumoMedioDiario * (DiasStockSeguridad + DiasReaprovisionamiento * 2));

        internal void FijarStockMaximoCalculado(int maximo) => stockMaximoFijado = maximo;

        /// <summary>NestoAPI#628: «media de 24 meses × (7 + 2 × días)», la fórmula de siempre de la ficha.</summary>
        public const string FORMULA_CLASICA = "Media de 24 meses × (7 + 2 × días)";

        /// <summary>NestoAPI#628: cómo sale el máximo de la fórmula de siempre, en una frase (para el tooltip de Nesto).</summary>
        internal string ExplicacionMaximoClasico(string donde)
        {
            int dias = DiasStockSeguridad + 2 * DiasReaprovisionamiento;
            return $"Media de {Num(MesesAntiguedad, 0)} {(Math.Round(MesesAntiguedad) == 1 ? "mes" : "meses")} en {donde}: {ConsumoAnual} uds, " +
                $"{Num(ConsumoMedioMensual, 1)} uds/mes; seguridad {DiasStockSeguridad} + 2 × {DiasReaprovisionamiento} días del proveedor = {dias} días " +
                $"→ máximo {Num(ConsumoMedioMensual, 1)} × {dias} ÷ 30 = {Num(ConsumoMedioDiario * dias, 2)}, redondeado hacia arriba {StockMaximoFormulaClasica}.";
        }

        internal static string Num(decimal valor, int decimales) => valor.ToString("N" + decimales, ES);
    }
}
