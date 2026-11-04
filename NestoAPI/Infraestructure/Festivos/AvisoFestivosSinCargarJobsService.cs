using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Festivos
{
    /// <summary>
    /// NestoAPI#779: el día 1 de cada mes comprueba que el calendario tiene festivos del año que toca: en diciembre, los del
    /// año SIGUIENTE (para cargarlos antes de enero); el resto de meses, los del año en curso (red de seguridad). Si no hay
    /// ninguno (ni en la tabla ni en la lista del código, <see cref="CalendarioFestivos.DelAnio"/>), avisa a Dirección en la
    /// campana de Nesto (buzón + SignalR, como el aviso de Verifactu) y por correo a direccion@. Sin festivos, remesas,
    /// fechas de entrega, picking y avisos tratan los festivos de ese año como laborables.
    /// </summary>
    public class AvisoFestivosSinCargarJobsService
    {
        public const string JOB_ID = "festivos-sin-cargar";
        /// <summary>El día 1 de cada mes a las 9:00.</summary>
        public const string CRON = "0 9 1 * *";
        public const string TIPO_NOTIFICACION = "FestivosSinCargar";
        internal const string DOMINIO = "NUEVAVISION\\";
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";

        private readonly Func<int, bool> hayFestivos;
        private readonly Func<List<string>> usuariosDireccion;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly IServicioCorreoElectronico correo;

        public AvisoFestivosSinCargarJobsService() : this(null)
        {
        }

        internal AvisoFestivosSinCargarJobsService(Func<int, bool> hayFestivos, Func<List<string>> usuariosDireccion = null,
            IServicioNotificacionesPush notificaciones = null, IServicioCorreoElectronico correo = null)
        {
            this.hayFestivos = hayFestivos ?? (anio => CalendarioFestivos.DelAnio(anio).Any());
            this.usuariosDireccion = usuariosDireccion ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION));
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.correo = correo ?? new ServicioCorreoElectronico();
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Comprobar()
        {
            _ = await new AvisoFestivosSinCargarJobsService().ComprobarAsync(DateTime.Today).ConfigureAwait(false);
        }

        /// <summary>El año que hay que tener cargado ese día: en diciembre, el siguiente; si no, el actual.</summary>
        internal static int AnioAComprobar(DateTime hoy) => hoy.Month == 12 ? hoy.Year + 1 : hoy.Year;

        /// <summary>A cuántos usuarios se ha avisado en la campana (0 si hay festivos). Nunca lanza.</summary>
        public async Task<int> ComprobarAsync(DateTime hoy)
        {
            int anio = AnioAComprobar(hoy);
            try
            {
                if (hayFestivos(anio))
                {
                    return 0;
                }
                NotificacionPushDTO notificacion = CrearNotificacion(anio);
                EnviarCorreo(notificacion);
                List<string> usuarios = (usuariosDireccion() ?? new List<string>())
                    .Select(u => u?.Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string usuario in usuarios)
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                return usuarios.Count;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Festivos #779] No se ha podido avisar de que faltan los festivos de {anio}: {ex.Message}", ex));
                return 0;
            }
        }

        private void EnviarCorreo(NotificacionPushDTO notificacion)
        {
            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(CORREO_REMITENTE),
                    Subject = notificacion.Titulo,
                    Body = notificacion.Cuerpo,
                    IsBodyHtml = false
                };
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_DIRECCION));
                _ = correo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Festivos #779] No se ha podido enviar el correo de festivos sin cargar: {ex.Message}", ex));
            }
        }

        internal static NotificacionPushDTO CrearNotificacion(int anio) => new NotificacionPushDTO
        {
            Titulo = $"Faltan los festivos de {anio}",
            Cuerpo = $"El calendario no tiene ningún festivo de {anio}. Mientras no se carguen, las remesas, las fechas de entrega, " +
                "el picking y los avisos tratarán esos festivos como días laborables. Cárguelos en Nesto (Festivos) " +
                "con el calendario laboral de la Comunidad de Madrid (BOCM) y las fiestas locales.",
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["anio"] = anio.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }
}
