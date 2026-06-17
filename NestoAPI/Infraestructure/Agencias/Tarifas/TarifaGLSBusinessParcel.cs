using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Tarifa GLS/ASM "BusinessParcel" (oferta 2026, ASM_2026.pdf). Portada de Nesto.
    /// Precios ANTES de fuel (GLS aplica carburante + climat protec aparte; el fuel se añade
    /// en el comparador por agencia desde AgenciasTransporte.RecargoCombustible).
    /// </summary>
    public class TarifaGLSBusinessParcel : ITarifaAgencia
    {
        public int AgenciaId => 1; // GLS/ASM
        public byte ServicioId => 96; // BusinessParcel
        public string NombreServicio => "BusinessParcel";
        public byte HorarioDefectoId => 18;

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
            new TramoCosteEnvio(15m, ZonasEnvioAgencia.Peninsular, 6.07m)
        };

        public IReadOnlyList<TramoCosteEnvio> CosteEnvio => _costeEnvio;

        public decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            if (zona == ZonasEnvioAgencia.Peninsular)
            {
                return 0.41m;
            }
            if (zona == ZonasEnvioAgencia.Provincial)
            {
                return 0.31m;
            }
            return decimal.MaxValue;
        }

        public decimal CosteReembolso(decimal reembolso) => 1.80m;
    }
}
