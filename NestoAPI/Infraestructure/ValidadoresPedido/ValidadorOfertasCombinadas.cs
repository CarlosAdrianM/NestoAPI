﻿using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOfertasCombinadas : IValidadorAceptacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No hay ninguna oferta combinada que autorice a vender el producto " + numeroProducto + " a ese precio",
                ProductoId = numeroProducto
            };
            List<OfertaCombinada> ofertasCombinadas = servicio.BuscarOfertasCombinadas(numeroProducto);
            if (ofertasCombinadas == null || ofertasCombinadas.Count() == 0)
            {
                return respuesta;
            }

            OfertaCombinada ofertaCumplida = ofertasCombinadas.FirstOrDefault(o => 
                o.OfertasCombinadasDetalles.All(d => 
                    pedido.LineasPedido.Where(p=>p.precio >= d.Precio && p.cantidad >= d.Cantidad).Select(p => p.producto.Trim()).Contains(d.Producto.Trim())
                )
            );

            if (ofertaCumplida == null || ofertaCumplida.ImporteMinimo > 0)
            {
                IEnumerable<OfertaCombinada> ofertasConImporteMinimo = ofertasCombinadas.Where(o =>
                    o.OfertasCombinadasDetalles.All(d =>
                        pedido.LineasPedido.Where(p => p.precio >= d.Precio && p.cantidad >= d.Cantidad).Select(p => p.producto.Trim()).Contains(d.Producto.Trim())
                        && o.ImporteMinimo>0
                    )
                );
                OfertaCombinada ofertaConImporteMinimo;
                for (int i = 0; i < ofertasConImporteMinimo.Count(); i++)
                {
                    //ofertaConImporteMinimo = ofertaCumplida ?? ofertasCombinadas.FirstOrDefault(o => o.ImporteMinimo > 0);
                    ofertaConImporteMinimo = ofertasConImporteMinimo.ElementAt(i);

                    if (ofertaConImporteMinimo != null)
                    {
                        IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = pedido.LineasPedido.Where(l =>
                            ofertaConImporteMinimo.OfertasCombinadasDetalles.Select(d => d.Producto.Trim()).Contains(l.producto.Trim())
                        );
                        var sumaImporte = lineasOfertaPedido.Sum(l => l.baseImponible);
                        if (sumaImporte >= ofertaConImporteMinimo.ImporteMinimo)
                        {
                            ofertaCumplida = ofertaConImporteMinimo;
                            respuesta.ValidacionSuperada = true;
                            respuesta.Motivo = "La oferta "+ ofertaCumplida.Id.ToString() 
                                +" permite poner el producto "+ numeroProducto +" a ese precio";
                            break;
                        }
                        else
                        {
                            respuesta.Motivo = "La oferta " + ofertaConImporteMinimo.Id
                                + " tiene que tener un importe mínimo de " + ofertaConImporteMinimo.ImporteMinimo.ToString("C") + " para que sea válida";
                            ofertaCumplida = null;
                        }
                    }
                }
            }

            // Comprobamos los múltiplos
            if (ofertaCumplida != null)
            {
                var cantidadLineas = pedido.LineasPedido.Where(l => l.producto == numeroProducto).Sum(l => l.cantidad);
                var cantidadOferta = ofertaCumplida.OfertasCombinadasDetalles.Where(o => o.Producto == numeroProducto).Sum(o => o.Cantidad);
                if (cantidadLineas > cantidadOferta)
                {
                    IEnumerable<LineaPedidoVentaDTO> lineasOfertaPedido = pedido.LineasPedido.Where(l =>
                        ofertaCumplida.OfertasCombinadasDetalles.Where(o => (float)l.cantidad / o.Cantidad < (float)cantidadLineas / cantidadOferta).Select(d => d.Producto).Contains(l.producto)
                    );
                    if (lineasOfertaPedido != null && lineasOfertaPedido.Count() > 0)
                    {
                        respuesta.Motivo = "Está ofertando más cantidad de la permitida en el producto " + numeroProducto + " para que la oferta " + ofertaCumplida.Id.ToString() + " sea válida";
                        ofertaCumplida = null;
                    }
                }
            }

            if (ofertaCumplida == null)
            {
                return respuesta;
            }

            return new RespuestaValidacion
            {
                ValidacionSuperada = true,
                Motivo = "La oferta " + ofertaCumplida.Id.ToString()
                    + " permite poner el producto " + numeroProducto + " a ese precio",
                ProductoId = numeroProducto
            };
        }
    }
}