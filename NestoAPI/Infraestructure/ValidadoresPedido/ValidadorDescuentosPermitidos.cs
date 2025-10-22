using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorDescuentosPermitidos : IValidadorDenegacion
    {
        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            List<ErrorValidacion> erroresEncontrados = new List<ErrorValidacion>();

            // Obtener productos únicos para evitar validar el mismo producto varias veces
            var productosUnicos = pedido.Lineas
                .Where(l => l.tipoLinea == 1)
                .Select(l => l.Producto)
                .Distinct();

            foreach (string numeroProducto in productosUnicos)
            {
                Producto producto = servicio.BuscarProducto(numeroProducto);

                if (producto == null)
                {
                    erroresEncontrados.Add(new ErrorValidacion
                    {
                        Motivo = "No existe el producto " + numeroProducto,
                        ProductoId = numeroProducto,
                        AutorizadaDenegadaExpresamente = false
                    });
                    continue;
                }

                if (producto.Ficticio && producto.Familia != null && producto.Familia.Trim() == Constantes.Productos.FAMILIA_BONIFICACION)
                {
                    continue;
                }

                RespuestaValidacion respuestaProducto = EsDescuentoPermitido(producto, pedido, servicio);

                if (!respuestaProducto.ValidacionSuperada)
                {
                    erroresEncontrados.Add(new ErrorValidacion
                    {
                        Motivo = respuestaProducto.Motivo,
                        ProductoId = respuestaProducto.ProductoId,
                        AutorizadaDenegadaExpresamente = respuestaProducto.AutorizadaDenegadaExpresamente
                    });
                }
                else if (!string.IsNullOrEmpty(respuestaProducto.Motivo))
                {
                    // Guardamos el último mensaje exitoso para compatibilidad con tests
                    respuesta.Motivo = respuestaProducto.Motivo;
                }
            }

            // Si hay errores, consolidamos
            if (erroresEncontrados.Any())
            {
                respuesta.ValidacionSuperada = false;
                respuesta.Errores = erroresEncontrados;
                respuesta.Motivos = erroresEncontrados.Select(e => e.Motivo).ToList();
                // Para compatibilidad: si solo hay un error, lo ponemos en Motivo también
                if (erroresEncontrados.Count == 1)
                {
                    respuesta.ProductoId = erroresEncontrados[0].ProductoId;
                    respuesta.AutorizadaDenegadaExpresamente = erroresEncontrados[0].AutorizadaDenegadaExpresamente;
                }
            }

            return respuesta;
        }

        internal static RespuestaValidacion EsDescuentoPermitido(Producto producto, PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            if (producto == null || pedido == null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "El producto o el pedido no existen"
                };
            }

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido(producto.Número, pedido);

            // Buscar si hay oferta combinada
            List<OfertaPermitida> ofertas = servicio.BuscarOfertasPermitidas(producto.Número);
            IEnumerable<OfertaPermitida> ofertasFiltradas = ofertas.Where(o =>
                (o.Cliente == null || o.Cliente == pedido.cliente) &&
                (o.Contacto == null || (o.Cliente == pedido.cliente && o.Contacto == pedido.contacto))
            );
            OfertaPermitida ofertaCombinada = ofertasFiltradas.FirstOrDefault(o =>
                o.FiltroProducto != null && o.FiltroProducto.Trim() != "" && producto.Nombre.StartsWith(o.FiltroProducto)
            );

            // Este validador solo valida descuentos.
            // Si el producto tiene oferta (cantidadOferta > 0 Y (cantidad > 0 O ofertaCombinada)),
            // entonces NO debe ser validado por este validador, debe ser validado por ValidadorOfertasPermitidas
            if (oferta.cantidadOferta > 0 && (oferta.cantidad > 0 || ofertaCombinada != null))
            {
                // Este producto tiene oferta, no validamos descuentos
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true
                };
            }

            // Si no tiene ninguna oferta ni descuento, está siempre permitido
            if ((oferta.cantidadOferta == 0 && oferta.descuentoReal == 0) || oferta.descuentoReal < 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "El producto " + producto.Número.Trim() + " no lleva oferta ni descuento"
                };
            }

            // Validación de descuentos
            IEnumerable<DescuentosProducto> descuentos = servicio.BuscarDescuentosPermitidos(oferta.producto.Número, pedido.cliente, pedido.contacto);

            IEnumerable<DescuentosProducto> descuentosEspecificosProducto = descuentos.Where(d => d.Nº_Producto?.Trim() == oferta.producto.Número.Trim());
            // Si hay un descuento específico para el producto, éste prevale sobre el de la familia o grupo
            if (descuentosEspecificosProducto != null && descuentosEspecificosProducto.Where(d => d.CantidadMínima <= (oferta.cantidad + oferta.cantidadOferta)).Any())
            {
                descuentos = descuentosEspecificosProducto.Where(d => d.CantidadMínima <= (oferta.cantidad + oferta.cantidadOferta));
            }

            DescuentosProducto descuentoAutorizado = descuentos.FirstOrDefault(d =>
                d.Descuento >= Math.Round((decimal)oferta.descuentoReal, 3) && (oferta.cantidad + oferta.cantidadOferta) >= d.CantidadMínima
                && (d.FiltroProducto == null || string.IsNullOrEmpty(d.FiltroProducto.Trim()) || producto.Nombre.StartsWith(d.FiltroProducto))
            );
            if (descuentoAutorizado != null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "Hay un descuento autorizado del " + descuentoAutorizado.Descuento.ToString("P2")
                };
            }

            DescuentosProducto precioAutorizado = descuentos.FirstOrDefault(d =>
                d.Precio <= Math.Round(oferta.precioCalculado, 2, MidpointRounding.AwayFromZero) && oferta.cantidad >= d.CantidadMínima);
            if (precioAutorizado != null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "Hay un precio autorizado de " + precioAutorizado.Precio.Value.ToString("C")
                };
            }

            return new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No se encuentra autorizado el descuento del " + oferta.descuentoReal.Value.ToString("P2")
                    + " para el producto " + producto.Número.Trim(),
                ProductoId = producto.Número.Trim()
            };
        }
    }
}