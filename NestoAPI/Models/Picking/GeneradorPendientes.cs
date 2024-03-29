﻿using NestoAPI.Controllers;
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
                    linea.Cantidad = linea.CantidadReservada;
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