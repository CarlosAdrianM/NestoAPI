using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class AsignadorPicking
    {
        private NVEntities db;
        private List<PedidoPicking> pedidos;
        public AsignadorPicking(NVEntities db, List<PedidoPicking> pedidos)
        {
            this.db = db;
            this.pedidos = pedidos;
        }

        public void Ejecutar()
        {
            if (pedidos.Count == 0)
            {
                return;
            }

            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            int numeroPicking = ++contador.Picking;

            RellenadorUbicacionesService rellenador = new RellenadorUbicacionesService();
            List<UbicacionPicking> ubicaciones = rellenador.Rellenar(pedidos);
            GestorUbicaciones gestor;

            foreach(PedidoPicking pedido in pedidos)
            {
                foreach (LineaPedidoPicking linea in pedido.Lineas)
                {
                    LinPedidoVta lineaActual;
                    if (linea.Id != 0)
                    {
                        lineaActual = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.Id);
                    } else
                    {
                        // ponemos nº orden = 0 porque tiene que ser una línea que aún no se haya guardado en la base de datos
                        //lineaActual = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == pedido.Empresa && l.Número == pedido.Id && l.TipoLinea == linea.TipoLinea && l.Producto == linea.Producto && l.Nº_Orden == 0);
                        lineaActual = db.LinPedidoVtas.Local.OrderBy(l => l.Nº_Orden).FirstOrDefault(l => l.Empresa == pedido.Empresa && l.Número == pedido.Id && l.TipoLinea == linea.TipoLinea && l.Producto == linea.Producto && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO);
                    }

                    if (lineaActual.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                    {
                        lineaActual.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                    }
                    
                    lineaActual.Picking = numeroPicking;

                    // System.Diagnostics.Debug.WriteLine("Pedido: " + pedido.Id.ToString() + ", producto: " +  linea.Producto.ToString() + ", cantidad: " + linea.CantidadReservada.ToString());

                    if (!pedido.EsNotaEntrega && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    {
                        gestor = new GestorUbicaciones(linea, ubicaciones);
                        gestor.Ejecutar();
                    }
                }
            }

            GestorUbicaciones.Persistir(db, ubicaciones);

        }
    }
}