using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

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
            // Carlos lo dejo por si en algún sitio se llama sin el almacén
            // pero en próximas refactorizaciones se puede quitar
            return HayStockDisponibleDeTodo(pedido, Constantes.Almacenes.REINA);
        }

        public bool HayStockDisponibleDeTodo(PedidoVentaDTO pedido, string almacen)
        {
            if (pedido.Lineas == null || pedido.Lineas.Count == 0)
            {
                return true;
            }
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                int stock = servicio.Stock(linea.Producto, almacen);
                if (stock < linea.Cantidad)
                {
                    return false;
                }

                if (stock - servicio.UnidadesPendientesEntregarAlmacen(linea.Producto, almacen) < linea.Cantidad)
                {
                    return false;
                }

                if (servicio.UnidadesDisponiblesTodosLosAlmacenes(linea.Producto) < linea.Cantidad)
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