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

        public int numeroPicking { get; internal set; }

        public void Ejecutar()
        {
            if (pedidos.Count == 0)
            {
                return;
            }

            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            numeroPicking = ++contador.Picking;

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

                    // NestoAPI#406: segunda barrera contra la doble asignación. El rellenador ya
                    // filtra por Picking null o 0, así que en marcha normal esto no salta nunca;
                    // salta si dos ejecuciones se solapan y la otra ya asignó esta línea después
                    // de que nosotros la leyéramos. Volver a ubicarla es lo que dejaba la línea
                    // con el doble de unidades reservadas y el packing con el doble de cantidad.
                    // Se deja pasar solo cuando el picking es EL NUESTRO (reentrada legítima).
                    if (YaTienePickingDeOtraPasada(lineaActual, numeroPicking))
                    {
                        Infraestructure.ElmahHelper.Log(new Exception(
                            $"Picking {numeroPicking}: la línea {lineaActual.Nº_Orden} del pedido {lineaActual.Número} " +
                            $"ya tenía asignado el picking {lineaActual.Picking}. Se salta para no duplicar su ubicación " +
                            "(NestoAPI#406: dos ejecuciones solapadas)."),
                            "Sistema (picking)");
                        continue;
                    }

                    if (lineaActual.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                    {
                        lineaActual.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                    }

                    lineaActual.Picking = numeroPicking;

                    // System.Diagnostics.Debug.WriteLine("Pedido: " + pedido.Id.ToString() + ", producto: " +  linea.Producto.ToString() + ", cantidad: " + linea.CantidadReservada.ToString());

                    if (!pedido.EsNotaEntrega && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && linea.CantidadReservada > 0)
                    {
                        gestor = new GestorUbicaciones(linea, ubicaciones);
                        gestor.Ejecutar();
                    }
                }
            }

            GestorUbicaciones.Persistir(db, ubicaciones);

        }

        /// <summary>
        /// NestoAPI#406: ¿esta línea ya la asignó OTRA pasada de picking? Se compara contra el
        /// número de esta ejecución para no confundir una reentrada legítima (la misma pasada
        /// tocando la línea otra vez) con la asignación de una ejecución solapada.
        /// </summary>
        internal static bool YaTienePickingDeOtraPasada(LinPedidoVta linea, int numeroPicking)
        {
            return linea.Picking != null && linea.Picking != 0 && linea.Picking != numeroPicking;
        }
    }
}