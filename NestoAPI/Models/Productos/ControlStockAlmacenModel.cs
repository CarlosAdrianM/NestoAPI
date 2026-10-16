namespace NestoAPI.Models.Productos
{
    public class ControlStockAlmacenModel : ControlStockBase
    {
        public string Almacen { get; set; }
        public string Estacionalidad { get; set; }
        public string Categoria { get; set; }
        public int Multiplos { get; set; }
        public int StockMaximoActual { get; set; }
        public bool YaExiste { get; internal set; }

        // NestoAPI#628 (ficha = Excel): cómo se ha calculado lo sugerido, para el tooltip de Nesto. Aditivo: los clientes viejos lo ignoran.
        /// <summary>
        /// La frase que explica el máximo sugerido (en ALG, también el mínimo): en ALG, la del Excel de propuestas de stock
        /// (<see cref="Infraestructure.Stocks.Tendencias.MotorTendencias"/>); en las tiendas, la de la media de 24 meses.
        /// </summary>
        public string Explicacion { get; set; }
        /// <summary>La fórmula con la que sale (la columna «Fórmula» del Excel en ALG; <see cref="ControlStockBase.FORMULA_CLASICA"/> en las tiendas).</summary>
        public string Formula { get; set; }
        /// <summary>
        /// En ALG, la acción que propone el Excel («Ajustar máx/mín», «Mantener», «Liquidar», «Pasar a estado 1»…). Null en las
        /// tiendas, o si en ALG no se ha podido calcular como en el Excel.
        /// </summary>
        public string Accion { get; set; }
        /// <summary>True si lo sugerido sale del motor de las propuestas de stock (lo mismo que el Excel); false si es la fórmula de siempre.</summary>
        public bool CalculadoComoExcel { get; set; }
    }
}
