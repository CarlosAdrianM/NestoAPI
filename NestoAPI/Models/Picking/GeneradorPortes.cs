using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GeneradorPortes
    {
        //No podemos hacer test, porque escribe en base de datos

        private NVEntities db;
        private PedidoPicking pedido;

        public GeneradorPortes(NVEntities db, PedidoPicking pedido)
        {
            this.db = db;
            this.pedido = pedido;
        }

        public void Ejecutar()
        {
            String cuenta;
            decimal portes;
            
            if (pedido.CodigoPostal.StartsWith("28") || pedido.CodigoPostal.StartsWith("19") || pedido.CodigoPostal.StartsWith("45"))
            {
                portes = Constantes.Portes.PROVINCIAL;
                cuenta = Constantes.Cuentas.CUENTA_PORTES_ONTIME;
            } else
            {
                portes = Constantes.Portes.PENINSULAR;
                cuenta = Constantes.Cuentas.CUENTA_PORTES_CEX;
            }

            // if (es contrarrembolso)
            // portes += Constantes.Portes.INCREMENTO_REEMBOLSO;

            // Si ya tiene portes, no los volvemos a añadir
            // PERO HABRÍA QUE COMPROBAR SI HAN CAMBIADO, POR EL CÓDIGO POSTAL O POR LA FORMA DE PAGO
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
                Id = 0, // para luego poder dar picking a la línea recién insertada en db.LinPedidoVtas
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = portes,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto =  cuenta,
                FechaEntrega = DateTime.Today
            });
        }
    }
}