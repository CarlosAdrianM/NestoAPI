namespace NestoAPI.Models
{
    public class PrestashopProductoDTO
    {
        public string ProductoId { get; set; }
        public string Nombre { get; set; }
        public string DescripcionBreve { get; set; }
        public string DescripcionCompleta { get; set; }
        public decimal? PvpIvaIncluido { get; set; }
        public bool VistoBueno { get; set; }
    }
}
