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
        /// <param name="esOssPorPais">NestoAPI#347: veredicto OSS según ParámetrosIVA.Pais
        /// (true = país extranjero, false = ES). Null = sin país persistido: se infiere con la
        /// lista blanca de códigos nacionales.</param>
        internal static VerifactuFacturaRequest Mapear(CabFacturaVta factura,
            System.Collections.Generic.List<VerifactuFacturaRectificada> facturasRectificadas = null,
            bool? esOssPorPais = null)
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
            // NestoAPI#347: la señal principal de venta OSS es el país persistido en
            // ParámetrosIVA para el código de IVA de la cabecera (si viene informado, manda).
            // Fallback: código fuera de la lista blanca nacional (I22, B21...). El porcentaje
            // solo NO basta: Bélgica, Países Bajos o Chequia también tienen el 21%. El tipo
            // extranjero (22, 23...) queda como último respaldo por si el código no se reconoce.
            bool facturaConCodigoOss = esOssPorPais ?? EsCodigoIvaExtranjero(factura.IVA);
            var gruposIva = factura.LinPedidoVtas
                .GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE })
                .OrderByDescending(g => g.Key.PorcentajeIVA);
            foreach (var grupo in gruposIva)
            {
                // NestoAPI#347: una venta OSS tributa el IVA del país de destino y la AEAT la
                // exige declarada como NO SUJETA por localización (N2, clave régimen 17),
                // PROHIBIENDO informar tipo y cuota (validaciones §15.4): el IVA extranjero se
                // liquida por el modelo 369, no viaja a Verifactu. Contrato del ejemplo OSS
                // oficial de Verifacti: la línea lleva solo base + clave_regimen +
                // calificacion_operacion, y el importe_total va SIN la cuota extranjera.
                if (facturaConCodigoOss || EsTipoIvaExtranjero(grupo.Key.PorcentajeIVA))
                {
                    var desgloseOss = new VerifactuDesgloseIva
                    {
                        BaseImponible = grupo.Sum(l => l.Base_Imponible),
                        ClaveRegimen = CLAVE_REGIMEN_OSS,
                        CalificacionOperacion = CALIFICACION_NO_SUJETA_LOCALIZACION
                    };
                    request.DesgloseIva.Add(desgloseOss);
                    importeTotal += desgloseOss.BaseImponible;
                    continue;
                }

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

        /// <summary>NestoAPI#347: clave de régimen AEAT (L8) para operaciones OSS/IOSS.</summary>
        internal const string CLAVE_REGIMEN_OSS = "17";

        /// <summary>NestoAPI#347: calificación AEAT (L9) "no sujeta por reglas de localización".</summary>
        internal const string CALIFICACION_NO_SUJETA_LOCALIZACION = "N2";

        /// <summary>
        /// Tipos de IVA españoles que la AEAT admite con impuesto=01 (mensaje literal del rechazo:
        /// "el campo tipo_impositivo debe ser 0, 2, 4, 5, 7.5, 10 o 21"). Cualquier otro tipo en
        /// las líneas es IVA extranjero de una venta OSS.
        /// </summary>
        private static readonly decimal[] TIPOS_IVA_ESPANOLES = { 0M, 2M, 4M, 5M, 7.5M, 10M, 21M };

        internal static bool EsTipoIvaExtranjero(decimal porcentajeIva)
        {
            return !TIPOS_IVA_ESPANOLES.Contains(porcentajeIva);
        }

        /// <summary>
        /// Códigos de IVA cliente NACIONALES de ParametrosIVA (régimen español: general, reducido,
        /// recargo de equivalencia, exento, exportación, importación, intracomunitario B2B e
        /// históricos). Cualquier otro código de la cabecera es un país OSS (I22 Italia, B21
        /// Bélgica, P23 Portugal...): la parrilla de países crece con los marketplaces, la
        /// nacional no, por eso la lista blanca es la nacional.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> CODIGOS_IVA_NACIONALES =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "G21", "R10", "SR", "RE", "E52", "E14", "EX", "EXP", "IM", "IN", "GN", "G18", "RD8"
            };

        /// <summary>
        /// NestoAPI#347: ¿el código de IVA de la cabecera es de un país OSS? Sin código (null o
        /// vacío) se asume nacional: el respaldo por tipo extranjero sigue cazando el desglose.
        /// </summary>
        internal static bool EsCodigoIvaExtranjero(string codigoIvaCabecera)
        {
            string codigo = codigoIvaCabecera?.Trim();
            return !string.IsNullOrEmpty(codigo) && !CODIGOS_IVA_NACIONALES.Contains(codigo);
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
