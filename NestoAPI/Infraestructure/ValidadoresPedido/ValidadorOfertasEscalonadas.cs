using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// Ofertas escalonadas (Issue #226): descuento por volumen sobre una lista de referencias
    /// combinables entre sí. Los tramos son "cantidad mínima o más" (no cantidad exacta), así que
    /// 7 unidades con tramo tope en 6 llevan el descuento del tramo 6 en las 7. Sustituye al
    /// patrón de duplicar ofertas combinadas, una por tramo (Allure 247-250).
    /// </summary>
    public class ValidadorOfertasEscalonadas : IValidadorAceptacion
    {
        // El suelo de precio de un tramo se calcula con precisión completa (PrecioBase × (1−dto))
        // y las líneas llegan con hasta 4 decimales: medio céntimo absorbe el redondeo a 2
        // decimales del precio que teclea el vendedor sin dejar pasar descuentos de más.
        private const decimal ToleranciaRedondeo = 0.005m;

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No hay ninguna oferta escalonada que autorice a vender el producto " + numeroProducto + " a ese precio",
                ProductoId = numeroProducto
            };

            List<OfertaEscalonada> ofertas = servicio.BuscarOfertasEscalonadas(numeroProducto);
            if (ofertas == null || ofertas.Count == 0)
            {
                return respuesta;
            }

            foreach (OfertaEscalonada oferta in ofertas)
            {
                OfertaEscalonadaProducto productoOferta = oferta.OfertasEscalonadasProductos
                    .FirstOrDefault(p => p.Producto != null && p.Producto.Trim() == numeroProducto.Trim());
                if (productoOferta == null)
                {
                    continue;
                }

                OfertaEscalonadaTramo tramo = TramoAlcanzado(oferta, pedido);
                if (tramo == null)
                {
                    respuesta.Motivo = "El pedido no alcanza la cantidad mínima de ninguna escala de la oferta escalonada "
                        + oferta.Id.ToString() + " para el producto " + numeroProducto;
                    continue;
                }

                decimal precioMinimo = productoOferta.PrecioBase * (1 - tramo.Descuento);
                IEnumerable<LineaPedidoVentaDTO> lineasProducto = pedido.Lineas.Where(l =>
                    l.Producto != null && l.Producto.Trim() == numeroProducto.Trim());

                if (lineasProducto.All(l => PrecioNeto(l) >= precioMinimo - ToleranciaRedondeo))
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        Motivo = "La oferta escalonada " + oferta.Id.ToString() + " (tramo de "
                            + tramo.CantidadMinima.ToString() + " unidades, " + tramo.Descuento.ToString("P2")
                            + ") permite poner el producto " + numeroProducto + " a ese precio",
                        ProductoId = numeroProducto
                    };
                }

                respuesta.Motivo = "La oferta escalonada " + oferta.Id.ToString() + " solo permite un "
                    + tramo.Descuento.ToString("P2") + " de descuento para el producto " + numeroProducto
                    + " con las unidades que lleva el pedido";
            }

            return respuesta;
        }

        /// <summary>
        /// Tramo más alto cuya cantidad mínima alcanzan las unidades del pedido. Para cada tramo
        /// (de mayor a menor) solo cuentan las unidades cuyo precio neto respeta el suelo de ESE
        /// tramo: así una línea regalada al 100 % (o con más descuento del que la oferta da) no
        /// infla la cantidad para desbloquear descuentos que el pedido no está pagando.
        /// </summary>
        private static OfertaEscalonadaTramo TramoAlcanzado(OfertaEscalonada oferta, PedidoVentaDTO pedido)
        {
            foreach (OfertaEscalonadaTramo tramo in oferta.OfertasEscalonadasTramos.OrderByDescending(t => t.CantidadMinima))
            {
                int unidades = 0;
                foreach (OfertaEscalonadaProducto producto in oferta.OfertasEscalonadasProductos)
                {
                    if (producto.Producto == null)
                    {
                        continue;
                    }
                    decimal precioMinimo = producto.PrecioBase * (1 - tramo.Descuento);
                    unidades += (int)pedido.Lineas
                        .Where(l => l.Producto != null
                                    && l.Producto.Trim() == producto.Producto.Trim()
                                    && PrecioNeto(l) >= precioMinimo - ToleranciaRedondeo)
                        .Sum(l => l.Cantidad);
                }

                if (unidades >= tramo.CantidadMinima)
                {
                    return tramo;
                }
            }
            return null;
        }

        /// <summary>
        /// Precio por unidad realmente cobrado, sin contar el pronto pago: cubre tanto al vendedor
        /// que teclea el descuento en la línea (PrecioUnitario íntegro + DescuentoLinea) como al
        /// que pone directamente el precio rebajado.
        /// </summary>
        private static decimal PrecioNeto(LineaPedidoVentaDTO linea)
        {
            return linea.PrecioUnitario * (1 - linea.SumaDescuentosSinPP);
        }
    }
}
