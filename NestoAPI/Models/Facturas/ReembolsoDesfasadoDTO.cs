namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#810: un envío del reparto propio sin entregar cuyo reembolso no coincide con lo que queda por cobrar de las facturas
    /// de su pedido (en la respuesta de Facturar rutas y de FacturarPedido). Caso 928723: Envio 250518, ReembolsoActual 480,02,
    /// ReembolsoFacturado 510,15.
    /// </summary>
    public class ReembolsoDesfasadoDTO
    {
        /// <summary>Número del envío (EnviosAgencia.Numero): el {id} de PUT api/EnviosAgencias/{id}/Reembolso.</summary>
        public int Envio { get; set; }
        public int Pedido { get; set; }
        /// <summary>Lo que lleva ahora el envío.</summary>
        public decimal ReembolsoActual { get; set; }
        /// <summary>Lo que debería llevar: lo pendiente en efectivo de las facturas del pedido (menos lo de otros envíos sin entregar).</summary>
        public decimal ReembolsoFacturado { get; set; }
    }
}
