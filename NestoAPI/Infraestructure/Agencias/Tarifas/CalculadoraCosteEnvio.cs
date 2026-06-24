using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Helper REUTILIZABLE de cálculo de coste por tramos de peso y zona (lo usan las tarifas que se
    /// zonifican por tablas, p.ej. las nacionales). NO conoce ninguna agencia: recibe los tramos, el
    /// coste por kilo adicional, la zona ya resuelta y el coste de reembolso ya calculado. El fuel se
    /// aplica al PORTE (no al reembolso), porque las tarifas se guardan ANTES de fuel.
    /// </summary>
    public static class CalculadoraCosteEnvio
    {
        public static decimal CalcularCoste(
            IReadOnlyList<TramoCosteEnvio> tramos,
            Func<ZonasEnvioAgencia, decimal> costeKiloAdicional,
            ZonasEnvioAgencia zona,
            decimal peso,
            decimal recargoCombustible,
            decimal costeReembolso)
        {
            List<TramoCosteEnvio> tramosZona = tramos
                .Where(c => c.Zona == zona)
                .OrderBy(c => c.PesoMaximo)
                .ToList();

            // La tarifa no cubre esta zona: no es una opción válida.
            if (!tramosZona.Any())
            {
                return decimal.MaxValue;
            }

            decimal porte;
            TramoCosteEnvio tramo = tramosZona.FirstOrDefault(c => c.PesoMaximo >= peso);
            if (tramo == null)
            {
                // Por encima del último tramo: último precio + kilos de más * coste por kilo adicional.
                TramoCosteEnvio ultimo = tramosZona.Last();
                porte = ultimo.Precio + ((peso - ultimo.PesoMaximo) * costeKiloAdicional(zona));
            }
            else
            {
                porte = tramo.Precio;
            }

            // Fuel sobre el porte (los precios de la oferta son antes de combustible).
            porte = porte * (1m + recargoCombustible);

            return porte + costeReembolso;
        }
    }
}
