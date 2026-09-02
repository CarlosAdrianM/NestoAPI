using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#446: qué precios puede ver la persona de contacto que ha iniciado sesión en la
    /// app de clientes, según su cargo. La regla vive aquí, en el servidor: la app solo pinta
    /// coherente con lo que le llega.
    ///
    /// <para>Tres niveles: <see cref="NivelPrecios.Completo"/> (lo de siempre: tarifa, "tu
    /// precio" con descuento, importes), <see cref="NivelPrecios.SinDescuentos"/> (cargo 31: ve
    /// la tarifa profesional, que es pública para profesionales, pero no el precio al que compra
    /// su empresa: ni descuentos, ni importes del pedido) y <see cref="NivelPrecios.SinPrecios"/>
    /// (cargo 30: solo el PVP público). Si un mismo correo tiene varios cargos, manda el más
    /// restrictivo.</para>
    ///
    /// <para>En los dos niveles restringidos el pedido va con la forma de pago habitual de la
    /// ficha, nunca con tarjeta (la pasarela enseñaría el importe), la respuesta va sin importes
    /// y no se calculan portes del carrito (el "te faltan X € para el envío gratis" es un
    /// importe).</para>
    /// </summary>
    public static class PoliticaPreciosOcultos
    {
        public const string CLAIM_NIVEL_PRECIOS = "NivelPrecios";

        public const string MOTIVO_SIN_FORMA_DE_PAGO_HABITUAL =
            "Tu usuario hace pedidos sin ver los precios y no puede pagar con tarjeta, " +
            "y ahora mismo la única forma de pago disponible para tu cuenta es la tarjeta. " +
            "Pídeselo al titular de la cuenta.";

        public const string MOTIVO_PORTES =
            "Tu usuario hace pedidos sin ver los precios: los gastos de envío se calculan al crear el pedido.";

        /// <summary>Ordenado de menos a más restrictivo: el mayor gana.</summary>
        public enum NivelPrecios
        {
            Completo = 0,
            SinDescuentos = 1,
            SinPrecios = 2
        }

        public static NivelPrecios NivelDeCargo(short? cargo)
        {
            if (cargo == Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_PRECIOS)
            {
                return NivelPrecios.SinPrecios;
            }
            if (cargo == Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_DESCUENTOS)
            {
                return NivelPrecios.SinDescuentos;
            }
            return NivelPrecios.Completo;
        }

        /// <summary>
        /// Con varios cargos para el mismo correo (22 + 30 + 31, por ejemplo) manda el más
        /// restrictivo: es lo que impide que un cargo de más abra lo que otro cierra.
        /// </summary>
        public static NivelPrecios NivelMasRestrictivo(IEnumerable<short?> cargos)
        {
            return cargos == null
                ? NivelPrecios.Completo
                : cargos.Select(NivelDeCargo).DefaultIfEmpty(NivelPrecios.Completo).Max();
        }

        public static NivelPrecios NivelDe(IIdentity identity)
        {
            ClaimsIdentity claims = identity as ClaimsIdentity;
            string valor = claims?.FindFirst(CLAIM_NIVEL_PRECIOS)?.Value;
            return !string.IsNullOrWhiteSpace(valor) && Enum.TryParse(valor, out NivelPrecios nivel)
                ? nivel
                : NivelPrecios.Completo;
        }

        public static bool EsUsuarioSinPrecios(IIdentity identity)
        {
            return NivelDe(identity) == NivelPrecios.SinPrecios;
        }

        /// <summary>Los dos niveles restringidos van sin importes, sin tarjeta y sin portes.</summary>
        public static bool OcultaImportes(IIdentity identity)
        {
            return NivelDe(identity) != NivelPrecios.Completo;
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

        /// <summary>
        /// Aplica el nivel al precio de cliente de un producto: SinPrecios lo deja a 0;
        /// SinDescuentos lo deja en la tarifa profesional (sin descuento ni precio especial).
        /// </summary>
        public static void AplicarNivel(ProductoPlantillaDTO producto, NivelPrecios nivel, decimal tarifaProfesional)
        {
            if (producto == null)
            {
                return;
            }
            switch (nivel)
            {
                case NivelPrecios.SinPrecios:
                    OcultarPrecio(producto);
                    break;
                case NivelPrecios.SinDescuentos:
                    producto.precio = tarifaProfesional;
                    producto.descuento = 0;
                    producto.aplicarDescuento = false;
                    producto.descuentoOculto = true;
                    break;
            }
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
            return string.Equals(formaPago?.Trim(), Constantes.FormasPago.TARJETA, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsPrepago(string plazosPago)
        {
            return string.Equals(plazosPago?.Trim(), Constantes.PlazosPago.PREPAGO, StringComparison.OrdinalIgnoreCase);
        }
    }
}
