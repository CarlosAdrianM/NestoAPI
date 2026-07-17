using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// Construye el VerifactuFacturaRequest a partir de una factura recién creada (issue #34).
    /// El desglose por tipo de IVA replica el cálculo de GestorFacturas.LeerFactura
    /// (agrupación por PorcentajeIVA/PorcentajeRE con redondeo AwayFromZero) para que
    /// lo declarado a la AEAT coincida con lo impreso en la factura.
    /// </summary>
    internal static class MapeadorFacturaVerifactu
    {
        internal static VerifactuFacturaRequest Mapear(CabFacturaVta factura)
        {
            if (factura == null)
            {
                throw new ArgumentNullException(nameof(factura));
            }

            ISerieFacturaVerifactu serie = RegistroSeriesVerifactu.ObtenerSerie(factura.Serie);
            if (serie == null || !serie.TramitaVerifactu)
            {
                throw new InvalidOperationException($"La serie '{factura.Serie?.Trim()}' no tramita Verifactu");
            }

            var request = new VerifactuFacturaRequest
            {
                Serie = factura.Serie?.Trim(),
                Numero = NumeroSinSerie(factura.Número, factura.Serie),
                FechaExpedicion = factura.Fecha,
                TipoFactura = serie.TipoFacturaVerifactuPorDefecto,
                Descripcion = serie.DescripcionVerifactu,
                NifDestinatario = factura.CifNif?.Trim(),
                NombreDestinatario = factura.NombreFiscal?.Trim()
            };

            decimal importeTotal = 0;
            var gruposIva = factura.LinPedidoVtas
                .GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE })
                .OrderByDescending(g => g.Key.PorcentajeIVA);
            foreach (var grupo in gruposIva)
            {
                var desglose = new VerifactuDesgloseIva
                {
                    BaseImponible = grupo.Sum(l => l.Base_Imponible),
                    TipoIva = grupo.Key.PorcentajeIVA,
                    CuotaIva = Math.Round(grupo.Sum(l => l.ImporteIVA), 2, MidpointRounding.AwayFromZero),
                    // PorcentajeRE viene de BD como fracción (0.052) y Verifactu espera porcentaje (5.2)
                    TipoRecargoEquivalencia = grupo.Key.PorcentajeRE * 100M,
                    CuotaRecargoEquivalencia = Math.Round(grupo.Sum(l => l.PorcentajeRE * l.Base_Imponible), 2, MidpointRounding.AwayFromZero)
                };
                request.DesgloseIva.Add(desglose);
                importeTotal += desglose.BaseImponible + desglose.CuotaIva + desglose.CuotaRecargoEquivalencia;
            }
            request.ImporteTotal = Math.Round(importeTotal, 2, MidpointRounding.AwayFromZero);

            return request;
        }

        private static string NumeroSinSerie(string numeroFactura, string serie)
        {
            string numero = numeroFactura?.Trim() ?? string.Empty;
            string prefijo = serie?.Trim();
            return !string.IsNullOrEmpty(prefijo) && numero.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
                ? numero.Substring(prefijo.Length)
                : numero;
        }
    }
}
