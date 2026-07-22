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
        /// <summary>NestoAPI#345: fecha en la que el banco cargará el efecto (puede ser
        /// posterior al día del envío si la remesa respeta los vencimientos originales).</summary>
        public DateTime? FechaVencimiento { get; set; }
        public string NombrePersona { get; set; }
        public Iban Iban { get; set; }
        public int NOrden { get; set; }
        public string NumeroDocumento { get; set; }
        public string Efecto { get; set; }
    }
}