namespace NestoAPI.Models.Rapports
{
    public class CopiarSeguimientosRequest
    {
        public string Empresa { get; set; }
        public string ClienteOrigen { get; set; }
        public string ContactoOrigen { get; set; }
        public string ClienteDestino { get; set; }
        public string ContactoDestino { get; set; }
        public bool EliminarOrigen { get; set; }
    }
}
