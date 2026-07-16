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
                From = new MailAddress("nesto@nuevavision.es"),
                Subject = $"Pedido {pedido.Id} del cliente {pedido.Cliente?.Trim()}: va a salir por {importe:C}",
                IsBodyHtml = true,
                Body = GenerarCuerpo(pedido, importe)
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(new MailAddress(destinatario));
            }
            return mail;
        }

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

        private static string GenerarCuerpo(PedidoPicking pedido, decimal importe)
        {
            System.Text.StringBuilder s = new System.Text.StringBuilder();
            _ = s.AppendLine($"<p>El pedido <b>{pedido.Id}</b> del cliente <b>{pedido.Cliente?.Trim()}</b> ha cogido picking.</p>");
            _ = s.AppendLine($"<p>Importe (base imponible) de las líneas que salen: <b>{importe:C}</b></p>");
            _ = s.AppendLine("<table border=\"1\"><thead><tr><th>Producto</th><th>Cantidad</th><th>Importe</th></tr></thead><tbody>");
            foreach (LineaPedidoPicking linea in pedido.Lineas.Where(l => l.BaseImponibleEntrega != 0))
            {
                _ = s.AppendLine($"<tr><td>{linea.Producto?.Trim()}</td><td style=\"text-align:right\">{linea.CantidadReservada}</td><td style=\"text-align:right\">{linea.BaseImponibleEntrega:C}</td></tr>");
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
