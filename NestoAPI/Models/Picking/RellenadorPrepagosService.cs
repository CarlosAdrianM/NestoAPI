using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class RellenadorPrepagosService : IRellenadorPrepagosService
    {
        NVEntities db = new NVEntities();

        public string CorreoUsuario(string usuario)
        {
            if (usuario == null)
            {
                return String.Empty;
            }
            string usuarioSinDominio = usuario.Contains("\\") ? usuario.Substring(usuario.IndexOf("\\") + 1).Trim() : usuario;
            return db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == usuarioSinDominio && p.Clave == Parametros.Claves.CorreoDefecto)?.Valor;
        }

        public List<ExtractoClienteDTO> ExtractosPendientes(int pedido)
        {
            var cliente = db.CabPedidoVtas.First(p => p.Número == pedido).Nº_Cliente;
            List<ExtractoClienteDTO> extractos = db.ExtractosCliente.Where(e => e.ImportePdte != 0 && e.Número == cliente)
                .Select(e => new ExtractoClienteDTO {
                    importePendiente = e.ImportePdte,
                    estado = e.Estado,
                    vencimiento = e.FechaVto,
                    documento = e.Nº_Documento,
                    concepto = e.Concepto
                })
                .ToList();
            MarcarReembolsosPendientes(extractos, PedidosConReembolsoPendiente(extractos));
            ClasificarSaldosAFavor(cliente, extractos);
            return extractos;
        }

        /// <summary>
        /// Novedades 1302: de los números que nombran los saldos a favor, cuáles son pedidos y cuáles facturas de ESE cliente (una
        /// consulta para cada cosa, sea cual sea el número de apuntes), y la clasificación pura de <see cref="ClasificadorSaldoAFavor"/>.
        /// </summary>
        private void ClasificarSaldosAFavor(string cliente, List<ExtractoClienteDTO> extractos)
        {
            List<int> numeros = ClasificadorSaldoAFavor.PedidosCandidatos(extractos).ToList();
            List<string> referencias = ClasificadorSaldoAFavor.FacturasCandidatas(extractos).ToList();
            List<int> pedidos = numeros.Count == 0 ? new List<int>()
                : db.CabPedidoVtas.Where(c => c.Nº_Cliente == cliente && numeros.Contains(c.Número))
                    .Select(c => c.Número).Distinct().ToList();
            List<string> facturas = referencias.Count == 0 ? new List<string>()
                : db.CabsFacturasVtas.Where(f => f.Nº_Cliente == cliente && referencias.Contains(f.Número))
                    .Select(f => f.Número).Distinct().ToList();
            ClasificadorSaldoAFavor.Clasificar(extractos, pedidos, facturas);
        }

        /// <summary>
        /// NestoAPI#815: de las facturas pendientes del cliente (las deudas), las que son de un pedido con un envío cuyo
        /// reembolso diferido (#742) sigue sin contabilizar, con su pedido. La regla vive en
        /// <see cref="FacturasConReembolsoPendienteDeEntrega"/> (la comparte la cartera vencida de PlazosPagoController).
        /// </summary>
        private IDictionary<string, int> PedidosConReembolsoPendiente(List<ExtractoClienteDTO> extractos)
        {
            IEnumerable<string> facturas = extractos
                .Where(e => e.importePendiente > 0)
                .Select(e => e.documento);
            return FacturasConReembolsoPendienteDeEntrega.Leer(db, facturas);
        }

        /// <summary>
        /// NestoAPI#815: marca como <see cref="ExtractoClienteDTO.ReembolsoPendienteDeEntrega"/> las deudas cuyo documento es
        /// una de esas facturas (el reembolso se contabilizará cuando la agencia entregue: no es deuda vencida).
        /// </summary>
        internal static void MarcarReembolsosPendientes(List<ExtractoClienteDTO> extractos, IDictionary<string, int> pedidoPorFactura)
        {
            if (extractos == null || pedidoPorFactura == null || pedidoPorFactura.Count == 0)
            {
                return;
            }
            foreach (ExtractoClienteDTO extracto in extractos.Where(e => e.importePendiente > 0 && !string.IsNullOrWhiteSpace(e.documento)))
            {
                if (pedidoPorFactura.TryGetValue(extracto.documento.Trim(), out int pedido))
                {
                    extracto.ReembolsoPendienteDeEntrega = true;
                    extracto.PedidoReembolsoPendiente = pedido;
                }
            }
        }

        /// <summary>
        /// Novedades 1302: los pedidos PRE del mismo cliente (distintos de este) con líneas en un picking anterior y sin
        /// facturar (Picking &gt; 0, Estado de -1 a 2). Una sola consulta: líneas agrupadas por pedido y sus prepagos sin
        /// factura en una subconsulta.
        /// </summary>
        public List<PedidoPrepagoEnPickingDTO> PedidosPrepagoEnPicking(int pedido)
        {
            string cliente = db.CabPedidoVtas.Where(p => p.Número == pedido).Select(p => p.Nº_Cliente).FirstOrDefault();
            if (cliente == null)
            {
                return new List<PedidoPrepagoEnPickingDTO>();
            }
            string prepago = Constantes.PlazosPago.PREPAGO;
            int pendiente = Constantes.EstadosLineaVenta.PENDIENTE;
            int factura = Constantes.EstadosLineaVenta.FACTURA;
            return (from l in db.LinPedidoVtas
                    join c in db.CabPedidoVtas on new { l.Empresa, l.Número } equals new { c.Empresa, c.Número }
                    where c.Nº_Cliente == cliente && c.PlazosPago == prepago && c.Número != pedido
                        && l.Picking > 0 && l.Estado >= pendiente && l.Estado < factura
                    group l by l.Número into g
                    select new PedidoPrepagoEnPickingDTO
                    {
                        Pedido = g.Key,
                        ImporteEnPicking = g.Sum(x => x.Total),
                        Prepagos = db.Prepagos.Where(p => p.Pedido == g.Key && p.Factura == null).Sum(p => (decimal?)p.Importe) ?? 0
                    })
                .ToList();
        }

        public List<PrepagoDTO> Prepagos(int pedido)
        {
            return db.Prepagos.Where(p => p.Pedido == pedido && p.Factura == null)
                .Select(p => new PrepagoDTO
                {
                    Importe = p.Importe
                })
                .ToList();
        }
    }
}