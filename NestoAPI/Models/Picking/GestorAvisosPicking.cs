using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#253: aviso automático (al vendedor y al usuario que metió el pedido) con el
    /// importe de las líneas que han cogido picking, para los pedidos con la casilla
    /// "Avisar con importe cuando coja picking". Sustituye al aviso manual que un compañero
    /// escribía leyendo el comentario de picking. Se avisa en cada tanda con el importe de esa
    /// tanda (un picking parcial genera un aviso por cada salida).
    /// </summary>
    public class GestorAvisosPicking
    {
        /// <summary>
        /// Nunca lanza: un fallo del aviso no debe romper el proceso de picking.
        /// </summary>
        public static void EnviarCorreos(List<PedidoPicking> candidatos, Func<string, string> buscarCorreoVendedor)
        {
            if (candidatos == null)
            {
                return;
            }

            foreach (PedidoPicking pedido in candidatos.Where(c => c != null && c.AvisarConImporteAlCogerPicking))
            {
                try
                {
                    string correoVendedor = string.IsNullOrWhiteSpace(pedido.Vendedor) ? null : buscarCorreoVendedor?.Invoke(pedido.Vendedor);
                    MailMessage mail = ComponerCorreo(pedido, correoVendedor, pedido.CorreoUsuarioPedido);
                    if (mail == null)
                    {
                        continue;
                    }
                    Enviar(mail);
                }
                catch (Exception ex)
                {
                    // Sin correo válido o SMTP caído: loguear y seguir con el resto
                    try
                    {
                        Elmah.ErrorSignal.FromCurrentContext().Raise(new Exception(
                            $"[AvisoPicking] No se pudo enviar el aviso del pedido {pedido.Empresa?.Trim()}/{pedido.Id}", ex));
                    }
                    catch { /* nunca romper el picking */ }
                }
            }
        }

        /// <summary>
        /// Compone el correo del aviso. Devuelve null si no hay importe que avisar o ningún
        /// destinatario con correo (no se debe enviar nada).
        /// </summary>
        internal static MailMessage ComponerCorreo(PedidoPicking pedido, string correoVendedor, string correoUsuario)
        {
            decimal importe = ImporteCogido(pedido);
            if (importe <= 0)
            {
                return null;
            }

            var destinatarios = new List<string>();
            if (!string.IsNullOrWhiteSpace(correoVendedor))
            {
                destinatarios.Add(correoVendedor.Trim());
            }
            if (!string.IsNullOrWhiteSpace(correoUsuario) && !destinatarios.Contains(correoUsuario.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                destinatarios.Add(correoUsuario.Trim());
            }
            if (!destinatarios.Any())
            {
                return null;
            }

            MailMessage mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                // #314: asunto "Pedido {n} - c/ {cliente}". Outlook agrupa por asunto, así que el
                // aviso queda en la misma conversación que el resto de correos de ese pedido. El
                // importe se ha movido al cuerpo.
                Subject = $"Pedido {pedido.Id} - c/ {pedido.Cliente?.Trim()}",
                IsBodyHtml = true,
                Body = GenerarCuerpo(pedido, importe, TotalConIvaCogido(pedido))
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(new MailAddress(destinatario));
            }
            // #314: al responder, la respuesta va a almacén (no al buzón técnico nesto@), que es
            // quien puede hacer algo con ella. Enviar DESDE almacen@ requeriría permiso Send As en
            // Office 365 sobre ese buzón; mientras no se conceda, esta es la alternativa acordada.
            mail.ReplyToList.Add(new MailAddress(CORREO_ALMACEN));
            return mail;
        }

        private const string CORREO_REMITENTE = "nesto@nuevavision.es";
        private const string CORREO_ALMACEN = "almacen@nuevavision.es";

        /// <summary>
        /// Importe de lo que ha cogido picking en ESTA tanda (solo las líneas que salen).
        /// </summary>
        internal static decimal ImporteCogido(PedidoPicking pedido)
        {
            if (pedido?.Lineas == null)
            {
                return 0;
            }
            return pedido.Lineas.Sum(l => l.BaseImponibleEntrega);
        }

        /// <summary>
        /// NestoAPI#314: total CON IVA (y recargo de equivalencia) de lo que sale en esta tanda.
        /// Es el dinero que el cliente tiene que tener preparado, que es para lo que se usa el aviso.
        /// </summary>
        internal static decimal TotalConIvaCogido(PedidoPicking pedido)
        {
            if (pedido?.Lineas == null)
            {
                return 0;
            }
            return pedido.Lineas.Sum(l => l.TotalEntrega);
        }

        internal static string GenerarCuerpo(PedidoPicking pedido, decimal importe, decimal totalConIva)
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            _ = s.AppendLine($"<p>El pedido <b>{pedido.Id}</b> del cliente <b>{pedido.Cliente?.Trim()}</b> ha cogido picking.</p>");
            // #314: el TOTAL con IVA va primero y destacado (es el dinero que el cliente tiene que
            // tener preparado); la base imponible se mantiene desglosada debajo.
            _ = s.AppendLine($"<p style=\"font-size:14px\">Total a cobrar (IVA incluido): <b>{totalConIva:C}</b></p>");
            _ = s.AppendLine($"<p>Base imponible de las líneas que salen: <b>{importe:C}</b></p>");
            _ = s.AppendLine("<table border=\"1\"><thead><tr><th>Producto</th><th>Cantidad</th><th>Base imponible</th><th>Total con IVA</th></tr></thead><tbody>");
            foreach (LineaPedidoPicking linea in pedido.Lineas.Where(l => l.BaseImponibleEntrega != 0))
            {
                _ = s.AppendLine($"<tr><td>{linea.Producto?.Trim()}</td><td style=\"text-align:right\">{linea.CantidadReservada}</td><td style=\"text-align:right\">{linea.BaseImponibleEntrega:C}</td><td style=\"text-align:right\">{linea.TotalEntrega:C}</td></tr>");
            }
            _ = s.AppendLine("</tbody></table>");
            _ = s.AppendLine("<p>Este aviso se genera automáticamente porque el pedido tiene marcada la casilla \"Avisar con importe cuando coja picking\".</p>");
            return s.ToString();
        }

        // Mismo transporte que GestorPrepagos (reintento único a los 2s)
        private static void Enviar(MailMessage mail)
        {
            SmtpClient client = new SmtpClient
            {
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", ConfigurationManager.AppSettings["office365password"]),
                Host = "smtp.office365.com"
            };
            try
            {
                client.Send(mail);
            }
            catch
            {
                Task.Delay(2000).Wait();
                client.Send(mail);
            }
        }
    }
}
