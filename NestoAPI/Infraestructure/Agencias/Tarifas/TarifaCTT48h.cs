using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Tarifa CTT Express, servicio "CTT 48h" (oferta NUEVA VISION 2026, CTT_NUEVA_VISION_2026.pdf).
    /// Es el servicio económico de CTT, equivalente a cómo usamos GLS BusinessParcel. Precios ANTES de
    /// fuel (CTT aplica suplemento de combustible variable según el gasóleo; el % se guarda en
    /// AgenciasTransporte.RecargoCombustible y lo añade el comparador al porte).
    ///
    /// Es una AGENCIA SOMBRA (AgenciasTransporte.EsSombra = 1): compite en el comparador para medir
    /// cuántos envíos ganaría, pero MasEconomica nunca la devuelve (no se selecciona de verdad).
    ///
    /// Zonas: las 4 zonas peninsulares de CTT (Provincial/Regional/Peninsular/Peninsular+) se mapean a
    /// nuestras 2 (Provincial / Peninsular); usamos la columna "Peninsular" de CTT (la media). Canarias
    /// no se modela: el comparador la resuelve siempre por Canteras.
    /// </summary>
    public class TarifaCTT48h : ITarifaAgencia
    {
        public int AgenciaId => 13; // CTT (debe coincidir con AgenciasTransporte.Numero)
        public byte ServicioId => 48; // CTT 48h
        public string NombreServicio => "CTT 48h";
        public byte HorarioDefectoId => 0;

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            // Provincial
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Provincial, 2.66m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Provincial, 2.82m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Provincial, 2.96m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Provincial, 3.11m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Provincial, 3.21m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Provincial, 4.20m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Provincial, 5.11m),
            // Peninsular (columna "Peninsular" de la oferta)
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Peninsular, 3.03m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Peninsular, 3.18m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Peninsular, 3.33m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Peninsular, 3.49m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Peninsular, 3.58m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Peninsular, 4.60m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 5.58m),
            // Portugal
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Portugal, 3.38m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.Portugal, 3.67m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Portugal, 3.85m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.Portugal, 4.33m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Portugal, 4.48m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Portugal, 6.46m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Portugal, 8.68m),
            // Baleares (servicio Economy): Mallorca = Mayores, Islas Menores = Menores
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.BalearesMayores, 3.82m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.BalearesMayores, 4.48m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.BalearesMayores, 5.15m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.BalearesMayores, 5.81m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMayores, 6.47m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMayores, 9.80m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.BalearesMayores, 13.12m),
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.BalearesMenores, 4.80m),
            new TramoCosteEnvio(2m, ZonasEnvioAgencia.BalearesMenores, 5.43m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.BalearesMenores, 6.07m),
            new TramoCosteEnvio(4m, ZonasEnvioAgencia.BalearesMenores, 6.70m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMenores, 7.33m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.BalearesMenores, 10.49m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.BalearesMenores, 13.64m)
        };

        public IReadOnlyList<TramoCosteEnvio> CosteEnvio => _costeEnvio;

        public decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            switch (zona)
            {
                case ZonasEnvioAgencia.Provincial: return 0.20m;
                case ZonasEnvioAgencia.Peninsular: return 0.28m;
                case ZonasEnvioAgencia.Portugal: return 0.52m;
                case ZonasEnvioAgencia.BalearesMayores: return 0.67m;
                case ZonasEnvioAgencia.BalearesMenores: return 0.63m;
                default: return decimal.MaxValue;
            }
        }

        /// <summary>Reembolso CTT: 0% del importe, mínimo 1,15€ (oferta 2026). No se le aplica fuel.</summary>
        public decimal CosteReembolso(decimal reembolso) => 1.15m;
    }
}
