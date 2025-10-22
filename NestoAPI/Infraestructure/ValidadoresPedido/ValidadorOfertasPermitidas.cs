using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    public class ValidadorOfertasPermitidas : IValidadorDenegacion
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

                RespuestaValidacion respuestaProducto = EsOfertaPermitida(producto, pedido, servicio);

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

        internal static RespuestaValidacion EsOfertaPermitida(Producto producto, PedidoVentaDTO pedido, IServicioPrecios servicio)
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

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido(producto.Número, pedido);

            if (oferta == null)
                return new RespuestaValidacion { ValidacionSuperada = true };

            List<OfertaPermitida> ofertas = servicio.BuscarOfertasPermitidas(producto.Número);

            IEnumerable<OfertaPermitida> ofertasFiltradas = ofertas.Where(o =>
                (o.Cliente == null || o.Cliente == pedido.cliente) &&
                (o.Contacto == null || (o.Cliente == pedido.cliente && o.Contacto == pedido.contacto))
            );

            OfertaPermitida ofertaCombinada = ofertasFiltradas.FirstOrDefault(o =>
                o.FiltroProducto != null && o.FiltroProducto.Trim() != "" && producto.Nombre.StartsWith(o.FiltroProducto)
            );

            // Lógica clave de separación entre ofertas y descuentos:
            // Solo validamos como OFERTA si hay cantidadOferta > 0 Y (cantidad > 0 O ofertaCombinada)
            // Si solo hay líneas de regalo sin cantidad cobrada y sin oferta combinada, debe validarse como descuento
            if (!(oferta.cantidadOferta > 0 && (oferta.cantidad > 0 || ofertaCombinada != null)))
            {
                // No entra en validación de ofertas, devolvemos true para que pase al ValidadorDescuentosPermitidos
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

            // Validaciones específicas de ofertas
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
                && (o.FiltroProducto == null || o.FiltroProducto.Trim() == "" || producto.Nombre.StartsWith(o.FiltroProducto))
            );

            // Validación de ofertas (ya sabemos que cantidad > 0 o hay oferta combinada por el if de arriba)
            if (oferta.cantidad > 0 || ofertaCombinada != null)
            {
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

                if (ofertaCombinada != null)
                {
                    var lineas = servicio.FiltrarLineas(pedido, ofertaCombinada.FiltroProducto, ofertaCombinada.Familia);
                    int cantidadCobrada = 0;
                    int cantidadOferta = 0;
                    foreach (LineaPedidoVentaDTO linea in lineas)
                    {
                        if (linea.BaseImponible == 0)
                        {
                            cantidadOferta += linea.Cantidad;
                        }
                        else
                        {
                            cantidadCobrada += linea.Cantidad;
                        }
                    }
                    if (cantidadCobrada >= ofertaCombinada.CantidadConPrecio &&
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

            return new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No se encuentra autorización para la oferta del producto " + producto.Número.Trim(),
                ProductoId = producto.Número.Trim()
            };
        }
    }
}