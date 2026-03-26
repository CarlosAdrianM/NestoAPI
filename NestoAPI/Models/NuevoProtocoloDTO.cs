using System.ComponentModel.DataAnnotations;

namespace NestoAPI.Models
{
    public class NuevoProtocoloDTO
    {
        [Required]
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string ImagenUrl { get; set; }
        public int? VideoId { get; set; }
    }
}
