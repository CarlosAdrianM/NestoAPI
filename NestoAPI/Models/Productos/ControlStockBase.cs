using System;

namespace NestoAPI.Models.Productos
{
    public abstract class ControlStockBase
    {
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
        public int StockMaximoCalculado => (int)Math.Ceiling(ConsumoMedioDiario * (DiasStockSeguridad + DiasReaprovisionamiento * 2));
    }
}