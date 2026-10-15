using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#613: la línea negativa del cupón no es una oferta ni un descuento que tenga que pasar el pipeline de precios: sus
    /// reglas (cliente, fechas, uno por pedido, pedido mínimo) son reglas duras que mira ServicioCanjeCupones en el POST y el
    /// PUT, sin que nadie se las pueda saltar. Aquí solo se acepta, para que ningún validador de denegación la tumbe.
    /// </summary>
    public class ValidadorCupon : IValidadorAceptacion
    {
        private readonly string productoCupon;

        public ValidadorCupon() : this(null)
        {
        }

        internal ValidadorCupon(string productoCupon)
        {
            this.productoCupon = productoCupon;
        }

        public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio)
        {
            string producto = productoCupon ?? ReglasCupon.ProductoCupon;
            bool esCupon = ReglasCupon.EsProductoCupon(numeroProducto, producto)
                && (pedido?.Lineas?.Any(l => ReglasCupon.EsProductoCupon(l.Producto, producto) && l.Cantidad < 0) ?? false);
            return new RespuestaValidacion
            {
                ValidacionSuperada = esCupon,
                Motivo = esCupon
                    ? $"El producto {numeroProducto} es la línea de un cupón (sus reglas se comprueban al guardar el pedido)"
                    : $"El producto {numeroProducto} no es un cupón",
                ProductoId = numeroProducto
            };
        }
    }
}
