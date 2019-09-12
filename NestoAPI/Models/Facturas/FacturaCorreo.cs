using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Facturas
{
    public class FacturaCorreo
    {
        public string Empresa { get; set; }
        public string Factura { get; set; }
        public string Correo { get; set; }
    }
}