using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Verifactu;
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
    /// <summary>Dependencias del job, inyectables para testearlo sin BD, sin dominio y sin SMTP.</summary>
    internal class DependenciasInscripcionesEventos
    {
        /// <summary>Pasa a Caducada las Reservada vencidas y las devuelve.</summary>
        public Func<Task<List<InscripcionEventoDTO>>> CaducarVencidas { get; set; }
        /// <summary>Da por asistidos los eventos pasados sin lista y los devuelve con sus inscripciones.</summary>
        public Func<Task<List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>>> DarPorAsistidosSinLista { get; set; }
        /// <summary>(empresa, almacén) → los correos de quien mantiene los eventos de esa tienda.</summary>
        public Func<string, string, Task<List<string>>> CorreosTienda { get; set; }
        /// <summary>(usuario que creó la inscripción, notificación) → campana de Nesto + push de NestoApp. Nunca lanza.</summary>
        public Func<string, NotificacionPushDTO, Task> Avisar { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        /// <summary>NestoAPI#612: relanza la facturación de los cursos que fallaron (y el cupón que falte). Opcional.</summary>
        public Func<Task<int>> ReintentarFacturacionCursos { get; set; }
        /// <summary>NestoAPI#613: Emitido → Activo el día del evento y Activo → Caducado a los 6 meses. Opcional.</summary>
        public Func<Task<int>> ActualizarCupones { get; set; }
    }

    /// <summary>
    /// NestoAPI#611: job diario de las inscripciones a eventos.
    /// <list type="number">
    /// <item>Caducidad: las inscripciones sin pagar con la fecha límite pasada pasan a Caducada y se avisa a quien la creó
    /// (campana de Nesto y push de NestoApp; si la creó el cliente desde la tienda, a nadie).</item>
    /// <item>Lista de asistencia: si al día siguiente del evento nadie ha pasado lista, todas las pagadas se dan por asistidas
    /// (no se bloquea a los clientes) y se manda un correo a quien mantiene la tienda del evento (grupo Tiendas con ese
    /// AlmacénPedidoVta; Carlos, 14/10/26) para que lo corrija si alguien no vino.</item>
    /// </list>
    /// </summary>
    public static class InscripcionesEventosJobsService
    {
        public const string TIPO_NOTIFICACION_CADUCADA = "InscripcionEventoCaducada";
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

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
                    await Procesar(new DependenciasInscripcionesEventos
                    {
                        CaducarVencidas = () => servicio.CaducarVencidas(),
                        DarPorAsistidosSinLista = () => servicio.DarPorAsistidosSinLista(),
                        CorreosTienda = (empresa, almacen) => LeerCorreosTienda(db, empresa, almacen),
                        Avisar = (usuario, notificacion) => AvisoCobroNestoPago.Avisar(usuario, notificacion, new ServicioNotificacionesPush()),
                        Correo = new ServicioCorreoElectronico(),
                        ReintentarFacturacionCursos = () => servicio.ReintentarFacturacionCursos(),
                        ActualizarCupones = () => new Cupones.ServicioCupones(db).ActualizarEstados()
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Inscripciones de eventos #611] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job. Devuelve cuántas caducadas y cuántos eventos dados por asistidos.</summary>
        internal static async Task<(int caducadas, int eventosSinLista)> Procesar(DependenciasInscripcionesEventos deps)
        {
            List<InscripcionEventoDTO> caducadas = await deps.CaducarVencidas().ConfigureAwait(false) ?? new List<InscripcionEventoDTO>();
            foreach (InscripcionEventoDTO inscripcion in caducadas)
            {
                try
                {
                    await deps.Avisar(inscripcion.UsuarioCreacion, NotificacionCaducada(inscripcion)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Inscripciones de eventos #611] No se pudo avisar de la caducada {inscripcion.Id}: {ex.Message}", ex));
                }
            }

            List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>> sinLista =
                await deps.DarPorAsistidosSinLista().ConfigureAwait(false) ?? new List<KeyValuePair<EventoDTO, List<InscripcionEventoDTO>>>();
            foreach (KeyValuePair<EventoDTO, List<InscripcionEventoDTO>> evento in sinLista)
            {
                List<string> destinatarios = await deps.CorreosTienda(evento.Key.Empresa ?? EMPRESA, evento.Key.Almacen).ConfigureAwait(false)
                    ?? new List<string>();
                if (!destinatarios.Any())
                {
                    destinatarios.Add(CorreoGeneralTienda(evento.Key.Almacen));
                }
                using (MailMessage correo = ConstruirCorreoSinLista(evento.Key, evento.Value, destinatarios))
                {
                    if (!deps.Correo.EnviarCorreoSMTP(correo))
                    {
                        ElmahHelper.Log(new Exception($"[Inscripciones de eventos #611] No se ha podido mandar el correo de la lista del evento {evento.Key.Id}."));
                    }
                }
            }

            // NestoAPI#613: los cupones que hoy empiezan a valer o caducan
            if (deps.ActualizarCupones != null)
            {
                try
                {
                    _ = await deps.ActualizarCupones().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cupones #613] Error al actualizar los estados de los cupones: {ex.Message}", ex));
                }
            }

            // NestoAPI#612: lo que se quedó a medias al facturar el curso, desde donde se quedó (detrás de su interruptor)
            if (deps.ReintentarFacturacionCursos != null)
            {
                try
                {
                    _ = await deps.ReintentarFacturacionCursos().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Cursos #612] Error al relanzar la facturación de los cursos: {ex.Message}", ex));
                }
            }
            return (caducadas.Count, sinLista.Count);
        }

        internal static NotificacionPushDTO NotificacionCaducada(InscripcionEventoDTO inscripcion)
        {
            string cliente = string.IsNullOrEmpty(inscripcion.Contacto) || inscripcion.Contacto == "0"
                ? inscripcion.Cliente
                : $"{inscripcion.Cliente}/{inscripcion.Contacto}";
            string nombre = string.IsNullOrWhiteSpace(inscripcion.Nombre) ? "" : $" ({inscripcion.Nombre})";
            return new NotificacionPushDTO
            {
                Titulo = "Inscripción caducada",
                Cuerpo = $"La inscripción del cliente {cliente}{nombre} a «{inscripcion.Evento}» ha caducado sin pagar. " +
                    "Si todavía quiere ir, vuelva a inscribirlo.",
                Tipo = TIPO_NOTIFICACION_CADUCADA,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION_CADUCADA,
                    ["empresa"] = inscripcion.Empresa,
                    ["cliente"] = inscripcion.Cliente,
                    ["contacto"] = inscripcion.Contacto,
                    ["eventoId"] = inscripcion.EventoId.ToString(CultureInfo.InvariantCulture),
                    ["inscripcionId"] = inscripcion.Id.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        /// <summary>Si no se encuentra a nadie de la tienda con correo, el buzón general de la tienda.</summary>
        internal static string CorreoGeneralTienda(string almacen) =>
            almacen?.Trim() == Constantes.Almacenes.ALCOBENDAS ? Constantes.Correos.TIENDA_ALCOBENDAS : Constantes.Correos.TIENDA_REINA;

        /// <summary>
        /// Quien mantiene los eventos de la tienda: los miembros del grupo Tiendas del dominio cuyo AlmacénPedidoVta es el del
        /// evento, con su CorreoDefecto de ParametrosUsuario.
        /// </summary>
        internal static async Task<List<string>> LeerCorreosTienda(NVEntities db, string empresa, string almacen)
        {
            string almacenLimpio = almacen?.Trim();
            if (string.IsNullOrEmpty(almacenLimpio))
            {
                return new List<string>();
            }
            List<string> miembros = MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.TIENDAS);
            return await CorreosDe(db, empresa, almacenLimpio, miembros).ConfigureAwait(false);
        }

        internal static async Task<List<string>> CorreosDe(NVEntities db, string empresa, string almacen, List<string> miembrosTiendas)
        {
            if (miembrosTiendas == null || !miembrosTiendas.Any())
            {
                return new List<string>();
            }
            string claveAlmacen = PermisosEventos.CLAVE_ALMACEN_USUARIO;
            string claveCorreo = Parametros.Claves.CorreoDefecto;
            List<ParametroUsuario> parametros = await db.ParametrosUsuario
                .Where(p => p.Empresa == empresa && (p.Clave == claveAlmacen || p.Clave == claveCorreo))
                .ToListAsync().ConfigureAwait(false);
            var deLaTienda = new HashSet<string>(parametros
                .Where(p => p.Clave?.Trim() == claveAlmacen && string.Equals(p.Valor?.Trim(), almacen, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Usuario?.Trim()), StringComparer.OrdinalIgnoreCase);
            var miembros = new HashSet<string>(miembrosTiendas.Select(m => PermisosEventos.SinDominio(m)), StringComparer.OrdinalIgnoreCase);
            return parametros
                .Where(p => p.Clave?.Trim() == claveCorreo && deLaTienda.Contains(p.Usuario?.Trim()) && miembros.Contains(p.Usuario?.Trim())
                    && !string.IsNullOrWhiteSpace(p.Valor))
                .Select(p => p.Valor.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static MailMessage ConstruirCorreoSinLista(EventoDTO evento, List<InscripcionEventoDTO> inscripciones, List<string> destinatarios)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Nadie ha pasado lista en «{evento.Titulo}» ({Fecha(evento.Fecha)}): los {inscripciones.Count} inscritos se dan por asistidos",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(destinatario);
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine($"<h2>{Html(evento.Titulo)} ({Fecha(evento.Fecha)}, {Html(PermisosEventos.NombreAlmacen(evento.Almacen))})</h2>");
            sb.AppendLine("<p>Nadie ha pasado lista el día del evento, así que se ha dado por asistidos a todos los inscritos que habían pagado " +
                "(para no dejar a nadie sin su cupón), salvo a quienes avisaron de que no podrían ir, que quedan como «No asistió». " +
                "Si alguno no vino, pasen lista en Nesto: se puede corregir.</p>");
            sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
            sb.AppendLine("<tr style='background:#eee'><th>Cliente</th><th>Nombre</th><th>Pagado</th><th>Forma de pago</th><th>Queda</th></tr>");
            foreach (InscripcionEventoDTO i in inscripciones.OrderBy(i => i.Nombre))
            {
                sb.Append("<tr>")
                    .Append($"<td>{Html(i.Cliente)}/{Html(i.Contacto)}</td>")
                    .Append($"<td>{Html(i.Nombre)}</td>")
                    .Append($"<td style='text-align:right'>{(i.ImportePagado.HasValue ? i.ImportePagado.Value.ToString("N2", es) + " €" : "")}</td>")
                    .Append($"<td>{Html(i.FormaPago)}</td>")
                    .Append($"<td>{Html(i.EstadoTexto)}</td>")
                    .AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
            sb.AppendLine("<p style='color:#666'>Inscripciones a eventos (NestoAPI#611).</p>");
            sb.AppendLine("</body></html>");
            mail.Body = sb.ToString();
            return mail;
        }

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        private static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", es);

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? "");
    }
}
