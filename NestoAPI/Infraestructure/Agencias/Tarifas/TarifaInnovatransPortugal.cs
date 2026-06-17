using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Innovatrans — Servicio 14H a Portugal peninsular. Precios de la oferta, ANTES de fuel.
    /// Madeira y Azores van por Servicio Marítimo con precios distintos y zona propia (no
    /// modelada): quedan fuera.
    /// </summary>
    public class TarifaInnovatransPortugal : TarifaInnovatransBase
    {
        public override byte ServicioId => 2; // 14H Portugal
        public override string NombreServicio => "14H Portugal";

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Portugal, 7.42m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Portugal, 7.70m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Portugal, 7.96m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Portugal, 9.91m),
            new TramoCosteEnvio(20m, ZonasEnvioAgencia.Portugal, 13.15m),
            new TramoCosteEnvio(25m, ZonasEnvioAgencia.Portugal, 16.39m),
            new TramoCosteEnvio(30m, ZonasEnvioAgencia.Portugal, 19.64m),
            new TramoCosteEnvio(35m, ZonasEnvioAgencia.Portugal, 22.88m),
            new TramoCosteEnvio(40m, ZonasEnvioAgencia.Portugal, 26.12m),
            new TramoCosteEnvio(45m, ZonasEnvioAgencia.Portugal, 29.37m),
            new TramoCosteEnvio(50m, ZonasEnvioAgencia.Portugal, 32.61m)
        };

        public override IReadOnlyList<TramoCosteEnvio> CosteEnvio => _costeEnvio;

        public override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            if (zona == ZonasEnvioAgencia.Portugal)
            {
                return 0.65m;
            }
            return decimal.MaxValue;
        }
    }
}
