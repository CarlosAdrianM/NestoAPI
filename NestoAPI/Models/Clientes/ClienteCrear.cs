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
        /// <summary>
        /// NestoAPI#499: la dirección la eligió el usuario de entre las que propone Google (Nesto#480,
        /// NestoApp#180), no la tecleó. Con el parámetro ExigirDireccionVerificadaAlta encendido, un
        /// alta con dirección y este flag a false se rechaza. Los clientes que no lo mandan lo dejan
        /// en false: por eso el interruptor nace apagado.
        /// </summary>
        public bool DireccionVerificada { get; set; }
        public bool EsContacto {get;set;}
        public short? Estado { get; set; }
        public bool Estetica { get; set; }
        public string FormaPago { get; set; }
        public string Iban { get; set; }
        public string Nif {get;set;}
        /// <summary>NestoAPI#355: país del cliente (ISO-2). Null/vacío = ES (default de la casa).
        /// Para país ≠ ES el NIF puede ser un NIF-IVA intracomunitario (varchar(20), #356).</summary>
        [StringLength(2)]
        public string Pais { get; set; }
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
        // NestoAPI#471: días que el cliente cierra (5 posiciones L..V, '1' abre). Null en el PUT = no tocar.
        public string DiasEnServir { get; set; }
        /// <summary>
        /// NestoAPI#541: si el cambio de DiasEnServir cierra un día y el contacto tiene pedidos con picking, el PUT
        /// se rechaza (código DIAS_CON_PICKING) hasta que el usuario acepte avisar a almacén: entonces se repite
        /// con esto a true, se guarda y se manda el correo. Los clientes que no lo mandan reciben el rechazo.
        /// </summary>
        public bool ConfirmarDiasEnServirConPicking { get; set; }

        public string Usuario { get; set; }


        public virtual ICollection<PersonaContactoDTO> PersonasContacto { get; set; }
    }
}