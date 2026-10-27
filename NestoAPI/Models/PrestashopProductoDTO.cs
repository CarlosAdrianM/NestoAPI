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
        /// <summary>
        /// NestoAPI#701: solo en la respuesta del PUT, y solo cuando lo guardado llegará a la tienda más tarde
        /// («Guardado. La tienda se actualizará en unos minutos.»). null = publicado al momento o nada que publicar.
        /// </summary>
        public string Aviso { get; set; }
    }
}
