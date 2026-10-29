using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#728 (TiendasNuevaVision#97): el método de pago que la app puede fijar en los cobros del pedido
    /// (POST api/Pedidos/Cliente) y del carrito (POST api/Pedidos/Cliente/Carrito/Pago).
    /// <list type="bullet">
    /// <item>Sin método (las versiones de la app de antes): todo como hasta ahora.</item>
    /// <item>"C" (tarjeta): como hasta ahora, pero la pasarela solo enseña tarjeta. Puede ir con tarjeta guardada.</item>
    /// <item>"xpay" (Apple Pay), "google" (Google Pay) y "z" (Bizum): no hay tarjeta guardada (no dejan token reutilizable, #178),
    /// así que no se manda TarjetaId ni se pide tokenizar. La app abre <c>Pago.UrlPaginaPago</c> en el navegador del sistema
    /// (Custom Tabs / SFSafariViewController): con el método fijado, la página <c>/pago/{token}</c> enseña solo ese botón y se
    /// envía sola (#728). Al terminar vuelve a la app por UrlOk/UrlKo (nestotiendas://pago/ok|ko).</item>
    /// </list>
    /// </summary>
    public static class MetodoPagoApp
    {
        public static string MensajeNoValido(string metodo) =>
            $"El método de pago «{metodo?.Trim()}» no es válido. Use C (tarjeta), z (Bizum), xpay (Apple Pay) o google (Google Pay), " +
            "o no lo indique para pagar como siempre.";

        public const string MENSAJE_SIN_TARJETA_GUARDADA =
            "Con Apple Pay, Google Pay o Bizum no se paga con una tarjeta guardada: no indique la tarjeta (TarjetaId) o elija pagar con tarjeta.";

        /// <summary>
        /// Comprueba el método de la petición y lo deja normalizado en <paramref name="metodo"/> (null = sin fijar). Devuelve el
        /// motivo del 400 o null si vale.
        /// </summary>
        public static string Validar(PedidoClienteRequest peticion, out string metodo)
        {
            metodo = null;
            string pedido = peticion?.MetodoPago;
            if (string.IsNullOrWhiteSpace(pedido))
            {
                return null;
            }
            if (!MetodosPagoRedsys.EsValido(pedido))
            {
                return MensajeNoValido(pedido);
            }
            metodo = MetodosPagoRedsys.Normalizar(pedido);
            if (!UsaTarjetaGuardada(metodo) && (peticion.TarjetaId.HasValue || peticion.PagarConTarjetaGuardada))
            {
                return MENSAJE_SIN_TARJETA_GUARDADA;
            }
            return null;
        }

        /// <summary>Sin método o con tarjeta ("C") se puede pagar con una tarjeta guardada; con wallet o Bizum, no.</summary>
        public static bool UsaTarjetaGuardada(string metodo) =>
            string.IsNullOrEmpty(metodo) || metodo == MetodosPagoRedsys.TARJETA;
    }
}
