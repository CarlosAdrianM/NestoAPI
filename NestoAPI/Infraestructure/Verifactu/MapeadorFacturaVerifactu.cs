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
        internal static VerifactuFacturaRequest Mapear(CabFacturaVta factura,
            System.Collections.Generic.List<VerifactuFacturaRectificada> facturasRectificadas = null)
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

            // Issue #325: las ventas a consumidor final se agrupan en clientes ficticios
            // ("FACT. SIMP. VENTAS AMAZON", NIF "NV"...). Son facturas SIMPLIFICADAS: en Verifactu
            // van como F2 y SIN destinatario (art. 6.1.d RD 1619/2012). Enviarlas como F1 con ese
            // NIF ficticio hacía que la AEAT las rechazara (26 rechazos en las primeras horas de
            // la fase en sombra).
            bool esSimplificada = EsFacturaSimplificada(factura);

            var request = new VerifactuFacturaRequest
            {
                Serie = factura.Serie?.Trim(),
                Numero = NumeroSinSerie(factura.Número, factura.Serie),
                FechaExpedicion = factura.Fecha,
                TipoFactura = esSimplificada ? TIPO_FACTURA_SIMPLIFICADA : TipoFactura(serie, factura),
                Descripcion = serie.DescripcionVerifactu,
                NifDestinatario = esSimplificada ? null : factura.CifNif?.Trim(),
                NombreDestinatario = esSimplificada ? null : factura.NombreFiscal?.Trim()
            };

            // Issue #36: nuestras rectificativas son abonos con los importes en negativo, que en
            // Verifactu es la rectificativa "por diferencias" (I). Contrato verificado contra los
            // ejemplos oficiales de Verifacti el 20/07/26: líneas e importe_total en negativo, SIN
            // importe_rectificativa (eso es solo para las de sustitución) y con las facturas
            // rectificadas identificadas.
            if (serie.EsRectificativa)
            {
                request.TipoRectificacion = "I";
                request.FacturasRectificadas = facturasRectificadas
                    ?? new System.Collections.Generic.List<VerifactuFacturaRectificada>();
            }

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

        /// <summary>Tipo AEAT de la factura simplificada / sin identificación del destinatario.</summary>
        internal const string TIPO_FACTURA_SIMPLIFICADA = "F2";

        /// <summary>
        /// Límite legal de importe de una factura simplificada (art. 4 RD 1619/2012). Por encima,
        /// la operación NO puede documentarse con factura simplificada.
        /// </summary>
        internal const decimal LIMITE_FACTURA_SIMPLIFICADA = 400M;

        /// <summary>
        /// Issue #325: ¿es una factura simplificada (venta a consumidor final sin destinatario
        /// identificado)? Se reconoce por el cliente: las ventas de Amazon, tienda online y tienda
        /// física se agrupan en clientes ficticios con NIF que no existe en el censo de la AEAT.
        /// </summary>
        internal static bool EsFacturaSimplificada(CabFacturaVta factura)
        {
            string cliente = factura?.Nº_Cliente?.Trim();
            return cliente == Constantes.ClientesEspeciales.AMAZON
                || cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE
                || cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL;
        }

        /// <summary>
        /// Issue #36: el tipo AEAT de una rectificativa (R1-R5) sale del TipoRectificativa
        /// persistido en la factura si existe; si no, del defecto de la serie (R1). Hoy Nesto
        /// aún no rellena el campo (la UI por causa es Nesto#244), así que aplica el defecto.
        /// </summary>
        internal static string TipoFactura(ISerieFacturaVerifactu serie, CabFacturaVta factura)
        {
            if (!serie.EsRectificativa)
            {
                return serie.TipoFacturaVerifactuPorDefecto;
            }
            string tipo = factura.TipoRectificativa?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(tipo) ? serie.TipoFacturaVerifactuPorDefecto : tipo;
        }

        internal static string NumeroSinSerie(string numeroFactura, string serie)
        {
            string numero = numeroFactura?.Trim() ?? string.Empty;
            string prefijo = serie?.Trim();
            return !string.IsNullOrEmpty(prefijo) && numero.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
                ? numero.Substring(prefijo.Length)
                : numero;
        }
    }
}
