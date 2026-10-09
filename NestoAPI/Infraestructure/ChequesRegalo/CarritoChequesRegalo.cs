using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (TNV): lo que el carrito de la app le enseña a la clienta sobre su cheque. Va en la respuesta de
    /// <c>POST api/Pedidos/Cliente/Portes</c>, que es la que calcula el total que se enseña y se cobra.
    /// </summary>
    public class ChequeRegaloCarritoDTO
    {
        /// <summary>El cheque está descontado en el total del carrito (TotalConIva ya lo lleva).</summary>
        public bool Aplicado { get; set; }
        public string Producto { get; set; }
        /// <summary>Base imponible del cheque (50): resta del pedido con su IVA.</summary>
        public decimal Importe { get; set; }
        /// <summary>Si no se ha podido aplicar, el código CHEQUE_REGALO_* (el carrito se calcula sin el cheque).</summary>
        public string Codigo { get; set; }
        /// <summary>Frase para la clienta.</summary>
        public string Mensaje { get; set; }
        /// <summary>Con CHEQUE_REGALO_MINIMO_NO_SUPERADO, cuánto le falta de producto computable (base imponible).</summary>
        public decimal? Falta { get; set; }
    }

    /// <summary>El cheque del carrito: si lo lleva y, si no se puede usar, por qué (con el texto para la clienta).</summary>
    public class ResultadoChequeCarrito
    {
        public bool LlevaCheque { get; set; }
        public string Producto { get; set; }
        public decimal Importe { get; set; }
        public NestoBusinessException Error { get; set; }
        public bool Aplicado => LlevaCheque && Error == null;

        public ChequeRegaloCarritoDTO ParaElCarrito()
        {
            if (!LlevaCheque)
            {
                return null;
            }
            return new ChequeRegaloCarritoDTO
            {
                Aplicado = Aplicado,
                Producto = Producto,
                Importe = Importe,
                Codigo = Error?.Context?.ErrorCode,
                Mensaje = Aplicado ? CarritoChequesRegalo.MensajeAplicado(Importe) : Error.Message,
                Falta = CarritoChequesRegalo.Falta(Error)
            };
        }
    }

    /// <summary>
    /// NestoAPI#593 (TNV): el canje del cheque regalo desde la app de clientas (<c>api/Pedidos/Cliente</c>: carrito,
    /// portes, cobro del carrito y pedido). No hay reglas propias: la línea la normaliza y la comprueba el MISMO
    /// <see cref="IServicioCanjeChequesRegalo"/> que POST/PUT de api/PedidosVenta (que vuelve a hacerlo al crear el pedido
    /// y es quien reserva el cheque). Aquí se hace antes para que el carrito enseñe el total con el cheque (y el cobro
    /// por adelantado cuadre con el pedido) o diga por qué no se puede usar sin llegar a cobrar nada.
    /// <para>La clienta solo puede canjear SU cheque: el cliente del pedido sale del JWT (ConstructorPedidoCliente), así
    /// que el cheque se busca siempre en el suyo; si no tiene, CHEQUE_REGALO_SIN_CHEQUE.</para>
    /// </summary>
    public class CarritoChequesRegalo
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        private readonly IServicioCanjeChequesRegalo servicio;

        public CarritoChequesRegalo(IServicioCanjeChequesRegalo servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        public static bool EsLineaCheque(string producto, IEnumerable<string> productosCheque)
            => !string.IsNullOrWhiteSpace(producto) && productosCheque != null
               && productosCheque.Any(p => string.Equals(p?.Trim(), producto.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Normaliza la línea del cheque del pedido ya montado (cantidad −1, importe de la campaña, IVA del producto) y
        /// comprueba las reglas del canje como pedido nuevo. No reserva nada: eso lo hace PostPedidoVenta al guardar.
        /// </summary>
        public async Task<ResultadoChequeCarrito> Preparar(PedidoVentaDTO pedido, IEnumerable<string> productosCheque)
        {
            List<string> productos = (productosCheque ?? Enumerable.Empty<string>()).ToList();
            if (pedido?.Lineas == null || !pedido.Lineas.Any(l => EsLineaCheque(l.Producto, productos)))
            {
                return new ResultadoChequeCarrito();
            }

            await servicio.NormalizarLineas(pedido).ConfigureAwait(false);
            LineaPedidoVentaDTO lineaCheque = pedido.Lineas.First(l => EsLineaCheque(l.Producto, productos));
            var resultado = new ResultadoChequeCarrito
            {
                LlevaCheque = true,
                Producto = lineaCheque.Producto?.Trim(),
                Importe = lineaCheque.PrecioUnitario
            };
            try
            {
                _ = await servicio.Comprobar(pedido.empresa, 0, pedido.cliente, LineasParaComprobar(pedido), pedido.iva,
                    null, pedido.Usuario, pedidoNuevo: true).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex)
            {
                resultado.Error = ParaLaClienta(ex);
            }
            return resultado;
        }

        /// <summary>Quita las líneas del cheque (el carrito se calcula sin él cuando no se puede usar).</summary>
        public static void QuitarLineasCheque(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null)
            {
                return;
            }
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => l.EsChequeRegalo).ToList())
            {
                _ = pedido.Lineas.Remove(linea);
            }
        }

        /// <summary>Las líneas del DTO como las verá PostPedidoVenta (LinPedidoVta), con su base imponible.</summary>
        internal static List<LinPedidoVta> LineasParaComprobar(PedidoVentaDTO pedido)
            => pedido.Lineas.Select(l => new LinPedidoVta
            {
                Empresa = pedido.empresa,
                TipoLinea = l.tipoLinea,
                Producto = l.Producto,
                Texto = l.texto,
                Cantidad = (short)l.Cantidad,
                Precio = l.PrecioUnitario,
                Descuento = l.DescuentoLinea,
                DescuentoProducto = l.DescuentoProducto,
                Aplicar_Dto = l.AplicarDescuento,
                NºOferta = l.oferta,
                IVA = l.iva,
                Base_Imponible = l.BaseImponible,
                Estado = l.estado,
                Picking = 0
            }).ToList();

        internal static string MensajeAplicado(decimal importe)
            => $"Tu cheque regalo de {PlantillaCorreoChequeRegalo.Euros(importe)} € está descontado en este pedido.";

        internal static decimal? Falta(NestoBusinessException error)
        {
            if (error?.Context?.AdditionalData == null || !error.Context.AdditionalData.TryGetValue("falta", out object falta) || falta == null)
            {
                return null;
            }
            try
            {
                return Convert.ToDecimal(falta, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// El mismo error (código y detalles intactos, para que la app los lea) con un texto para la clienta: los de
        /// c4 hablan a quien pasa el pedido («quite la línea CHEQUE50_OCT26»).
        /// </summary>
        internal static NestoBusinessException ParaLaClienta(NestoBusinessException ex)
            => new NestoBusinessException(MensajeParaLaClienta(ex), ex.Context, ex);

        internal static string MensajeParaLaClienta(NestoBusinessException ex)
        {
            Dictionary<string, object> datos = ex?.Context?.AdditionalData ?? new Dictionary<string, object>();
            switch (ex?.Context?.ErrorCode)
            {
                case ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE:
                    return "No tienes ningún cheque regalo para usar en este pedido. Quítalo del carrito para hacer el pedido.";
                case ReglasCanjeChequeRegalo.ERROR_OTRA_CAMPANA:
                    return "Ese no es tu cheque regalo. Vuelve a abrir el carrito para cargar el que tienes.";
                case ReglasCanjeChequeRegalo.ERROR_YA_USADO:
                    return datos.TryGetValue("pedidoCanje", out object pedido) && pedido != null
                        ? $"Tu cheque regalo ya está aplicado en el pedido {pedido}: solo se puede usar una vez."
                        : "Tu cheque regalo ya está aplicado en otro pedido: solo se puede usar una vez.";
                case ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO:
                    return "El plazo para usar tu cheque regalo ya ha terminado.";
                case ReglasCanjeChequeRegalo.ERROR_CAMPANA_INACTIVA:
                    return "El cheque regalo no se puede usar ahora mismo.";
                case ReglasCanjeChequeRegalo.ERROR_ANULADO:
                    return "Tu cheque regalo está anulado. Si crees que es un error, llámanos.";
                case ReglasCanjeChequeRegalo.ERROR_NO_ACTIVADO:
                    return ex.Message; // «El cheque regalo se podrá usar a partir del …»: ya habla a la clienta
                case ReglasCanjeChequeRegalo.ERROR_MISMO_PEDIDO:
                    return "El cheque regalo tiene que ir en un pedido distinto del que lo generó.";
                case ReglasCanjeChequeRegalo.ERROR_VARIAS_LINEAS:
                    return "Solo puedes usar un cheque regalo por pedido.";
                case ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO:
                    string minimo = datos.TryGetValue("minimo", out object m) && m != null ? Euros(Convert.ToDecimal(m, CultureInfo.InvariantCulture)) : null;
                    decimal? falta = Falta(ex);
                    return $"Para usar tu cheque regalo, el pedido tiene que superar {minimo ?? "el mínimo"} € de producto " +
                        "(sin IVA; no cuentan los portes ni los productos excluidos en las condiciones del cheque)." +
                        (falta.HasValue ? $" Te faltan {Euros(falta.Value)} €." : string.Empty);
                default:
                    return ex?.Message;
            }
        }

        private static string Euros(decimal importe) => importe.ToString("N2", es);

        /// <summary>El error es del cheque regalo (sus códigos empiezan por CHEQUE_REGALO_).</summary>
        public static bool EsErrorDelCheque(NestoBusinessException ex)
            => ex?.Context?.ErrorCode?.StartsWith("CHEQUE_REGALO_", StringComparison.Ordinal) == true;
    }
}
