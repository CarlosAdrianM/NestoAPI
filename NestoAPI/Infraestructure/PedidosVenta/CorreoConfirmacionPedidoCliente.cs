using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;

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
        public const string PAGO_PENDIENTE_PASARELA = "PagoPendientePasarela";
        public const string PAGO_PENDIENTE = "PagoPendiente";
        public const string CONDICIONES_HABITUALES = "CondicionesHabituales";

        public string Correo { get; set; }
        public int Numero { get; set; }
        public List<LineaCorreoConfirmacionPedidoDTO> Lineas { get; set; } = new List<LineaCorreoConfirmacionPedidoDTO>();
        public decimal BaseImponible { get; set; }
        public decimal Portes { get; set; }
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
        public decimal Total { get; set; }
    }

    public static class CorreoConfirmacionPedidoCliente
    {
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

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
                    Total = l.Total
                }).ToList(),
                BaseImponible = respuesta.BaseImponible,
                Portes = respuesta.Portes,
                Total = respuesta.Total,
                SituacionPago = SituacionPagoDe(respuesta),
                SinImportes = sinImportes
            };
        }

        internal static string SituacionPagoDe(PedidoClienteResponse respuesta)
        {
            if (respuesta.Pagado)
            {
                return CorreoConfirmacionPedidoDTO.PAGADO;
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
            string asunto = $"Gracias por tu pedido nº {dto.Numero}";
            StringBuilder s = new StringBuilder();
            _ = s.AppendLine("<div style=\"font-family: Arial, sans-serif; color: #333; max-width: 640px; margin: 0 auto;\">");
            _ = s.AppendLine($"<h2 style=\"color: #222;\">Gracias por tu pedido nº {dto.Numero}</h2>");
            _ = s.AppendLine("<p>Hemos recibido tu pedido y ya está en manos de nuestro equipo. Te avisaremos cuando salga hacia tu dirección.</p>");

            _ = s.AppendLine("<table style=\"width: 100%; border-collapse: collapse;\" cellpadding=\"6\">");
            // Carlos (25/09/26): la referencia también, que es con lo que se busca el producto
            _ = s.AppendLine("<thead><tr style=\"background: #f2f2f2;\"><th align=\"left\">Referencia</th><th align=\"left\">Producto</th><th align=\"right\">Cantidad</th>"
                + (dto.SinImportes ? "" : "<th align=\"right\">Importe</th>") + "</tr></thead><tbody>");
            foreach (LineaCorreoConfirmacionPedidoDTO linea in dto.Lineas)
            {
                _ = s.Append($"<tr><td style=\"border-bottom: 1px solid #eee; white-space: nowrap;\">{WebUtility.HtmlEncode(linea.Producto?.Trim())}</td>");
                _ = s.Append($"<td style=\"border-bottom: 1px solid #eee;\">{WebUtility.HtmlEncode(linea.Texto)}</td>");
                _ = s.Append($"<td align=\"right\" style=\"border-bottom: 1px solid #eee;\">{linea.Cantidad}</td>");
                if (!dto.SinImportes)
                {
                    _ = s.Append($"<td align=\"right\" style=\"border-bottom: 1px solid #eee;\">{Euros(linea.Total)}</td>");
                }
                _ = s.AppendLine("</tr>");
            }
            _ = s.AppendLine("</tbody></table>");

            if (!dto.SinImportes)
            {
                decimal impuestos = dto.Total - dto.BaseImponible;
                _ = s.AppendLine("<table style=\"margin-top: 12px; margin-left: auto;\" cellpadding=\"4\">");
                _ = s.AppendLine($"<tr><td>Base imponible</td><td align=\"right\">{Euros(dto.BaseImponible)}</td></tr>");
                if (dto.Portes > 0)
                {
                    _ = s.AppendLine($"<tr><td>Gastos de envío (incluidos en la base)</td><td align=\"right\">{Euros(dto.Portes)}</td></tr>");
                }
                _ = s.AppendLine($"<tr><td>IVA</td><td align=\"right\">{Euros(impuestos)}</td></tr>");
                _ = s.AppendLine($"<tr><td><b>Total</b></td><td align=\"right\"><b>{Euros(dto.Total)}</b></td></tr>");
                _ = s.AppendLine("</table>");
            }

            _ = s.AppendLine($"<p style=\"margin-top: 16px;\">{TextoPago(dto.SituacionPago)}</p>");
            _ = s.AppendLine("<p style=\"color: #777; font-size: 12px;\">Si tienes cualquier duda, responde a este correo y te ayudamos.</p>");
            _ = s.AppendLine("</div>");
            return (asunto, s.ToString());
        }

        internal static string TextoPago(string situacion)
        {
            switch (situacion)
            {
                case CorreoConfirmacionPedidoDTO.PAGADO:
                    return "Pago recibido con tu tarjeta. No tienes que hacer nada más.";
                case CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE_PASARELA:
                    return "Este pedido se paga con tarjeta: si no has terminado el pago en la pasarela, no se preparará hasta que lo completes.";
                case CorreoConfirmacionPedidoDTO.PAGO_PENDIENTE:
                    return "Este pedido se prepara en cuanto recibamos el pago.";
                default:
                    return "Se cobrará con tus condiciones de pago habituales.";
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
