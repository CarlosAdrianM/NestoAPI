namespace NestoAPI.Models.Buscador
{
    public class ProductoBuscadorDTO
    {
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public decimal? PVP { get; set; }
        public string CodBarras { get; set; }
        public short? Estado { get; set; }

        public string DescripcionBreve { get; set; }
        public string DescripcionLarga { get; set; }
        public decimal? PVPConIVA { get; set; }
    }
}
