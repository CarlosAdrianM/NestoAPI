namespace NestoAPI.Models
{
    public class CrearEtiquetaPendienteDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>
        /// NestoAPI#494 (28/09/26): 0 = que la elija el comparador (con retorno 1, envío + retorno; con 2,
        /// solo retorno). Los clientes que mandan una agencia concreta siguen igual.
        /// </summary>
        public int Agencia { get; set; }
        public short Retorno { get; set; }
        public bool CobrarReembolso { get; set; } = true;
        public decimal? ImporteReembolso { get; set; }
    }
}
