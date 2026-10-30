using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#746: dependencias de la pasada, inyectables para testearla sin BD, Hangfire, Firebase ni SMTP.</summary>
    internal class DependenciasPlazasLlenasEventos
    {
        public Func<DateTime> Ahora { get; set; }
        /// <summary>(evento o null = todos) → lo que hay que avisar y lo anulado (ya apuntado en la BD).</summary>
        public Func<int?, Task<ResultadoPlazasLlenas>> Procesar { get; set; }
        /// <summary>(empresa, cliente, contacto) → el correo del cliente (el de las facturas), o null.</summary>
        public Func<string, string, string, Task<string>> CorreoCliente { get; set; }
        public Func<string, string, string, Task<string>> NombreCliente { get; set; }
        public IServicioNotificacionesPush Push { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        /// <summary>(usuario que creó la inscripción, notificación) → campana de Nesto + push de NestoApp. Nunca lanza.</summary>
        public Func<string, NotificacionPushDTO, Task> AvisarUsuario { get; set; }
        /// <summary>(evento, cuándo) → programa otra pasada de ese evento (Hangfire en producción).</summary>
        public Action<int, DateTime> Programar { get; set; }
    }

    /// <summary>
    /// NestoAPI#746 (política de Pilar aprobada por Carlos, 30/10/26): «se reserva sin pagar MIENTRAS HAYA PLAZAS».
    /// <list type="bullet">
    /// <item>Sin sondeo: la pasada de un evento se encola al llenarse (inscripción nueva, pagada que entra, pago fuera de plazo)
    /// y se programa para el momento exacto en que vence el próximo plazo. Una pasada diaria a las 10:05 recoge lo que se
    /// haya escapado (p. ej. un evento al que se le bajan las plazas).</item>
    /// <item>Solo entre las 9:00 y las 21:00 (como el recordatorio): fuera de horario se aplaza a las 9:00.</item>
    /// <item>Aviso al cliente (push a la app y correo de usted, con su enlace de pago) y a quien la inscribió (campana de Nesto y
    /// push de NestoApp); lo mismo al anular.</item>
    /// </list>
    /// </summary>
    public static class PlazasLlenasEventosJobsService
    {
        public const string CRON = "5 10 * * *"; // todos los días a las 10:05 (red de seguridad)
        public const string TIPO_NOTIFICACION_AVISO = "PlazasLlenasEvento";
        public const string TIPO_NOTIFICACION_ANULADA = "ReservaEventoAnuladaSinPago";
        public const string TIPO_NOTIFICACION_PAGO_FUERA_DE_PLAZO = "PagoEventoFueraDePlazo";
        internal const string MOTIVO_HORARIO = "Fuera de horario: los avisos de plazas llenas solo salen entre las 9:00 y las 21:00.";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        // ---------------- Hangfire ----------------

        /// <summary>Encola la pasada de ese evento (al llenarse). Nunca lanza.</summary>
        public static void Encolar(int eventoId)
        {
            try
            {
                _ = BackgroundJob.Enqueue(() => ProcesarEvento(eventoId));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido encolar el evento {eventoId}: {ex.Message}", ex));
            }
        }

        private static void ProgramarEnHangfire(int eventoId, DateTime cuando)
        {
            try
            {
                _ = BackgroundJob.Schedule(() => ProcesarEvento(eventoId), new DateTimeOffset(cuando));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido programar el evento {eventoId} para {cuando:g}: {ex.Message}", ex));
            }
        }

        /// <summary>La pasada diaria (todos los eventos activos por venir con plazas).</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static Task Procesar() => Ejecutar(null);

        /// <summary>La pasada de un evento (al llenarse o al vencer un plazo).</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static Task ProcesarEvento(int eventoId) => Ejecutar(eventoId);

        private static async Task Ejecutar(int? eventoId)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var servicio = new ServicioInscripcionesEventos(db);
                    _ = await Procesar(new DependenciasPlazasLlenasEventos
                    {
                        Ahora = () => DateTime.Now,
                        Procesar = id => servicio.ProcesarPlazasLlenas(id),
                        CorreoCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.CorreoDelCliente(db, empresa, cliente, contacto),
                        NombreCliente = (empresa, cliente, contacto) => RecordatorioEventosJobsService.NombreDelCliente(db, empresa, cliente, contacto),
                        Push = new ServicioNotificacionesPush(),
                        Correo = new ServicioCorreoElectronico(),
                        AvisarUsuario = (usuario, notificacion) => AvisoCobroNestoPago.Avisar(usuario, notificacion, new ServicioNotificacionesPush()),
                        Programar = ProgramarEnHangfire
                    }, eventoId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Plazas llenas de eventos] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        // ---------------- Núcleo ----------------

        internal static async Task<ResultadoPlazasLlenas> Procesar(DependenciasPlazasLlenasEventos deps, int? eventoId)
        {
            DateTime ahora = deps.Ahora();
            if (!RecordatorioEventosJobsService.DentroDeHorario(ahora))
            {
                // Nunca de noche: la pasada del evento se aplaza a las 9:00 (la diaria ya cae dentro de horario)
                if (eventoId.HasValue)
                {
                    deps.Programar?.Invoke(eventoId.Value, SiguienteApertura(ahora));
                }
                return new ResultadoPlazasLlenas { MotivoNoSeProcesa = MOTIVO_HORARIO };
            }
            ResultadoPlazasLlenas resultado = await deps.Procesar(eventoId).ConfigureAwait(false) ?? new ResultadoPlazasLlenas();
            if (resultado.MotivoNoSeProcesa != null)
            {
                return resultado;
            }

            foreach (AvisoPlazasLlenasInscripcion anulada in resultado.Anuladas)
            {
                await AvisarCliente(deps, anulada, NotificacionAnulada(anulada), CorreoAnulada).ConfigureAwait(false);
                await AvisarQuienInscribio(deps, anulada, NotificacionInternaAnulada(anulada)).ConfigureAwait(false);
            }
            foreach (AvisoPlazasLlenasInscripcion aviso in resultado.Avisos)
            {
                await AvisarCliente(deps, aviso, NotificacionAviso(aviso), CorreoAviso).ConfigureAwait(false);
                await AvisarQuienInscribio(deps, aviso, NotificacionInternaAviso(aviso)).ConfigureAwait(false);
            }
            // La siguiente pasada, justo cuando vence cada plazo pendiente (sin sondeo)
            foreach (KeyValuePair<int, DateTime> plazo in resultado.ProximosPlazos)
            {
                deps.Programar?.Invoke(plazo.Key, plazo.Value > ahora ? plazo.Value.AddMinutes(1) : ahora.AddMinutes(1));
            }
            return resultado;
        }

        /// <summary>Las 9:00 de hoy si aún no han llegado; si no, las de mañana.</summary>
        internal static DateTime SiguienteApertura(DateTime ahora)
        {
            DateTime hoy = ahora.Date + new TimeSpan(9, 0, 0);
            return ahora < hoy ? hoy : hoy.AddDays(1);
        }

        private static async Task AvisarCliente(DependenciasPlazasLlenasEventos deps, AvisoPlazasLlenasInscripcion aviso, NotificacionPushDTO push,
            Func<AvisoPlazasLlenasInscripcion, string, string, MailMessage> correo)
        {
            EventoInscripcion inscripcion = aviso.Inscripcion;
            string empresa = Limpiar(inscripcion.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = Limpiar(inscripcion.Cliente);
            string contacto = Limpiar(inscripcion.Contacto);
            try
            {
                _ = await deps.Push.EnviarACliente(empresa, cliente, push).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido mandar la push al cliente {cliente} (inscripción {inscripcion.Id}): {ex.Message}", ex));
            }
            try
            {
                string destino = await deps.CorreoCliente(empresa, cliente, contacto).ConfigureAwait(false);
                if (destino == null)
                {
                    return;
                }
                string nombre = await deps.NombreCliente(empresa, cliente, contacto).ConfigureAwait(false);
                using (MailMessage mail = correo(aviso, nombre, destino))
                {
                    if (!deps.Correo.EnviarCorreoSMTP(mail))
                    {
                        ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido mandar el correo a {destino} (inscripción {inscripcion.Id})."));
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido mandar el correo de la inscripción {inscripcion.Id}: {ex.Message}", ex));
            }
        }

        private static async Task AvisarQuienInscribio(DependenciasPlazasLlenasEventos deps, AvisoPlazasLlenasInscripcion aviso, NotificacionPushDTO notificacion)
        {
            try
            {
                await deps.AvisarUsuario(aviso.Inscripcion.UsuarioCreacion, notificacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido avisar a quien inscribió la {aviso.Inscripcion.Id}: {ex.Message}", ex));
            }
        }

        // ---------------- Textos (de usted) ----------------

        private static string Titulo(Evento evento) => evento?.Titulo?.Trim();

        private static string ClienteYNombre(EventoInscripcion inscripcion)
        {
            string contacto = Limpiar(inscripcion.Contacto);
            return string.IsNullOrEmpty(contacto) || contacto == "0"
                ? Limpiar(inscripcion.Cliente)
                : $"{Limpiar(inscripcion.Cliente)}/{contacto}";
        }

        private static Dictionary<string, string> Datos(string tipo, AvisoPlazasLlenasInscripcion aviso) => new Dictionary<string, string>
        {
            ["tipo"] = tipo,
            ["empresa"] = Limpiar(aviso.Inscripcion.Empresa),
            ["cliente"] = Limpiar(aviso.Inscripcion.Cliente),
            ["contacto"] = Limpiar(aviso.Inscripcion.Contacto),
            ["eventoId"] = aviso.Evento.Id.ToString(CultureInfo.InvariantCulture),
            ["inscripcionId"] = aviso.Inscripcion.Id.ToString(CultureInfo.InvariantCulture),
            ["plazo"] = aviso.Plazo.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            ["urlPago"] = aviso.UrlPago ?? string.Empty
        };

        /// <summary>La push al cliente: ya no quedan plazas libres y tiene que pagar antes del plazo.</summary>
        internal static NotificacionPushDTO NotificacionAviso(AvisoPlazasLlenasInscripcion aviso) => new NotificacionPushDTO
        {
            Titulo = $"Pague su plaza en «{Titulo(aviso.Evento)}»",
            Cuerpo = $"Ya están reservadas todas las plazas. Para mantener la suya, pague antes del {ReglasPlazasLlenas.DiaYHora(aviso.Plazo)}; si no, la perderá.",
            Tipo = TIPO_NOTIFICACION_AVISO,
            Datos = Datos(TIPO_NOTIFICACION_AVISO, aviso)
        };

        internal static MailMessage CorreoAviso(AvisoPlazasLlenasInscripcion aviso, string nombre, string destino)
        {
            var cuerpo = new StringBuilder();
            Inicio(cuerpo, nombre);
            cuerpo.Append($"<p>Ya están reservadas todas las plazas de <b>«{Html(Titulo(aviso.Evento))}»</b>, " +
                $"{Html(RecordatorioEventosJobsService.CuandoYDonde(aviso.Evento))}, y la suya está pendiente de pago.</p>");
            cuerpo.Append($"<p>Para mantener su plaza, <b>pague antes del {Html(ReglasPlazasLlenas.DiaYHora(aviso.Plazo))}</b>. " +
                "Si no, la perderá y quedará libre para otra persona.</p>");
            BotonPago(cuerpo, aviso.UrlPago, aviso.Inscripcion?.FormaPago);
            cuerpo.Append("<p>Si ya la ha pagado, no tenga en cuenta este mensaje.</p>");
            return Correo(cuerpo, $"Pague su plaza en «{Titulo(aviso.Evento)}» antes del {ReglasPlazasLlenas.DiaYHora(aviso.Plazo)}", destino);
        }

        internal static NotificacionPushDTO NotificacionAnulada(AvisoPlazasLlenasInscripcion aviso) => new NotificacionPushDTO
        {
            Titulo = $"Su reserva en «{Titulo(aviso.Evento)}» se ha anulado",
            Cuerpo = "No hemos recibido el pago a tiempo y su plaza ha quedado libre. Si quiere ir y queda alguna plaza, puede volver a inscribirse.",
            Tipo = TIPO_NOTIFICACION_ANULADA,
            Datos = Datos(TIPO_NOTIFICACION_ANULADA, aviso)
        };

        internal static MailMessage CorreoAnulada(AvisoPlazasLlenasInscripcion aviso, string nombre, string destino)
        {
            var cuerpo = new StringBuilder();
            Inicio(cuerpo, nombre);
            cuerpo.Append($"<p>No hemos recibido el pago de su plaza en <b>«{Html(Titulo(aviso.Evento))}»</b> antes del " +
                $"{Html(ReglasPlazasLlenas.DiaYHora(aviso.Plazo))}, así que su reserva se ha anulado y la plaza ha quedado libre.</p>");
            cuerpo.Append("<p>Si todavía quiere ir y queda alguna plaza, puede volver a inscribirse. Si ya había pagado, " +
                "díganoslo y lo revisamos enseguida.</p>");
            return Correo(cuerpo, $"Su reserva en «{Titulo(aviso.Evento)}» se ha anulado", destino);
        }

        /// <summary>Campana y NestoApp de quien la inscribió.</summary>
        internal static NotificacionPushDTO NotificacionInternaAviso(AvisoPlazasLlenasInscripcion aviso) => new NotificacionPushDTO
        {
            Titulo = "Evento lleno: reserva pendiente de pago",
            Cuerpo = $"«{Titulo(aviso.Evento)}» está lleno. Se ha avisado al cliente {ClienteYNombre(aviso.Inscripcion)} de que pague " +
                $"antes del {ReglasPlazasLlenas.DiaYHora(aviso.Plazo)}; si no, su reserva se anulará sola.",
            Tipo = TIPO_NOTIFICACION_AVISO,
            Datos = Datos(TIPO_NOTIFICACION_AVISO, aviso)
        };

        internal static NotificacionPushDTO NotificacionInternaAnulada(AvisoPlazasLlenasInscripcion aviso) => new NotificacionPushDTO
        {
            Titulo = "Reserva anulada por falta de pago",
            Cuerpo = $"La reserva del cliente {ClienteYNombre(aviso.Inscripcion)} en «{Titulo(aviso.Evento)}» se ha anulado: no pagó antes del " +
                $"{ReglasPlazasLlenas.DiaYHora(aviso.Plazo)}. La plaza ha quedado libre.",
            Tipo = TIPO_NOTIFICACION_ANULADA,
            Datos = Datos(TIPO_NOTIFICACION_ANULADA, aviso)
        };

        // ---------------- Pago fuera de plazo ----------------

        /// <summary>
        /// Llega dinero de una inscripción Caducada (ya ha quedado Pagada) o Anulada (sigue anulada): correo a Administración y
        /// campana a quien la inscribió, para que decidan si se mantiene la plaza o se devuelve el dinero.
        /// </summary>
        public static async Task AvisarPagoFueraDePlazo(EventoInscripcion inscripcion, Evento evento, EstadoInscripcionEvento estadoAnterior, int ocupadas)
        {
            using (MailMessage mail = CorreoPagoFueraDePlazo(inscripcion, evento, estadoAnterior, ocupadas))
            {
                if (!new ServicioCorreoElectronico().EnviarCorreoSMTP(mail))
                {
                    ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido mandar a Administración el pago fuera de plazo de la inscripción {inscripcion.Id}."));
                }
            }
            await AvisoCobroNestoPago.Avisar(inscripcion.UsuarioCreacion, NotificacionPagoFueraDePlazo(inscripcion, evento, estadoAnterior, ocupadas),
                new ServicioNotificacionesPush()).ConfigureAwait(false);
        }

        internal static string TextoPagoFueraDePlazo(EventoInscripcion inscripcion, Evento evento, EstadoInscripcionEvento estadoAnterior, int ocupadas)
        {
            string plazas = evento.Plazas.HasValue ? $"{ocupadas} de {evento.Plazas.Value} plazas ocupadas" : $"{ocupadas} plazas ocupadas (sin límite)";
            string sobrepasado = evento.Plazas.HasValue && ocupadas > evento.Plazas.Value ? " El evento está por encima de sus plazas." : "";
            string queda = estadoAnterior == EstadoInscripcionEvento.Anulada
                ? "La inscripción sigue anulada y el cobro está en su extracto: decidan si se reactiva (marcándola pagada) o se devuelve el dinero."
                : "El dinero manda: la inscripción ha quedado pagada y ocupa plaza. Decidan si se mantiene o se devuelve el dinero.";
            return $"El cliente {ClienteYNombre(inscripcion)} ha pagado su inscripción en «{Titulo(evento)}» ({evento.Fecha:dd/MM/yyyy}), " +
                $"que estaba {ReglasInscripcionEvento.Texto(estadoAnterior).ToLowerInvariant()}. {queda} Ahora hay {plazas}.{sobrepasado}";
        }

        internal static MailMessage CorreoPagoFueraDePlazo(EventoInscripcion inscripcion, Evento evento, EstadoInscripcionEvento estadoAnterior, int ocupadas)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Pago de una inscripción {ReglasInscripcionEvento.Texto(estadoAnterior).ToLowerInvariant()} en «{Titulo(evento)}» (cliente {Limpiar(inscripcion.Cliente)})",
                Body = "<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>" +
                    $"<p>{Html(TextoPagoFueraDePlazo(inscripcion, evento, estadoAnterior, ocupadas))}</p>" +
                    $"<p>Inscripción {inscripcion.Id}, importe {(inscripcion.ImportePagado ?? 0).ToString("N2", es)} €, forma de pago {Html(inscripcion.FormaPago?.Trim())}, " +
                    $"enlace de pago {(inscripcion.PagoTPVId?.ToString(CultureInfo.InvariantCulture) ?? "—")}.</p>" +
                    "<p style='color:#666'>Reservas de eventos sin pagar (NestoAPI#746).</p></body></html>",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);
            return mail;
        }

        internal static NotificacionPushDTO NotificacionPagoFueraDePlazo(EventoInscripcion inscripcion, Evento evento, EstadoInscripcionEvento estadoAnterior, int ocupadas) =>
            new NotificacionPushDTO
            {
                Titulo = "Pago fuera de plazo de un evento",
                Cuerpo = TextoPagoFueraDePlazo(inscripcion, evento, estadoAnterior, ocupadas),
                Tipo = TIPO_NOTIFICACION_PAGO_FUERA_DE_PLAZO,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION_PAGO_FUERA_DE_PLAZO,
                    ["empresa"] = Limpiar(inscripcion.Empresa),
                    ["cliente"] = Limpiar(inscripcion.Cliente),
                    ["contacto"] = Limpiar(inscripcion.Contacto),
                    ["eventoId"] = evento.Id.ToString(CultureInfo.InvariantCulture),
                    ["inscripcionId"] = inscripcion.Id.ToString(CultureInfo.InvariantCulture)
                }
            };

        // ---------------- Utilidades ----------------

        private static void Inicio(StringBuilder cuerpo, string nombre)
        {
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre.Trim()))}:</p>");
        }

        private static void BotonPago(StringBuilder cuerpo, string urlPago, string formaPago)
        {
            if (!string.IsNullOrWhiteSpace(urlPago))
            {
                cuerpo.Append($"<p><a href='{Html(urlPago)}' style='display:inline-block;padding:12px 20px;background:#2e7d32;color:#ffffff;" +
                    "text-decoration:none;border-radius:6px;font-weight:bold;'>Pagar mi plaza</a></p>");
            }
            else
            {
                cuerpo.Append($"<p>{Html(ReglasInscripcionEvento.ComoPagarSinEnlace(formaPago))}</p>"); // Sugerencia 899: según su forma de pago
            }
        }

        private static MailMessage Correo(StringBuilder cuerpo, string asunto, string destino)
        {
            cuerpo.Append("<p>Muchas gracias.</p><p>Nueva Visión</p></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = asunto,
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            return mail;
        }

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }
}
