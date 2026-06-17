using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Calcula la zona de envío a partir del código postal. Portado fielmente de Nesto
    /// (AgenciasViewModel.CalcularZonaEnvio) al mover el comparador de agencias a NestoAPI.
    /// </summary>
    public static class CalculadoraZonaEnvio
    {
        private static readonly Regex CodigoPostalPortugal = new Regex(@"^\d{4}[ -]\d{3}$");

        // Islas "mayores" = capitales y poblaciones grandes (tarifa distinta a las "menores").
        private static readonly string[] CodigosMallorcaMayores =
        {
            "07001", "07002", "07003", "07004", "07005", "07006", "07007", "07008", "07009", "07010",
            "07011", "07012", "07013", "07014", "07015", "07070", "07071", "07080", "07120", "07121",
            "07122", "07198", "07199", "07600", "07610", "07611", "07710"
        };

        private static readonly string[] CodigosCanariasMayores =
        {
            "38001", "38002", "38003", "38004", "38005", "38006", "38007", "38008", "38009", "38010",
            "38070", "38071", "38080", "38111", "38150", "38170",
            "35001", "35002", "35003", "35004", "35005", "35006", "35007", "35008", "35009", "35010",
            "35011", "35012", "35013", "35014", "35015", "35016", "35017", "35018", "35019", "35070",
            "35071", "35080", "35220", "35229"
        };

        public static ZonasEnvioAgencia CalcularZona(string codigoPostal)
        {
            if (codigoPostal == null)
            {
                return ZonasEnvioAgencia.Extranjero;
            }
            codigoPostal = codigoPostal.Trim();

            if (CodigoPostalPortugal.IsMatch(codigoPostal))
            {
                return ZonasEnvioAgencia.Portugal;
            }

            if (codigoPostal.Length != 5 || codigoPostal == "EXTER")
            {
                return ZonasEnvioAgencia.Extranjero;
            }

            if (codigoPostal.StartsWith("28"))
            {
                return ZonasEnvioAgencia.Provincial;
            }
            if (codigoPostal.StartsWith("07") && !CodigosMallorcaMayores.Contains(codigoPostal))
            {
                return ZonasEnvioAgencia.BalearesMenores;
            }
            if (CodigosMallorcaMayores.Contains(codigoPostal))
            {
                return ZonasEnvioAgencia.BalearesMayores;
            }
            if ((codigoPostal.StartsWith("35") || codigoPostal.StartsWith("38")) && !CodigosCanariasMayores.Contains(codigoPostal))
            {
                return ZonasEnvioAgencia.CanariasMenores;
            }
            if (CodigosCanariasMayores.Contains(codigoPostal))
            {
                return ZonasEnvioAgencia.CanariasMayores;
            }
            return ZonasEnvioAgencia.Peninsular;
        }
    }
}
