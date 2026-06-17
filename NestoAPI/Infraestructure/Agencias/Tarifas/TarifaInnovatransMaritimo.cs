using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Innovatrans — Servicio Express marítimo a islas (Baleares y Canarias). Precios de la
    /// oferta, ANTES de fuel. Isla Mayor = Mallorca / Tenerife-Gran Canaria capitales; Isla Menor
    /// = resto. Los despachos fijos de Canarias (18,03 origen + 25,00 destino) se incorporan al
    /// precio, como hace Sending con su DUA. Seguro marítimo, T3 y peso volumétrico van aparte.
    /// </summary>
    public class TarifaInnovatransMaritimo : TarifaInnovatransBase
    {
        private const decimal DespachoCanarias = 18.03m + 25m; // origen + destino, por envío

        public override byte ServicioId => 3; // Marítimo islas
        public override string NombreServicio => "Marítimo islas";

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMayores, 8.47m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMayores, 11.88m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMenores, 10.86m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMenores, 14.26m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.CanariasMayores, 15.36m + DespachoCanarias),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.CanariasMenores, 30.91m + DespachoCanarias)
        };

        public override IReadOnlyList<TramoCosteEnvio> CosteEnvio => _costeEnvio;

        public override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            switch (zona)
            {
                case ZonasEnvioAgencia.BalearesMayores:
                    return 0.97m;
                case ZonasEnvioAgencia.BalearesMenores:
                    return 1.13m;
                case ZonasEnvioAgencia.CanariasMayores:
                    return 1.35m;
                case ZonasEnvioAgencia.CanariasMenores:
                    return 2.59m;
                default:
                    return decimal.MaxValue;
            }
        }
    }
}
