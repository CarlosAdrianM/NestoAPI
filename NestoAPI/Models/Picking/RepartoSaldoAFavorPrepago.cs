using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// Novedades 1302 (17/11/26): un pedido de prepago (PRE) del cliente que ya está en un picking anterior y aún no se ha
    /// facturado. Lo lee <see cref="IRellenadorPrepagosService.PedidosPrepagoEnPicking"/>.
    /// </summary>
    public class PedidoPrepagoEnPickingDTO
    {
        public int Pedido { get; set; }
        /// <summary>Total con IVA de sus líneas con picking y sin facturar.</summary>
        public decimal ImporteEnPicking { get; set; }
        /// <summary>Sus prepagos sin factura.</summary>
        public decimal Prepagos { get; set; }
    }

    /// <summary>
    /// Novedades 1302 (17/11/26): la regla pura del saldo a favor de un cliente con varios pedidos de prepago. El saldo a favor
    /// (menos la deuda vencida) se consume UNA sola vez entre sus pedidos PRE: cada uno que sale (o que ya está en un picking sin
    /// facturar) se lleva lo que no cubren sus propios prepagos. Antes cada pedido veía el saldo entero y el mismo pago a cuenta
    /// cubría varios (pedidos 930831 y 930835 del cliente 42004, picking 100105).
    /// </summary>
    public static class ReglaSaldoAFavorPrepago
    {
        /// <summary>Lo que un pedido se lleva del saldo a favor: lo que no cubren sus prepagos (nunca negativo).</summary>
        public static decimal Consumo(decimal importe, decimal prepagos)
        {
            return Math.Max(0, importe - prepagos);
        }

        /// <summary>Lo que consumen del saldo LIBRE los pedidos PRE del cliente que ya están en un picking anterior, sin contar el
        /// propio: lo que no cubren sus prepagos ni el saldo a favor reservado a ellos (<paramref name="reservadoA"/>).</summary>
        public static decimal ConsumoEnPickingsAnteriores(IEnumerable<PedidoPrepagoEnPickingDTO> enPicking, int pedidoActual,
            Func<int, decimal> reservadoA = null)
        {
            return (enPicking ?? Enumerable.Empty<PedidoPrepagoEnPickingDTO>())
                .Where(p => p != null && p.Pedido != pedidoActual)
                .Sum(p => Consumo(p.ImporteEnPicking, p.Prepagos + (reservadoA?.Invoke(p.Pedido) ?? 0)));
        }

        /// <summary>
        /// El saldo a favor neto (saldo − deuda vencida) que les queda libre a este pedido después de lo que consumen los demás.
        /// Nunca negativo: lo de los otros no se come los prepagos propios.
        /// </summary>
        public static decimal SaldoLibre(decimal saldoAFavor, decimal deudaVencida, decimal consumidoPorOtros)
        {
            return Math.Max(0, saldoAFavor - deudaVencida - consumidoPorOtros);
        }

        /// <summary>
        /// Lo que hay para cubrir el pedido: prepagos propios + saldo a favor − deuda vencida − lo que consumen otros pedidos. Sin
        /// consumo de otros es la cuenta de siempre (prepagos + saldo − deuda, que puede quedar por debajo de los prepagos si
        /// la deuda supera el saldo); lo de los otros solo resta de la parte positiva del saldo neto.
        /// </summary>
        public static decimal Disponible(decimal prepagos, decimal saldoAFavor, decimal deudaVencida, decimal consumidoPorOtros)
        {
            decimal saldoNeto = saldoAFavor - deudaVencida;
            decimal consumidoDeVerdad = Math.Min(Math.Max(0, consumidoPorOtros), Math.Max(0, saldoNeto));
            return prepagos + saldoNeto - consumidoDeVerdad;
        }

        /// <summary>Lo que se lleva del saldo libre un pedido que sale: lo que no cubren sus prepagos (y su saldo reservado), como mucho el saldo libre
        /// (si sale por el descuadre permitido no se lleva lo que no había).</summary>
        public static decimal ConsumoAlSalir(decimal total, decimal prepagos, decimal saldoLibre)
        {
            return Math.Min(Consumo(total, prepagos), Math.Max(0, saldoLibre));
        }
    }

    /// <summary>
    /// Novedades 1302 (17/11/26): el acumulador de UNA pasada de picking. Guarda, por cliente, lo que se lleva del saldo a favor
    /// cada pedido PRE que sale y, leído una vez por cliente, lo que consumen sus pedidos PRE en pickings anteriores.
    ///
    /// <para>Orden determinista: un pedido solo ve lo que consumen los de su cliente con número MENOR (el más antiguo primero),
    /// así que da igual cuántas veces se pregunte (ensayo de muestras, «todo ahora», el bucle) ni en qué orden: el bucle de
    /// GestorPicking los recorre por número y es el que decide (registra al que sale y suelta al que no).</para>
    /// </summary>
    public class RepartoSaldoAFavorPrepago
    {
        private readonly Dictionary<string, Dictionary<int, decimal>> consumoEnEstaPasada = new Dictionary<string, Dictionary<int, decimal>>();
        private readonly Dictionary<string, List<PedidoPrepagoEnPickingDTO>> enPickingsAnteriores = new Dictionary<string, List<PedidoPrepagoEnPickingDTO>>();

        private static string Clave(string cliente) => cliente?.Trim() ?? string.Empty;

        /// <summary>Los pedidos PRE del cliente en pickings anteriores (se leen la primera vez y se guardan).</summary>
        public List<PedidoPrepagoEnPickingDTO> EnPickingsAnteriores(string cliente, Func<List<PedidoPrepagoEnPickingDTO>> leer)
        {
            string clave = Clave(cliente);
            if (!enPickingsAnteriores.TryGetValue(clave, out List<PedidoPrepagoEnPickingDTO> lista))
            {
                lista = leer?.Invoke() ?? new List<PedidoPrepagoEnPickingDTO>();
                enPickingsAnteriores[clave] = lista;
            }
            return lista;
        }

        /// <summary>Lo que se llevan en esta pasada los pedidos del cliente con número menor que este.</summary>
        public decimal ConsumidoEnEstaPasadaPorAnteriores(string cliente, int pedido)
        {
            return consumoEnEstaPasada.TryGetValue(Clave(cliente), out Dictionary<int, decimal> porPedido)
                ? porPedido.Where(p => p.Key < pedido).Sum(p => p.Value)
                : 0;
        }

        public void Registrar(string cliente, int pedido, decimal consumo)
        {
            string clave = Clave(cliente);
            if (!consumoEnEstaPasada.TryGetValue(clave, out Dictionary<int, decimal> porPedido))
            {
                porPedido = new Dictionary<int, decimal>();
                consumoEnEstaPasada[clave] = porPedido;
            }
            porPedido[pedido] = consumo;
        }

        public void Liberar(string cliente, int pedido)
        {
            if (consumoEnEstaPasada.TryGetValue(Clave(cliente), out Dictionary<int, decimal> porPedido))
            {
                _ = porPedido.Remove(pedido);
            }
        }

        /// <summary>Lo que lleva registrado ese pedido en esta pasada (0 si nada).</summary>
        public decimal Registrado(string cliente, int pedido)
        {
            return consumoEnEstaPasada.TryGetValue(Clave(cliente), out Dictionary<int, decimal> porPedido)
                && porPedido.TryGetValue(pedido, out decimal consumo) ? consumo : 0;
        }

        /// <summary>
        /// Prepara la pasada: un reparto común para todos los candidatos y una primera vuelta POR NÚMERO para que los ensayos de
        /// antes del bucle (muestras sin stock, «todo ahora») ya vean lo que se llevan los más antiguos del mismo cliente. No marca
        /// RetenidoPorPrepago: eso lo decide el bucle.
        /// </summary>
        public static RepartoSaldoAFavorPrepago Preparar(List<PedidoPicking> candidatos)
        {
            var reparto = new RepartoSaldoAFavorPrepago();
            if (candidatos == null)
            {
                return reparto;
            }
            foreach (PedidoPicking pedido in candidatos.Where(c => c != null))
            {
                pedido.RepartoSaldo = reparto;
            }
            foreach (PedidoPicking pedido in EnOrden(candidatos))
            {
                pedido.EvaluarPrepago();
            }
            return reparto;
        }

        /// <summary>El orden en que se reparte el saldo: por número de pedido, el más antiguo primero.</summary>
        public static List<PedidoPicking> EnOrden(IEnumerable<PedidoPicking> candidatos)
        {
            return (candidatos ?? Enumerable.Empty<PedidoPicking>()).Where(c => c != null).OrderBy(c => c.Id).ToList();
        }
    }
}
