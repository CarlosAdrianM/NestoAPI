namespace NestoAPI.Models.PedidosVenta
{
    public class ParametroStringIntInt
    {
        public string Empresa { get; set; }
        public int NumeroPedidoOriginal { get; set; }
        public int NumeroPedidoAmpliacion { get; set; }

        /// <summary>
        /// NestoAPI#324: el usuario confirmó "unir de todas formas" tras un fallo de validación de
        /// precios/descuentos. Al unir dos pedidos EXISTENTES no hay DTO donde viaje
        /// CreadoSinPasarValidacion, así que el flag va aquí.
        /// </summary>
        public bool SinPasarValidacion { get; set; }
    }
}