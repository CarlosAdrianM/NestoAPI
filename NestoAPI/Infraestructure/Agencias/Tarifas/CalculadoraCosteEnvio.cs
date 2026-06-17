using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Calcula el coste de un envío para una tarifa concreta. Portado de Nesto
    /// (AgenciasViewModel.CalcularCostoEnvio) al mover el comparador a NestoAPI, AÑADIENDO el
    /// recargo de combustible (fuel) por agencia: se aplica al PORTE (no al reembolso), porque
    /// las tarifas se guardan ANTES de fuel y cada agencia tiene su % (AgenciasTransporte).
    /// </summary>
    public static class CalculadoraCosteEnvio
    {
        public static decimal CalcularCoste(
            ITarifaAgencia tarifa,
            ZonasEnvioAgencia zona,
            decimal peso,
            decimal reembolso,
            decimal recargoCombustible)
        {
            List<TramoCosteEnvio> tramos = tarifa.CosteEnvio
                .Where(c => c.Zona == zona)
                .OrderBy(c => c.PesoMaximo)
                .ToList();

            // La tarifa no cubre esta zona: no es una opción válida.
            if (!tramos.Any())
            {
                return decimal.MaxValue;
            }

            decimal porte;
            TramoCosteEnvio tramo = tramos.FirstOrDefault(c => c.PesoMaximo >= peso);
            if (tramo == null)
            {
                // Por encima del último tramo: último precio + kilos de más * coste por kilo adicional.
                TramoCosteEnvio ultimo = tramos.Last();
                porte = ultimo.Precio + ((peso - ultimo.PesoMaximo) * tarifa.CosteKiloAdicional(zona));
            }
            else
            {
                porte = tramo.Precio;
            }

            // Fuel sobre el porte (los precios de la oferta son antes de combustible).
            porte = porte * (1m + recargoCombustible);

            decimal costoReembolso = reembolso != 0 ? tarifa.CosteReembolso(reembolso) : 0m;
            return porte + costoReembolso;
        }
    }
}
