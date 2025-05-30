﻿using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Linq;

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

            if (!pedido.Lineas.All(l => l.formaVenta == "WEB"))
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

            if (!pedido.Lineas.Any(l => l.Producto == numeroProducto && l.texto.StartsWith("PRESENTE:")) && !EstamosDeBlackFriday())
            {
                Producto producto = servicio.BuscarProducto(numeroProducto);
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
            DateTime comienzaBlackFriday = new DateTime(2025, 4, 22);
            DateTime terminaBlackFriday = new DateTime(2025, 4, 29);
            return DateTime.Today >= comienzaBlackFriday && DateTime.Today <= terminaBlackFriday;
        }
    }
}