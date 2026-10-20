using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#444 (parte 1): el correo de confirmación que recibe el CLIENTE cuando hace un pedido
    /// desde la APP de clientes (POST api/Pedidos/Cliente, el único que llama a este endpoint). Hasta
    /// ahora no recibía nada: el carrito se vaciaba y se quedaba sin constancia de que el pedido hubiera
    /// entrado (TNV#66). Los pedidos de la tienda online de PrestaShop NO pasan por aquí (entran por
    /// CanalesExternos → POST api/PedidosVenta) y ya reciben el correo de la propia tienda.
    /// El correo interno de «Pedido nuevo» sigue igual y va a las mismas personas de siempre.
    ///
    /// Lo que viaja al job de Hangfire es un DTO plano (sin los parámetros firmados de Redsys ni
    /// nada del pedido que no salga en el correo), y el envío nunca puede tumbar la respuesta a la
    /// app: se encola, y si encolar o enviar falla queda en ELMAH.
    /// </summary>
    public class CorreoConfirmacionPedidoDTO
    {
        public const string PAGADO = "Pagado";
        /// <summary>NestoAPI#663: pagado con Aplázame (el pedido va TRN / PRE, como los de PrestaShop pagados con Aplázame).</summary>
        public const string PAGADO_APLAZAME = "PagadoAplazame";
        public const string PAGO_PENDIENTE_PASARELA = "PagoPendientePasarela";
        public const string PAGO_PENDIENTE = "PagoPendiente";
        public const string CONDICIONES_HABITUALES = "CondicionesHabituales";

        public string Correo { get; set; }
        public int Numero { get; set; }
        public List<LineaCorreoConfirmacionPedidoDTO> Lineas { get; set; } = new List<LineaCorreoConfirmacionPedidoDTO>();
        public decimal BaseImponible { get; set; }
        public decimal Portes { get; set; }
        /// <summary>NestoAPI#660: el recargo de equivalencia del pedido (0 si el cliente no lo lleva), para desglosarlo al pie.</summary>
        public decimal ImporteRecargoEquivalencia { get; set; }
        public decimal Total { get; set; }
        public string SituacionPago { get; set; }
        /// <summary>NestoAPI#446: a quien hace pedidos sin ver los precios tampoco se le cuentan por correo.</summary>
        public bool SinImportes { get; set; }
    }

    public class LineaCorreoConfirmacionPedidoDTO
    {
        public string Producto { get; set; }
        public string Texto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>
        /// NestoAPI#660 (Carlos 20/10/26: «vendemos a profesionales: siempre bases imponibles»): el precio por unidad SIN
        /// IVA y SIN descuento; el descuento va aparte (<see cref="Descuento"/>), para que la cuenta de la tarjeta cuadre siempre.
        /// </summary>
        public decimal PrecioUnitario { get; set; }
        /// <summary>NestoAPI#660: el descuento de la línea en tanto por uno (0,15 = 15 %), el mismo con el que se calcula su base.</summary>
        public decimal Descuento { get; set; }
        /// <summary>NestoAPI#660: el importe de la línea sin IVA (su base imponible), que es lo que sale en la tarjeta.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>El importe de la línea con IVA. Ya no sale en el correo (NestoAPI#660); se queda por los jobs ya encolados.</summary>
        public decimal Total { get; set; }
        /// <summary>
        /// NestoAPI#660: la foto del producto (la de la tienda, la misma que las fichas y Ariadna). La pone el job
        /// justo antes de mandar (<see cref="CorreoConfirmacionPedidoCliente.CompletarFotos"/>), no la petición de la
        /// app; null si no tiene o la tienda no contesta, y entonces la línea sale como siempre.
        /// </summary>
        public string UrlFoto { get; set; }
    }

    public static class CorreoConfirmacionPedidoCliente
    {
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");
        /// <summary>NestoAPI#660: el ancho de la miniatura, en píxeles (fijo: Outlook no entiende max-width ni object-fit).</summary>
        internal const int ANCHO_FOTO = 64;

        /// <summary>Lo que hay que mandar, o null si no hay correo al que mandarlo.</summary>
        public static CorreoConfirmacionPedidoDTO Preparar(PedidoClienteResponse respuesta, string correoCliente, bool sinImportes)
        {
            if (respuesta == null || string.IsNullOrWhiteSpace(correoCliente))
            {
                return null;
            }
            return new CorreoConfirmacionPedidoDTO
            {
                Correo = correoCliente.Trim(),
                Numero = respuesta.Numero,
                Lineas = (respuesta.Lineas ?? new List<LineaPedidoClienteResponse>()).Select(l => new LineaCorreoConfirmacionPedidoDTO
                {
                    Producto = l.Producto,
                    Texto = l.Texto?.Trim(),
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    Descuento = l.Descuento,
                    BaseImponible = l.BaseImponible,
                    Total = l.Total
                }).ToList(),
                BaseImponible = respuesta.BaseImponible,
                Portes = respuesta.Portes,
                ImporteRecargoEquivalencia = respuesta.ImporteRecargoEquivalencia,
                Total = respuesta.Total,
                SituacionPago = SituacionPagoDe(respuesta),
                SinImportes = sinImportes
            };
        }

        internal static string SituacionPagoDe(PedidoClienteResponse respuesta)
        {
            if (respuesta.Pagado)
            {
                // NestoAPI#663: el único cobro previo que deja el pedido en transferencia es el de Aplázame
                return string.Equals(respuesta.FormaPago?.Trim(), Constantes.FormasPago.TRANSFERENCIA, StringComparison.OrdinalIgnoreCase)
                    ? CorreoConfirmacionPedidoDTO.PAGADO_APLAZAME
                    : CorreoConfirmacionPedidoDTO.PAGADO;
            }
            if (respuesta.RequierePago)
            {
                return respuesta.Pago != null
                    ? CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE_PASARELA
                    : CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE;
            }
            return CorreoConfirmacionPedidoDTO.CONDICIONES_HABITUALES;
        }

        internal static (string Asunto, string Html) Generar(CorreoConfirmacionPedidoDTO dto)
        {
            // NestoAPI#660 (Carlos 20/10/26): de usted, como el resto de lo que mandamos a los clientes
            string asunto = $"Gracias por su pedido nº {dto.Numero}";
            StringBuilder s = new StringBuilder();
            _ = s.AppendLine("<div style=\"font-family: Arial, sans-serif; color: #333; max-width: 640px; margin: 0 auto;\">");
            _ = s.AppendLine($"<h2 style=\"color: #222;\">Gracias por su pedido nº {dto.Numero}</h2>");
            _ = s.AppendLine("<p>Hemos recibido su pedido y ya está en manos de nuestro equipo. Le avisaremos cuando salga hacia su dirección.</p>");

            // NestoAPI#660 (Carlos 20/10/26): una tarjeta por línea, que en el móvil la tabla de cuatro columnas no cabía
            _ = s.AppendLine("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width: 100%; border-collapse: collapse; border-top: 1px solid #eee;\">");
            foreach (LineaCorreoConfirmacionPedidoDTO linea in dto.Lineas)
            {
                _ = s.AppendLine(TarjetaLinea(linea, dto.SinImportes));
            }
            _ = s.AppendLine("</table>");

            if (!dto.SinImportes)
            {
                _ = s.AppendLine(Pie(dto));
            }

            _ = s.AppendLine($"<p style=\"margin-top: 16px;\">{TextoPago(dto.SituacionPago)}</p>");
            _ = s.AppendLine("<p style=\"color: #777; font-size: 12px;\">Si tiene cualquier duda, responda a este correo y le ayudaremos.</p>");
            _ = s.AppendLine("</div>");
            return (asunto, s.ToString());
        }

        /// <summary>
        /// NestoAPI#660 (Carlos 20/10/26): al pie, la base imponible y los impuestos encima. La base y el total salen de
        /// donde salían (los del pedido), así que el total no cambia: solo se desglosa. Con portes, la base se separa en
        /// productos (la suma de las tarjetas: la base del pedido menos los portes) y gastos de envío. El RE va redondeado
        /// a céntimos y el IVA es lo que queda hasta el total, para que el pie cuadre al céntimo.
        /// </summary>
        internal static string Pie(CorreoConfirmacionPedidoDTO dto)
        {
            decimal recargo = RoundingHelper.DosDecimalesRound(dto.ImporteRecargoEquivalencia);
            decimal iva = dto.Total - dto.BaseImponible - recargo;
            StringBuilder p = new StringBuilder();
            _ = p.AppendLine("<table role=\"presentation\" cellpadding=\"4\" cellspacing=\"0\" border=\"0\" style=\"margin-top: 12px; margin-left: auto;\">");
            if (dto.Portes != 0)
            {
                _ = p.AppendLine(FilaPie("Productos", dto.BaseImponible - dto.Portes));
                _ = p.AppendLine(FilaPie("Gastos de envío", dto.Portes));
            }
            _ = p.AppendLine(FilaPie("Base imponible", dto.BaseImponible));
            _ = p.AppendLine(FilaPie("IVA", iva));
            if (recargo != 0)
            {
                _ = p.AppendLine(FilaPie("Recargo de equivalencia", recargo));
            }
            _ = p.AppendLine($"<tr><td><b>Total</b></td><td align=\"right\"><b>{Euros(dto.Total)}</b></td></tr>");
            _ = p.Append("</table>");
            return p.ToString();
        }

        private static string FilaPie(string concepto, decimal importe)
            => $"<tr><td>{concepto}</td><td align=\"right\">{Euros(importe)}</td></tr>";

        /// <summary>
        /// NestoAPI#660: la tarjeta de una línea: la miniatura a la izquierda (si la tiene) y, apilados a la derecha, el
        /// nombre, la referencia (Carlos 25/09/26: es con lo que se busca el producto) y «2 × 12,50 € = 25,00 €». Todo con
        /// tablas y estilos en línea, que es lo único que respetan a la vez Outlook y Gmail. La foto va por URL https
        /// absoluta (sin adjuntarla: con una por línea el correo pesaría mucho), con ancho fijo y el nombre en el alt para
        /// quien tenga las imágenes bloqueadas. Sin foto no se pinta su celda: no queda hueco.
        /// </summary>
        internal static string TarjetaLinea(LineaCorreoConfirmacionPedidoDTO linea, bool sinImportes)
        {
            string nombre = WebUtility.HtmlEncode(linea.Texto);
            string url = UrlFotoValida(linea.UrlFoto);
            StringBuilder t = new StringBuilder();
            _ = t.Append("<tr><td style=\"padding: 10px 0; border-bottom: 1px solid #eee;\">");
            _ = t.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>");
            if (url != null)
            {
                _ = t.Append($"<td width=\"{ANCHO_FOTO}\" valign=\"top\" style=\"width: {ANCHO_FOTO}px; padding-right: 12px;\">"
                    + $"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"{nombre}\" width=\"{ANCHO_FOTO}\" "
                    + $"style=\"display: block; width: {ANCHO_FOTO}px; height: auto; border: 0; outline: none; text-decoration: none;\"></td>");
            }
            _ = t.Append("<td valign=\"top\" style=\"font-size: 14px; line-height: 20px;\">");
            _ = t.Append($"<div style=\"font-weight: bold; color: #222;\">{nombre}</div>");
            _ = t.Append($"<div style=\"font-size: 12px; color: #777;\">Ref. {WebUtility.HtmlEncode(linea.Producto?.Trim())}</div>");
            _ = t.Append($"<div>{TextoCantidad(linea, sinImportes)}</div>");
            _ = t.Append("</td></tr></table></td></tr>");
            return t.ToString();
        }

        /// <summary>
        /// La cuenta de la tarjeta, sin IVA (Carlos 20/10/26: a profesionales, siempre bases imponibles), con el precio sin
        /// descuento y el descuento aparte para que cuadre siempre con la base de la línea, que es el importe:
        /// «2 × 12,50 € = 25,00 €», «1 × 63,50 € −15 % = 53,97 €». Un regalo (100 %) pone «Regalo: 1 × 10,00 €»: dice
        /// sin cuentas que no se cobra y deja ver lo que vale. Sin importes (NestoAPI#446), solo la cantidad.
        /// </summary>
        internal static string TextoCantidad(LineaCorreoConfirmacionPedidoDTO linea, bool sinImportes)
        {
            string unidades = linea.Cantidad == 1 ? "1 unidad" : $"{linea.Cantidad} unidades";
            if (sinImportes)
            {
                return unidades;
            }
            if (linea.Descuento >= 1)
            {
                return linea.PrecioUnitario > 0 ? $"Regalo: {linea.Cantidad} × {Euros(linea.PrecioUnitario)}" : $"Regalo: {unidades}";
            }
            string descuento = linea.Descuento != 0 ? $" −{Porcentaje(linea.Descuento)}" : string.Empty;
            return $"{linea.Cantidad} × {Euros(linea.PrecioUnitario)}{descuento} = {Euros(linea.BaseImponible)}";
        }

        /// <summary>0,15 → «15 %»; 0,125 → «12,5 %»: con los decimales que hagan falta y ninguno de sobra.</summary>
        internal static string Porcentaje(decimal tantoPorUno)
            => (tantoPorUno * 100).ToString("0.####", ES) + " %";

        /// <summary>Solo URLs absolutas https (Gmail y Outlook bloquean o avisan con las http, y una relativa no lleva a ningún sitio).</summary>
        internal static string UrlFotoValida(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || FotosProductoAlmacen.EsRota(url))
            {
                return null;
            }
            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)
                ? uri.AbsoluteUri
                : null;
        }

        /// <summary>
        /// NestoAPI#660: pone la foto a cada línea que no la tenga, con el servicio de fotos de Ariadna (caché, como mucho
        /// unas pocas a la vez y tiempo máximo por foto). Nunca lanza: sin fotos el correo sale igual.
        /// </summary>
        internal static void CompletarFotos(CorreoConfirmacionPedidoDTO dto, IFotosProductoAlmacen fotos)
        {
            if (dto?.Lineas == null || fotos == null)
            {
                return;
            }
            try
            {
                List<LineaCorreoConfirmacionPedidoDTO> sinFoto = dto.Lineas
                    .Where(l => l != null && string.IsNullOrWhiteSpace(l.UrlFoto) && !string.IsNullOrWhiteSpace(l.Producto))
                    .ToList();
                if (!sinFoto.Any())
                {
                    return;
                }
                // Task.Run: sin contexto de sincronización, para poder esperar aquí sin bloqueos
                IDictionary<string, string> urls = Task.Run(() => fotos.Urls(sinFoto.Select(l => l.Producto.Trim()))).GetAwaiter().GetResult();
                foreach (LineaCorreoConfirmacionPedidoDTO linea in sinFoto)
                {
                    if (urls != null && urls.TryGetValue(linea.Producto.Trim(), out string url))
                    {
                        linea.UrlFoto = url;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Pedido app] No se han podido poner las fotos en el correo de confirmación del pedido {dto.Numero}: {ex.Message}", ex));
            }
        }

        internal static string TextoPago(string situacion)
        {
            switch (situacion)
            {
                case CorreoConfirmacionPedidoDTO.PAGADO:
                    return "Pago recibido con su tarjeta. No tiene que hacer nada más.";
                case CorreoConfirmacionPedidoDTO.PAGADO_APLAZAME:
                    return "Pago aprobado por Aplázame. No tiene que hacer nada más.";
                case CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE_PASARELA:
                    return "Este pedido se paga con tarjeta: si no ha terminado el pago en la pasarela, no se preparará hasta que lo complete.";
                case CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE:
                    return "Este pedido se prepara en cuanto recibamos el pago.";
                default:
                    return "Se cobrará con sus condiciones de pago habituales.";
            }
        }

        private static string Euros(decimal importe) => importe.ToString("C", ES);

        /// <summary>
        /// Encola el envío en Hangfire (para no retrasar la respuesta a la app). <paramref name="encolador"/>
        /// existe para los tests; en producción es BackgroundJob.Enqueue. Nunca lanza: un fallo aquí
        /// queda en ELMAH y el pedido, que ya está creado, se responde igual.
        /// </summary>
        public static void Encolar(CorreoConfirmacionPedidoDTO dto, Action<CorreoConfirmacionPedidoDTO> encolador = null)
        {
            if (dto == null)
            {
                return;
            }
            try
            {
                (encolador ?? (d => Hangfire.BackgroundJob.Enqueue(() => Enviar(d))))(dto);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Pedido app] No se ha podido encolar el correo de confirmación del pedido {dto.Numero} a {dto.Correo}: {ex.Message}", ex));
            }
        }

        /// <summary>El job: construye y manda el correo desde la tienda online. Lanza si el SMTP falla, para que Hangfire reintente y lo registre.</summary>
        public static void Enviar(CorreoConfirmacionPedidoDTO dto)
        {
            CompletarFotos(dto, new FotosProductoAlmacen());
            (string asunto, string html) = Generar(dto);
            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress(Constantes.Correos.TIENDA_ONLINE, "Tienda Online Nueva Visión");
                mail.To.Add(dto.Correo);
                mail.Bcc.Add(Constantes.Correos.TIENDA_ONLINE);
                mail.Subject = asunto;
                mail.Body = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(asunto) + "</title></head><body>" + html + "</body></html>";
                mail.IsBodyHtml = true;
                if (!new ServicioCorreoElectronico().EnviarCorreoSMTP(mail))
                {
                    throw new Exception($"No se ha podido enviar el correo de confirmación del pedido {dto.Numero} a {dto.Correo}");
                }
            }
        }
    }
}
