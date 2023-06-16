namespace NestoAPI.Models.PedidosBase
{
    public class LineaPedidoBase
    {
        // Propiedades
        // Empezamos desde la base imponible, pero una vez vaya funcionando hay que ir hacia abajo hasta tener todos los campos comunes
        //public decimal BaseImponible { get => Bruto - ImporteDescuento; }
        public virtual decimal BaseImponible { get; set; }
        public decimal PorcentajeIva { get; set; }
        public string Producto { get; set; }

        // Propiedades calculadas
        public virtual decimal ImporteIva { get => BaseImponible * PorcentajeIva; }
        public virtual decimal Total { get => BaseImponible + ImporteIva; }
    }
}