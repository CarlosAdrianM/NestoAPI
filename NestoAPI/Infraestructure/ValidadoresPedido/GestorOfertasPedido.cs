using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public static class GestorOfertasPedido
    {
        /*
         * Dado un pedido y un producto determinados, nos devuelve la oferta de ese producto que 
         * hay en ese pedido.
         */
        public static PrecioDescuentoProducto MontarOfertaPedido(string numeroProducto, PedidoVentaDTO pedido)
        {
            if (numeroProducto != null)
            {
                numeroProducto = numeroProducto.Trim();
            }
            List<string> productosMismoPrecio = ProductosMismoPrecio(numeroProducto, pedido);
            IEnumerable<LineaPedidoVentaDTO> lineasProducto = pedido.Lineas.Where(p => productosMismoPrecio.Contains(p.Producto));
            if (lineasProducto == null || lineasProducto.Count() == 0)
            {
                return null;
            }

            IEnumerable<LineaPedidoVentaDTO> lineasConPrecio = lineasProducto.Where(l => l.BaseImponible / l.Cantidad != 0);
            IEnumerable<LineaPedidoVentaDTO> lineasSinPrecio = lineasProducto.Where(l => l.BaseImponible / l.Cantidad == 0);

            Producto producto = GestorPrecios.servicio.BuscarProducto(numeroProducto);

            if (!lineasSinPrecio.Any(l => l.Producto == numeroProducto))
            {
                return new PrecioDescuentoProducto
                {
                    cantidadOferta = (short)lineasSinPrecio.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad),
                    cantidad = (short)lineasConPrecio.Where(l => l.Producto == numeroProducto).Sum(l => l.Cantidad),
                    producto = producto,
                    precioCalculado = Math.Round(lineasConPrecio.Where(l => l.Producto == numeroProducto).Select(l => l.PrecioUnitario).DefaultIfEmpty().Average(), 2, MidpointRounding.AwayFromZero),
                    descuentoCalculado = lineasConPrecio.Where(l => l.Producto == numeroProducto).Select(l => 1 - ((1 - l.DescuentoLinea) * (1 - l.DescuentoProducto))).DefaultIfEmpty().Average()
                };
            }

            return new PrecioDescuentoProducto
            {
                cantidadOferta = (short)lineasSinPrecio.Sum(l => l.Cantidad),
                cantidad = (short)lineasConPrecio.Sum(l => l.Cantidad),
                producto = producto,
                precioCalculado = lineasConPrecio.Select(l => l.PrecioUnitario).DefaultIfEmpty().Average(),
                descuentoCalculado = lineasConPrecio.Select(l => 1 - ((1 - l.DescuentoLinea) * (1 - l.DescuentoProducto))).DefaultIfEmpty().Average()
            };

        }

        public static List<string> ProductosMismoPrecio(string numeroProducto, PedidoVentaDTO pedido)
        {
            Producto productoBuscado = GestorPrecios.servicio.BuscarProducto(numeroProducto);
            List<string> productosMismoPrecio = new List<string>();
            foreach (string productoLinea in pedido.Lineas.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO).Select(l => l.Producto).Distinct())
            {
                Producto productoEncontrado = GestorPrecios.servicio.BuscarProducto(productoLinea);
                if (productoEncontrado.PVP == productoBuscado.PVP && productoEncontrado.Familia == productoBuscado.Familia)
                {
                    productosMismoPrecio.Add(productoLinea);
                }
            }
            return productosMismoPrecio;
        }
    }
}