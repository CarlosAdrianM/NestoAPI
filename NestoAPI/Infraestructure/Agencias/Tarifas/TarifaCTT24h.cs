using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// NestoAPI#505: tarifa CTT Express del servicio URGENTE, "CTT 24h" (oferta NUEVA VISION 2026,
    /// CTT_NUEVA_VISION_2026.pdf, pág. 4) y, en Baleares, "CTT Baleares Express" (pág. 9: 24 h en
    /// Mallorca, 48 h en las islas menores). Precios ANTES de fuel, igual que <see cref="TarifaCTT48h"/>,
    /// y con la misma simplificación de zonas: la columna "Peninsular" de CTT para todo lo que no es
    /// Provincial. Canarias no se modela (el comparador la resuelve por Canteras).
    ///
    /// Es un servicio SOLO A PETICIÓN (<see cref="ITarifaSoloAPeticion"/>): el comparador NO lo propone
    /// nunca (ni en MasEconomica ni en el ranking de la comparativa sombra); solo se tarifica cuando el
    /// usuario lo fuerza en la ventana de agencias (coste con servicioId = 24, para el ImporteGasto).
    /// </summary>
    public class TarifaCTT24h : TarifaNacionalBase, ITarifaSoloAPeticion
    {
        public override int AgenciaId => 13; // CTT (debe coincidir con AgenciasTransporte.Numero)
        public override byte ServicioId => 24; // CTT 24h
        public override string NombreServicio => "CTT 24h";
        public override byte HorarioDefectoId => 0;

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            // Provincial
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Provincial, 2.70m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Provincial, 2.85m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Provincial, 3.00m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Provincial, 3.15m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Provincial, 3.25m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Provincial, 4.26m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Provincial, 5.17m),
            // Peninsular (columna "Peninsular" de la oferta)
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Peninsular, 3.07m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Peninsular, 3.23m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Peninsular, 3.38m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Peninsular, 3.54m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Peninsular, 3.65m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Peninsular, 4.67m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 5.67m),
            // Portugal
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Portugal, 4.09m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Portugal, 4.45m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Portugal, 4.66m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Portugal, 5.24m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Portugal, 5.43m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Portugal, 7.83m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Portugal, 10.52m),
            // Baleares (servicio Express): Mallorca = Mayores, Islas Menores = Menores
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.BalearesMayores, 7.82m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.BalearesMayores, 12.25m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.BalearesMayores, 16.70m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.BalearesMayores, 21.22m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMayores, 25.74m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMayores, 46.88m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.BalearesMayores, 73.04m),
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.BalearesMenores, 9.41m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.BalearesMenores, 14.09m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.BalearesMenores, 18.77m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.BalearesMenores, 23.45m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMenores, 28.13m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMenores, 51.52m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.BalearesMenores, 74.92m)
        };

        protected override IReadOnlyList<TramoCosteEnvio> Tramos => _costeEnvio;

        protected override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            switch (zona)
            {
                case ZonasEnvioAgencia.Provincial: return 0.20m;
                case ZonasEnvioAgencia.Peninsular: return 0.28m;
                case ZonasEnvioAgencia.Portugal: return 0.63m;
                case ZonasEnvioAgencia.BalearesMayores: return 4.22m;
                case ZonasEnvioAgencia.BalearesMenores: return 4.68m;
                default: return decimal.MaxValue;
            }
        }

        /// <summary>Reembolso CTT: 0% del importe, mínimo 1,15€ (oferta 2026, igual para todos los servicios).</summary>
        protected override decimal CosteReembolso(decimal reembolso) => 1.15m;
    }
}
