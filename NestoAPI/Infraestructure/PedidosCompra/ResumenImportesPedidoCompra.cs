using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models.Informes;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#510: los importes de la «ORDEN DE COMPRA» (PDF y Excel) con el pronto pago a pie de
    /// documento, como en venta, en vez de mezclado en el descuento de cada línea.
    /// <para>En la base de datos el pronto pago va dentro de cada línea: <c>SumaDescuentos</c> lo incluye
    /// (trigger trgLinPedidoCmpUpd) y <c>BaseImponible</c> ya lo lleva restado. Aquí se despliega:
    /// la línea muestra su descuento SIN el PP y su importe SIN el PP; el pie muestra la base, el PP
    /// y el total. El total es la suma de las bases grabadas (lo que se va a facturar), y el importe
    /// del PP se obtiene por diferencia, para que el documento cuadre siempre al céntimo con la BD.</para>
    /// Sin pronto pago todo queda exactamente como antes.
    /// </summary>
    public class ResumenImportesPedidoCompra
    {
        public ResumenImportesPedidoCompra(IEnumerable<LineaPedidoCompraInformeDTO> lineas, decimal descuentoPP)
        {
            List<LineaPedidoCompraInformeDTO> lista = (lineas ?? Enumerable.Empty<LineaPedidoCompraInformeDTO>()).ToList();
            DescuentoPP = EsPPValido(descuentoPP) ? descuentoPP : 0;
            Total = lista.Sum(l => l.BaseImponible);
            Subtotal = TienePP ? lista.Sum(l => ImporteLineaSinPP(l, DescuentoPP)) : Total;
            ImportePP = Subtotal - Total;
        }

        /// <summary>Pronto pago en tanto por uno (0 si no hay).</summary>
        public decimal DescuentoPP { get; }
        public bool TienePP => DescuentoPP > 0;
        /// <summary>Suma de los importes de línea sin el pronto pago.</summary>
        public decimal Subtotal { get; }
        /// <summary>Lo que descuenta el pronto pago (Subtotal − Total).</summary>
        public decimal ImportePP { get; }
        /// <summary>Suma de las bases imponibles grabadas: lo que se factura.</summary>
        public decimal Total { get; }

        /// <summary>El pronto pago del pedido es el de sus líneas (todas llevan el mismo).</summary>
        public static decimal DescuentoPPDelPedido(IEnumerable<LineaPedidoCompraInformeDTO> lineas)
        {
            List<LineaPedidoCompraInformeDTO> lista = (lineas ?? Enumerable.Empty<LineaPedidoCompraInformeDTO>()).ToList();
            return lista.Any() ? lista.Max(l => l.DescuentoPP) : 0;
        }

        /// <summary>
        /// Descuento de la línea sin el pronto pago: deshace el factor (1 − PP) de la suma grabada.
        /// 0,4775 con PP 0,05 → 0,45.
        /// </summary>
        public static decimal DescuentoSinPP(decimal sumaDescuentos, decimal descuentoPP)
        {
            if (!EsPPValido(descuentoPP))
            {
                return sumaDescuentos;
            }
            return 1 - (1 - sumaDescuentos) / (1 - descuentoPP);
        }

        /// <summary>
        /// Importe de la línea sin el pronto pago, con el mismo redondeo que las líneas del pedido
        /// (ROUND(Bruto) − ROUND(Bruto × dto)). Si la línea no trae Bruto, se deshace el PP sobre la base.
        /// </summary>
        public static decimal ImporteLineaSinPP(LineaPedidoCompraInformeDTO linea, decimal descuentoPP)
        {
            if (linea == null)
            {
                return 0;
            }
            if (!EsPPValido(descuentoPP))
            {
                return linea.BaseImponible;
            }
            if (linea.Bruto == 0)
            {
                return RoundingHelper.DosDecimalesRound(linea.BaseImponible / (1 - descuentoPP));
            }
            decimal dtoSinPP = DescuentoSinPP(linea.SumaDescuentos, descuentoPP);
            return RoundingHelper.DosDecimalesRound(linea.Bruto) - RoundingHelper.DosDecimalesRound(linea.Bruto * dtoSinPP);
        }

        private static bool EsPPValido(decimal pp) => pp > 0 && pp < 1;
    }
}
