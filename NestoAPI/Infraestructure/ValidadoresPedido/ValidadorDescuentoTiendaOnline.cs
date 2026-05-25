using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    // NestoAPI#203: Prestashop aplica vouchers (cupones de descuento) que el módulo
    // CanalesExternos transforma en un descuento sobre la línea (hasta 5%). Ese
    // descuento ya viene aplicado por el motor de la tienda online, así que lo
    // aceptamos automáticamente cuando TODAS las líneas del pedido son WEB y el
    // descuento real no supera el tope. Si supera el tope, queremos que pase por
    // revisión manual (puede ser un error o un caso fuera de política).
    public class ValidadorDescuentoTiendaOnline : IValidadorAceptacion
    {
        public const decimal DESCUENTO_MAXIMO_VOUCHER = 0.05m;

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            RespuestaValidacion respuestaDenegada = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "El producto " + numeroProducto + " no es elegible para el descuento por voucher de tienda online",
                ProductoId = numeroProducto
            };

            if (pedido?.Lineas == null || !pedido.Lineas.Any())
            {
                return respuestaDenegada;
            }

            if (!pedido.Lineas.All(l => l.formaVenta == Constantes.FormasVenta.TIENDA_ONLINE))
            {
                return respuestaDenegada;
            }

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido(numeroProducto, pedido);
            if (oferta?.descuentoReal == null)
            {
                return respuestaDenegada;
            }

            decimal descuentoRedondeado = Math.Round(oferta.descuentoReal.Value, 3);
            if (descuentoRedondeado <= 0 || descuentoRedondeado > DESCUENTO_MAXIMO_VOUCHER)
            {
                return respuestaDenegada;
            }

            return new RespuestaValidacion
            {
                ValidacionSuperada = true,
                Motivo = "Descuento del " + descuentoRedondeado.ToString("P2") + " para el producto " + numeroProducto + " permitido por venir de tienda online (voucher Prestashop)",
                ProductoId = numeroProducto
            };
        }
    }
}
