using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#446: una persona de contacto con el cargo "Pedidos sin ver precios" puede pedir
    /// desde la app de clientes, pero no puede saber a qué precio compra. La regla vive aquí,
    /// en el servidor: la app solo pinta coherente con lo que le llega.
    ///
    /// <para>Qué se tapa: el precio de cliente de un producto, los importes de la respuesta del
    /// pedido y el cálculo de portes del carrito (el "te faltan X € para el envío gratis" es un
    /// importe). Qué se fuerza: la forma de pago habitual de la ficha, porque la pasarela de
    /// tarjeta enseñaría el importe.</para>
    /// </summary>
    public static class PoliticaPreciosOcultos
    {
        public const string CLAIM_SIN_PRECIOS = "SinPrecios";

        public const string MOTIVO_SIN_FORMA_DE_PAGO_HABITUAL =
            "Tu usuario hace pedidos sin ver los precios y no puede pagar con tarjeta, " +
            "y ahora mismo la única forma de pago disponible para tu cuenta es la tarjeta. " +
            "Pídeselo al titular de la cuenta.";

        public const string MOTIVO_PORTES =
            "Tu usuario hace pedidos sin ver los precios: los gastos de envío se calculan al crear el pedido.";

        public static bool EsUsuarioSinPrecios(IIdentity identity)
        {
            ClaimsIdentity claims = identity as ClaimsIdentity;
            return claims?.FindFirst(CLAIM_SIN_PRECIOS)?.Value == "true";
        }

        /// <summary>
        /// Deja la petición sin tarjeta ni forma de pago elegida: el servidor resolverá la
        /// habitual de la ficha. Lo que llegue en el cuerpo se ignora (a propósito).
        /// </summary>
        public static void ForzarFormaDePagoHabitual(PedidoClienteRequest peticion)
        {
            if (peticion == null)
            {
                return;
            }
            peticion.PagarConTarjeta = false;
            peticion.PagarConTarjetaGuardada = false;
            peticion.TarjetaId = null;
            peticion.FormaPago = null;
            peticion.PlazosPago = null;
        }

        /// <summary>
        /// La forma y los plazos de pago habituales. Las condiciones del canal APP ya vienen
        /// filtradas a lo que la ficha del cliente tiene autorizado (más la tarjeta), así que la
        /// habitual es la primera que no sea tarjeta, con sus plazos que no sean prepago. Null si
        /// solo queda la tarjeta (ficha al contado, o con deuda), que es cuando el pedido se rechaza.
        /// </summary>
        public static FormaYPlazos ResolverFormaDePagoHabitual(CondicionesPagoResponse condiciones)
        {
            if (condiciones?.FormasPago == null)
            {
                return null;
            }
            FormaPagoDTO forma = condiciones.FormasPago.FirstOrDefault(f => !EsTarjeta(f.formaPago));
            if (forma == null)
            {
                return null;
            }

            PlazoPagoDTO plazos = (condiciones.PlazosPago ?? Enumerable.Empty<PlazoPagoDTO>())
                .OrderByDescending(p => !EsPrepago(p.plazoPago))
                .FirstOrDefault();

            return new FormaYPlazos
            {
                FormaPago = forma.formaPago.Trim(),
                PlazosPago = plazos?.plazoPago?.Trim() ?? condiciones.PlazoPagoRecomendado
            };
        }

        public static void OcultarPrecio(ProductoPlantillaDTO producto)
        {
            if (producto == null)
            {
                return;
            }
            producto.precio = 0;
            producto.descuento = 0;
            producto.aplicarDescuento = false;
            producto.precioOculto = true;
        }

        public static void OcultarImportes(PedidoClienteResponse respuesta)
        {
            if (respuesta == null)
            {
                return;
            }
            respuesta.BaseImponible = 0;
            respuesta.Total = 0;
            respuesta.Portes = 0;
            respuesta.ImportesOcultos = true;
            foreach (LineaPedidoClienteResponse linea in respuesta.Lineas ?? Enumerable.Empty<LineaPedidoClienteResponse>())
            {
                linea.PrecioUnitario = 0;
                linea.Descuento = 0;
                linea.BaseImponible = 0;
                linea.Total = 0;
            }
        }

        public class FormaYPlazos
        {
            public string FormaPago { get; set; }
            public string PlazosPago { get; set; }
        }

        private static bool EsTarjeta(string formaPago)
        {
            return string.Equals(formaPago?.Trim(), Constantes.FormasPago.TARJETA, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsPrepago(string plazosPago)
        {
            return string.Equals(plazosPago?.Trim(), Constantes.PlazosPago.PREPAGO, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
