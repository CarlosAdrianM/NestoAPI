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

        /// <summary>
        /// NestoAPI#662 (TNV#87): si el cliente quiere recibir consejos y recordatorios de reposición
        /// (las push que no son de su pedido). Null = no cambia lo que ya estaba guardado (un
        /// dispositivo nuevo nace con true). Los avisos de pedidos, protocolos y cheques no lo miran.
        /// </summary>
        public bool? RecibirConsejos { get; set; }
    }
}
