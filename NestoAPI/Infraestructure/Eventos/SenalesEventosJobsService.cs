using Hangfire;
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
    /// <summary>Dependencias del job, inyectables para testearlo sin BD ni SMTP.</summary>
    internal class DependenciasSenalesEventos
    {
        /// <summary>Todas las señales de la empresa con su estado calculado.</summary>
        public Func<Task<List<SenalEventoDTO>>> LeerSenales { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        public DateTime Ahora { get; set; } = DateTime.Now;
        /// <summary>NestoAPI#615: el barrido de cobros a cuenta recientes (null con el interruptor apagado).</summary>
        public Func<Task> BuscarPropuestas { get; set; }
        /// <summary>NestoAPI#615: las propuestas sin confirmar (null con el interruptor apagado).</summary>
        public Func<Task<List<PropuestaSenalDTO>>> LeerPropuestas { get; set; }
    }

    /// <summary>
    /// NestoAPI#591: correo a administración el día del evento (patrón de #534). Corre de lunes a viernes por la mañana; el
    /// lunes recoge también lo del sábado y el domingo. Manda «Señales liberadas hoy» (eventos de hoy —o del fin de semana—
    /// con la señal aún a favor) y el apartado «Sin compra» (todas las que llevan <see cref="CalculadoraEstadoSenalEvento.DIAS_SIN_COMPRA"/>
    /// días o más liberadas sin consumir, marcando las que han llegado hoy). Para no repetir el mismo correo cada mañana,
    /// solo sale si hoy hay algo nuevo: una señal que se libera o una que pasa a «Sin compra». Si no, no manda nada.
    /// <para>NestoAPI#615: con Eventos:ProponerSenales encendido, antes barre los cobros a cuenta de los últimos
    /// <see cref="ServicioPropuestasSenales.DIAS_BARRIDO"/> días y el correo empieza por «Señales propuestas sin confirmar»;
    /// mientras haya alguna, sale cada mañana.</para>
    /// </summary>
    public static class SenalesEventosJobsService
    {
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
                    var servicio = new ServicioEventos(db);
                    DateTime ahora = DateTime.Now;
                    var deps = new DependenciasSenalesEventos
                    {
                        LeerSenales = () => servicio.LeerSenales(EMPRESA, null, null, null),
                        Correo = new ServicioCorreoElectronico(),
                        Ahora = ahora
                    };
                    if (InterruptorPropuestasSenales.Encendido)
                    {
                        var propuestas = new ServicioPropuestasSenales(db);
                        deps.BuscarPropuestas = () => propuestas.ProponerRecientes(EMPRESA, ahora.Date.AddDays(-ServicioPropuestasSenales.DIAS_BARRIDO));
                        deps.LeerPropuestas = () => propuestas.LeerPendientes(EMPRESA);
                    }
                    await Procesar(deps).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Señales de eventos #591] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job. Devuelve true si ha mandado el correo.</summary>
        internal static async Task<bool> Procesar(DependenciasSenalesEventos deps)
        {
            DateTime hoy = deps.Ahora.Date;
            DateTime desde = DesdeCuando(hoy);
            List<SenalEventoDTO> senales = await deps.LeerSenales().ConfigureAwait(false) ?? new List<SenalEventoDTO>();

            List<SenalEventoDTO> liberadasHoy = senales
                .Where(s => s.Estado != EstadoSenalEvento.Consumida && s.FechaEvento.Date >= desde && s.FechaEvento.Date <= hoy)
                .OrderBy(s => s.FechaEvento).ThenBy(s => s.Evento).ThenBy(s => s.Nombre)
                .ToList();
            List<SenalEventoDTO> sinCompra = senales
                .Where(s => s.Estado == EstadoSenalEvento.SinCompra)
                .OrderBy(s => s.FechaEvento).ThenBy(s => s.Evento).ThenBy(s => s.Nombre)
                .ToList();
            bool haySinCompraNueva = sinCompra.Any(s => EsNuevaSinCompra(s, desde, hoy));
            List<PropuestaSenalDTO> propuestas = await Propuestas(deps).ConfigureAwait(false);

            // NestoAPI#615: las propuestas sin confirmar salen cada mañana mientras las haya (son trabajo pendiente de Administración).
            if (!liberadasHoy.Any() && !haySinCompraNueva && !propuestas.Any())
            {
                return false;
            }

            using (MailMessage correo = ConstruirCorreo(liberadasHoy, sinCompra, desde, hoy, propuestas))
            {
                if (!deps.Correo.EnviarCorreoSMTP(correo))
                {
                    ElmahHelper.Log(new Exception("[Señales de eventos #591] No se ha podido mandar el correo a administración."));
                    return false;
                }
            }
            return true;
        }

        /// <summary>NestoAPI#615: primero el barrido (si falla, el correo sale igual con lo que ya hubiera) y luego las pendientes.</summary>
        private static async Task<List<PropuestaSenalDTO>> Propuestas(DependenciasSenalesEventos deps)
        {
            if (deps.BuscarPropuestas != null)
            {
                try
                {
                    await deps.BuscarPropuestas().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Señales #615] Ha fallado el barrido de señales propuestas: " + ex.Message, ex));
                }
            }
            if (deps.LeerPropuestas == null)
            {
                return new List<PropuestaSenalDTO>();
            }
            try
            {
                return await deps.LeerPropuestas().ConfigureAwait(false) ?? new List<PropuestaSenalDTO>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Señales #615] No se han podido leer las señales propuestas: " + ex.Message, ex));
                return new List<PropuestaSenalDTO>();
            }
        }

        /// <summary>El lunes mira también el sábado y el domingo (el job no corre en fin de semana).</summary>
        internal static DateTime DesdeCuando(DateTime hoy) => hoy.DayOfWeek == DayOfWeek.Monday ? hoy.AddDays(-2) : hoy;

        private static bool EsNuevaSinCompra(SenalEventoDTO s, DateTime desde, DateTime hoy) =>
            s.FechaSinCompra.Date >= desde && s.FechaSinCompra.Date <= hoy;

        internal static MailMessage ConstruirCorreo(List<SenalEventoDTO> liberadasHoy, List<SenalEventoDTO> sinCompra, DateTime desde, DateTime hoy,
            List<PropuestaSenalDTO> propuestas = null)
        {
            propuestas = propuestas ?? new List<PropuestaSenalDTO>();
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Señales de eventos {Fecha(hoy)}: {liberadasHoy.Count} liberadas hoy, {sinCompra.Count} sin compra" +
                    (propuestas.Any() ? $", {propuestas.Count} por confirmar" : string.Empty),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);
            mail.Body = GenerarHtml(liberadasHoy, sinCompra, desde, hoy, propuestas);
            return mail;
        }

        internal static string GenerarHtml(List<SenalEventoDTO> liberadasHoy, List<SenalEventoDTO> sinCompra, DateTime desde, DateTime hoy,
            List<PropuestaSenalDTO> propuestas = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");

            // NestoAPI#615: lo primero, porque es lo único que pide hacer algo hoy.
            if (propuestas != null && propuestas.Any())
            {
                sb.AppendLine("<h2>Señales propuestas sin confirmar</h2>");
                sb.AppendLine("<p>Estos cobros a cuenta parecen la señal de un evento. Compruébelos en Nesto (Clientes → Señales de eventos) " +
                    "y confirme el evento que corresponda: es lo mismo que marcar la señal a mano. Si no es una señal, descártela.</p>");
                TablaPropuestas(sb, propuestas);
            }

            sb.AppendLine(desde < hoy
                ? $"<h2>Señales liberadas desde el {Fecha(desde)}</h2>"
                : "<h2>Señales liberadas hoy</h2>");
            if (liberadasHoy.Any())
            {
                sb.AppendLine("<p>El evento ya ha llegado: estas señales se pueden liquidar contra la primera compra del cliente. " +
                    "Lo que sobre sigue a su favor para la siguiente.</p>");
                Tabla(sb, liberadasHoy, false, desde, hoy);
            }
            else
            {
                sb.AppendLine("<p>Ninguna.</p>");
            }

            sb.AppendLine("<h2>Sin compra</h2>");
            if (sinCompra.Any())
            {
                sb.AppendLine($"<p>Llevan {CalculadoraEstadoSenalEvento.DIAS_SIN_COMPRA} días o más liberadas y el cliente no ha comprado: " +
                    "decidid caso a caso si se factura como curso o se deja para otra compra. Las marcadas como <b>nueva</b> han llegado hoy.</p>");
                Tabla(sb, sinCompra, true, desde, hoy);
            }
            else
            {
                sb.AppendLine("<p>Ninguna.</p>");
            }

            sb.AppendLine("<p style='color:#666'>La lista completa está en Nesto, en Clientes → Señales de eventos (NestoAPI#591).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void Tabla(StringBuilder sb, List<SenalEventoDTO> senales, bool marcarNuevas, DateTime desde, DateTime hoy)
        {
            sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
            sb.AppendLine("<tr style='background:#eee'><th>Evento</th><th>Fecha</th><th>Cliente</th><th>Nombre</th>" +
                "<th>Señal</th><th>Pendiente</th><th>Apunte</th>" + (marcarNuevas ? "<th></th>" : "") + "</tr>");
            foreach (SenalEventoDTO s in senales)
            {
                sb.Append("<tr>")
                    .Append($"<td>{Html(s.Evento)}</td>")
                    .Append($"<td>{Fecha(s.FechaEvento)}</td>")
                    .Append($"<td>{Html(s.Cliente)}/{Html(s.Contacto)}</td>")
                    .Append($"<td>{Html(s.Nombre)}</td>")
                    .Append($"<td style='text-align:right'>{Importe(s.Importe)}</td>")
                    .Append($"<td style='text-align:right'>{Importe(s.ImportePendiente)}</td>")
                    .Append($"<td>{s.NumOrdenExtracto} {Html(s.Concepto)}</td>");
                if (marcarNuevas)
                {
                    sb.Append($"<td>{(EsNuevaSinCompra(s, desde, hoy) ? "<b>nueva</b>" : "")}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }

        private static void TablaPropuestas(StringBuilder sb, List<PropuestaSenalDTO> propuestas)
        {
            sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
            sb.AppendLine("<tr style='background:#eee'><th>Cobro</th><th>Cliente</th><th>Nombre</th><th>Importe</th><th>Concepto</th><th>Evento propuesto</th></tr>");
            foreach (PropuestaSenalDTO p in propuestas)
            {
                string candidatos = string.Join("<br/>", p.Candidatos.Select(c =>
                    $"{Html(c.Evento)} ({Fecha(c.FechaEvento)}): {Html(c.Motivo)}"));
                string concepto = string.IsNullOrWhiteSpace(p.ConceptoBanco) ? p.Concepto : p.ConceptoBanco;
                sb.Append("<tr>")
                    .Append($"<td>{Fecha(p.FechaApunte)} · {p.NumOrdenExtracto}</td>")
                    .Append($"<td>{Html(p.Cliente)}/{Html(p.Contacto)}</td>")
                    .Append($"<td>{Html(p.Nombre)}</td>")
                    .Append($"<td style='text-align:right'>{Importe(p.Importe)}</td>")
                    .Append($"<td>{Html(concepto)}</td>")
                    .Append($"<td>{(p.Candidatos.Count > 1 ? "<b>Varios, elija uno:</b><br/>" : "")}{candidatos}</td>")
                    .AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        private static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", es);

        private static string Importe(decimal importe) => importe.ToString("N2", es) + " €";

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? "");
    }
}
