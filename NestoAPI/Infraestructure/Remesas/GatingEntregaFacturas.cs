using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// Gating de entrega (#172, refinado por Carlos 21/07/26): ¿ha entregado ya la agencia lo
    /// que se factura? Vivía dentro de <see cref="SelectorEfectosCobrables"/> (#332) y se saca
    /// aquí, SIN cambiar la regla, para que lo consuman todos los que cobran o reclaman una
    /// factura: la remesa SEPA, la de tarjetas (#181) y el aviso de facturas vencidas (#534).
    /// No reimplementar por canal: si la regla cambia, cambia para todos.
    ///
    /// Una factura se retiene si ALGÚN envío de los pedidos de la factura no está entregado
    /// (pedidos parciales: TODOS entregados). Cadena: factura → LinPedidoVta.[Nº Factura] →
    /// Número (pedido) → EnviosAgencia.Pedido → Estado. Sin envíos = se libera. Matices:
    /// - Envíos ANTERIORES a la fecha de corte del poll de seguimiento: sin seguimiento posible
    ///   ('tramitado' eterno, caso NV2515520 de sept/2025) → NO retienen. La señal correcta es
    ///   la fecha de corte, no un timeout de N días.
    /// - Envíos posteriores al corte sin entregar: retienen SIN timeout (el poll los sigue; si
    ///   no confirma, puede estar perdido o en reparto — no liberar).
    /// - INCIDENTADO: retiene siempre, con su motivo.
    /// - DEVUELTO: retiene siempre — la mercancía volvió; salida manual (abono / corregir el envío).
    /// - Fallo 20/08/26 (caso 3028653): solo cuentan los envíos de agencias CON SEGUIMIENTO (las
    ///   que el poll actualiza hasta Entregado: ASM, Innovatrans...). Un envío de Correos Express
    ///   u otra agencia sin integración se queda en 'tramitado' PARA SIEMPRE y retenía el efecto
    ///   eternamente.
    /// </summary>
    public class GatingEntregaFacturas
    {
        private readonly NVEntities db;
        private readonly Func<int[]> leerAgenciasConSeguimiento;

        public GatingEntregaFacturas(NVEntities db, Func<int[]> leerAgenciasConSeguimiento = null)
        {
            this.db = db;
            // Fallo 20/08/26: el gating solo puede mirar agencias cuyo estado SÍ actualizamos
            // (las del poll de seguimiento). Inyectable para tests.
            this.leerAgenciasConSeguimiento = leerAgenciasConSeguimiento
                ?? (() => new Agencias.FabricaAgenciasRemotas(db).AgenciasConSeguimiento.ToArray());
        }

        /// <summary>
        /// Para cada factura (Nº de documento, sin relleno) que tiene envíos de agencia SIN
        /// entregar, el peor estado de esos envíos. Las facturas que no aparecen en el
        /// diccionario están liberadas (entregadas, sin envíos o sin seguimiento posible).
        /// </summary>
        public async Task<Dictionary<string, short>> PeorEstadoSinEntregarPorFactura(string empresa, IEnumerable<string> facturas)
        {
            List<string> documentos = (facturas ?? Enumerable.Empty<string>())
                .Select(d => d?.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .ToList();
            if (!documentos.Any())
            {
                return new Dictionary<string, short>();
            }

            var facturaPedidos = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && documentos.Contains(l.Nº_Factura))
                .Select(l => new { l.Nº_Factura, Pedido = l.Número })
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            List<int> pedidos = facturaPedidos.Select(fp => fp.Pedido).Distinct().ToList();
            DateTime corteSeguimiento = SeguimientoEnviosJobsService.FECHA_CORTE;
            int[] agenciasConSeguimiento = leerAgenciasConSeguimiento();
            var enviosNoEntregados = await db.EnviosAgencias
                .Where(ea => ea.Pedido != null && pedidos.Contains(ea.Pedido.Value)
                    && agenciasConSeguimiento.Contains(ea.Agencia)
                    && ea.Estado != Constantes.Agencias.ESTADO_ENTREGADO
                    && ea.Fecha >= corteSeguimiento)
                .Select(ea => new { Pedido = ea.Pedido.Value, ea.Estado })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<int, short> peorEstadoPorPedido = enviosNoEntregados
                .GroupBy(e => e.Pedido)
                .ToDictionary(g => g.Key, g => g.Max(x => (short)x.Estado));

            Dictionary<string, short> resultado = new Dictionary<string, short>();
            foreach (var factura in facturaPedidos.GroupBy(fp => fp.Nº_Factura?.Trim()))
            {
                if (factura.Key == null)
                {
                    continue;
                }
                List<short> estadosEnvios = factura.Select(x => x.Pedido).Distinct()
                    .Where(p => peorEstadoPorPedido.ContainsKey(p))
                    .Select(p => peorEstadoPorPedido[p])
                    .ToList();
                if (estadosEnvios.Any())
                {
                    resultado[factura.Key] = estadosEnvios.Max();
                }
            }
            return resultado;
        }

        /// <summary>Motivo de retención para el peor estado de los envíos sin entregar.</summary>
        public static string MotivoRetencion(short peorEstado)
        {
            return peorEstado >= Constantes.Agencias.ESTADO_DEVUELTO
                ? "Retenido: envío DEVUELTO — el cobro no procede por remesa; requiere abono o gestión manual."
                : peorEstado >= Constantes.Agencias.ESTADO_INCIDENTADO
                    ? "Retenido: envío INCIDENTADO — esperar a que se resuelva la incidencia (#172)."
                    : "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172).";
        }

        /// <summary>
        /// Fallo 20/08/26: la retención por entrega pendiente o incidencia se puede FORZAR desde
        /// la remesa (el usuario confirma que quiere girarlo igual); el DEVUELTO no — la
        /// mercancía volvió y ese cobro no procede.
        /// </summary>
        public static bool EsForzable(short peorEstado) => peorEstado < Constantes.Agencias.ESTADO_DEVUELTO;
    }
}
