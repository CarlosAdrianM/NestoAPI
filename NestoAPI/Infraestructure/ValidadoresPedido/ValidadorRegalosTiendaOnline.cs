using System;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorRegalosTiendaOnline : IValidadorAceptacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "El producto " + numeroProducto + " no es un regalo de tienda online",
                ProductoId = numeroProducto
            };

            if (pedido.comentarios == null || !pedido.comentarios.Contains("TOTAL PEDIDO:"))
            {
                return respuesta;
            }

            if (!pedido.LineasPedido.All(l => l.formaVenta == "WEB"))
            {
                return respuesta;
            } 
            else if (EstamosDeBlackFriday())
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "El producto " + numeroProducto + " se permite por ser de tienda online en Black Friday",
                    ProductoId = numeroProducto
                };
            }

            if (!pedido.LineasPedido.Any(l => l.producto == numeroProducto && l.texto.StartsWith("PRESENTE:")) && !EstamosDeBlackFriday())
            {
                var producto = servicio.BuscarProducto(numeroProducto);
                if (producto.Estado <= 2)
                {
                    return respuesta;
                }
            }

            return new RespuestaValidacion
            {
                ValidacionSuperada = true,
                Motivo = "El producto " + numeroProducto + " se permite por ser un regalo de tienda online",
                ProductoId = numeroProducto
            };
        }

        private bool EstamosDeBlackFriday()
        {
            DateTime comienzaBlackFriday = new DateTime(2020, 11, 27);
            DateTime terminaBlackFriday = new DateTime(2020, 12, 1);
            return DateTime.Today >= comienzaBlackFriday && DateTime.Today <= terminaBlackFriday;
        }
    }
}