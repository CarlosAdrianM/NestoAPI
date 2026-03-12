using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class GeneradorPortes
    {
        private NVEntities db;
        private PedidoPicking pedido;

        public GeneradorPortes(NVEntities db, PedidoPicking pedido)
        {
            this.db = db;
            this.pedido = pedido;
        }

        public void Ejecutar()
        {
            // Delegamos el cálculo a GestorPortes (lógica centralizada)
            bool esProvincial = GestorPortes.EsProvincial(pedido.CodigoPostal);
            string cuenta = esProvincial
                ? Constantes.Cuentas.CUENTA_PORTES_ONTIME
                : Constantes.Cuentas.CUENTA_PORTES_CEX;
            decimal portes = esProvincial
                ? Constantes.Portes.PROVINCIAL
                : Constantes.Portes.PENINSULAR;

            // Si ya tiene portes, no los volvemos a añadir
            LinPedidoVta lineaPortes = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == pedido.Empresa && l.Número == pedido.Id && l.Producto != null && l.Producto.Trim() == cuenta && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO);
            if (lineaPortes != null)
            {
                return;
            }

            GestorPedidosVenta gestorPedidos = new GestorPedidosVenta(new ServicioPedidosVenta());
            LinPedidoVta lineaVta = gestorPedidos.CrearLineaVta(pedido.Empresa, pedido.Id, PedidosVentaController.TIPO_LINEA_CUENTA_CONTABLE, cuenta, 1, portes, "");
            db.LinPedidoVtas.Add(lineaVta);
            pedido.Lineas.Add(new LineaPedidoPicking
            {
                Id = 0,
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = portes,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuenta,
                FechaEntrega = DateTime.Today
            });
        }
    }
}
