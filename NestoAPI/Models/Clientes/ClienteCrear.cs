using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Clientes
{
    public class ClienteCrear
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string CodigoPostal { get; set; }
        public string Comentarios { get; set; }
        [StringLength(50)]
        public string ComentariosPicking { get; set; }
        [StringLength(50)]
        public string ComentariosRuta { get; set; }
        [StringLength(50)]
        public string Direccion { get; set; }
        public bool EsContacto {get;set;}
        public short? Estado { get; set; }
        public bool Estetica { get; set; }
        public string FormaPago { get; set; }
        public string Iban { get; set; }
        public string Nif {get;set;}
        [StringLength(50)]
        public string Nombre { get; set; }
        public bool Peluqueria { get; set; }
        public string PlazosPago { get; set; }
        [StringLength(50)]
        public string Poblacion { get; set; }
        [StringLength(50)]
        public string Provincia { get; set; }
        public string Ruta { get; set; }
        [StringLength(29)]
        public string Telefono { get; set; }
        public string VendedorEstetica { get; set; }
        public string VendedorPeluqueria { get; set; }

        public string Usuario { get; set; }


        public virtual ICollection<PersonaContactoDTO> PersonasContacto { get; set; }
    }
}