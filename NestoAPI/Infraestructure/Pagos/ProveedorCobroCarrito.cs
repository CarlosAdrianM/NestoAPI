using NestoAPI.Models;
using System;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#663: lo que cambia de un proveedor de cobro del carrito a otro. El flujo (cobrar antes de crear el pedido,
    /// reservar el cobro, crear el pedido, apuntar el Prepago, deshacer el cobro si el pedido no sale y recoger los
    /// huérfanos) es UNO y vive en <see cref="ServicioPagos"/>, <c>PedidosClienteController</c> y
    /// <see cref="CobrosCarritoJobsService"/>; aquí solo está lo que es propio de cada uno:
    /// <list type="bullet">
    /// <item><description>Redsys (TNV#68): tarjeta guardada, TAR / PRE, se DEVUELVE por Redsys, vale 30 minutos desde el cobro.</description></item>
    /// <item><description>Aplázame (#663): financiación, TRN / PRE como los pedidos de PrestaShop pagados con Aplázame, se
    /// CANCELA en Aplázame, vale 2 horas desde que lo confirmamos (el cliente puede tardar en pasar la validación de
    /// identidad, y el reloj empieza cuando Aplázame lo aprueba, no cuando se abrió el checkout).</description></item>
    /// </list>
    /// </summary>
    public sealed class ProveedorCobroCarrito
    {
        private ProveedorCobroCarrito() { }

        /// <summary>PagoTPV.Tipo de sus cobros.</summary>
        public string Tipo { get; private set; }

        /// <summary>Cómo se le nombra en los avisos internos («el panel de ...»).</summary>
        public string Nombre { get; private set; }

        /// <summary>Forma y plazos de pago con los que se crea el pedido.</summary>
        public string FormaPago { get; private set; }
        public string PlazosPago { get; private set; }

        /// <summary>Cuenta del Prepago que se apunta al pedido.</summary>
        public string CuentaPrepago { get; private set; }

        /// <summary>Lo que va delante del pedido en el concepto del Prepago.</summary>
        public string PrefijoConceptoPrepago { get; private set; }

        /// <summary>Cuántos minutos vale el cobro, sin pedido, para crear el pedido; después se deshace.</summary>
        public int MinutosValidez { get; private set; }

        /// <summary>Si los minutos cuentan desde la confirmación del proveedor (Aplázame) o desde que se creó el cobro (Redsys).</summary>
        public bool CuentaDesdeLaConfirmacion { get; private set; }

        public static readonly ProveedorCobroCarrito Redsys = new ProveedorCobroCarrito
        {
            Tipo = Constantes.TiposPagoTPV.CARRITO_APP,
            Nombre = "Redsys",
            FormaPago = Constantes.FormasPago.TARJETA,
            PlazosPago = Constantes.PlazosPago.PREPAGO,
            CuentaPrepago = Constantes.Prepagos.CUENTA_REDSYS,
            PrefijoConceptoPrepago = "Tarjeta app pedido",
            MinutosValidez = ServicioPagos.MINUTOS_VALIDEZ_COBRO_CARRITO,
            CuentaDesdeLaConfirmacion = false
        };

        public static readonly ProveedorCobroCarrito Aplazame = new ProveedorCobroCarrito
        {
            Tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME,
            Nombre = "Aplázame",
            // Igual que CanalExternoPedidosPrestashop (FORMA_PAGO_APLAZAME): TRN / PRE / 57200013
            FormaPago = Constantes.FormasPago.TRANSFERENCIA,
            PlazosPago = Constantes.PlazosPago.PREPAGO,
            CuentaPrepago = Constantes.Prepagos.CUENTA_APLAZAME,
            PrefijoConceptoPrepago = "Aplazame app pedido",
            MinutosValidez = MINUTOS_VALIDEZ_APLAZAME,
            CuentaDesdeLaConfirmacion = true
        };

        /// <summary>NestoAPI#663: un cobro de Aplázame confirmado y sin pedido a las 2 horas se cancela.</summary>
        public const int MINUTOS_VALIDEZ_APLAZAME = 120;

        /// <summary>El proveedor de un cobro del carrito, o null si no es un cobro del carrito.</summary>
        public static ProveedorCobroCarrito De(PagoTPV pago) => DeTipo(pago?.Tipo);

        public static ProveedorCobroCarrito DeTipo(string tipo)
        {
            string limpio = tipo?.Trim();
            if (string.Equals(limpio, Redsys.Tipo, StringComparison.OrdinalIgnoreCase))
            {
                return Redsys;
            }
            if (string.Equals(limpio, Aplazame.Tipo, StringComparison.OrdinalIgnoreCase))
            {
                return Aplazame;
            }
            return null;
        }

        public static bool EsAplazame(PagoTPV pago) => De(pago) == Aplazame;

        /// <summary>Hasta cuándo vale el cobro, sin pedido, para crear el pedido.</summary>
        public DateTime CaducaEl(PagoTPV pago)
        {
            DateTime desde = CuentaDesdeLaConfirmacion ? (pago.FechaConfirmacion ?? pago.FechaCreacion) : pago.FechaCreacion;
            return desde.AddMinutes(MinutosValidez);
        }

        /// <summary>
        /// El concepto del Prepago: qué es, de qué pedido y el número de orden (o el mid de Aplázame), que es único por
        /// cobro y hace idempotente apuntarlo. 50 caracteres.
        /// </summary>
        public string ConceptoPrepago(int numeroPedido, string numeroOrden)
        {
            string concepto = $"{PrefijoConceptoPrepago} {numeroPedido} {numeroOrden?.Trim()}";
            return concepto.Length > 50 ? concepto.Substring(0, 50) : concepto;
        }
    }
}
