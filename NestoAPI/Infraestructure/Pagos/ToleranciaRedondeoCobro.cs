using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#824 (929852, 05/11/26): la tolerancia de redondeo al liquidar con un cobro con tarjeta el efecto de una factura.
    /// <para>Caso real: el enlace del pedido 929852 se generó por 40,02 € (el total del pedido antes de facturar) y la factura
    /// NV2617824 salió por 40,03 € (la factura redondea línea a línea, <see cref="RoundingHelper"/>). Con la regla estricta del
    /// 31/10 (solo se liquida si el pendiente es justo el importe; si no, a cuenta) el cobro quedó a cuenta, el efecto siguió
    /// pendiente y el reembolso del envío no bajó aunque el cliente ya había pagado: la agencia fue a cobrarle otra vez.</para>
    /// <para>Por qué 0,05 €: es una diferencia que solo puede venir del redondeo (varias líneas redondeadas al céntimo), nunca
    /// de un pago parcial real; por encima sigue la regla estricta (a cuenta), porque ya puede ser otra cosa. El efecto se
    /// liquida ENTERO y la diferencia va a la pareja de cuentas en la que Administración regulariza hoy a mano esos céntimos
    /// (Carlos 05/11/26): si el cliente paga de menos, al Debe de <see cref="Constantes.Cuentas.REGULARIZACION_SALDO"/>
    /// (65900004, gasto); si paga de más, al Haber de <see cref="Constantes.Cuentas.REGULARIZACION_SALDO_INGRESO"/> (75900004,
    /// ingreso).</para>
    /// </summary>
    public static class ToleranciaRedondeoCobro
    {
        /// <summary>La diferencia máxima (en valor absoluto) que se trata como redondeo.</summary>
        public const decimal MAXIMA = 0.05m;

        /// <summary>¿Es una diferencia de redondeo (distinta de 0 y como mucho <see cref="MAXIMA"/>)?</summary>
        public static bool EsRedondeo(decimal diferencia)
            => diferencia != 0m && Math.Abs(RoundingHelper.DosDecimalesRound(diferencia)) <= MAXIMA;

        /// <summary>¿Lo pendiente y lo cobrado son iguales o solo se diferencian en el redondeo?</summary>
        public static bool CuadraConRedondeo(decimal pendiente, decimal importe)
        {
            decimal diferencia = RoundingHelper.DosDecimalesRound(importe) - RoundingHelper.DosDecimalesRound(pendiente);
            return diferencia == 0m || EsRedondeo(diferencia);
        }

        /// <summary>
        /// Pura (para los tests): los efectos que liquida el enlace de pago de un pedido ya facturado (los de cartera pendientes de
        /// su factura). Se liquidan ENTEROS si su pendiente suma lo cobrado o se queda a <see cref="MAXIMA"/>; si no (o no hay
        /// ninguno: el pedido aún no se ha facturado), null y el cobro queda a cuenta, como hasta ahora. No entran los remesados
        /// (ya se están cobrando por el banco) ni los de deuda vencida.
        /// </summary>
        public static List<PagoTPV_Efecto> EfectosDelEnlaceDePedido(IEnumerable<ExtractoCliente> efectosDeLaFactura, decimal importe)
        {
            List<ExtractoCliente> pendientes = (efectosDeLaFactura ?? Enumerable.Empty<ExtractoCliente>())
                .Where(e => e != null && e.ImportePdte > 0m && !e.Remesa.HasValue
                    && e.TipoApunte?.Trim() == Constantes.TiposExtractoCliente.CARTERA
                    && e.Estado?.Trim() != Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                .OrderBy(e => e.Nº_Orden)
                .ToList();
            if (pendientes.Count == 0 || importe <= 0m)
            {
                return null;
            }
            decimal total = pendientes.Sum(e => RoundingHelper.DosDecimalesRound(e.ImportePdte));
            if (!CuadraConRedondeo(total, importe))
            {
                return null;
            }
            return pendientes.Select(e => new PagoTPV_Efecto
            {
                ExtractoClienteId = e.Nº_Orden,
                Importe = RoundingHelper.DosDecimalesRound(e.ImportePdte),
                Documento = e.Nº_Documento?.Trim(),
                Efecto = e.Efecto?.Trim(),
                Contacto = e.Contacto?.Trim(),
                Vendedor = e.Vendedor?.Trim(),
                FormaVenta = e.FormaVenta?.Trim(),
                Delegacion = e.Delegación?.Trim(),
                TipoApunte = e.TipoApunte?.Trim()
            }).ToList();
        }
    }
}
