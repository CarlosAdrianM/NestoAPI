using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#565 (Carlos, 29/09/26): cuando un cliente paga un enlace de NestoPago, además del correo a
    /// administración (con copia a quien creó el enlace, #142), quien lo creó recibe el aviso en la campana
    /// de Nesto (SignalR) y en NestoApp (push). Mismo reparto que el aviso de picking con importe (#555).
    /// Solo los enlaces que crea un usuario (TPV virtual): los cobros de la tienda y el alta de tarjeta no.
    /// </summary>
    public static class AvisoCobroNestoPago
    {
        public const string TIPO_NOTIFICACION = "CobroNestoPago";
        private const string DOMINIO = "NUEVAVISION\\";
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        /// <summary>
        /// El usuario (sin dominio) al que avisar, o null si el enlace no lo creó un empleado o un vendedor:
        /// los que crea un cliente se guardan con su correo o su número («APP\15191», «15191»).
        /// </summary>
        internal static string Destinatario(string usuarioDelPago)
        {
            if (string.IsNullOrWhiteSpace(usuarioDelPago) || usuarioDelPago.Contains("@"))
            {
                return null;
            }
            string usuario = usuarioDelPago.Trim();
            int barra = usuario.IndexOf('\\');
            if (barra >= 0)
            {
                string dominio = usuario.Substring(0, barra);
                if (!string.Equals(dominio, DOMINIO.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                usuario = usuario.Substring(barra + 1).Trim();
            }
            return usuario.Length == 0 || usuario.All(char.IsDigit) ? null : usuario;
        }

        /// <summary>¿Este cobro se avisa en las aplicaciones? Solo enlaces de pago creados por un usuario.</summary>
        internal static bool SeAvisa(PagoTPV pago)
        {
            return pago != null
                && string.Equals(pago.Tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase)
                && Destinatario(pago.Usuario) != null;
        }

        internal static NotificacionPushDTO ComponerNotificacion(PagoTPV pago, string errorContabilizacion = null)
        {
            if (!SeAvisa(pago))
            {
                return null;
            }
            string cliente = pago.Cliente?.Trim();
            string contacto = pago.Contacto?.Trim();
            string clienteConContacto = string.IsNullOrEmpty(contacto) || contacto == "0" ? cliente : $"{cliente}/{contacto}";
            string concepto = string.IsNullOrWhiteSpace(pago.Descripcion) ? "" : $" ({pago.Descripcion.Trim()})";
            string cuerpo = $"El cliente {clienteConContacto} ha pagado el enlace{concepto}.";
            if (!string.IsNullOrEmpty(errorContabilizacion))
            {
                cuerpo += " No se ha podido contabilizar: administración ya tiene el aviso por correo.";
            }

            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = pago.Empresa?.Trim(),
                ["cliente"] = cliente,
                ["contacto"] = contacto,
                ["numeroOrden"] = pago.NumeroOrden?.Trim(),
                ["importe"] = pago.Importe.ToString(CultureInfo.InvariantCulture)
            };
            return new NotificacionPushDTO
            {
                Titulo = $"Cobro NestoPago: {pago.Importe.ToString("N2", ES)} €",
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = datos
            };
        }

        /// <summary>
        /// Campana de Nesto + push de NestoApp. Nunca lanza: un fallo aquí no puede afectar al cobro.
        /// </summary>
        public static async Task Avisar(string usuarioDelPago, NotificacionPushDTO notificacion, IServicioNotificacionesPush notificaciones)
        {
            string usuario = Destinatario(usuarioDelPago);
            if (usuario == null || notificacion == null || notificaciones == null)
            {
                return;
            }
            try
            {
                await notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[CobroNestoPago #565] No se pudo avisar en la campana de Nesto a {usuario}: {ex.Message}", ex));
            }
            try
            {
                _ = await notificaciones.EnviarAUsuario(usuario, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[CobroNestoPago #565] No se pudo enviar la push de NestoApp a {usuario}: {ex.Message}", ex));
            }
        }
    }
}
