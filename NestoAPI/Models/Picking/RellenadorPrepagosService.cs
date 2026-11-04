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
                    documento = e.Nº_Documento
                })
                .ToList();
            MarcarReembolsosPendientes(extractos, PedidosConReembolsoPendiente(extractos));
            return extractos;
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