using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOtrosAparatosSiempreSinDescuento : IValidadorPedido
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion();
            bool esValidoDeMomento = true;
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido.Where(l => l.tipoLinea == 1)) //1=Producto
            {
                Producto producto = servicio.BuscarProducto(linea.producto);

                if (producto == null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        OfertaAutorizadaExpresamente = false,
                        Motivo = "No existe el producto " + linea.producto + " en la línea " + linea.id.ToString()
                    };
                }

                PrecioDescuentoProducto datos = new PrecioDescuentoProducto
                {
                    precioCalculado = linea.precio,
                    descuentoCalculado = linea.descuento,
                    producto = producto,
                    cantidad = linea.cantidad,
                    aplicarDescuento = linea.aplicarDescuento
                };

                respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);
                esValidoDeMomento = respuesta.ValidacionSuperada;
                // Si la oferta está autorizada expresamente, ni nos molestamos en comprobar
                if (!respuesta.ValidacionSuperada && !respuesta.OfertaAutorizadaExpresamente)
                {
                    esValidoDeMomento = new OtrosAparatosNoPuedeLlevarDescuento().precioAceptado(datos);
                }
                
                // Una vez que una línea no es válida, todo el pedido deja de ser válido
                if (!esValidoDeMomento) 
                {
                    break;
                }
            }
            

            return respuesta;
        }
    }
}