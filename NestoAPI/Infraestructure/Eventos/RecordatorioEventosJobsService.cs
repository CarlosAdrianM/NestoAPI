using Hangfire;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#690: dependencias del recordatorio, inyectables para testearlo sin BD, Firebase ni SMTP.</summary>
    internal class DependenciasRecordatorioEventos
    {
        /// <summary>El interruptor (Eventos:RecordatorioAsistencia).</summary>
        public Func<bool> Encendido { get; set; }
        public Func<DateTime> Ahora { get; set; }
        /// <summary>(días antes) → los eventos con las inscripciones que esperan su recordatorio (ya con su clave).</summary>
        public Func<int, Task<List<KeyValuePair<Evento, List<EventoInscripcion>>>>> Pendientes { get; set; }
        /// <summary>Apunta que ya salió el recordatorio de esas inscripciones.</summary>
        public Func<IEnumerable<EventoInscripcion>, Task> MarcarEnviados { get; set; }
        /// <summary>(empresa, cliente, contacto) → el correo del cliente (el de las facturas), o null.</summary>
        public Func<string, string, string, Task<string>> CorreoCliente { get; set; }
        /// <summary>(empresa, cliente, contacto) → el nombre del cliente, o null.</summary>
        public Func<string, string, string, Task<string>> NombreCliente { get; set; }
        public IServicioNotificacionesPush Push { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
    }

    /// <summary>NestoAPI#690: lo que ha hecho una pasada del recordatorio (para los tests y el diagnóstico).</summary>
    public class ResultadoRecordatorioEventos
    {
        /// <summary>Por qué no se ha mirado nada (apagado, fuera de horario). Null si se ha procesado.</summary>
        public string MotivoNoSeEnvia { get; set; }
        public int Inscripciones { get; set; }
        public int Push { get; set; }
        public int Correos { get; set; }
        /// <summary>Sin app ni correo: no le ha llegado nada (la tienda puede llamarle).</summary>
        public List<int> SinCanal { get; set; } = new List<int>();
    }

    /// <summary>
    /// NestoAPI#690: recordatorio de los eventos. Todos los días a las 10:00 (nunca fuera de 9:00-21:00, aunque Hangfire lo
    /// relance tarde) mira los eventos de dentro de dos días (y los de mañana que se quedaran sin él) y, una sola vez por
    /// inscripción (pagadas y gratuitas):
    /// <list type="bullet">
    /// <item>push a la app del cliente (NestoTiendas). Es un aviso de su inscripción, no un consejo: va por EnviarACliente,
    /// como los avisos de pedidos, sin mirar RecibirConsejos. Lleva el tipo y la inscripción para que TNV enseñe «Confirmo
    /// asistencia» y «No podré ir» (POST api/Eventos/Inscripciones/{id}/ConfirmarAsistencia).</item>
    /// <item>correo de usted, al correo de las facturas, con los dos botones del enlace (GET api/Eventos/Asistencia/{clave}).</item>
    /// </list>
    /// Nace detrás del interruptor Eventos:RecordatorioAsistencia (apagado). WhatsApp queda fuera (necesita la Cloud API de Meta).
    /// </summary>
    public static class RecordatorioEventosJobsService
    {
        public const string CRON = "0 10 * * *"; // todos los días a las 10:00 (hay eventos en sábado)
        public const string TIPO_NOTIFICACION = "RecordatorioEvento";
        public const int DIAS_ANTES = 2;
        internal static readonly TimeSpan HORA_DESDE = new TimeSpan(9, 0, 0);
        internal static readonly TimeSpan HORA_HASTA = new TimeSpan(21, 0, 0);
        internal const string MOTIVO_APAGADO = "El recordatorio está apagado (Eventos:RecordatorioAsistencia).";
        internal const string MOTIVO_HORARIO = "Fuera de horario: el recordatorio solo sale entre las 9:00 y las 21:00.";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Procesar()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var servicio = new ServicioInscripcionesEventos(db);
                    _ = await Procesar(new DependenciasRecordatorioEventos
                    {
                        Encendido = () => InterruptoresEventos.RecordatorioAsistencia,
                        Ahora = () => DateTime.Now,
                        Pendientes = dias => servicio.PendientesDeRecordatorio(dias),
                        MarcarEnviados = inscripciones => servicio.MarcarRecordatorioEnviado(inscripciones),
                        CorreoCliente = (empresa, cliente, contacto) => CorreoDelCliente(db, empresa, cliente, contacto),
                        NombreCliente = (empresa, cliente, contacto) => NombreDelCliente(db, empresa, cliente, contacto),
                        Push = new ServicioNotificacionesPush(),
                        Correo = new ServicioCorreoElectronico()
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio de eventos #690] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job.</summary>
        internal static async Task<ResultadoRecordatorioEventos> Procesar(DependenciasRecordatorioEventos deps)
        {
            var resultado = new ResultadoRecordatorioEventos();
            if (!deps.Encendido())
            {
                resultado.MotivoNoSeEnvia = MOTIVO_APAGADO;
                return resultado;
            }
            if (!DentroDeHorario(deps.Ahora()))
            {
                resultado.MotivoNoSeEnvia = MOTIVO_HORARIO;
                return resultado;
            }
            List<KeyValuePair<Evento, List<EventoInscripcion>>> pendientes =
                await deps.Pendientes(DIAS_ANTES).ConfigureAwait(false) ?? new List<KeyValuePair<Evento, List<EventoInscripcion>>>();
            foreach (KeyValuePair<Evento, List<EventoInscripcion>> evento in pendientes)
            {
                var enviadas = new List<EventoInscripcion>();
                foreach (EventoInscripcion inscripcion in evento.Value)
                {
                    try
                    {
                        (bool push, bool correo) = await Avisar(deps, evento.Key, inscripcion).ConfigureAwait(false);
                        resultado.Inscripciones++;
                        resultado.Push += push ? 1 : 0;
                        resultado.Correos += correo ? 1 : 0;
                        if (!push && !correo)
                        {
                            resultado.SinCanal.Add(inscripcion.Id);
                        }
                        enviadas.Add(inscripcion);
                    }
                    catch (Exception ex)
                    {
                        // Una que falla no deja sin recordatorio a las demás; se reintenta mañana (si el evento sigue por delante)
                        ElmahHelper.Log(new Exception($"[Recordatorio de eventos #690] Inscripción {inscripcion.Id}: {ex.Message}", ex));
                    }
                }
                if (enviadas.Any())
                {
                    await deps.MarcarEnviados(enviadas).ConfigureAwait(false);
                }
            }
            return resultado;
        }

        /// <summary>Entre las 9:00 y las 21:00: nunca de madrugada, aunque Hangfire relance el job tarde.</summary>
        internal static bool DentroDeHorario(DateTime ahora) =>
            ahora.TimeOfDay >= HORA_DESDE && ahora.TimeOfDay < HORA_HASTA;

        private static async Task<(bool push, bool correo)> Avisar(DependenciasRecordatorioEventos deps, Evento evento, EventoInscripcion inscripcion)
        {
            string empresa = Limpiar(inscripcion.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = Limpiar(inscripcion.Cliente);
            string contacto = Limpiar(inscripcion.Contacto);

            bool push = false;
            try
            {
                push = await deps.Push.EnviarACliente(empresa, cliente, Notificacion(evento, inscripcion)).ConfigureAwait(false) > 0;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recordatorio de eventos #690] No se ha podido mandar la push al cliente {cliente}: {ex.Message}", ex));
            }

            bool correo = false;
            string destino = await deps.CorreoCliente(empresa, cliente, contacto).ConfigureAwait(false);
            if (destino != null && inscripcion.ClaveAsistencia.HasValue)
            {
                string nombre = await deps.NombreCliente(empresa, cliente, contacto).ConfigureAwait(false);
                using (MailMessage mail = Correo(evento, inscripcion, nombre, destino))
                {
                    correo = deps.Correo.EnviarCorreoSMTP(mail);
                }
                if (!correo)
                {
                    ElmahHelper.Log(new Exception($"[Recordatorio de eventos #690] No se ha podido mandar el correo del recordatorio a {destino} (inscripción {inscripcion.Id})."));
                }
            }
            return (push, correo);
        }

        /// <summary>«el jueves 30 de octubre a las 10:00 en Reina».</summary>
        internal static string CuandoYDonde(Evento evento)
        {
            string cuando = "el " + evento.Fecha.ToString("dddd d 'de' MMMM", es);
            string hora = ReglasModalidadEvento.EscribirHora(evento.HoraInicio);
            if (hora != null)
            {
                cuando += " a las " + hora;
            }
            string donde = Limpiar(evento.Lugar) ?? PermisosEventos.NombreAlmacen(evento.Almacen);
            return string.IsNullOrWhiteSpace(donde) ? cuando : cuando + " en " + donde;
        }

        /// <summary>La push (de usted). TNV pinta los dos botones con el tipo y la inscripción.</summary>
        internal static NotificacionPushDTO Notificacion(Evento evento, EventoInscripcion inscripcion) => new NotificacionPushDTO
        {
            Titulo = $"Recordatorio: «{evento.Titulo?.Trim()}»",
            Cuerpo = $"Le esperamos {CuandoYDonde(evento)}. ¿Nos confirma su asistencia?",
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = Limpiar(inscripcion.Empresa),
                ["cliente"] = Limpiar(inscripcion.Cliente),
                ["contacto"] = Limpiar(inscripcion.Contacto),
                ["eventoId"] = evento.Id.ToString(CultureInfo.InvariantCulture),
                ["inscripcionId"] = inscripcion.Id.ToString(CultureInfo.InvariantCulture),
                ["fecha"] = evento.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["horaInicio"] = ReglasModalidadEvento.EscribirHora(evento.HoraInicio) ?? string.Empty
            }
        };

        /// <summary>https://api.nuevavision.es/api/Eventos/Asistencia/{clave}?respuesta=Confirma</summary>
        internal static string Enlace(Guid clave, RespuestaAsistenciaEvento respuesta, string urlBase = null) =>
            $"{(string.IsNullOrWhiteSpace(urlBase) ? MiniaturasChequeRegalo.UrlPublicaApi : urlBase.Trim().TrimEnd('/'))}" +
            $"/api/Eventos/Asistencia/{clave:D}?respuesta={respuesta}";

        /// <summary>El correo (de usted) con los dos botones.</summary>
        internal static MailMessage Correo(Evento evento, EventoInscripcion inscripcion, string nombre, string destino)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            Guid clave = inscripcion.ClaveAsistencia ?? Guid.Empty;
            string boton(string texto, string color, RespuestaAsistenciaEvento respuesta) =>
                $"<a href='{Html(Enlace(clave, respuesta))}' style='display:inline-block;padding:12px 20px;margin:4px 8px 4px 0;" +
                $"background:{color};color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;'>{Html(texto)}</a>";

            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre.Trim()))}:</p>");
            cuerpo.Append($"<p>Le recordamos su inscripción en <b>«{Html(evento.Titulo?.Trim())}»</b>, {Html(CuandoYDonde(evento))}.</p>");
            string ponente = Limpiar(evento.Ponente);
            if (ponente != null)
            {
                cuerpo.Append($"<p>Lo imparte {Html(ponente)}.</p>");
            }
            cuerpo.Append("<p>¿Nos confirma si podrá venir? Así le guardamos su sitio.</p>");
            cuerpo.Append("<p>")
                .Append(boton("Confirmo asistencia", "#2e7d32", RespuestaAsistenciaEvento.Confirma))
                .Append(boton("No podré ir", "#757575", RespuestaAsistenciaEvento.NoPodraIr))
                .Append("</p>");
            cuerpo.Append("<p>Muchas gracias.</p><p>Nueva Visión</p></body></html>");

            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = $"Le esperamos en «{evento.Titulo?.Trim()}» {evento.Fecha.ToString("dddd d 'de' MMMM", es)}",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            return mail;
        }

        /// <summary>El correo de las facturas (cargo 22) del contacto, como el cupón y el cheque regalo.</summary>
        internal static async Task<string> CorreoDelCliente(NVEntities db, string empresa, string cliente, string contacto)
        {
            List<string> correos = (await db.PersonasContactoClientes
                .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                    && p.CorreoElectrónico != null)
                .Select(p => new { p.Contacto, p.CorreoElectrónico }).ToListAsync().ConfigureAwait(false))
                .OrderBy(p => p.Contacto?.Trim() == contacto ? 0 : 1)
                .Select(p => p.CorreoElectrónico.Trim()).Where(c => c.Length > 0).Distinct().ToList();
            return correos.FirstOrDefault();
        }

        internal static async Task<string> NombreDelCliente(NVEntities db, string empresa, string cliente, string contacto)
        {
            var fichas = await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                .Select(c => new { c.Contacto, c.Nombre }).ToListAsync().ConfigureAwait(false);
            return (fichas.FirstOrDefault(c => c.Contacto?.Trim() == contacto) ?? fichas.FirstOrDefault())?.Nombre?.Trim();
        }

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }

    /// <summary>NestoAPI#690: las páginas del enlace del correo (de usted). La respuesta se apunta en el POST, nunca en el GET.</summary>
    public static class PaginasAsistenciaEvento
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>El GET: el evento y el botón (o los dos, si no viene respuesta). No apunta nada.</summary>
        public static string Pregunta(ResultadoAsistenciaEnlace resultado, Guid clave, string respuesta)
        {
            if (!resultado.Existe || resultado.Mensaje != null)
            {
                return Pagina(resultado.Mensaje ?? "Este enlace ya no es válido.", null);
            }
            RespuestaAsistenciaEvento? pedida = ReglasAsistenciaEvento.Respuesta(respuesta);
            // Relativa a la página (…/api/Eventos/Asistencia/{clave}): el formulario vuelve a la misma dirección, por POST
            string accion = clave.ToString("D");
            var botones = new StringBuilder();
            if (pedida != RespuestaAsistenciaEvento.NoPodraIr)
            {
                botones.Append(Boton(accion, RespuestaAsistenciaEvento.Confirma, "Confirmo asistencia", "#2e7d32"));
            }
            if (pedida != RespuestaAsistenciaEvento.Confirma)
            {
                botones.Append(Boton(accion, RespuestaAsistenciaEvento.NoPodraIr, "No podré ir", "#757575"));
            }
            string texto = $"«{resultado.Evento}», el {resultado.FechaEvento.ToString("dddd d 'de' MMMM", es)}" +
                (resultado.HoraInicio != null ? $" a las {resultado.HoraInicio}" : "") + ".";
            return Pagina(texto, botones.ToString());
        }

        /// <summary>El POST: lo que ha pasado.</summary>
        public static string Resultado(ResultadoAsistenciaEnlace resultado) =>
            Pagina(resultado.Mensaje ?? (resultado.Existe ? "Hecho." : "Este enlace ya no es válido."), null);

        private static string Boton(string accion, RespuestaAsistenciaEvento respuesta, string texto, string color) =>
            $"<form method=\"post\" action=\"{accion}\" style=\"display:inline\">" +
            $"<input type=\"hidden\" name=\"respuesta\" value=\"{respuesta}\">" +
            $"<button type=\"submit\" style=\"font-size:18px;padding:12px 20px;margin:8px 8px 0 0;border:0;border-radius:6px;" +
            $"background:{color};color:#fff\">{WebUtility.HtmlEncode(texto)}</button></form>";

        private static string Pagina(string texto, string botones) =>
            "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
            "<title>Nueva Visión</title></head><body style=\"font-family:sans-serif;padding:24px\"><p style=\"font-size:20px\">" +
            WebUtility.HtmlEncode(texto) + "</p>" + (botones ?? string.Empty) + "</body></html>";
    }
}
