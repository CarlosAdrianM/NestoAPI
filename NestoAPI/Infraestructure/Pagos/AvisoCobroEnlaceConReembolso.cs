using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>NestoAPI#679, punto 3: el cobro de un enlace de pago cuyo pedido va en reparto contra reembolso.</summary>
    public class CobroEnlaceConEnvioEnReparto
    {
        public PagoTPV Pago { get; set; }
        public int Pedido { get; set; }
        public EnviosAgencia Envio { get; set; }
        public string NombreAgencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#679, punto 3 (pedido 928521): el cliente paga un enlace de pago del pedido cuando el envío ya está en reparto
    /// con reembolso, así que la agencia intentará cobrarle otra vez («REPARTO FALLIDO»). Aviso inmediato a logística (con
    /// copia a administración) por correo y en la campana de Nesto a quien generó el enlace, para quitar o cambiar el
    /// reembolso con la agencia antes del reparto. Cuando esté NestoAPI#597, el reembolso se podrá cambiar solo.
    /// <para>Va en un job de Hangfire: la notificación de Redsys no espera ni puede fallar por esto.</para>
    /// </summary>
    public static class AvisoCobroEnlaceConReembolsoJobs
    {
        public const string TIPO_NOTIFICACION = "CobroEnlaceConReembolso";
        private const string DOMINIO = "NUEVAVISION\\";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>Sin reintentos automáticos: un reintento podría mandar el correo dos veces.</summary>
        [Hangfire.AutomaticRetry(Attempts = 0)]
        public static Task Avisar(int idPago)
        {
            return Avisar(idPago, LeerDeBd, new ServicioCorreoElectronico(), new ServicioNotificacionesPush(), ex => ElmahHelper.Log(ex),
                Agencias.RepartoPropio.AvisoPagoReparto.EsDelReparto);
        }

        /// <returns>True si había que avisar (el envío del pedido está en reparto con reembolso).</returns>
        internal static async Task<bool> Avisar(int idPago, Func<int, Task<CobroEnlaceConEnvioEnReparto>> leer,
            IServicioCorreoElectronico correo, IServicioNotificacionesPush notificaciones, Action<Exception> registrar,
            Func<int, Task<bool>> esDelReparto = null)
        {
            CobroEnlaceConEnvioEnReparto cobro;
            try
            {
                // NestoAPI#773: un enlace del reparto propio (cobro con tarjeta por WhatsApp) lo tiene resuelto el repartidor en la mano:
                // nada de «URGENTE» a logística ni a quien lo creó
                if (esDelReparto != null && await esDelReparto(idPago).ConfigureAwait(false))
                {
                    return false;
                }
                cobro = await leer(idPago).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Enlace cobrado con reembolso #679] No se pudo comprobar el envío del enlace de pago {idPago}: {ex.Message}", ex));
                return false;
            }
            if (cobro?.Pago == null || cobro.Envio == null)
            {
                return false;
            }

            try
            {
                using (MailMessage mail = ComponerCorreo(cobro))
                {
                    if (!correo.EnviarCorreoSMTP(mail))
                    {
                        registrar(new Exception($"[Enlace cobrado con reembolso #679] No ha salido el correo a logística del envío {cobro.Envio.Numero} (pedido {cobro.Pedido})."));
                    }
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Enlace cobrado con reembolso #679] No se pudo mandar el correo a logística del envío {cobro.Envio.Numero} (pedido {cobro.Pedido}): {ex.Message}", ex));
            }

            string usuario = AvisoCobroNestoPago.Destinatario(cobro.Pago.Usuario);
            if (usuario != null)
            {
                try
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + usuario, Constantes.Aplicaciones.NESTO, ComponerNotificacion(cobro)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Enlace cobrado con reembolso #679] No se pudo avisar en la campana de Nesto a {usuario} (pedido {cobro.Pedido}): {ex.Message}", ex));
                }
            }
            return true;
        }

        internal static MailMessage ComponerCorreo(CobroEnlaceConEnvioEnReparto cobro)
        {
            PagoTPV pago = cobro.Pago;
            EnviosAgencia envio = cobro.Envio;
            string agencia = string.IsNullOrWhiteSpace(cobro.NombreAgencia) ? envio.Agencia.ToString(CultureInfo.InvariantCulture) : cobro.NombreAgencia.Trim();
            string Fila(string etiqueta, string valor) =>
                $"<tr><td style='padding:6px 0;color:#888;width:170px'>{etiqueta}</td><td style='padding:6px 0'>{HttpUtility.HtmlEncode(valor ?? string.Empty)}</td></tr>";

            string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden'>
                <tr><td style='background:#c0392b;padding:20px 30px;text-align:center'>
                    <h1 style='color:white;font-size:18px;margin:0'>Enlace de pago cobrado con el envío en reparto contra reembolso</h1>
                </td></tr>
                <tr><td style='padding:25px 30px;font-size:14px'>
                    <p style='margin:0 0 16px 0'>El cliente acaba de pagar con un enlace de pago el pedido {cobro.Pedido}, pero el envío sigue en reparto con un
                    reembolso de <strong>{envio.Reembolso.ToString("N2", Espanol)} &euro;</strong>. Si no se quita o se cambia el reembolso con la agencia
                    antes del reparto, la agencia intentará cobrarle otra vez.</p>
                    <table style='width:100%;font-size:14px'>
                        {Fila("Pedido", cobro.Pedido.ToString(CultureInfo.InvariantCulture))}
                        {Fila("Cliente", pago.Cliente?.Trim())}
                        {Fila("Envío", envio.Numero.ToString(CultureInfo.InvariantCulture))}
                        {Fila("Agencia", agencia)}
                        {Fila("Nº de seguimiento", envio.CodigoBarras?.Trim())}
                        {Fila("Reembolso del envío", envio.Reembolso.ToString("N2", Espanol) + " €")}
                        {Fila("Cobrado con el enlace", pago.Importe.ToString("N2", Espanol) + " €")}
                        {Fila("Concepto del enlace", pago.Descripcion?.Trim())}
                        {Fila("Nº de orden", pago.NumeroOrden?.Trim())}
                        {Fila("Autorización", pago.CodigoAutorizacion?.Trim())}
                        {Fila("Enlace creado por", pago.Usuario?.Trim())}
                    </table>
                </td></tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

            var mail = new MailMessage
            {
                From = new MailAddress(Constantes.Correos.CORREO_ADMON, "NestoPago"),
                Subject = string.Format(Espanol, "URGENTE: pedido {0} pagado por enlace con el envío {1} en reparto contra reembolso de {2:N2} €",
                    cobro.Pedido, envio.Numero, envio.Reembolso),
                Body = html,
                IsBodyHtml = true
            };
            mail.To.Add(Constantes.Correos.LOGISTICA);
            mail.CC.Add(Constantes.Correos.CORREO_ADMON);
            return mail;
        }

        internal static NotificacionPushDTO ComponerNotificacion(CobroEnlaceConEnvioEnReparto cobro) => new NotificacionPushDTO
        {
            Titulo = $"Pedido {cobro.Pedido}: pagado con el envío en reparto contra reembolso",
            Cuerpo = string.Format(Espanol, "El cliente {0} ha pagado el enlace de {1:C}, pero el envío {2} va contra reembolso de {3:C}. " +
                "Logística y Administración ya tienen el aviso para quitar el reembolso con la agencia; hable con ellos si hace falta.",
                cobro.Pago.Cliente?.Trim(), cobro.Pago.Importe, cobro.Envio.Numero, cobro.Envio.Reembolso),
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = cobro.Pago.Empresa?.Trim(),
                ["pedido"] = cobro.Pedido.ToString(CultureInfo.InvariantCulture),
                ["envio"] = cobro.Envio.Numero.ToString(CultureInfo.InvariantCulture),
                ["cliente"] = cobro.Pago.Cliente?.Trim()
            }
        };

        /// <summary>El enlace cobrado, su pedido y el envío en reparto con reembolso; null si no hay que avisar.</summary>
        internal static async Task<CobroEnlaceConEnvioEnReparto> LeerDeBd(int idPago)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                PagoTPV pago = await db.PagosTPV.AsNoTracking().FirstOrDefaultAsync(p => p.Id == idPago).ConfigureAwait(false);
                int? pedido = pago == null ? null : EnlacesPagoPedido.PedidoDelConcepto(pago.Descripcion);
                if (pedido == null || !EnlacesPagoPedido.EsEnlaceDelPedido(pago.Tipo, pago.Descripcion, pedido.Value) ||
                    !string.Equals(pago.Estado?.Trim(), Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                string empresa = pago.Empresa?.Trim();
                int numeroPedido = pedido.Value;
                List<EnviosAgencia> envios = await db.EnviosAgencias.AsNoTracking()
                    .Where(e => e.Empresa == empresa && e.Pedido == numeroPedido)
                    .ToListAsync().ConfigureAwait(false);
                EnviosAgencia envio = EnlacesPagoPedido.EnvioEnRepartoConReembolso(envios);
                if (envio == null)
                {
                    return null;
                }
                string nombreAgencia = await db.AgenciasTransportes.AsNoTracking()
                    .Where(a => a.Numero == envio.Agencia).Select(a => a.Nombre)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                return new CobroEnlaceConEnvioEnReparto { Pago = pago, Pedido = numeroPedido, Envio = envio, NombreAgencia = nombreAgencia };
            }
        }
    }
}
