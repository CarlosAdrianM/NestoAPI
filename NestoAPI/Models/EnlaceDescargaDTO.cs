using System;
using System.Collections.Generic;

namespace NestoAPI.Models
{
    /// <summary>
    /// NestoAPI#737 (sugerencias 866/867, Paloma; 831, María José): lo que pide Nesto para mandar por WhatsApp Web un pedido o
    /// una factura con un enlace de descarga. <c>Tipo</c>: «Pedido» o «Factura»; <c>Numero</c>: el del pedido (929498) o el de
    /// la factura (NV2616199). Empresa en blanco = la 1.
    /// </summary>
    public class PeticionEnlaceDescargaDTO
    {
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public string Numero { get; set; }
    }

    /// <summary>
    /// NestoAPI#737: el enlace público (<c>https://api.nuevavision.es/d/{token}</c>) y lo que hace falta para el mensaje de
    /// WhatsApp: el texto de usted ya escrito y los móviles del cliente en formato internacional (34… si es de España), sin «+»
    /// (como los pide <c>https://web.whatsapp.com/send?phone=</c>). Sin móviles, lista vacía: el cliente de la API avisa.
    /// </summary>
    public class EnlaceDescargaDTO
    {
        public string Url { get; set; }
        public DateTime Caduca { get; set; }
        public string Tipo { get; set; }
        public string Numero { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public decimal Importe { get; set; }
        public List<string> Moviles { get; set; } = new List<string>();
        /// <summary>El mensaje para el cliente, de usted y con el enlace: el usuario solo pulsa «Enviar».</summary>
        public string Texto { get; set; }
    }
}
