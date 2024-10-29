namespace NestoAPI.Models.Clientes
{
    public class ClienteProbabilidadVenta : ClienteDTO
    {        
        public float Probabilidad { get; set; } 
        public int DiasDesdeUltimoPedido { get; set; }
        public int DiasDesdeUltimaInteraccion { get; set; }
    }
}
