﻿using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorRegaloPorImportePedido : IValidadorAceptacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            if (servicio == null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    ProductoId = numeroProducto,
                    Motivo = "No se ha pasado el servicio para validar"
                };
            }
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                ProductoId = numeroProducto,
                Motivo = "El producto " + numeroProducto
                        + " no puede ir a ese precio porque no es un regalo autorizado para pedidos de este importe"
            };

            var listaRegalos = GestorPrecios.servicio.BuscarRegaloPorImportePedido(numeroProducto);

            if (listaRegalos.Any(l => pedido.Lineas.Sum(p => p.BaseImponible) >= l.ImportePedido &&
                pedido.Lineas.Where(p => p.Producto == numeroProducto).Sum(p => p.Cantidad) <= l.Cantidad))
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    ProductoId = numeroProducto,
                    Motivo = "El producto " + numeroProducto
                        + " puede ir a ese precio porque es un regalo autorizado para pedidos de este importe"
                };
            }

            return respuesta;
        }

    }
}