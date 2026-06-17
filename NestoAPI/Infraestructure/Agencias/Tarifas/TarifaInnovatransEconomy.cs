using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Innovatrans — Servicio Economy terrestre (48h, 95% en 24h). "Regional" de la oferta se
    /// mapea a la zona Provincial (CP 28, Madrid). Precios de la oferta, ANTES de fuel.
    /// </summary>
    public class TarifaInnovatransEconomy : TarifaInnovatransBase
    {
        public override byte ServicioId => 1; // Economy
        public override string NombreServicio => "Economy";

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Provincial, 3.94m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Provincial, 4.04m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Provincial, 4.63m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Provincial, 5.13m),
            new TramoCosteEnvio(20m, ZonasEnvioAgencia.Provincial, 5.95m),
            new TramoCosteEnvio(25m, ZonasEnvioAgencia.Provincial, 6.78m),
            new TramoCosteEnvio(30m, ZonasEnvioAgencia.Provincial, 7.61m),
            new TramoCosteEnvio(35m, ZonasEnvioAgencia.Provincial, 8.43m),
            new TramoCosteEnvio(40m, ZonasEnvioAgencia.Provincial, 9.26m),
            new TramoCosteEnvio(45m, ZonasEnvioAgencia.Provincial, 10.09m),
            new TramoCosteEnvio(50m, ZonasEnvioAgencia.Provincial, 10.91m),
            new TramoCosteEnvio(60m, ZonasEnvioAgencia.Provincial, 12.07m),
            new TramoCosteEnvio(70m, ZonasEnvioAgencia.Provincial, 13.72m),
            new TramoCosteEnvio(80m, ZonasEnvioAgencia.Provincial, 15.38m),
            new TramoCosteEnvio(90m, ZonasEnvioAgencia.Provincial, 17.03m),
            new TramoCosteEnvio(100m, ZonasEnvioAgencia.Provincial, 18.69m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Peninsular, 3.97m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Peninsular, 4.53m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Peninsular, 5.64m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 6.96m),
            new TramoCosteEnvio(20m, ZonasEnvioAgencia.Peninsular, 9.16m),
            new TramoCosteEnvio(25m, ZonasEnvioAgencia.Peninsular, 11.37m),
            new TramoCosteEnvio(30m, ZonasEnvioAgencia.Peninsular, 13.57m),
            new TramoCosteEnvio(35m, ZonasEnvioAgencia.Peninsular, 15.78m),
            new TramoCosteEnvio(40m, ZonasEnvioAgencia.Peninsular, 17.98m),
            new TramoCosteEnvio(45m, ZonasEnvioAgencia.Peninsular, 20.19m),
            new TramoCosteEnvio(50m, ZonasEnvioAgencia.Peninsular, 22.39m),
            new TramoCosteEnvio(60m, ZonasEnvioAgencia.Peninsular, 25.48m),
            new TramoCosteEnvio(70m, ZonasEnvioAgencia.Peninsular, 29.89m),
            new TramoCosteEnvio(80m, ZonasEnvioAgencia.Peninsular, 34.30m),
            new TramoCosteEnvio(90m, ZonasEnvioAgencia.Peninsular, 38.71m),
            new TramoCosteEnvio(100m, ZonasEnvioAgencia.Peninsular, 43.12m)
        };

        public override IReadOnlyList<TramoCosteEnvio> CosteEnvio => _costeEnvio;

        public override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            if (zona == ZonasEnvioAgencia.Provincial)
            {
                return 0.17m;
            }
            if (zona == ZonasEnvioAgencia.Peninsular)
            {
                return 0.44m;
            }
            return decimal.MaxValue;
        }
    }
}
