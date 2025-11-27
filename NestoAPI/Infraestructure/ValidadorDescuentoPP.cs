using NestoAPI.Models.PedidosBase;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Validador para detectar diferencias de redondeo en el descuento pronto pago.
    ///
    /// Issue #243: El descuento PP se calcula línea a línea (compatible con BD),
    /// pero esto puede acumular errores de redondeo. Este validador detecta
    /// cuando la diferencia supera un umbral configurable.
    /// </summary>
    public static class ValidadorDescuentoPP
    {
        /// <summary>
        /// Umbral máximo de diferencia aceptable (en euros).
        /// Por defecto 0.02€, que es el umbral usado en el procedimiento de facturación.
        /// </summary>
        public static decimal UmbralDiferenciaMaxima { get; set; } = 0.02m;

        /// <summary>
        /// Calcula la diferencia entre el total calculado línea a línea
        /// y el total calculado sobre la suma de bases imponibles.
        /// </summary>
        /// <typeparam name="T">Tipo de línea de pedido</typeparam>
        /// <param name="pedido">Pedido a validar</param>
        /// <returns>Resultado de la validación con la diferencia detectada</returns>
        public static ResultadoValidacionPP ValidarDescuentoPP<T>(PedidoBase<T> pedido)
            where T : LineaPedidoBase
        {
            if (pedido == null || pedido.Lineas == null || !pedido.Lineas.Any())
            {
                return new ResultadoValidacionPP
                {
                    EsValido = true,
                    DiferenciaDetectada = 0,
                    Mensaje = "Pedido sin líneas"
                };
            }

            if (pedido.DescuentoPP == 0)
            {
                return new ResultadoValidacionPP
                {
                    EsValido = true,
                    DiferenciaDetectada = 0,
                    Mensaje = "Sin descuento PP"
                };
            }

            // Total calculado línea a línea (cómo se hace actualmente)
            decimal totalLineaALinea = pedido.Total;

            // Calcular total aplicando PP sobre la suma de bases imponibles
            decimal sumaBasesImponiblesSinPP = pedido.Lineas.Sum(l => CalcularBaseImponibleSinPP(l, pedido.DescuentoPP));
            decimal importePPSobreTotal = RoundingHelper.DosDecimalesRound(sumaBasesImponiblesSinPP * pedido.DescuentoPP);
            decimal sumaIva = pedido.Lineas.Sum(l => l.ImporteIva);
            decimal sumaRE = pedido.Lineas.Sum(l => l.ImporteRecargoEquivalencia);
            decimal totalSobreSuma = RoundingHelper.DosDecimalesRound(sumaBasesImponiblesSinPP - importePPSobreTotal + sumaIva + sumaRE);

            decimal diferencia = Math.Abs(totalLineaALinea - totalSobreSuma);

            return new ResultadoValidacionPP
            {
                EsValido = diferencia <= UmbralDiferenciaMaxima,
                DiferenciaDetectada = diferencia,
                TotalLineaALinea = totalLineaALinea,
                TotalSobreSuma = totalSobreSuma,
                Mensaje = diferencia > UmbralDiferenciaMaxima
                    ? $"Diferencia de redondeo PP: {diferencia:F2}€ (umbral: {UmbralDiferenciaMaxima:F2}€)"
                    : diferencia > 0
                        ? $"Diferencia menor al umbral: {diferencia:F2}€"
                        : "Sin diferencia de redondeo"
            };
        }

        /// <summary>
        /// Calcula la base imponible de una línea sin aplicar el descuento PP.
        /// Útil para recalcular el total aplicando PP sobre la suma.
        /// </summary>
        private static decimal CalcularBaseImponibleSinPP(LineaPedidoBase linea, decimal descuentoPP)
        {
            if (descuentoPP == 0)
            {
                return linea.BaseImponible;
            }

            // La base imponible actual ya tiene el PP aplicado.
            // Para obtener la base sin PP, dividimos por (1 - PP)
            decimal factorPP = 1 - descuentoPP;
            if (factorPP == 0)
            {
                return 0; // PP del 100%, caso edge
            }

            return RoundingHelper.DosDecimalesRound(linea.BaseImponible / factorPP);
        }
    }

    /// <summary>
    /// Resultado de la validación del descuento PP.
    /// </summary>
    public class ResultadoValidacionPP
    {
        /// <summary>
        /// Indica si la diferencia está dentro del umbral aceptable.
        /// </summary>
        public bool EsValido { get; set; }

        /// <summary>
        /// Diferencia absoluta entre ambos métodos de cálculo.
        /// </summary>
        public decimal DiferenciaDetectada { get; set; }

        /// <summary>
        /// Total calculado sumando líneas redondeadas individualmente.
        /// </summary>
        public decimal TotalLineaALinea { get; set; }

        /// <summary>
        /// Total calculado aplicando PP sobre la suma de bases imponibles.
        /// </summary>
        public decimal TotalSobreSuma { get; set; }

        /// <summary>
        /// Mensaje descriptivo del resultado.
        /// </summary>
        public string Mensaje { get; set; }
    }
}
