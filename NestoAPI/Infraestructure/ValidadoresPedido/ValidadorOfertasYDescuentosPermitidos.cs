using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOfertasYDescuentosPermitidos : IValidadorDenegacion
    {
        // TODO: refactorizar para dividir en dos validadores más sencillos (ofertas y descuentos)

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion {
                ValidacionSuperada = true,
                Motivo = "El pedido " + pedido.numero + " no tiene ninguna línea de productos"
            };

            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido.Where(l => l.tipoLinea == 1)) //1=Producto
            {
                Producto producto = servicio.BuscarProducto(linea.producto);

                if (producto == null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        AutorizadaDenegadaExpresamente = false,
                        Motivo = "No existe el producto " + linea.producto + " en la línea " + linea.id.ToString(),
                        ProductoId = linea.producto
                    };
                }

                if (producto.Ficticio && producto.Familia != null && producto.Familia.Trim() == Constantes.Productos.FAMILIA_BONIFICACION)
                {
                    continue;
                }

                PrecioDescuentoProducto datos = new PrecioDescuentoProducto
                {
                    precioCalculado = linea.precio,
                    descuentoCalculado = linea.descuento,
                    producto = producto,
                    cantidad = linea.cantidad,
                    aplicarDescuento = linea.aplicarDescuento
                };

                respuesta = EsOfertaPermitida(producto, pedido);
                
                // Una vez que una línea no es válida, todo el pedido deja de ser válido
                if (!respuesta.ValidacionSuperada) 
                {
                    break;
                }
            }
            

            return respuesta;
        }

        public static RespuestaValidacion EsOfertaPermitida(Producto producto, PedidoVentaDTO pedido)
        {
            /*
             *  Validaciones a la hora de insertar una oferta permitida:
             *  - No puede tener contacto si no tiene cliente (sí al revés)
             *  - No puede tener familia y producto en blanco. Al menos hay que poner una.
             *  - Hay que crear "Filtro nombre", para poder buscar productos que tengan
             *    un texto ("Esmalte F ", por ejemplo)
             *  - No tiene sentido un precio fijo para toda la familia (son productos diferentes)
             */

            if (producto == null || pedido == null)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "El producto o el pedido no existen"
                };
            }

            PrecioDescuentoProducto oferta = MontarOfertaPedido(producto.Número, pedido);

            // Si no tiene ninguna oferta ni descuento, está siempre permitido
            if ((oferta.cantidadOferta == 0 && oferta.descuentoReal == 0) || oferta.descuentoReal < 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "El producto " + producto.Número.Trim() + " no lleva oferta ni descuento"
                };
            }

            // Si oferta.cantidad es 0, comprobamos más abajo si se puede o no regalar el producto
            if (oferta.cantidadOferta != 0 && oferta.precioCalculado < producto.PVP && oferta.cantidad > 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "Oferta a precio inferior al de ficha en el producto " + producto.Número.Trim(),
                    ProductoId = producto.Número.Trim()
                };
            }

            if (oferta.cantidadOferta != 0 && oferta.descuentoCalculado > 0)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Motivo = "Oferta no puede llevar descuento en el producto " + producto.Número.Trim(),
                    ProductoId = producto.Número.Trim()
                };
            }

            
            List<OfertaPermitida> ofertas = GestorPrecios.servicio.BuscarOfertasPermitidas(producto.Número);

            // Hacemos el cast (double) para que no sea división entera y 11/5 no de 2
            // Lo que mira es que sea múltiplo de la oferta y que sea mayor (no permite 3+1 
            // si lo autorizado es 6+2, por ejemplo).
            IEnumerable<OfertaPermitida> ofertasFiltradas = ofertas.Where(o =>
                (o.Cliente == null || o.Cliente == pedido.cliente) &&
                (o.Contacto == null || o.Cliente == pedido.cliente && o.Contacto == pedido.contacto)
            );

            // Si hay oferta específica para el producto, la cogemos
            IEnumerable<OfertaPermitida> ofertasEspecificasProducto = ofertasFiltradas.Where(o => o.Número?.Trim() == producto.Número.Trim());

            if (ofertasEspecificasProducto != null && ofertasEspecificasProducto.Count() > 0)
            {
                ofertasFiltradas = ofertasEspecificasProducto;
            }

            OfertaPermitida ofertaEncontrada = ofertasFiltradas.FirstOrDefault(o =>
                (
                ((double)oferta.cantidad / o.CantidadConPrecio == (double)oferta.cantidadOferta / o.CantidadRegalo &&
                (double)oferta.cantidadOferta / o.CantidadRegalo >= 1)
                || // para que acepte el 3+1 si está aceptado el 2+1, por ejemplo
                ((double)oferta.cantidad / oferta.cantidadOferta / o.CantidadRegalo > (double)o.CantidadConPrecio / oferta.cantidadOferta / o.CantidadRegalo)
                )
                && (o.FiltroProducto == null || o.FiltroProducto.Trim()=="" || producto.Nombre.StartsWith(o.FiltroProducto))
            );

            OfertaPermitida ofertaCombinada = ofertasFiltradas.FirstOrDefault(o =>
                o.FiltroProducto != null && o.FiltroProducto.Trim() != "" && producto.Nombre.StartsWith(o.FiltroProducto)
            );

            // si solo es una línea de regalo, que entre por descuento
            if (oferta.cantidadOferta > 0 && (oferta.cantidad > 0 || ofertaCombinada != null))
            {
                // también hay que controlar que denegar en OfertasPermitidas == false --> otro test
                if (ofertaEncontrada != null)
                {
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = true,
                        Motivo = "Existe una oferta autorizada expresa de " + oferta.cantidad.ToString()
                        + "+" + oferta.cantidadOferta.ToString() + " del producto " + producto.Número.Trim(),
                        AutorizadaDenegadaExpresamente = true,
                        ProductoId = producto.Número.Trim()
                    };
                }

                if (ofertasEspecificasProducto != null && ofertasEspecificasProducto.Count() > 0)
                {
                    OfertaPermitida ofertaEspecifica = ofertasEspecificasProducto.FirstOrDefault();
                    return new RespuestaValidacion
                    {
                        ValidacionSuperada = false,
                        Motivo = "La oferta máxima para el producto " + producto.Número.Trim() +
                    " es el " + ofertaEspecifica.CantidadConPrecio.ToString() + "+" + ofertaEspecifica.CantidadRegalo.ToString(),
                        ProductoId = producto.Número.Trim()
                    };
                }

                if (ofertaCombinada!=null)
                {
                    // comprobar si cumple la ofertacombinada
                    // leer productos del pedido que cumplen el FiltroProducto
                    var lineas = GestorPrecios.servicio.FiltrarLineas(pedido, ofertaCombinada.FiltroProducto, ofertaCombinada.Familia);
                    int cantidadCobrada = 0;
                    int cantidadOferta = 0;
                    foreach (LineaPedidoVentaDTO linea in lineas)
                    {
                        if (linea.baseImponible == 0) {
                            cantidadOferta += linea.cantidad;
                        } else {
                            cantidadCobrada += linea.cantidad;
                        };
                    }
                    if (cantidadCobrada>=ofertaCombinada.CantidadConPrecio && 
                        cantidadOferta <= ofertaCombinada.CantidadRegalo * cantidadCobrada / ofertaCombinada.CantidadConPrecio)
                    {
                        return new RespuestaValidacion
                        {
                            ValidacionSuperada = true,
                            Motivo = "Se permite el " + cantidadCobrada.ToString()
                        + "+" + cantidadOferta.ToString() + " para el filtro de producto " + ofertaCombinada.FiltroProducto,
                            AutorizadaDenegadaExpresamente = true,
                            ProductoId = producto.Número.Trim()
                        };
                    }
                }

            }
            else
            {
                // mirar si está en Descuentos producto para ese cliente, familia o producto
                IEnumerable<DescuentosProducto> descuentos = GestorPrecios.servicio.BuscarDescuentosPermitidos(oferta.producto.Número, pedido.cliente, pedido.contacto);

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

            return new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No se encuentra autorización para la oferta del producto " + producto.Número.Trim(),
                ProductoId = producto.Número.Trim()
            };
        }

        /*
         * Dado un pedido y un producto determinados, nos devuelve la oferta de ese producto que 
         * hay que ese pedido.
         */
        public static PrecioDescuentoProducto MontarOfertaPedido(String numeroProducto, PedidoVentaDTO pedido)
        {
            if (numeroProducto != null)
            {
                numeroProducto = numeroProducto.Trim();
            }
            List<string> productosMismoPrecio = ProductosMismoPrecio(numeroProducto, pedido);
            IEnumerable<LineaPedidoVentaDTO> lineasProducto = pedido.LineasPedido.Where(p => productosMismoPrecio.Contains(p.producto));
            if (lineasProducto == null || lineasProducto.Count() == 0)
            {
                return null;
            }

            IEnumerable<LineaPedidoVentaDTO> lineasConPrecio = lineasProducto.Where(l => l.baseImponible / l.cantidad != 0);
            IEnumerable<LineaPedidoVentaDTO> lineasSinPrecio = lineasProducto.Where(l => l.baseImponible / l.cantidad == 0);

            Producto producto = GestorPrecios.servicio.BuscarProducto(numeroProducto);

            if (!lineasSinPrecio.Any())
            {
                return new PrecioDescuentoProducto
                {
                    cantidadOferta = (short)lineasSinPrecio.Where(l => l.producto == numeroProducto).Sum(l => l.cantidad),
                    cantidad = (short)lineasConPrecio.Where(l => l.producto == numeroProducto).Sum(l => l.cantidad),
                    producto = producto,
                    precioCalculado = (decimal)Math.Round(lineasConPrecio.Where(l=> l.producto == numeroProducto).Select(l => l.precio).DefaultIfEmpty().Average(), 2, MidpointRounding.AwayFromZero),
                    descuentoCalculado = lineasConPrecio.Where(l => l.producto == numeroProducto).Select(l => 1 - (1-l.descuento) * (1-l.descuentoProducto)).DefaultIfEmpty().Average()
                };
            }

            return new PrecioDescuentoProducto
            {
                cantidadOferta = (short)lineasSinPrecio.Sum(l => l.cantidad),
                cantidad = (short)lineasConPrecio.Sum(l => l.cantidad),
                producto = producto,
                precioCalculado = (decimal)lineasConPrecio.Select(l => l.precio).DefaultIfEmpty().Average(),
                descuentoCalculado = lineasConPrecio.Select(l => 1 - (1 - l.descuento) * (1 - l.descuentoProducto)).DefaultIfEmpty().Average()
            };

        }

        private static List<string> ProductosMismoPrecio(string numeroProducto, PedidoVentaDTO pedido)
        {
            Producto productoBuscado = GestorPrecios.servicio.BuscarProducto(numeroProducto);
            List<string> productosMismoPrecio = new List<string>();
            foreach (string productoLinea in pedido.LineasPedido.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO).Select(l=>l.producto).Distinct())
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