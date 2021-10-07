using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Domiciliaciones
{
    public class EfectoDomiciliado
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Ccc { get; set; }
        public string Correo { get; set; }
        public string Concepto { get; set; }
        public decimal Importe { get; set; }
        public DateTime Fecha { get; set; }
        public string NombrePersona { get; set; }
        public Iban Iban { get; set; }
    }
}