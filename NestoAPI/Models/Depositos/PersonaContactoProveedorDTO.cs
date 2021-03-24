using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Depositos
{
    public class PersonaContactoProveedorDTO
    {
        public string ProveedorId { get; set; }
        public string NombrePersonaContacto { get; set; }
        public string NombreProveedor { get; set; }
        public string CorreoElectronico { get; set; }
    }
}