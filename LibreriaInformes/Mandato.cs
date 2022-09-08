using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.LibreriaInformes
{
    public class Mandato
    {
        public string Referencia { get; set; }
        public string IdentificadorAcreedor { get; set; }
        public string NombreAcreedor { get; set; }
        public string DireccionAcreedor { get; set; }
        public string CodigoPostalAcreedor { get; set; }
        public string PoblacionAcreedor { get; set; }
        public string ProvinciaAcreedor { get; set; }
        public string PaisAcreedor { get; set; }
        public string PoblacionCompletaAcreedor { get => $"{CodigoPostalAcreedor} {PoblacionAcreedor} ({ProvinciaAcreedor})"; }
        public string NombreDeudor { get; set; }
        public string DireccionDeudor { get; set; }
        public string CodigoPostalDeudor { get; set; }
        public string PoblacionDeudor { get; set; }
        public string ProvinciaDeudor { get; set; }
        public string PaisDeudor { get; set; }
        public string PoblacionCompletaDeudor { get => $"{CodigoPostalDeudor} {PoblacionDeudor} ({ProvinciaDeudor})"; }
        public string SwiftBic { get; set; }
        public string Iban { get; set; }
        public string PersonaFirmante { get; set; }
    }
}
