using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// Novedades 1302 (Carlos, 17/11/26): un pago es de UN documento. Cada apunte de saldo a favor (ImportePdte &lt; 0) se mira
    /// por su concepto (y su nº de documento): si nombra un pedido o una factura DEL MISMO CLIENTE, ese saldo queda reservado a
    /// él. Reservado a un pedido = solo cuenta para ese pedido (aunque no salga o ya esté facturado); reservado a una factura =
    /// no cubre ningún pedido. Lo que no nombra nada del cliente (fechas, importes, pedidos de otro cliente) es saldo libre.
    /// Puro: los pedidos y facturas del cliente los lee el rellenador.
    /// </summary>
    public static class ClasificadorSaldoAFavor
    {
        private static readonly Regex DIGITOS = new Regex(@"\d+", RegexOptions.Compiled);
        private static readonly Regex REFERENCIAS = new Regex(@"[A-Za-z0-9]+", RegexOptions.Compiled);

        /// <summary>Los números del texto, en orden («S/Pago a cuenta a Paloma Pedido 930831» → 930831). Los de más de 9 cifras, fuera.</summary>
        public static List<int> Numeros(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return new List<int>();
            }
            return DIGITOS.Matches(texto).Cast<Match>()
                .Where(m => m.Value.Length <= 9)
                .Select(m => int.Parse(m.Value))
                .ToList();
        }

        /// <summary>Las palabras con alguna cifra, en mayúsculas y en orden («Factura NV2617724» → NV2617724): candidatas a factura.</summary>
        public static List<string> Referencias(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return new List<string>();
            }
            return REFERENCIAS.Matches(texto).Cast<Match>()
                .Select(m => m.Value.ToUpperInvariant())
                .Where(r => r.Any(char.IsDigit))
                .ToList();
        }

        private static IEnumerable<ExtractoClienteDTO> SaldosAFavor(IEnumerable<ExtractoClienteDTO> extractos)
            => (extractos ?? Enumerable.Empty<ExtractoClienteDTO>()).Where(e => e != null && e.importePendiente < 0);

        /// <summary>Los números que podrían ser pedidos (de concepto y documento de los saldos a favor), para una sola consulta.</summary>
        public static HashSet<int> PedidosCandidatos(IEnumerable<ExtractoClienteDTO> extractos)
        {
            return new HashSet<int>(SaldosAFavor(extractos).SelectMany(e => Numeros(e.concepto).Concat(Numeros(e.documento))));
        }

        /// <summary>Las referencias que podrían ser facturas (solo del concepto), para una sola consulta.</summary>
        public static HashSet<string> FacturasCandidatas(IEnumerable<ExtractoClienteDTO> extractos)
        {
            return new HashSet<string>(SaldosAFavor(extractos).SelectMany(e => Referencias(e.concepto)));
        }

        /// <summary>
        /// Marca <see cref="ExtractoClienteDTO.PedidoReservado"/> o <see cref="ExtractoClienteDTO.FacturaReservada"/> en cada saldo
        /// a favor. Orden: el primer pedido del cliente que nombra el concepto; si no, el del nº de documento; si no, la primera
        /// factura del cliente que nombra el concepto. El nº de documento no se mira como factura: el de un abono es el propio
        /// abono, no el documento al que se paga.
        /// </summary>
        public static void Clasificar(IEnumerable<ExtractoClienteDTO> extractos, ICollection<int> pedidosDelCliente, ICollection<string> facturasDelCliente)
        {
            var pedidos = new HashSet<int>(pedidosDelCliente ?? new List<int>());
            var facturas = new HashSet<string>((facturasDelCliente ?? new List<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().ToUpperInvariant()));
            foreach (ExtractoClienteDTO extracto in SaldosAFavor(extractos))
            {
                extracto.PedidoReservado = null;
                extracto.FacturaReservada = null;
                int? pedido = Numeros(extracto.concepto).Concat(Numeros(extracto.documento))
                    .Where(pedidos.Contains).Select(n => (int?)n).FirstOrDefault();
                if (pedido.HasValue)
                {
                    extracto.PedidoReservado = pedido;
                    continue;
                }
                // Un abono o rectificativa suele nombrarse a sí mismo («Efecto 1/1 factura RV2600154»): su propio documento no
                // lo reserva (sería reservarlo a sí mismo y dejaría de cubrir pedidos, como hacía antes).
                string propio = extracto.documento?.Trim().ToUpperInvariant();
                extracto.FacturaReservada = Referencias(extracto.concepto).FirstOrDefault(f => facturas.Contains(f) && f != propio);
            }
        }

        public static bool EsLibre(ExtractoClienteDTO extracto)
            => extracto != null && !extracto.PedidoReservado.HasValue && string.IsNullOrEmpty(extracto.FacturaReservada);

        /// <summary>El saldo a favor reservado a ese pedido (positivo).</summary>
        public static decimal ReservadoA(IEnumerable<ExtractoClienteDTO> extractos, int pedido)
            => -SaldosAFavor(extractos).Where(e => e.PedidoReservado == pedido).Sum(e => e.importePendiente);

        /// <summary>El saldo a favor libre (sin documento del cliente; positivo).</summary>
        public static decimal Libre(IEnumerable<ExtractoClienteDTO> extractos)
            => -SaldosAFavor(extractos).Where(EsLibre).Sum(e => e.importePendiente);
    }
}
