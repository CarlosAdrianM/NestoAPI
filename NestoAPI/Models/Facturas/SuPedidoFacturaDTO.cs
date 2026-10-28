namespace NestoAPI.Models.Facturas
{
    /// <summary>NestoAPI#723: cuerpo y respuesta de PUT api/Facturas/{empresa}/{numero}/SuPedido.</summary>
    public class SuPedidoFacturaDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string SuPedido { get; set; }
        public string SuPedidoAnterior { get; set; }
    }
}
