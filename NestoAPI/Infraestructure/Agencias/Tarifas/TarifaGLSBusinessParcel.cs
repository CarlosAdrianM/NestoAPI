using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Tarifa GLS/ASM "BusinessParcel" (oferta 2026, ASM_2026.pdf). Portada de Nesto.
    /// Precios ANTES de fuel (GLS aplica carburante + climat protec aparte; el fuel se añade
    /// en el comparador por agencia desde AgenciasTransporte.RecargoCombustible).
    /// </summary>
    public class TarifaGLSBusinessParcel : TarifaNacionalBase
    {
        public override int AgenciaId => 1; // GLS/ASM
        public override byte ServicioId => 96; // BusinessParcel
        public override string NombreServicio => "BusinessParcel";
        public override byte HorarioDefectoId => 18;

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Provincial, 3.10m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Provincial, 3.28m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Provincial, 3.34m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Provincial, 3.56m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Provincial, 4.06m),
            new TramoCosteEnvio(1m, ZonasEnvioAgencia.Peninsular, 3.66m),
            new TramoCosteEnvio(3m, ZonasEnvioAgencia.Peninsular, 3.86m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Peninsular, 4.19m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Peninsular, 4.71m),
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 6.07m),
            // GLS Portugal (oferta ASM_2026.pdf, "GLS PORTUGAL", 24/48 h): hasta 5 kg 13,28 €,
            // hasta 10 kg 14,76 €, kilo adicional 0,88 €. Islas portuguesas (Azores/Madeira) "consultar":
            // no se modelan aquí. GLS cubre Portugal, pero Innovatrans (≈7,61 €) suele ser más barata.
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.Portugal, 13.28m),
            new TramoCosteEnvio(10m, ZonasEnvioAgencia.Portugal, 14.76m)
        };

        protected override IReadOnlyList<TramoCosteEnvio> Tramos => _costeEnvio;

        protected override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            if (zona == ZonasEnvioAgencia.Peninsular)
            {
                return 0.41m;
            }
            if (zona == ZonasEnvioAgencia.Provincial)
            {
                return 0.31m;
            }
            if (zona == ZonasEnvioAgencia.Portugal)
            {
                return 0.88m;
            }
            return decimal.MaxValue;
        }

        protected override decimal CosteReembolso(decimal reembolso) => 1.80m;
    }
}
