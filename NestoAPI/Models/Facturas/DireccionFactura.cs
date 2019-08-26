using System;

namespace NestoAPI.Models.Facturas
{
    public class DireccionFactura
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string PoblacionCompleta {
            get
            {
                return String.Format("{0} {1} ({2})", CodigoPostal, Poblacion, Provincia);
            }
        }
        public string Telefonos { get; set; }
        public string Comentarios { get; set; }
    }
}