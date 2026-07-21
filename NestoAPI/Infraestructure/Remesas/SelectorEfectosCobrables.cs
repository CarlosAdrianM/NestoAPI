using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// NestoAPI#332 (diseñado el 21/07/26 junto a #181): selector de efectos cobrables =
    /// NÚCLEO COMÚN (cartera, pendiente, vencido, gating de entrega #172, puerta de neteo)
    /// + ESTRATEGIA por medio de cobro. La estrategia SEPA exige CCC; la de tarjeta (#181)
    /// exigirá FormaPago=TAR + CCC vacío + token activo y CONSUMIRÁ ESTE MISMO selector:
    /// el gating y el neteo no deben reimplementarse por canal.
    /// Este es también el "modo simulación" de #332: devuelve TODO lo que está en juego,
    /// con motivo, sin tocar nada.
    /// </summary>
    public class SelectorEfectosCobrables
    {
        // Timeout fallback del gating (#172, diseño del 20/07: "si no llega confirmación tras
        // N días, liberar igualmente — sin esto un fallo de seguimiento bloquea tesorería
        // para siempre"). Caso real que lo exige (21/07): efecto de una factura de sept/2025
        // con envíos en 'tramitado' eterno por ser anteriores al poll de seguimiento. Un
        // envío de hace más de N días sin confirmar ya no es señal fiable de "no entregado".
        public const int DIAS_TIMEOUT_GATING = 30;

        private readonly NVEntities db;

        public SelectorEfectosCobrables(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>
        /// Candidatos SEPA (recibo bancario): cartera pendiente y vencida con CCC.
        /// Preseleccionado = entra; con Motivo = retenido/queda fuera; ClienteConNegativos =
        /// requiere pasar por la puerta de revisión (liquidar antes de remesar, #333).
        /// </summary>
        public async Task<List<EfectoCandidatoDTO>> CandidatosSepa(string empresa, DateTime? hoy = null)
        {
            DateTime fechaHoy = (hoy ?? DateTime.Today).Date;

            // NÚCLEO COMÚN: cartera (TipoApunte 2), algo pendiente, fecha anterior a hoy
            // (margen para lo facturado hoy, criterio Carlos 20/07) y vencimiento cumplido.
            // ESTRATEGIA SEPA: con CCC (el char relleno de espacios equivale a '' en SQL).
            List<ExtractoCliente> efectos = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa
                    && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.CARTERA
                    && e.ImportePdte != 0
                    && e.CCC != null && e.CCC != ""
                    && e.Fecha < fechaHoy
                    && e.FechaVto != null && e.FechaVto <= fechaHoy
                    && e.ImportePdte > 0)
                .OrderBy(e => e.Número).ThenBy(e => e.FechaVto)
                .ToListAsync().ConfigureAwait(false);

            if (!efectos.Any())
            {
                return new List<EfectoCandidatoDTO>();
            }

            // Gating de entrega (#172): un efecto se retiene si ALGÚN envío de agencia de los
            // pedidos de su factura no está entregado (pedidos parciales: se exigen TODOS
            // entregados). Cadena verificada 20/07: factura → LinPedidoVta.[Nº Factura] →
            // Número (pedido) → EnviosAgencia.Pedido → Estado. Sin envíos (mostrador,
            // servicios) = se libera, preservando la política actual.
            List<string> documentos = efectos.Select(e => e.Nº_Documento?.Trim())
                .Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
            var facturaPedidos = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && documentos.Contains(l.Nº_Factura))
                .Select(l => new { l.Nº_Factura, Pedido = l.Número })
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            List<int> pedidos = facturaPedidos.Select(fp => fp.Pedido).Distinct().ToList();
            DateTime limiteTimeoutGating = fechaHoy.AddDays(-DIAS_TIMEOUT_GATING);
            var enviosNoEntregados = await db.EnviosAgencias
                .Where(ea => ea.Pedido != null && pedidos.Contains(ea.Pedido.Value)
                    && ea.Estado != Constantes.Agencias.ESTADO_ENTREGADO
                    && ea.Fecha >= limiteTimeoutGating)
                .Select(ea => new { Pedido = ea.Pedido.Value, ea.Estado })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, List<int>> pedidosPorFactura = facturaPedidos
                .GroupBy(fp => fp.Nº_Factura?.Trim())
                .ToDictionary(g => g.Key, g => g.Select(x => x.Pedido).Distinct().ToList());
            HashSet<int> pedidosRetenidos = new HashSet<int>(enviosNoEntregados.Select(e => e.Pedido));

            // Puerta de neteo (#332): clientes de la selección con movimientos NEGATIVOS
            // pendientes (abonos, pagos a cuenta) de cualquier tipo → revisar/liquidar antes.
            List<string> clientes = efectos.Select(e => e.Número?.Trim()).Distinct().ToList();
            List<string> clientesConNegativos = await db.ExtractosCliente
                .Where(e => e.Empresa == empresa && clientes.Contains(e.Número.Trim())
                    && e.ImportePdte < 0)
                .Select(e => e.Número.Trim())
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            HashSet<string> conNegativos = new HashSet<string>(clientesConNegativos, StringComparer.OrdinalIgnoreCase);

            return efectos.Select(e =>
            {
                string documento = e.Nº_Documento?.Trim();
                bool retenidoPorEntrega = documento != null
                    && pedidosPorFactura.TryGetValue(documento, out List<int> pedidosFactura)
                    && pedidosFactura.Any(p => pedidosRetenidos.Contains(p));
                string cliente = e.Número?.Trim();
                return new EfectoCandidatoDTO
                {
                    Id = e.Nº_Orden,
                    Cliente = cliente,
                    Contacto = e.Contacto?.Trim(),
                    Documento = documento,
                    Efecto = e.Efecto?.Trim(),
                    Fecha = e.Fecha,
                    Vencimiento = e.FechaVto,
                    ImportePendiente = e.ImportePdte,
                    Ccc = e.CCC?.Trim(),
                    ClienteConNegativos = conNegativos.Contains(cliente ?? string.Empty),
                    Preseleccionado = !retenidoPorEntrega,
                    Motivo = retenidoPorEntrega
                        ? "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172)."
                        : null
                };
            }).ToList();
        }
    }
}
