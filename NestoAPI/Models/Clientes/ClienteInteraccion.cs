namespace NestoAPI.Models.Clientes
{
    public class ClienteInteraccion
    {
        public string ClienteId { get; set; }
        public string TipoInteraccion { get; set; }
        public float DiaDeLaSemana { get; set; }
        public float EsPorLaTarde { get; set; }
        public float DiasDesdeUltimaInteraccion { get; set; }
        public float DiasDesdeUltimoPedido { get; set; }
        public float PedidosUltimos12Meses { get; set; }
        public bool Target { get; set; }
    }
}
