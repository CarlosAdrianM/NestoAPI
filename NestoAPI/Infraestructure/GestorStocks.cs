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

        /// <summary>
        /// NestoAPI#517: un gestor para UN pedido con el stock de sus productos leído de golpe (3 consultas
        /// agrupadas en vez de 2-5 por producto). ColorStock da lo mismo que este gestor.
        /// </summary>
        public IGestorStocks PrecargarParaProductos(IEnumerable<string> productos)
        {
            List<string> lista = (productos ?? Enumerable.Empty<string>()).ToList();
            return new GestorStocksPrecargado(this, servicio.LeerResumenStocks(lista), lista);
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

        // NestoAPI#515: el correo de pedido colorea un pedido YA GRABADO, cuyas unidades están dentro de
        // UnidadesPendientesEntregarAlmacen; por eso aquí no se resta cantidad (se contaría dos veces).
        public string ColorStock(string producto, string almacen)
        {
            return ColorStock(producto, almacen, 0);
        }

        /// <summary>
        /// NestoAPI#515: el mismo color, pero para un pedido que TODAVÍA NO EXISTE (el sugeridor de modo
        /// de servicio, la estimación de portes): sus unidades no están en pendientes de entregar, así que
        /// hay que descontarlas a mano. Con cantidad 0 se comporta exactamente como la firma de siempre.
        /// </summary>
        public string ColorStock(string producto, string almacen, int cantidad)
        {
            int stockAlmacen = Stock(producto, almacen);
            int pendientesEntregar = UnidadesPendientesEntregarAlmacen(producto, almacen);
            string colorCantidad;
            if (stockAlmacen - pendientesEntregar - cantidad >= 0)
            {
                colorCantidad = "green";
            }
            else
            {
                int cantidadDisponible = UnidadesDisponiblesTodosLosAlmacenes(producto);
                if (cantidadDisponible - cantidad >= 0)
                {
                    colorCantidad = "DeepPink";
                }
                else
                {
                    colorCantidad = "red";
                }
            }

            return colorCantidad;
        }
    }
}