using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Sugerencia 1340: un pendiente con el que se junta el pedido y su propio cálculo del motor de #606.</summary>
    public class CalculoPendienteDelCliente
    {
        public PendienteDelCliente Pendiente { get; set; }
        /// <summary>Su fecha de entrega a la agencia, calculada como la de cualquier pedido (con SU modo). Null = no se ha podido calcular.</summary>
        public FechaEntregaAgenciaDTO Calculo { get; set; }
    }

    /// <summary>
    /// Sugerencia 1340 (decisión 6 de Carlos, 18/11/26): la fecha que se dice al cliente de un pedido en modo 6, pura. El pedido se calcula
    /// como «Todo junto» y cada pendiente con el que se junta, con su propio cálculo; sale el día del PEOR (sin fecha si alguno no la tiene) y
    /// se explica: «Sale el lunes 30/11/2026, junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238).» Como
    /// cada cálculo se hace en el momento, la fecha se mueve sola si cambia la compra, y la promesa en riesgo (#693) la ve como la de
    /// cualquier pedido.
    /// <para>Si es el pedido el que va más tarde, lo pendiente no le espera (solo el 6 espera): lo pendiente puede salir antes por su cuenta
    /// y se dice así.</para>
    /// </summary>
    public static class ReglaFechaJuntoConPendiente
    {
        public const string MOTIVO_NADA_PENDIENTE = "El cliente ya no tiene nada pendiente para esta dirección, así que sale como «Todo junto».";

        private static readonly string MODO_TODO_JUNTO = $"Modo «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.TODO_JUNTO)}»:";
        private static readonly string MODO_JUNTO = $"Modo «{Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE)}»:";

        /// <param name="propio">El cálculo del pedido como «Todo junto».</param>
        /// <param name="pendientes">Con lo que se junta (ya filtrado por <see cref="ReglaJuntoConPendiente"/>) y su cálculo.</param>
        public static FechaEntregaAgenciaDTO Combinar(FechaEntregaAgenciaDTO propio, IEnumerable<CalculoPendienteDelCliente> pendientes)
        {
            if (propio == null)
            {
                return null;
            }
            // El motor lo ha calculado como «Todo junto» (sale entero): se dice con el nombre de su modo.
            propio.Motivo = propio.Motivo?.Replace(MODO_TODO_JUNTO, MODO_JUNTO);
            List<CalculoPendienteDelCliente> conCalculo = (pendientes ?? Enumerable.Empty<CalculoPendienteDelCliente>())
                .Where(p => p?.Pendiente != null && p.Calculo != null)
                .ToList();
            if (!conCalculo.Any())
            {
                propio.Motivo = Juntar(propio.Motivo, MOTIVO_NADA_PENDIENTE);
                return propio;
            }

            DateTime? fechaPropia = propio.FechaEntregaAgencia?.Date;
            // El peor de lo pendiente: el que no tiene fecha y, si todos la tienen, el que sale más tarde.
            CalculoPendienteDelCliente peor = conCalculo
                .OrderBy(p => p.Calculo.FechaEntregaAgencia.HasValue ? 1 : 0)
                .ThenByDescending(p => p.Calculo.FechaEntregaAgencia ?? DateTime.MaxValue)
                .ThenBy(p => p.Pendiente.Pedido)
                .First();
            DateTime? fechaPeor = peor.Calculo.FechaEntregaAgencia?.Date;
            bool algunoSinFecha = !fechaPropia.HasValue || conCalculo.Any(p => !p.Calculo.FechaEntregaAgencia.HasValue);
            DateTime? fecha = algunoSinFecha ? (DateTime?)null : new[] { fechaPropia.Value, fechaPeor.Value }.Max();
            bool loRetrasaLoPendiente = fechaPropia.HasValue && (!fechaPeor.HasValue || fechaPeor.Value > fechaPropia.Value);

            var junto = new JuntoConPendienteFechaDTO
            {
                Pedidos = conCalculo.Select(p => p.Pendiente.Pedido).ToList(),
                Documentos = ReglaJuntoConPendiente.Documentos(conCalculo.Select(p => p.Pendiente)),
                Fecha = fecha,
                FechaPropia = fechaPropia,
                LoRetrasaLoPendiente = loRetrasaLoPendiente
            };
            if (loRetrasaLoPendiente)
            {
                PartidaFechaEntregaAgenciaDTO partida = PartidaQueRetrasa(peor.Calculo);
                junto.DocumentoQueRetrasa = ReglaJuntoConPendiente.Documento(peor.Pendiente);
                junto.Producto = partida?.Producto?.Trim();
                junto.NombreProducto = Nombre(peor.Pendiente, junto.Producto);
                junto.Certeza = partida?.Certeza;
            }
            junto.Texto = Texto(junto, conCalculo);

            propio.FechaEntregaAgencia = fecha;
            propio.EntregaCompleta = fecha;
            propio.PrimeraEntrega = fecha;
            propio.Entregas = fecha.HasValue ? new List<DateTime> { fecha.Value } : new List<DateTime>();
            propio.Aplica = FechaEntregaAgenciaDTO.APLICA_COMPLETA;
            propio.Motivo = Juntar(propio.Motivo, junto.Texto);
            propio.JuntoConPendiente = junto;
            return propio;
        }

        /// <summary>La frase, de usted.</summary>
        internal static string Texto(JuntoConPendienteFechaDTO junto, List<CalculoPendienteDelCliente> conCalculo)
        {
            if (junto.LoRetrasaLoPendiente)
            {
                string espera = string.IsNullOrWhiteSpace(junto.Producto)
                    ? "que espera a que llegue lo que le falta"
                    : string.IsNullOrWhiteSpace(junto.NombreProducto)
                        ? $"que espera el {junto.Producto}"
                        : $"que espera el {junto.NombreProducto} ({junto.Producto})";
                return junto.Fecha.HasValue
                    ? $"Sale el {GestorPresupuestos.DiaCorreo(junto.Fecha.Value)}, junto con {junto.DocumentoQueRetrasa}, {espera}."
                    : $"Saldrá junto con {junto.DocumentoQueRetrasa}, {espera} y todavía no tiene fecha.";
            }
            if (!junto.FechaPropia.HasValue)
            {
                return $"Saldrá junto con {junto.Documentos} cuando el pedido esté entero (todavía no tiene fecha).";
            }
            // El pedido es el que va más tarde: lo pendiente que pueda salir antes no le espera.
            List<CalculoPendienteDelCliente> antes = conCalculo
                .Where(p => p.Calculo.FechaEntregaAgencia.HasValue && p.Calculo.FechaEntregaAgencia.Value.Date < junto.FechaPropia.Value)
                .ToList();
            if (!antes.Any())
            {
                return $"Sale el {GestorPresupuestos.DiaCorreo(junto.FechaPropia.Value)}, junto con {junto.Documentos}.";
            }
            string documentosAntes = ReglaJuntoConPendiente.Documentos(antes.Select(p => p.Pendiente));
            return $"Sale el {GestorPresupuestos.DiaCorreo(junto.FechaPropia.Value)}, cuando el pedido esté entero; lo que el cliente tiene pendiente " +
                $"({documentosAntes}) puede salir antes por su cuenta, porque no espera a este pedido.";
        }

        /// <summary>La partida que pone la fecha de ese pendiente: la que no tiene fecha o, si todas la tienen, la que sale más tarde.</summary>
        internal static PartidaFechaEntregaAgenciaDTO PartidaQueRetrasa(FechaEntregaAgenciaDTO calculo)
        {
            return (calculo?.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto))
                .OrderBy(p => p.PuedeSalirEl.HasValue ? 1 : 0)
                .ThenByDescending(p => p.PuedeSalirEl ?? DateTime.MaxValue)
                .FirstOrDefault();
        }

        private static string Nombre(PendienteDelCliente pendiente, string producto)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }
            return (pendiente?.Lineas ?? new List<LineaPendienteDelCliente>())
                .Where(l => l != null && string.Equals(l.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(l.Texto))
                .Select(l => l.Texto.Trim())
                .FirstOrDefault();
        }

        private static string Juntar(string motivo, string frase)
            => string.IsNullOrWhiteSpace(motivo) ? frase : motivo.Trim() + " " + frase;
    }
}
