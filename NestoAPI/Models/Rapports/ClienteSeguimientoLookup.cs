using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Rapports
{
    public class ClienteSeguimientoLookup
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public short Estado { get; set; }
        public DateTime FechaUltimaVisita { get; set; }
    }
}