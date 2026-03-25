namespace NestoAPI.Models
{
    public class CrearEtiquetaPendienteDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Agencia { get; set; }
        public short Retorno { get; set; }
        public bool CobrarReembolso { get; set; } = true;
        public decimal? ImporteReembolso { get; set; }
    }
}
