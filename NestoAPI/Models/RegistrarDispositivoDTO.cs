using System.ComponentModel.DataAnnotations;

namespace NestoAPI.Models
{
    public class RegistrarDispositivoDTO
    {
        [Required]
        public string Token { get; set; }

        [Required]
        public string Plataforma { get; set; }

        [Required]
        public string Aplicacion { get; set; }

        public string Empresa { get; set; }

        public string Vendedor { get; set; }

        public string Cliente { get; set; }

        public string Contacto { get; set; }
    }
}
