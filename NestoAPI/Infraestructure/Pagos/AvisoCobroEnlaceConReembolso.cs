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
        /// <summary>El pedido del enlace (el que acaba de pagar el cliente).</summary>
        public int Pedido { get; set; }
        /// <summary>El primer envío vivo con reembolso (el del propio pedido si lo hay).</summary>
        public EnviosAgencia Envio { get; set; }
        public string NombreAgencia { get; set; }
        /// <summary>
        /// NestoAPI#824 (929852, 05/11/26): todos los envíos vivos con reembolso del cliente y contacto del pedido (el primero es
        /// <see cref="Envio"/>). Vacío: solo <see cref="Envio"/>.
        /// </summary>
        public List<EnvioVivoConReembolso> Envios { get; set; } = new List<EnvioVivoConReembolso>();

        /// <summary>Los envíos a avisar: <see cref="Envios"/> o, si no se ha rellenado, <see cref="Envio"/>.</summary>
        public List<EnvioVivoConReembolso> TodosLosEnvios()
            => Envios != null && Envios.Count > 0
                ? Envios
                : Envio == null ? new List<EnvioVivoConReembolso>() : new List<EnvioVivoConReembolso> { new EnvioVivoConReembolso { Envio = Envio, NombreAgencia = NombreAgencia } };
    }

    /// <summary>NestoAPI#824: un envío vivo con reembolso y el nombre de su agencia.</summary>
    public class EnvioVivoConReembolso
    {
        public EnviosAgencia Envio { get; set; }
        public string NombreAgencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#824 (929852, 05/11/26): qué se puede hacer con el reembolso de un envío que ya está en reparto cuando el cliente ya
    /// lo ha pagado por otro lado.
    /// </summary>
    public static class CambioReembolsoEnReparto
    {
        /// <summary>
        /// ¿La agencia deja quitar o cambiar el reembolso por API con la etiqueta ya en reparto? Hoy NINGUNA: lo de #597
        /// (<see cref="Agencias.IAgenciaRemota.ModificarYEtiquetarAsync"/>) modifica y REETIQUETA (en CTT anula y crea un envío
        /// nuevo), que no vale para un bulto que ya lleva la agencia; ASM/GLS no tienen nada.
        /// <para>TODO NestoAPI#726: cuando esté integrada la CTT Management API (cambio de reembolso con la etiqueta viva), devolver
        /// true para CTT, quitar el reembolso desde <see cref="AvisoCobroEnlaceConReembolsoJobs.Avisar(int)"/> y anotarlo en
        /// EnviosHistoria (y entonces el aviso dirá que ya está quitado).</para>
        /// </summary>
        public static bool AdmiteQuitarPorApi(int agencia) => false;

        /// <summary>Lo que tiene que hacer logística con el envío: el aviso lo dice explícitamente.</summary>
        public static string QueHacer(int agencia)
            => agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                ? "Avise al repartidor para que entregue sin cobrar."
                : "Llame a la agencia para que entreguen sin cobrar.";
    }

    /// <summary>
    /// NestoAPI#679, punto 3 (pedido 928521): el cliente paga un enlace de pago del pedido cuando el envío ya está en reparto
    /// con reembolso, así que la agencia intentará cobrarle otra vez («REPARTO FALLIDO»). Aviso inmediato a logística (con
    /// copia a administración) por correo y en la campana de Nesto a quien generó el enlace, para quitar o cambiar el
    /// reembolso con la agencia antes del reparto.
    /// <para>NestoAPI#824 (929852, 05/11/26): los envíos se buscan por CLIENTE y contacto, no solo por pedido. El reembolso de
    /// un envío se calcula con lo pendiente del cliente, así que la factura del 929852 (sin envío propio: salió «con este
    /// pedido») viajaba en el envío 250527 del 929323, y el aviso no saltó.</para>
    /// <para>Va en un job de Hangfire: la notificación de Redsys no espera ni puede fallar por esto.</para>
    /// </summary>
    public static class AvisoCobroEnlaceConReembolsoJobs
    {
        public const string TIPO_NOTIFICACION = "CobroEnlaceConReembolso";
        /// <summary>NestoAPI#824: los envíos de OTROS pedidos del cliente cuentan si son de los últimos días (los del pedido, siempre).</summary>
        internal const int DIAS_ENVIOS_DEL_CLIENTE = 30;
        private const string DOMINIO = "NUEVAVISION\\";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>Sin reintentos automáticos: un reintento podría mandar el correo dos veces.</summary>
        [Hangfire.AutomaticRetry(Attempts = 0)]
        public static Task Avisar(int idPago)
        {
            return Avisar(idPago, LeerDeBd, new ServicioCorreoElectronico(), new ServicioNotificacionesPush(), ex => ElmahHelper.Log(ex),
                Agencias.RepartoPropio.AvisoPagoReparto.EsDelReparto);
        }

        /// <returns>True si había que avisar (algún envío del cliente está en reparto con reembolso).</returns>
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
            if (cobro?.Pago == null || cobro.TodosLosEnvios().Count == 0)
            {
                return false;
            }
            if (cobro.Envio == null)
            {
                cobro.Envio = cobro.TodosLosEnvios()[0].Envio;
                cobro.NombreAgencia = cobro.TodosLosEnvios()[0].NombreAgencia;
            }

            // TODO NestoAPI#726: con CambioReembolsoEnReparto.AdmiteQuitarPorApi(agencia), quitar aquí el reembolso de ese envío y
            // anotarlo en EnviosHistoria antes de avisar. Hoy ninguna agencia lo admite: solo se avisa.

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

        /// <summary>«envío 250527 (pedido 929323)»; sin el pedido si es el mismo que se ha pagado.</summary>
        private static string EnvioYPedido(EnviosAgencia envio, int pedidoPagado)
            => envio.Pedido.HasValue && envio.Pedido.Value != pedidoPagado
                ? string.Format(CultureInfo.InvariantCulture, "envío {0} (pedido {1})", envio.Numero, envio.Pedido.Value)
                : string.Format(CultureInfo.InvariantCulture, "envío {0}", envio.Numero);

        /// <summary>
        /// NestoAPI#824: «El envío 250527 (pedido 929323) lleva reembolso de 40,03 € y el cliente acaba de pagar el pedido 929852 por
        /// enlace. Llame a la agencia para que entreguen sin cobrar.»
        /// </summary>
        internal static string TextoEnvio(EnviosAgencia envio, int pedidoPagado)
            => TextoSituacion(envio, pedidoPagado) + " " + CambioReembolsoEnReparto.QueHacer(envio.Agencia);

        private static string TextoSituacion(EnviosAgencia envio, int pedidoPagado)
        {
            string pedidoEnvio = envio.Pedido.HasValue ? envio.Pedido.Value.ToString(CultureInfo.InvariantCulture) : "sin pedido";
            return string.Format(Espanol, "El envío {0} (pedido {1}) lleva reembolso de {2:N2} € y el cliente acaba de pagar el pedido {3} por enlace.",
                envio.Numero, pedidoEnvio, envio.Reembolso, pedidoPagado);
        }

        internal static MailMessage ComponerCorreo(CobroEnlaceConEnvioEnReparto cobro)
        {
            PagoTPV pago = cobro.Pago;
            List<EnvioVivoConReembolso> envios = cobro.TodosLosEnvios();
            EnviosAgencia envio = envios[0].Envio;
            string Fila(string etiqueta, string valor) =>
                $"<tr><td style='padding:6px 0;color:#888;width:170px'>{etiqueta}</td><td style='padding:6px 0'>{HttpUtility.HtmlEncode(valor ?? string.Empty)}</td></tr>";
            string Agencia(EnvioVivoConReembolso e) => string.IsNullOrWhiteSpace(e.NombreAgencia)
                ? e.Envio.Agencia.ToString(CultureInfo.InvariantCulture)
                : e.NombreAgencia.Trim();

            string parrafos = string.Concat(envios.Select(e =>
                $"<p style='margin:0 0 12px 0'>{HttpUtility.HtmlEncode(TextoSituacion(e.Envio, cobro.Pedido))} " +
                $"<strong>{HttpUtility.HtmlEncode(CambioReembolsoEnReparto.QueHacer(e.Envio.Agencia))}</strong></p>"));
            string tablasEnvios = string.Concat(envios.Select(e => $@"
                    <table style='width:100%;font-size:14px;margin-top:12px;border-top:1px solid #eee'>
                        {Fila("Envío", e.Envio.Numero.ToString(CultureInfo.InvariantCulture))}
                        {Fila("Pedido del envío", e.Envio.Pedido?.ToString(CultureInfo.InvariantCulture))}
                        {Fila("Agencia", Agencia(e))}
                        {Fila("Nº de seguimiento", e.Envio.CodigoBarras?.Trim())}
                        {Fila("Reembolso del envío", e.Envio.Reembolso.ToString("N2", Espanol) + " €")}
                        {Fila("Qué hacer", CambioReembolsoEnReparto.QueHacer(e.Envio.Agencia))}
                    </table>"));

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
                    {parrafos}
                    <p style='margin:0 0 16px 0'>Si no se quita o se cambia el reembolso con la agencia antes del reparto, la agencia intentará
                    cobrarle otra vez.</p>
                    <table style='width:100%;font-size:14px'>
                        {Fila("Pedido pagado", cobro.Pedido.ToString(CultureInfo.InvariantCulture))}
                        {Fila("Cliente", pago.Cliente?.Trim())}
                        {Fila("Cobrado con el enlace", pago.Importe.ToString("N2", Espanol) + " €")}
                        {Fila("Concepto del enlace", pago.Descripcion?.Trim())}
                        {Fila("Nº de orden", pago.NumeroOrden?.Trim())}
                        {Fila("Autorización", pago.CodigoAutorizacion?.Trim())}
                        {Fila("Enlace creado por", pago.Usuario?.Trim())}
                    </table>
                    {tablasEnvios}
                </td></tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

            string masEnvios = envios.Count > 1 ? string.Format(Espanol, " y {0} más", envios.Count - 1) : string.Empty;
            var mail = new MailMessage
            {
                From = new MailAddress(Constantes.Correos.CORREO_ADMON, "NestoPago"),
                Subject = string.Format(Espanol, "URGENTE: pedido {0} pagado por enlace con el {1}{2} en reparto contra reembolso de {3:N2} €",
                    cobro.Pedido, EnvioYPedido(envio, cobro.Pedido), masEnvios, envio.Reembolso),
                Body = html,
                IsBodyHtml = true
            };
            mail.To.Add(Constantes.Correos.LOGISTICA);
            mail.CC.Add(Constantes.Correos.CORREO_ADMON);
            return mail;
        }

        internal static NotificacionPushDTO ComponerNotificacion(CobroEnlaceConEnvioEnReparto cobro)
        {
            List<EnvioVivoConReembolso> envios = cobro.TodosLosEnvios();
            EnviosAgencia envio = envios[0].Envio;
            string masEnvios = envios.Count > 1 ? string.Format(Espanol, " (y {0} envío{1} más con reembolso)", envios.Count - 1, envios.Count > 2 ? "s" : string.Empty) : string.Empty;
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {cobro.Pedido}: pagado con el envío en reparto contra reembolso",
                Cuerpo = string.Format(Espanol, "El cliente {0} ha pagado el enlace de {1:C} del pedido {2}, pero el {3} va contra reembolso de {4:C}{5}. " +
                    "Logística y Administración ya tienen el aviso para que la agencia entregue sin cobrar; hable con ellos si hace falta.",
                    cobro.Pago.Cliente?.Trim(), cobro.Pago.Importe, cobro.Pedido, EnvioYPedido(envio, cobro.Pedido), envio.Reembolso, masEnvios),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = cobro.Pago.Empresa?.Trim(),
                    ["pedido"] = cobro.Pedido.ToString(CultureInfo.InvariantCulture),
                    ["envio"] = envio.Numero.ToString(CultureInfo.InvariantCulture),
                    ["pedidoEnvio"] = envio.Pedido?.ToString(CultureInfo.InvariantCulture),
                    ["cliente"] = cobro.Pago.Cliente?.Trim()
                }
            };
        }

        /// <summary>
        /// El enlace cobrado, su pedido y los envíos en reparto con reembolso del cliente y contacto del pedido (NestoAPI#824); null
        /// si no hay que avisar.
        /// </summary>
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
                // El contacto, del pedido: en los enlaces de Nesto PagosTPV.Contacto va a NULL
                var cab = await db.CabPedidoVtas.AsNoTracking()
                    .Where(c => c.Empresa == empresa && c.Número == numeroPedido)
                    .Select(c => new { c.Nº_Cliente, c.Contacto })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                string cliente = (cab?.Nº_Cliente ?? pago.Cliente)?.Trim();
                string contacto = cab?.Contacto?.Trim();
                bool porCliente = !string.IsNullOrEmpty(cliente) && !string.IsNullOrEmpty(contacto);
                DateTime desde = DateTime.Today.AddDays(-DIAS_ENVIOS_DEL_CLIENTE);
                List<EnviosAgencia> leidos = await db.EnviosAgencias.AsNoTracking()
                    .Where(e => e.Empresa == empresa && (e.Pedido == numeroPedido ||
                        (porCliente && e.Cliente == cliente && e.Contacto == contacto && e.Fecha >= desde && e.Reembolso > 0)))
                    .ToListAsync().ConfigureAwait(false);
                List<EnviosAgencia> envios = EnlacesPagoPedido.EnviosEnRepartoConReembolso(leidos, numeroPedido);
                if (envios.Count == 0)
                {
                    return null;
                }
                List<int> agencias = envios.Select(e => e.Agencia).Distinct().ToList();
                Dictionary<int, string> nombres = (await db.AgenciasTransportes.AsNoTracking()
                        .Where(a => agencias.Contains(a.Numero))
                        .Select(a => new { a.Numero, a.Nombre })
                        .ToListAsync().ConfigureAwait(false))
                    .GroupBy(a => a.Numero)
                    .ToDictionary(g => g.Key, g => g.First().Nombre);
                List<EnvioVivoConReembolso> vivos = envios
                    .Select(e => new EnvioVivoConReembolso { Envio = e, NombreAgencia = nombres.TryGetValue(e.Agencia, out string nombre) ? nombre : null })
                    .ToList();
                return new CobroEnlaceConEnvioEnReparto
                {
                    Pago = pago,
                    Pedido = numeroPedido,
                    Envio = vivos[0].Envio,
                    NombreAgencia = vivos[0].NombreAgencia,
                    Envios = vivos
                };
            }
        }
    }
}
