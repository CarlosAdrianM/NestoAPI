using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Productos
{
    public class ControlStockProductoModel : ControlStockBase
    {
        public ControlStockProductoModel()
        {
            ControlesStocksAlmacen = new List<ControlStockAlmacenModel>();
        }
        public string ProductoId { get; set; }
        public decimal PuntoPedidoCalculado => ConsumoMedioDiario * (DiasStockSeguridad + DiasReaprovisionamiento);
        public int StockMinimoActual { get; set; }

        private int? stockMinimoFijado;

        /// <summary>
        /// El mínimo (de ALG) sugerido. NestoAPI#628 (ficha = Excel): lo fija el motor de las propuestas de stock
        /// (<see cref="FijarStockMinimoCalculado"/>); si no se ha podido calcular así, el punto de pedido de siempre.
        /// </summary>
        public int StockMinimoCalculado => stockMinimoFijado ?? StockMinimoFormulaClasica;

        internal int StockMinimoFormulaClasica
        {
            get
            {
                if (PuntoPedidoCalculado < 1)
                {
                    return (int)Math.Ceiling(PuntoPedidoCalculado);
                }
                return (int)Math.Round(PuntoPedidoCalculado, 0, MidpointRounding.AwayFromZero);
            }
        }

        internal void FijarStockMinimoCalculado(int minimo) => stockMinimoFijado = minimo;

        /// <summary>NestoAPI#628: cómo sale el mínimo de la fórmula de siempre (media de toda la empresa), en una frase.</summary>
        internal string ExplicacionMinimoClasico()
        {
            int dias = DiasStockSeguridad + DiasReaprovisionamiento;
            return $"Mínimo: media de {Num(MesesAntiguedad, 0)} {(Math.Round(MesesAntiguedad) == 1 ? "mes" : "meses")} de toda la empresa: {ConsumoAnual} uds, " +
                $"{Num(ConsumoMedioMensual, 1)} uds/mes; seguridad {DiasStockSeguridad} + {DiasReaprovisionamiento} días del proveedor = {dias} días " +
                $"→ {Num(ConsumoMedioMensual, 1)} × {dias} ÷ 30 = {Num(PuntoPedidoCalculado, 2)}, redondeado {StockMinimoFormulaClasica}.";
        }

        public ICollection<ControlStockAlmacenModel> ControlesStocksAlmacen { get; set; }
    }
}
