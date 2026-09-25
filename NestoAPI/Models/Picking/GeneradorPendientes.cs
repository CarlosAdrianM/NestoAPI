using NestoAPI.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class GeneradorPendientes
    {
        private NVEntities db;
        private List<PedidoPicking> pedidos;
        public GeneradorPendientes(NVEntities db, List<PedidoPicking> pedidos)
        {
            this.db = db;
            this.pedidos = pedidos;
        }

        public void Ejecutar()
        {
            PasarAEnCursoLoQueYaTieneStock();
            EscribirLoQueSeRecoge();

            for (int j = 0; j < pedidos.Count; j++)
            {
                PedidoPicking pedido = pedidos[j];
                List<LineaPedidoPicking> lineas = pedido.Lineas.Where(l => l.CantidadReservada < l.Cantidad).ToList();
                for (int i = 0; i < lineas.Count(); i++)
                {
                    LineaPedidoPicking linea = lineas[i];
                    LineaPedidoPicking lineaNueva = pasarAPendiente(linea, pedido.Borrar);
                    
                    if(linea.CantidadReservada == 0 && !(pedido.EsNotaEntrega && !pedido.EsProductoYaFacturado))
                    {
                        linea.Borrar = true;
                    }
                }
                pedido.Lineas.RemoveAll(l => l.Borrar);
                if (pedido.Lineas.Count == 0)
                {
                    pedido.Borrar = true;
                }
            }
            //pedidos.RemoveAll(p => p.Borrar);
        }

        /// <summary>
        /// NestoAPI#540: en un pedido que no sale (Todo junto al que le falta algo, retenido por
        /// prepago...), las líneas que ya tienen sus unidades vuelven de pendiente (-1) a en curso
        /// (1), para que en el pedido solo quede en -1 lo que de verdad falta. No basta con que haya
        /// stock: tiene que estar reservada ENTERA en esta pasada, y el reparto va por antigüedad
        /// (GestorReservasStock), así que ningún pedido más antiguo la necesita y no volverá a -1
        /// (ni saltará otra vez el correo de «Pasado a Pendientes» del trigger).
        /// </summary>
        private void PasarAEnCursoLoQueYaTieneStock()
        {
            List<int> idsConStock = pedidos
                .Where(p => p.Borrar && !(p.EsNotaEntrega && !p.EsProductoYaFacturado))
                .SelectMany(p => p.Lineas)
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > 0 && l.CantidadReservada == l.Cantidad)
                .Select(l => l.Id)
                .ToList();
            if (!idsConStock.Any())
            {
                return;
            }
            List<LinPedidoVta> pendientes = db.LinPedidoVtas
                .Where(l => idsConStock.Contains(l.Nº_Orden) && l.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                .ToList();
            foreach (LinPedidoVta linea in pendientes)
            {
                linea.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
            }
        }

        /// <summary>
        /// NestoAPI#542: en los pedidos que salen y se facturan «todo ahora», las unidades que
        /// GestorFacturarTodoAhora convirtió en memoria pasan a LinPedidoVta.Recoger. La línea se queda en
        /// curso (1): entra en el picking y en el albarán con la cantidad completa. El trigger
        /// trgLinPedidoVtaUpd admite el cambio porque la línea todavía no tiene picking (se lo pone
        /// AsignadorPicking en el mismo SaveChanges), y Recoger nunca supera la cantidad.
        /// </summary>
        private void EscribirLoQueSeRecoge()
        {
            foreach (PedidoPicking pedido in pedidos.Where(p => !p.Borrar && p.FacturaTodoAhora))
            {
                foreach (LineaPedidoPicking linea in pedido.Lineas.Where(l => l.CantidadARecoger > 0 && l.Id != 0))
                {
                    LinPedidoVta lineaActual = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.Id);
                    if (lineaActual == null)
                    {
                        continue;
                    }
                    lineaActual.Recoger += linea.CantidadARecoger;
                    lineaActual.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                }
            }
        }

        private LineaPedidoPicking pasarAPendiente(LineaPedidoPicking linea, bool elPedidoSeBorrara)
        {
            PedidosVentaController pedidosCtrl = new PedidosVentaController();

            // Lo suyo sería dejar pendiente la línea actual para que mantuviese la antigüedad,
            // pero no se puede, porque necesitamos el Nº Orden para ponerlo en Ubicaciones luego,
            // por lo que nos vemos obligados a dejar pendiente la línea nueva y decidir la 
            // antigüedad por fecha de modificación en lugar de por Nº Orden
            // (también hay que cambiarlo en la reposición).
            LinPedidoVta lineaActual = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.Id);

            if (lineaActual.Cantidad != linea.Cantidad - linea.CantidadReservada)
            {
                LinPedidoVta lineaNueva = null;
                lineaActual.Estado = Constantes.EstadosLineaVenta.PENDIENTE;
                if (!elPedidoSeBorrara)
                {
                    lineaNueva = pedidosCtrl.dividirLinea(db, lineaActual, (short)(linea.CantidadReservada));
                    // NestoAPI#485: lineaActual ya es solo la parte servida (dividirLinea la ha
                    // recalculado); la línea del picking tiene que llevar sus importes, no solo la cantidad
                    linea.AjustarALineaServida(lineaActual);
                }                
                // comprobar, pero creo que esto solo hay que hacerlo si la cantidad es distinta a la cantidadreservada
                // porque al no crear línea nueva, lo que hacemos es volver a poner en estado 1.
                // ¡¡¡ escribir test que falle antes de tocar nada!!!
                if (linea.Cantidad == linea.CantidadReservada)
                {
                    lineaActual.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                }

                if (lineaNueva != null)
                {
                    return new LineaPedidoPicking
                    {
                        Id = 0, // no importa que no tenga Nº Orden porque es la que se queda pendiente
                        Cantidad = (short)lineaNueva.Cantidad,
                        CantidadReservada = 0,
                        BaseImponible = lineaNueva.Base_Imponible,
                        TipoLinea = (byte)lineaNueva.TipoLinea,
                        Producto = lineaNueva.Producto,
                        FechaEntrega = lineaNueva.Fecha_Entrega
                    };
                }
            }
            else
            {
                lineaActual.Estado = Constantes.EstadosLineaVenta.PENDIENTE;
            }

            return null;
            
        }
    }
}