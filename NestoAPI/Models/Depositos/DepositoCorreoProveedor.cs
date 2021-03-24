using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Depositos
{
    public class DepositoCorreoProveedor
    {
        public List<DatosCorreoDeposito> DatosCorreo { get; set; }
        public string DireccionCorreo { get; set; }
        public bool EnvioConExito { get; set; }
        public string NombrePersonaContacto { get; set; }
        public string NombreProveedor { get; set; }
    }
}