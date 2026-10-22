using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>
    /// NestoAPI#614 (TNV#82): lo que el carrito de la app le enseña al cliente sobre su cupón. Va en la respuesta de
    /// <c>POST api/Pedidos/Cliente/Portes</c> (la que calcula el total que se enseña y se cobra) y en la de
    /// <c>POST api/Pedidos/Cliente</c>.
    /// </summary>
    public class CuponCarritoDTO
    {
        /// <summary>El cupón está descontado en el total del carrito (TotalConIva ya lo lleva).</summary>
        public bool Aplicado { get; set; }
        public string CodigoCupon { get; set; }
        /// <summary>IVA incluido: lo que baja el total (50).</summary>
        public decimal Importe { get; set; }
        /// <summary>La base de la línea del cupón, en positivo (41,32 al 21 %; con recargo de equivalencia, menos).</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>CUPON_* si no se ha podido aplicar (el carrito se calcula sin el cupón).</summary>
        public string ErrorCode { get; set; }
        /// <summary>La frase para el cliente, de usted.</summary>
        public string Mensaje { get; set; }
        /// <summary>Con CUPON_PEDIDO_MINIMO, cuánto falta de productos (IVA incluido).</summary>
        public decimal? Falta { get; set; }
    }

    /// <summary>NestoAPI#694: el carrito de la tienda PrestaShop (módulo nestocheques) que se pregunta en POST api/Cupones/ComprobarCarrito.</summary>
    public class ComprobarCarritoCuponDTO
    {
        /// <summary>Opcional: «1» si no viene.</summary>
        public string Empresa { get; set; }
        /// <summary>El cliente de Nesto (el módulo lo saca igual que para el cheque regalo).</summary>
        public string Cliente { get; set; }
        /// <summary>Opcional: sin código, el cupón del cliente que mejor encaja (<see cref="ReglasCupon.ElegirParaPedido"/>).</summary>
        public string Codigo { get; set; }
        public List<LineaCarritoCuponDTO> Lineas { get; set; }
    }

    public class LineaCarritoCuponDTO
    {
        /// <summary>Referencia del producto en PrestaShop (= el código de Nesto).</summary>
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Precio sin IVA × cantidad, ya con los descuentos de la tienda, sin portes ni el propio cupón.</summary>
        public decimal BaseImponible { get; set; }
    }

    /// <summary>
    /// NestoAPI#694: la respuesta de POST api/Cupones/ComprobarCarrito, con la forma de <c>Cupon</c> en la respuesta de TNV
    /// (<see cref="CuponCarritoDTO"/>) y los nombres del cheque regalo de la tienda (#652).
    /// </summary>
    public class ComprobacionCarritoCuponDTO
    {
        /// <summary>El cupón se puede usar con este carrito.</summary>
        public bool Aplicable { get; set; }
        /// <summary>El cupón que se ha comprobado (null si el cliente no tiene ninguno o el código no existe).</summary>
        public string Codigo { get; set; }
        /// <summary>IVA incluido: lo que baja el total (también cuando todavía no se puede usar, para el «le faltan…»). 0 sin cupón.</summary>
        public decimal Importe { get; set; }
        /// <summary>La base de la línea del cupón en positivo, con el IVA y el recargo del cliente (41,32 al 21 %). 0 sin cupón.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>CUPON_* si no se puede usar (CUPON_SIN_CUPON: el cliente no tiene ninguno).</summary>
        public string ErrorCode { get; set; }
        /// <summary>Listo para enseñar, de usted.</summary>
        public string Mensaje { get; set; }
        /// <summary>Con CUPON_PEDIDO_MINIMO, lo que falta de productos, IVA incluido (sin cursos ni portes).</summary>
        public decimal? Falta { get; set; }
        /// <summary>Se puede usar en el mismo pedido que el cheque regalo (<see cref="ReglasCupon.COMBINABLE_CON_CHEQUE_REGALO"/>).</summary>
        public bool CombinableConChequeRegalo { get; set; } = ReglasCupon.COMBINABLE_CON_CHEQUE_REGALO;
    }

    /// <summary>El cupón del carrito: si lo lleva y, si no se puede usar, por qué.</summary>
    public class ResultadoCuponCarrito
    {
        public bool LlevaCupon { get; set; }
        public string Codigo { get; set; }
        public decimal Importe { get; set; }
        public decimal BaseImponible { get; set; }
        public NestoBusinessException Error { get; set; }
        public bool Aplicado => LlevaCupon && Error == null;

        public CuponCarritoDTO ParaElCarrito()
        {
            if (!LlevaCupon)
            {
                return null;
            }
            return new CuponCarritoDTO
            {
                Aplicado = Aplicado,
                CodigoCupon = Codigo,
                Importe = Aplicado ? Importe : 0,
                BaseImponible = Aplicado ? BaseImponible : 0,
                ErrorCode = Error?.Context?.ErrorCode,
                Mensaje = Aplicado ? CarritoCupones.MensajeAplicado(Importe) : Error.Message,
                Falta = CarritoCupones.Falta(Error)
            };
        }
    }

    /// <summary>
    /// NestoAPI#614 (TNV#82): el cupón desde la app de clientes (<c>api/Pedidos/Cliente</c>: portes, Ganavisiones, cobro del
    /// carrito y pedido). El código viene en la cabecera de la petición (<see cref="PedidoClienteRequest.CodigoCupon"/>) y aquí
    /// se convierte en la línea del cupón con su código: la normaliza y la comprueba el MISMO <see cref="IServicioCanjeCupones"/>
    /// que POST/PUT de api/PedidosVenta (cupón Activo, del cliente, mínimo sin cursos ni portes), que es también quien lo marca
    /// Usado al crear el pedido (igual que POST api/Cupones/{codigo}/Usar). El cliente sale del JWT: un cupón de otro, no.
    /// </summary>
    public class CarritoCupones
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        private readonly IServicioCanjeCupones servicio;

        public CarritoCupones(IServicioCanjeCupones servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        /// <summary>
        /// Añade la línea del cupón al pedido ya montado (con el almacén, la forma de venta y la fecha de la primera línea),
        /// la normaliza y comprueba las reglas como pedido nuevo. No reserva nada: lo hace PostPedidoVenta al guardar.
        /// <paramref name="completarImportes"/> deja los porcentajes de IVA en las líneas (para el total y el mínimo).
        /// </summary>
        public async Task<ResultadoCuponCarrito> Preparar(PedidoVentaDTO pedido, string codigoCupon, Action<PedidoVentaDTO> completarImportes)
        {
            string codigo = codigoCupon?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(codigo) || pedido?.Lineas == null)
            {
                return new ResultadoCuponCarrito();
            }
            var resultado = new ResultadoCuponCarrito { LlevaCupon = true, Codigo = codigo };
            QuitarLineasCupon(pedido); // uno por pedido: el de la cabecera
            LineaPedidoVentaDTO modelo = pedido.Lineas.FirstOrDefault(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO);
            var linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                CodigoCupon = codigo,
                Producto = servicio.ProductoCupon,
                Cantidad = -1,
                fechaEntrega = modelo?.fechaEntrega ?? DateTime.Today,
                almacen = modelo?.almacen ?? Constantes.Almacenes.ALGETE,
                delegacion = modelo?.delegacion ?? Constantes.Empresas.DELEGACION_POR_DEFECTO,
                formaVenta = modelo?.formaVenta ?? Constantes.FormasVenta.APP,
                iva = modelo?.iva,
                usuario = modelo?.usuario,
                vistoBueno = true,
                EsFicticio = true
            };
            pedido.Lineas.Add(linea);

            try
            {
                await servicio.NormalizarLineas(pedido).ConfigureAwait(false);
                completarImportes?.Invoke(pedido);
                pedido.VincularLineas();
                resultado.Importe = Math.Round(-linea.Total, 2, MidpointRounding.AwayFromZero);
                resultado.BaseImponible = Math.Round(-linea.BaseImponible, 2, MidpointRounding.AwayFromZero);
                _ = await servicio.Comprobar(pedido.empresa, 0, pedido.cliente, LineasParaComprobar(pedido), pedido.iva,
                    null, pedido.Usuario, pedidoNuevo: true, teniaLineaCupon: false).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex) when (EsErrorDelCupon(ex))
            {
                resultado.Error = ex;
            }
            return resultado;
        }

        /// <summary>Quita las líneas del cupón (el carrito se calcula sin él cuando no se puede usar).</summary>
        public void QuitarLineasCupon(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null)
            {
                return;
            }
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas
                .Where(l => l.EsCupon || !string.IsNullOrWhiteSpace(l.CodigoCupon) || ReglasCupon.EsLineaCupon(l.tipoLinea, l.Producto, servicio.ProductoCupon))
                .ToList())
            {
                _ = pedido.Lineas.Remove(linea);
            }
        }

        /// <summary>Las líneas del DTO como las verá PostPedidoVenta (LinPedidoVta), con su base y su total.</summary>
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
                Total = l.Total,
                Estado = l.estado,
                Picking = 0
            }).ToList();

        internal static string MensajeAplicado(decimal importe) =>
            $"Su cupón de {importe.ToString("N2", es)} € está descontado en este pedido.";

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

        // ---------------- NestoAPI#694: la tienda PrestaShop (POST api/Cupones/ComprobarCarrito) ----------------

        /// <summary>El usuario de auditoría de las comprobaciones del carrito de la tienda (no se graba nada).</summary>
        internal const string USUARIO_TIENDA = "TiendaPrestashop";

        internal static ComprobacionCarritoCuponDTO SinCupon() => new ComprobacionCarritoCuponDTO
        {
            ErrorCode = ReglasCupon.ERROR_SIN_CUPON,
            Mensaje = "No tiene ningún cupón para usar en este pedido."
        };

        internal static ComprobacionCarritoCuponDTO NoExiste(string codigo) => new ComprobacionCarritoCuponDTO
        {
            ErrorCode = ReglasCupon.ERROR_NO_EXISTE,
            Mensaje = $"No encontramos el cupón {codigo}. Revise el código."
        };

        internal static ComprobacionCarritoCuponDTO Aplicable(CuponLeido cupon, decimal baseImponible) => new ComprobacionCarritoCuponDTO
        {
            Aplicable = true,
            Codigo = cupon.Codigo?.Trim(),
            Importe = cupon.Importe,
            BaseImponible = baseImponible,
            Mensaje = $"Puede usar su cupón de {ReglasCupon.Euros(cupon.Importe)} € ({cupon.Codigo?.Trim()}) en este pedido."
        };

        /// <summary>De otro cliente no se dice de quién es (la tienda lo enseña tal cual); el resto de textos de ReglasCupon ya van de usted.</summary>
        internal static ComprobacionCarritoCuponDTO NoAplicable(CuponLeido cupon, decimal baseImponible, NestoBusinessException ex)
        {
            string codigoError = ex.Context?.ErrorCode;
            bool deOtro = codigoError == ReglasCupon.ERROR_OTRO_CLIENTE;
            return new ComprobacionCarritoCuponDTO
            {
                Codigo = cupon.Codigo?.Trim(),
                Importe = deOtro ? 0 : cupon.Importe,
                BaseImponible = deOtro ? 0 : baseImponible,
                ErrorCode = codigoError,
                Mensaje = deOtro ? $"El cupón {cupon.Codigo?.Trim()} no es suyo: solo lo puede usar el cliente que lo recibió." : ex.Message,
                Falta = Falta(ex)
            };
        }

        /// <summary>El error es del cupón (sus códigos empiezan por CUPON_).</summary>
        public static bool EsErrorDelCupon(NestoBusinessException ex) =>
            ex?.Context?.ErrorCode?.StartsWith("CUPON_", StringComparison.Ordinal) == true;
    }
}
