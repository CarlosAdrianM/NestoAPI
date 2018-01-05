using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOtrosAparatosSiempreSinDescuento : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion {
                ValidacionSuperada = true,
                Motivo = "El pedido " + pedido.numero + " no tiene ninguna línea de productos"
            };
            bool esValidoDeMomento = true;
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido.Where(l => l.tipoLinea == 1)) //1=Producto
            {
                Producto producto = servicio.BuscarProducto(linea.producto);

                if (producto == null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        AutorizadaDenegadaExpresamente = false,
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

                esValidoDeMomento = new OtrosAparatosNoPuedeLlevarDescuento().precioAceptado(datos);
                
                // Una vez que una línea no es válida, todo el pedido deja de ser válido
                if (!esValidoDeMomento) 
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        Motivo = "El producto " + linea.producto + " no puede llevar ningún descuento ni oferta porque es Otros Aparatos" ,
                        ProductoId = linea.producto,
                        AutorizadaDenegadaExpresamente = true
                    };
                }
            }
            

            return respuesta;
        }
    }
}