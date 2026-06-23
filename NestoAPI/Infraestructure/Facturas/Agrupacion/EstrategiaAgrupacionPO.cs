using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 2): estrategia de agrupación por P.O.
    /// </summary>
    public class EstrategiaAgrupacionPO : IEstrategiaAgrupacionPO
    {
        private readonly NVEntities db;

        public EstrategiaAgrupacionPO(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public IEnumerable<GrupoPedidosPO> SeleccionarGrupos(string empresa)
        {
            // Candidatos: pedidos de la empresa con MantenerJunto, PO informado y no agrupados.
            // Traemos a memoria para agrupar por PO y revisar el estado de las líneas (el
            // criterio "todas en albarán" se evalúa por pedido, no es trivial en SQL aquí).
            List<CabPedidoVta> candidatos = db.CabPedidoVtas
                .Where(c => c.Empresa == empresa
                         && c.MantenerJunto
                         && !c.Agrupada
                         && c.SuPedido != null
                         && c.SuPedido != "")
                .ToList();

            return candidatos
                .GroupBy(c => new
                {
                    c.Empresa,
                    Cliente = (c.Nº_Cliente ?? string.Empty).Trim(),
                    SuPedido = (c.SuPedido ?? string.Empty).Trim()
                })
                // El grupo está listo solo si TODOS sus pedidos tienen líneas y todas en albarán.
                .Where(g => g.All(PedidoListoParaAgrupar))
                .Select(g => new GrupoPedidosPO
                {
                    Empresa = g.Key.Empresa,
                    Cliente = g.Key.Cliente,
                    SuPedido = g.Key.SuPedido,
                    Pedidos = g.OrderBy(p => p.Número).ToList()
                })
                .ToList();
        }

        public CabPedidoVta ElegirDestino(GrupoPedidosPO grupo)
        {
            if (grupo == null)
            {
                throw new ArgumentNullException(nameof(grupo));
            }
            if (grupo.Pedidos == null || !grupo.Pedidos.Any())
            {
                throw new ArgumentException("El grupo no tiene pedidos.", nameof(grupo));
            }

            string contactoPrincipal = ContactoClientePrincipal(grupo.Empresa, grupo.Cliente);

            // Preferimos el pedido (más antiguo) cuyo contacto ya sea el del cliente principal.
            CabPedidoVta destino = grupo.Pedidos
                .Where(p => !string.IsNullOrEmpty(contactoPrincipal)
                         && string.Equals((p.Contacto ?? string.Empty).Trim(), contactoPrincipal,
                                StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Número)
                .FirstOrDefault();

            if (destino != null)
            {
                return destino;
            }

            // Si ninguno es el principal, tomamos el más antiguo y le ponemos el contacto
            // del principal en cabecera para que el deudor de la factura sea el principal.
            destino = grupo.Pedidos.OrderBy(p => p.Número).First();
            if (!string.IsNullOrEmpty(contactoPrincipal))
            {
                destino.Contacto = contactoPrincipal;
            }
            return destino;
        }

        private static bool PedidoListoParaAgrupar(CabPedidoVta pedido)
        {
            return pedido.LinPedidoVtas != null
                && pedido.LinPedidoVtas.Any()
                && pedido.LinPedidoVtas.All(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);
        }

        private string ContactoClientePrincipal(string empresa, string cliente)
        {
            return db.Clientes
                .Where(c => c.Empresa == empresa
                         && c.Nº_Cliente == cliente
                         && c.ClientePrincipal)
                .Select(c => c.Contacto)
                .FirstOrDefault()
                ?.Trim();
        }
    }
}
