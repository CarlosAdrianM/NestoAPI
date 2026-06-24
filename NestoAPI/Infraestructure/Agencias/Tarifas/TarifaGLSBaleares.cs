using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Tarifa GLS/ASM "Insular marítimo" (oferta 2026, ASM_2026.pdf). Portada de Nesto.
    /// Precios ANTES de fuel. Para Canarias se suma un coste aproximado de DUA (20,85); los
    /// DUAS/cabildos reales van aparte.
    /// </summary>
    public class TarifaGLSBaleares : TarifaNacionalBase
    {
        private const decimal CosteDuaCanariasAproximado = 20.85m;

        public override int AgenciaId => 1; // GLS/ASM
        public override byte ServicioId => 6; // Carga / insular marítimo
        public override string NombreServicio => "Insular marítimo";
        public override byte HorarioDefectoId => 10;

        private static readonly IReadOnlyList<TramoCosteEnvio> _costeEnvio = new List<TramoCosteEnvio>
        {
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMayores, 12.51m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.BalearesMenores, 15.18m),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.CanariasMayores, 12.94m + CosteDuaCanariasAproximado),
            new TramoCosteEnvio(5m, ZonasEnvioAgencia.CanariasMenores, 14.78m + CosteDuaCanariasAproximado)
        };

        protected override IReadOnlyList<TramoCosteEnvio> Tramos => _costeEnvio;

        protected override decimal CosteKiloAdicional(ZonasEnvioAgencia zona)
        {
            if (zona == ZonasEnvioAgencia.BalearesMayores)
            {
                return 0.94m;
            }
            if (zona == ZonasEnvioAgencia.BalearesMenores)
            {
                return 1.14m;
            }
            if (zona == ZonasEnvioAgencia.CanariasMayores)
            {
                return 1.00m;
            }
            if (zona == ZonasEnvioAgencia.CanariasMenores)
            {
                return 1.50m;
            }
            return decimal.MaxValue;
        }

        protected override decimal CosteReembolso(decimal reembolso) => 1.80m;
    }
}
