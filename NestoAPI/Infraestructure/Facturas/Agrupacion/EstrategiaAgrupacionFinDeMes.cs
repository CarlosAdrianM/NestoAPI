using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727 (Carlos, 28/10/26): fin de mes en la API. Para cada cliente con pedidos de periodo FDM, todos los
    /// albaranes hasta la fecha de corte, de TODOS sus contactos, se reúnen en un único pedido (el del cliente principal)
    /// y se factura ese pedido. Los pedidos con P.O. no entran nunca: el P.O. es la unidad de facturación y los factura
    /// <see cref="EstrategiaAgrupacionPO"/>. Las líneas pendientes y los albaranes posteriores al corte se quedan donde están.
    /// </summary>
    public class EstrategiaAgrupacionFinDeMes : IEstrategiaAgrupacionFinDeMes
    {
        private readonly NVEntities db;

        public EstrategiaAgrupacionFinDeMes(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public IEnumerable<GrupoPedidosFinDeMes> SeleccionarGrupos(string empresa, DateTime fechaCorte, string cliente = null, string contacto = null)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                throw new ArgumentException("Falta la empresa.", nameof(empresa));
            }
            string clienteFiltro = string.IsNullOrWhiteSpace(cliente) ? null : cliente.Trim();
            string contactoFiltro = string.IsNullOrWhiteSpace(contacto) ? null : contacto.Trim();
            if (contactoFiltro != null && clienteFiltro == null)
            {
                throw new ArgumentException("Para filtrar por contacto hay que indicar también el cliente.", nameof(contacto));
            }

            // Mismo tope que el SP (fecha < @FechaAlbarán + 1): el día de corte entero.
            DateTime limite = fechaCorte.Date.AddDays(1);
            const string FDM = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES;
            short albaran = Constantes.EstadosLineaVenta.ALBARAN;

            IQueryable<CabPedidoVta> consulta = db.CabPedidoVtas
                .Where(c => c.Empresa == empresa
                         && !c.Agrupada
                         && c.Periodo_Facturacion == FDM
                         && (c.SuPedido == null || c.SuPedido == "")
                         && c.LinPedidoVtas.Any(l => l.Estado == albaran && l.Fecha_Albarán < limite));
            if (clienteFiltro != null)
            {
                consulta = consulta.Where(c => c.Nº_Cliente == clienteFiltro);
            }
            if (contactoFiltro != null)
            {
                consulta = consulta.Where(c => c.Contacto == contactoFiltro);
            }

            // En memoria repetimos los filtros de texto recortados (los char de la BD vienen rellenos de espacios).
            List<CabPedidoVta> candidatos = consulta
                .ToList()
                .Where(c => string.IsNullOrWhiteSpace(c.SuPedido)
                         && c.LinPedidoVtas != null
                         && c.LinPedidoVtas.Any(l => MotorAgrupacionPedidos.LineaAgrupable(l, fechaCorte)))
                .ToList();

            return candidatos
                .GroupBy(c => new
                {
                    Empresa = (c.Empresa ?? string.Empty).Trim(),
                    Cliente = (c.Nº_Cliente ?? string.Empty).Trim()
                })
                .OrderBy(g => g.Key.Cliente)
                .Select(g => new GrupoPedidosFinDeMes
                {
                    Empresa = g.Key.Empresa,
                    Cliente = g.Key.Cliente,
                    Pedidos = g.OrderBy(p => p.Número).ToList()
                })
                .ToList();
        }

        public CabPedidoVta ElegirDestino(GrupoPedidosFinDeMes grupo, DateTime fechaCorte)
        {
            if (grupo == null)
            {
                throw new ArgumentNullException(nameof(grupo));
            }
            if (grupo.Pedidos == null || !grupo.Pedidos.Any())
            {
                throw new ArgumentException("El grupo no tiene pedidos.", nameof(grupo));
            }

            // prdCrearFacturaVta factura todas las líneas en albarán del pedido: el destino no puede tener albaranes
            // posteriores al corte, o se irían en la factura de este mes.
            List<CabPedidoVta> limpios = grupo.Pedidos
                .Where(p => !TieneAlbaranesFueraDelCorte(p, fechaCorte))
                .ToList();
            if (!limpios.Any())
            {
                return null;
            }

            string contactoPrincipal = DestinoAgrupacion.ContactoClientePrincipal(db, grupo.Empresa, grupo.Cliente);
            return DestinoAgrupacion.Elegir(limpios, contactoPrincipal);
        }

        /// <summary>¿Tiene el pedido líneas en albarán que no entran en este corte (posteriores o sin fecha de albarán)?</summary>
        public static bool TieneAlbaranesFueraDelCorte(CabPedidoVta pedido, DateTime fechaCorte)
        {
            return pedido?.LinPedidoVtas != null
                && pedido.LinPedidoVtas.Any(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN
                                              && !MotorAgrupacionPedidos.LineaAgrupable(l, fechaCorte));
        }
    }
}
