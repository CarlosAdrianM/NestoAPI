using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    /// <summary>
    /// NestoAPI#362: los días que un cliente CIERRA (Clientes.DiasEnServir, char(5), herencia del
    /// Nesto viejo: 5 posiciones = lunes..viernes, '1' abre y '0' cierra; "01111" = cierra los
    /// lunes). El picking no debe sacar un pedido cuya entrega caería en un día cerrado: la
    /// agencia entrega ~24h después de la salida, así que el día de entrega es el siguiente
    /// laborable tras el día de picking.
    ///
    /// <para>El filtro vive en el PICKING y no en una fecha puesta al crear el pedido (opción A
    /// de la issue): el día de salida es dinámico (hora de corte, stock, festivos) y aquí se
    /// recalcula en cada pasada — el pedido "se guarda solo" y sale en la primera pasada cuya
    /// entrega caiga en día abierto. Para forzar una excepción sigue valiendo el override manual
    /// de Fecha_Entrega.</para>
    ///
    /// <para>Ante un dato raro (null, longitud distinta de 5, caracteres que no son 0/1) se
    /// considera ABIERTO: un dato defectuoso no debe dejar pedidos sin salir.</para>
    /// </summary>
    public static class GestorDiasEnServir
    {
        /// <summary>
        /// El día de ENTREGA estimado: el siguiente laborable tras el día de salida
        /// (fechaPicking), saltando findes y festivos igual que hace el propio picking.
        /// </summary>
        internal static DateTime CalcularDiaEntrega(DateTime fechaPicking, Func<DateTime, bool> esFestivo)
        {
            DateTime entrega = fechaPicking.Date.AddDays(1);
            while (esFestivo(entrega))
            {
                entrega = entrega.AddDays(1);
            }
            return entrega;
        }

        internal static bool EstaAbierto(string diasEnServir, DateTime diaEntrega)
        {
            string dias = diasEnServir?.Trim();
            if (dias == null || dias.Length != 5 || dias.Any(c => c != '0' && c != '1'))
            {
                return true;
            }

            switch (diaEntrega.DayOfWeek)
            {
                case DayOfWeek.Monday: return dias[0] == '1';
                case DayOfWeek.Tuesday: return dias[1] == '1';
                case DayOfWeek.Wednesday: return dias[2] == '1';
                case DayOfWeek.Thursday: return dias[3] == '1';
                case DayOfWeek.Friday: return dias[4] == '1';
                default:
                    // La entrega nunca cae en finde (CalcularDiaEntrega los salta), y el campo
                    // solo modela lunes..viernes: si llegara, mejor abierto que retener.
                    return true;
            }
        }

        /// <summary>
        /// Quita del picking los pedidos cuya entrega caería en un día que el cliente cierra
        /// (vaciándoles las líneas, igual que las demás reglas de "no debe salir": el pedido se
        /// queda pendiente y se reevalúa en la siguiente pasada). Devuelve los retirados para el
        /// aviso: un pedido que no sale sin decir por qué parece un cuelgue.
        /// </summary>
        internal static List<PedidoPicking> RetirarPedidosDeClientesCerrados(
            List<PedidoPicking> candidatos, DateTime diaEntrega)
        {
            List<PedidoPicking> retirados = candidatos
                .Where(p => p.Lineas != null && p.Lineas.Count > 0 && !EstaAbierto(p.DiasEnServir, diaEntrega))
                .ToList();
            foreach (PedidoPicking pedido in retirados)
            {
                pedido.Lineas.Clear();
            }
            return retirados;
        }

        /// <summary>
        /// Aviso al usuario de cada pedido (CC administración), mismo patrón que el correo de
        /// retenidos por prepago. Nunca lanza: un fallo de correo no debe romper el picking.
        /// </summary>
        public static void EnviarCorreo(List<PedidoPicking> pedidosRetirados, DateTime diaEntrega)
        {
            if (pedidosRetirados == null || pedidosRetirados.Count == 0)
            {
                return;
            }

            try
            {
                MailMessage mail = new MailMessage();
                SmtpClient client = new SmtpClient
                {
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es",
                        ConfigurationManager.AppSettings["office365password"]),
                    Host = "smtp.office365.com"
                };
                mail.From = new MailAddress("nesto@nuevavision.es");
                foreach (string correoUsuario in pedidosRetirados.Select(p => p.CorreoUsuarioPedido).Distinct())
                {
                    mail.To.Add(new MailAddress(correoUsuario));
                }
                mail.CC.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                mail.Subject = "Pedidos sin picking: el cliente cierra el día de la entrega";
                mail.Body = GenerarCuerpo(pedidosRetirados, diaEntrega);
                mail.IsBodyHtml = true;
                try
                {
                    client.Send(mail);
                }
                catch
                {
                    _ = Task.Delay(2000);
                    client.Send(mail);
                }
            }
            catch
            {
                // El aviso es cortesía; el picking ya ha hecho lo correcto.
            }
        }

        private static string GenerarCuerpo(List<PedidoPicking> pedidos, DateTime diaEntrega)
        {
            System.Globalization.CultureInfo castellano = new System.Globalization.CultureInfo("es-ES");
            StringBuilder s = new StringBuilder();
            _ = s.Append("<p>Estos pedidos no han cogido picking porque se entregarían el ");
            _ = s.Append(diaEntrega.ToString("dddd d 'de' MMMM", castellano));
            _ = s.Append(", y ese día de la semana el cliente cierra (días de servir de su ficha). ");
            _ = s.Append("Saldrán solos en la primera pasada cuya entrega caiga en día abierto; ");
            _ = s.Append("para forzar la salida, poner una fecha de entrega concreta en el pedido.</p>");
            _ = s.Append("<table border='1' cellpadding='4' cellspacing='0'><tr><th>Pedido</th><th>Cliente</th><th>Días de servir (L-V)</th></tr>");
            foreach (PedidoPicking pedido in pedidos)
            {
                _ = s.Append($"<tr><td>{pedido.Id}</td><td>{pedido.Cliente?.Trim()}</td><td>{pedido.DiasEnServir?.Trim()}</td></tr>");
            }
            _ = s.Append("</table>");
            return s.ToString();
        }
    }
}
