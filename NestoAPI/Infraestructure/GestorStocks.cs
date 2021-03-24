using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    public class GestorStocks : IGestorStocks
    {
        private IServicioGestorStocks servicio;

        public GestorStocks() {
            this.servicio = new ServicioGestorStocks();
        }
        public GestorStocks(IServicioGestorStocks servicio)
        {
            this.servicio = servicio;
        }

        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido)
        {
            if (pedido.LineasPedido == null || pedido.LineasPedido.Count == 0)
            {
                return true;
            }
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido)
            {
                int stock = servicio.Stock(linea.producto, Constantes.Almacenes.REINA);
                if (stock < linea.cantidad)
                {
                    return false;
                }

                if (stock - servicio.UnidadesPendientesEntregarAlmacen(linea.producto, Constantes.Almacenes.REINA) < linea.cantidad) {
                    return false;
                }

                if (servicio.UnidadesDisponiblesTodosLosAlmacenes(linea.producto) < linea.cantidad)
                {
                    return false;
                }
            }
            return true;
        }

        public int Stock(string producto)
        {
            return servicio.Stock(producto);
        }
        public int Stock(string producto, string almacen)
        {
            return servicio.Stock(producto, almacen);
        }

        public int UnidadesDisponiblesTodosLosAlmacenes(string producto)
        {
            return servicio.UnidadesDisponiblesTodosLosAlmacenes(producto);
        }

        public int UnidadesPendientesEntregar(string producto)
        {
            return servicio.UnidadesPendientesEntregar(producto);
        }
        public int UnidadesPendientesEntregarAlmacen(string producto, string almacen)
        {
            return servicio.UnidadesPendientesEntregarAlmacen(producto, almacen);
        }
    }
}