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

                    // NestoAPI#405: segunda barrera contra la doble asignación. El rellenador ya
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
                            "(NestoAPI#405: dos ejecuciones solapadas)."),
                            "Sistema (picking)");
                        continue;
                    }

                    if (lineaActual.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                    {
                        lineaActual.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                    }

                    lineaActual.Picking = numeroPicking;

                    // System.Diagnostics.Debug.WriteLine("Pedido: " + pedido.Id.ToString() + ", producto: " +  linea.Producto.ToString() + ", cantidad: " + linea.CantidadReservada.ToString());

                    if (NecesitaUbicacion(pedido, linea))
                    {
                        gestor = new GestorUbicaciones(linea, ubicaciones);
                        try
                        {
                            gestor.Ejecutar();
                        }
                        catch (Exception ex) when (StockLibreAgotadoPicking.EsDescuadrada(ex))
                        {
                            // 11/11/26: si lo ubicado está reservado para otros pedidos, el mensaje claro; si no, el de siempre.
                            string claro = StockLibreAgotadoPicking.Mensaje(linea.Producto, linea.Almacen, linea.NumeroPedido,
                                ReservasDeOtros(linea, ubicaciones));
                            if (claro == null)
                            {
                                throw;
                            }
                            throw new Exception(claro, ex);
                        }
                    }
                }
            }

            GestorUbicaciones.Persistir(db, ubicaciones);

        }

        /// <summary>
        /// Lo reservado del producto para otros pedidos: lo que ya estaba en Ubicaciones con Estado 3 en el almacén de la línea (de
        /// otros pickings) y lo que esta misma pasada acaba de reservar para otras líneas. Solo se lee si ha saltado el error.
        /// </summary>
        private List<ReservaUbicacionPicking> ReservasDeOtros(LineaPedidoPicking linea, List<UbicacionPicking> ubicaciones)
        {
            var reservas = new List<ReservaUbicacionPicking>();
            try
            {
                string producto = linea.Producto;
                string almacen = string.IsNullOrWhiteSpace(linea.Almacen) ? Constantes.Almacenes.ALGETE : linea.Almacen.Trim();
                int estado = Constantes.Ubicaciones.RESERVADO_PICKING;
                reservas.AddRange(db.Ubicaciones
                    .Where(u => u.Número == producto && u.Almacén == almacen && u.Estado == estado && u.PedidoVta != null && u.Cantidad > 0)
                    .GroupBy(u => u.PedidoVta.Value)
                    .Select(g => new ReservaUbicacionPicking { Pedido = g.Key, Cantidad = g.Sum(u => u.Cantidad) })
                    .ToList());
            }
            catch (Exception ex)
            {
                Infraestructure.ElmahHelper.Log(new Exception($"Picking: no se han podido leer las reservas del producto {linea.Producto?.Trim()}: {ex.Message}", ex),
                    "Sistema (picking)");
            }
            Dictionary<int, int> pedidoDeLinea = pedidos.SelectMany(p => p.Lineas.Select(l => new { l.Id, Pedido = p.Id }))
                .Where(x => x.Id != 0)
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Pedido);
            reservas.AddRange((ubicaciones ?? new List<UbicacionPicking>())
                .Where(u => u.Producto == linea.Producto && u.EstadoNuevo == Constantes.Ubicaciones.RESERVADO_PICKING
                    && u.Estado != Constantes.Ubicaciones.RESERVADO_PICKING && u.LineaPedidoVentaId != linea.Id
                    && pedidoDeLinea.ContainsKey(u.LineaPedidoVentaId))
                .Select(u => new ReservaUbicacionPicking { Pedido = pedidoDeLinea[u.LineaPedidoVentaId], Cantidad = u.CantidadNueva }));
            return reservas;
        }

        /// <summary>
        /// Solo las líneas de producto reservadas de un pedido que no es nota de entrega reservan hueco. Una nota de entrega (también la
        /// de solo texto, NestoAPI#765) lleva picking pero no toca las ubicaciones.
        /// </summary>
        internal static bool NecesitaUbicacion(PedidoPicking pedido, LineaPedidoPicking linea)
            => !pedido.EsNotaEntrega && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && linea.CantidadReservada > 0;

        /// <summary>
        /// NestoAPI#405: ¿esta línea ya la asignó OTRA pasada de picking? Se compara contra el
        /// número de esta ejecución para no confundir una reentrada legítima (la misma pasada
        /// tocando la línea otra vez) con la asignación de una ejecución solapada.
        /// </summary>
        internal static bool YaTienePickingDeOtraPasada(LinPedidoVta linea, int numeroPicking)
        {
            return linea.Picking != null && linea.Picking != 0 && linea.Picking != numeroPicking;
        }
    }
}