using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Facturas
{
    public class ClienteCorreoFactura: IEquatable<ClienteCorreoFactura>
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Correo { get; set; }

        public bool Equals(ClienteCorreoFactura other)
        {
            //Check whether the compared object is null.
            if (Object.ReferenceEquals(other, null)) return false;

            //Check whether the compared object references the same data.
            if (Object.ReferenceEquals(this, other)) return true;

            //Check whether the products' properties are equal.
            return Cliente.Equals(other.Cliente) && Contacto.Equals(other.Contacto) && Correo.Equals(other.Correo);
        }

        public override int GetHashCode()
        {

            //Get hash code for the Name field if it is not null.
            int hasCliente = Cliente == null ? 0 : Cliente.GetHashCode();

            //Get hash code for the Code field.
            int hasContacto = Contacto.GetHashCode();

            int hasCorreo = Correo.GetHashCode();

            //Calculate the hash code for the product.
            return hasCliente ^ hasContacto ^ hasCorreo;
        }
    }
}