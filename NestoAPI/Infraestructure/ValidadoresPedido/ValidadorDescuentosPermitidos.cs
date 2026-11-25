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

            // Obtener productos únicos para evitar validar el mismo producto varias veces.
            // Issue #237: se omiten los productos cuyas líneas están TODAS intactas (preexistentes,
            // sin tocar y sin oferta; marcadas por el controller). Así una línea correcta que no se
            // modifica no se re-bloquea por una subida de tarifa posterior. Si alguna línea del
            // producto sí se toca (o es nueva), el producto se valida con normalidad.
            var productosUnicos = pedido.Lineas
                .Where(l => l.tipoLinea == 1)
                .GroupBy(l => l.Producto)
                .Where(g => g.Any(l => !l.NoRevalidarDescuento))
                .Select(g => g.Key);

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

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido(producto.Número, pedido, servicio);

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
            List<DescuentosProducto> todosLosDescuentos = descuentos?.ToList() ?? new List<DescuentosProducto>();
            descuentos = todosLosDescuentos;

            // Si hay un descuento específico PERMANENTE para el producto, prevalece sobre el de la familia o grupo (es el techo);
            // las filas de campaña del producto solo suman permiso (Carlos 25/11/26, caso Israel).
            descuentos = ReglaDescuentosQuePrevalecen.Filtrar(todosLosDescuentos, oferta.producto.Número, oferta.cantidad + oferta.cantidadOferta);

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

            // Distribuidores (Carlos 20/11/26): con una campaña marcada, el suyo y el de la campaña uno sobre otro.
            decimal? compuesto = DescuentoCompuestoDistribuidor(todosLosDescuentos, producto, oferta, pedido);
            if (compuesto.HasValue && Math.Round((decimal)oferta.descuentoReal, 3) <= compuesto.Value)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = true,
                    Motivo = "Hay un descuento de distribuidor sobre la campaña del " + compuesto.Value.ToString("P2")
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

        /// <summary>
        /// El compuesto que se autoriza a un distribuidor (<see cref="Campanas.DescuentoDistribuidorCampana.MaximoAutorizado"/>), o null
        /// si no hay campaña marcada vigente para el producto o el cliente no es distribuidor. Primero la marca (en memoria).
        /// </summary>
        internal static decimal? DescuentoCompuestoDistribuidor(List<DescuentosProducto> descuentos, Producto producto, PrecioDescuentoProducto oferta, PedidoVentaDTO pedido)
        {
            DateTime hoy = DateTime.Today;
            int cantidad = oferta.cantidad + oferta.cantidadOferta;
            List<DescuentosProducto> aplicables = descuentos.Where(d => d.CantidadMínima <= cantidad
                && (d.FiltroProducto == null || string.IsNullOrEmpty(d.FiltroProducto.Trim()) || producto.Nombre.StartsWith(d.FiltroProducto))).ToList();
            if (!aplicables.Any(d => Campanas.DescuentoDistribuidorCampana.EsFilaDeCampanaDeProducto(d, producto.Número)
                && Campanas.CampanasAcumulanDistribuidores.EstaMarcada(d.Campana, hoy)))
            {
                return null;
            }
            if (!Clientes.ClientesDistribuidores.EsDistribuidor(pedido.empresa, pedido.cliente))
            {
                return null;
            }
            return Campanas.DescuentoDistribuidorCampana.MaximoAutorizado(aplicables, producto.Número,
                c => Campanas.CampanasAcumulanDistribuidores.EstaMarcada(c, hoy));
        }
    }

    /// <summary>
    /// LA REGLA (pura) de qué filas de DescuentosProducto cuentan para autorizar el descuento de un producto (Carlos 25/11/26, caso
    /// Israel, cliente 206, Lisap 43165 con la fila de Black Friday del 25 % tapando el 52 % pactado por familia/grupo).
    /// - Con alguna fila específica PERMANENTE del producto (Nº_Producto = producto, sin Campana) aplicable por cantidad: solo cuentan
    ///   las específicas del producto aplicables (las permanentes, que son el techo, y las de campaña, que pueden subirlo esos días).
    /// - Si las únicas específicas aplicables son de campaña: cuentan TODAS (familia, grupo, cliente y campaña): la campaña autoriza
    ///   hasta su descuento y no tapa lo que ya estaba autorizado.
    /// - Sin específicas aplicables: todas, como siempre.
    /// </summary>
    public static class ReglaDescuentosQuePrevalecen
    {
        public static bool EsDeCampana(DescuentosProducto d) => d != null && !string.IsNullOrWhiteSpace(d.Campana);

        public static IEnumerable<DescuentosProducto> Filtrar(IEnumerable<DescuentosProducto> filas, string producto, int cantidad)
        {
            List<DescuentosProducto> todas = (filas ?? Enumerable.Empty<DescuentosProducto>()).Where(d => d != null).ToList();
            string numero = producto?.Trim();
            List<DescuentosProducto> especificasAplicables = todas
                .Where(d => d.Nº_Producto?.Trim() == numero && d.CantidadMínima <= cantidad)
                .ToList();
            if (especificasAplicables.Any(d => !EsDeCampana(d)))
            {
                return especificasAplicables;
            }
            return todas;
        }
    }
}