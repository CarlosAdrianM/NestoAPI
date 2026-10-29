using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#744: un hito público de la entrega: SOLO lo que se puede enseñar a quien tenga el enlace. A propósito no tiene
    /// DNI, firma, coordenadas, cobro ni el texto libre del repartidor: si no está en la clase, no puede salir en la página.
    /// </summary>
    public class HitoEntregaPublico
    {
        public string Tipo { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>Lo que lee la página pública (detrás de una interfaz para probarla sin BD).</summary>
    public interface ILectorSeguimientoPublicoReparto
    {
        /// <summary>El envío, sin seguimiento de EF. Null si no existe.</summary>
        Task<EnviosAgencia> LeerEnvio(int numero);
        /// <summary>Entregas e incidencias registradas por el repartidor, en orden. Vacía si no hay (o falta el script de #739).</summary>
        Task<List<HitoEntregaPublico>> LeerHitos(int envio);
    }

    public class LectorSeguimientoPublicoReparto : ILectorSeguimientoPublicoReparto
    {
        private readonly NVEntities db;

        public LectorSeguimientoPublicoReparto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<EnviosAgencia> LeerEnvio(int numero)
            => (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == numero).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<List<HitoEntregaPublico>> LeerHitos(int envio)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, RepositorioRepartoPropio.TABLA))
            {
                return new List<HitoEntregaPublico>();
            }
            // Solo estas cuatro columnas: nunca DniReceptor, Firma, Latitud/Longitud ni Texto.
            return await db.Database.SqlQuery<HitoEntregaPublico>(
                $"SELECT Tipo, FechaHora, Receptor, Motivo FROM dbo.{RepositorioRepartoPropio.TABLA} WHERE EnvioId = @p0 ORDER BY FechaHora, Id",
                envio).ToListAsync().ConfigureAwait(false);
        }
    }

    /// <summary>NestoAPI#744: la respuesta de la página: el código HTTP y el HTML.</summary>
    public class PaginaSeguimientoReparto
    {
        public HttpStatusCode Estado { get; set; }
        public string Html { get; set; }
    }

    /// <summary>
    /// NestoAPI#744: la página pública de seguimiento de los envíos del reparto propio (<c>GET /seguimiento/{token}</c>, sin
    /// usuario). El token lo firma <see cref="EnlaceSeguimientoRepartoPropio"/>; lo único que protege la página es esa firma.
    /// Enseña el pedido, los bultos, el estado y su historia (las entregas e incidencias que registra el repartidor desde Ariadna,
    /// las mismas que dejan su fila en EnviosHistoria pero con el motivo estructurado y sin el cobro ni el texto libre) y, si está
    /// entregado, cuándo y quién lo recogió. Nunca el DNI, la firma ni la ubicación.
    /// </summary>
    public class ServicioSeguimientoPublicoReparto
    {
        private readonly ILectorSeguimientoPublicoReparto lector;
        private readonly Func<string, (bool Valido, int Envio)> leerToken;

        public ServicioSeguimientoPublicoReparto(ILectorSeguimientoPublicoReparto lector, Func<string, (bool, int)> leerToken = null)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.leerToken = leerToken ?? (t => (EnlaceSeguimientoRepartoPropio.TryLeerEnvio(t, out int envio), envio));
        }

        public async Task<PaginaSeguimientoReparto> Pagina(string token)
        {
            (bool valido, int numero) = leerToken(token);
            if (!valido)
            {
                return NoEncontrada();
            }
            EnviosAgencia envio = await lector.LeerEnvio(numero).ConfigureAwait(false);
            // El mismo 404 para un token malo, un envío que no existe o uno que no es del reparto propio
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return NoEncontrada();
            }
            List<HitoEntregaPublico> hitos = await lector.LeerHitos(envio.Numero).ConfigureAwait(false) ?? new List<HitoEntregaPublico>();
            return new PaginaSeguimientoReparto { Estado = HttpStatusCode.OK, Html = PaginaHtmlSeguimientoReparto.Html(VistaSeguimientoReparto.Construir(envio, hitos)) };
        }

        public static PaginaSeguimientoReparto NoEncontrada()
            => new PaginaSeguimientoReparto { Estado = HttpStatusCode.NotFound, Html = PaginaHtmlSeguimientoReparto.HtmlNoEncontrado() };
    }

    public enum EstadoSeguimientoPublico
    {
        Pendiente,
        EnReparto,
        Incidencia,
        Entregado,
        Devuelto,
        Cerrado
    }

    public class EventoSeguimientoPublico
    {
        public DateTime Fecha { get; set; }
        public bool ConHora { get; set; }
        public string Titulo { get; set; }
        public string Detalle { get; set; }
    }

    /// <summary>Lo que se pinta, ya decidido (sin HTML): fácil de probar.</summary>
    public class VistaSeguimientoReparto
    {
        public int? Pedido { get; set; }
        public short Bultos { get; set; }
        public EstadoSeguimientoPublico Estado { get; set; }
        public string TituloEstado { get; set; }
        public string TextoEstado { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public string Receptor { get; set; }
        public string MotivoIncidencia { get; set; }
        /// <summary>Del más reciente al más antiguo.</summary>
        public List<EventoSeguimientoPublico> Eventos { get; set; } = new List<EventoSeguimientoPublico>();
        public bool MostrarContacto => Estado == EstadoSeguimientoPublico.Incidencia || Estado == EstadoSeguimientoPublico.Devuelto
            || Estado == EstadoSeguimientoPublico.Cerrado;

        /// <summary>Para el cliente, de usted: qué le pasó a su envío (no el código de Ariadna).</summary>
        internal static string MotivoLegible(string motivo)
        {
            switch (motivo?.Trim())
            {
                case IncidenciaRepartoDTO.AUSENTE: return "No había nadie en la dirección de entrega.";
                case IncidenciaRepartoDTO.DIRECCION_INCORRECTA: return "La dirección de entrega no es correcta o está incompleta.";
                case IncidenciaRepartoDTO.RECHAZADO: return "El envío no se aceptó en la entrega.";
                case IncidenciaRepartoDTO.FALTAN_BULTOS: return "Faltan bultos del envío: lo estamos revisando.";
                default: return "Ha surgido un problema con la entrega.";
            }
        }

        public static VistaSeguimientoReparto Construir(EnviosAgencia envio, IEnumerable<HitoEntregaPublico> hitos)
        {
            List<HitoEntregaPublico> lista = (hitos ?? Enumerable.Empty<HitoEntregaPublico>()).Where(h => h != null).OrderBy(h => h.FechaHora).ToList();
            HitoEntregaPublico entrega = lista.LastOrDefault(h => string.Equals(h.Tipo?.Trim(), PruebaEntregaReparto.TIPO_ENTREGA, StringComparison.OrdinalIgnoreCase));
            HitoEntregaPublico incidencia = lista.LastOrDefault(h => string.Equals(h.Tipo?.Trim(), PruebaEntregaReparto.TIPO_INCIDENCIA, StringComparison.OrdinalIgnoreCase));

            var vista = new VistaSeguimientoReparto { Pedido = envio.Pedido, Bultos = envio.Bultos };

            switch (envio.Estado)
            {
                case Constantes.Agencias.ESTADO_ENTREGADO:
                    vista.Estado = EstadoSeguimientoPublico.Entregado;
                    vista.TituloEstado = "Entregado";
                    vista.FechaEntrega = entrega?.FechaHora ?? envio.FechaEntrega;
                    vista.Receptor = Limpio(entrega?.Receptor);
                    vista.TextoEstado = "Su pedido ya está entregado. Gracias por confiar en Nueva Visión.";
                    break;
                case Constantes.Agencias.ESTADO_INCIDENTADO:
                    vista.Estado = EstadoSeguimientoPublico.Incidencia;
                    vista.TituloEstado = "Incidencia en la entrega";
                    vista.MotivoIncidencia = MotivoLegible(incidencia?.Motivo);
                    vista.TextoEstado = vista.MotivoIncidencia + " Volveremos a intentarlo o nos pondremos en contacto con usted.";
                    break;
                case Constantes.Agencias.ESTADO_TRAMITADO:
                    vista.Estado = EstadoSeguimientoPublico.EnReparto;
                    vista.TituloEstado = "En reparto";
                    vista.TextoEstado = "Su pedido está en nuestro reparto y se lo llevaremos en breve.";
                    break;
                case Constantes.Agencias.ESTADO_DEVUELTO:
                    vista.Estado = EstadoSeguimientoPublico.Devuelto;
                    vista.TituloEstado = "Devuelto a nuestro almacén";
                    vista.TextoEstado = "No hemos podido entregar el pedido y ha vuelto a nuestro almacén. Póngase en contacto con nosotros, por favor.";
                    break;
                case Constantes.Agencias.ESTADO_CERRADO_A_MANO:
                    vista.Estado = EstadoSeguimientoPublico.Cerrado;
                    vista.TituloEstado = "Envío cerrado";
                    vista.TextoEstado = "Este envío está cerrado. Si tiene cualquier duda, póngase en contacto con nosotros.";
                    break;
                default:
                    vista.Estado = EstadoSeguimientoPublico.Pendiente;
                    vista.TituloEstado = "Preparando su pedido";
                    vista.TextoEstado = "Estamos preparando su pedido. En cuanto salga a reparto lo verá aquí.";
                    break;
            }

            // La historia: la salida a reparto y lo que ha registrado el repartidor.
            if (envio.Estado >= Constantes.Agencias.ESTADO_TRAMITADO)
            {
                vista.Eventos.Add(new EventoSeguimientoPublico { Fecha = envio.Fecha, ConHora = false, Titulo = "Preparado para el reparto" });
            }
            foreach (HitoEntregaPublico hito in lista)
            {
                if (string.Equals(hito.Tipo?.Trim(), PruebaEntregaReparto.TIPO_ENTREGA, StringComparison.OrdinalIgnoreCase))
                {
                    string receptor = Limpio(hito.Receptor);
                    vista.Eventos.Add(new EventoSeguimientoPublico
                    {
                        Fecha = hito.FechaHora,
                        ConHora = true,
                        Titulo = "Entregado",
                        Detalle = receptor == null ? null : "Lo recogió " + receptor + "."
                    });
                }
                else if (string.Equals(hito.Tipo?.Trim(), PruebaEntregaReparto.TIPO_INCIDENCIA, StringComparison.OrdinalIgnoreCase))
                {
                    vista.Eventos.Add(new EventoSeguimientoPublico { Fecha = hito.FechaHora, ConHora = true, Titulo = "Incidencia", Detalle = MotivoLegible(hito.Motivo) });
                }
            }
            // Entregado sin prueba del repartidor (p. ej. cerrado desde Nesto): la fecha del envío.
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && entrega == null && envio.FechaEntrega.HasValue)
            {
                vista.Eventos.Add(new EventoSeguimientoPublico { Fecha = envio.FechaEntrega.Value, ConHora = envio.FechaEntrega.Value.TimeOfDay != TimeSpan.Zero, Titulo = "Entregado" });
            }
            vista.Eventos = vista.Eventos.OrderByDescending(e => e.Fecha).ToList();
            return vista;
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>NestoAPI#744: el HTML de la página (marca Nueva Visión, móvil primero, claro/oscuro, de usted).</summary>
    public static class PaginaHtmlSeguimientoReparto
    {
        public const string TELEFONO = "916 28 19 14";
        public const string TELEFONO_ENLACE = "+34916281914";
        public const string WHATSAPP = "https://wa.me/34916281914";
        public const string CORREO = Constantes.Correos.LOGISTICA;
        internal const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        public static string Html(VistaSeguimientoReparto vista)
        {
            var cuerpo = new StringBuilder();
            _ = cuerpo.Append("<p class=\"etiqueta\">Seguimiento de su envío</p>");
            _ = cuerpo.Append("<div class=\"estado estado-").Append(Clase(vista.Estado)).Append("\">")
                .Append("<h1>").Append(H(vista.TituloEstado)).Append("</h1>")
                .Append("<p>").Append(H(vista.TextoEstado)).Append("</p>");
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && vista.FechaEntrega.HasValue)
            {
                _ = cuerpo.Append("<p class=\"dato\"><strong>").Append(H(FechaLarga(vista.FechaEntrega.Value, vista.FechaEntrega.Value.TimeOfDay != TimeSpan.Zero))).Append("</strong></p>");
            }
            if (vista.Estado == EstadoSeguimientoPublico.Entregado && vista.Receptor != null)
            {
                _ = cuerpo.Append("<p class=\"dato\">Lo recogió <strong>").Append(H(vista.Receptor)).Append("</strong></p>");
            }
            _ = cuerpo.Append("</div>");

            _ = cuerpo.Append("<dl class=\"resumen\">");
            if (vista.Pedido.HasValue)
            {
                _ = cuerpo.Append("<div><dt>Pedido</dt><dd>").Append(vista.Pedido.Value.ToString(CultureInfo.InvariantCulture)).Append("</dd></div>");
            }
            _ = cuerpo.Append("<div><dt>Bultos</dt><dd>").Append(vista.Bultos.ToString(CultureInfo.InvariantCulture)).Append("</dd></div>");
            _ = cuerpo.Append("</dl>");

            if (vista.Eventos.Count > 0)
            {
                _ = cuerpo.Append("<h2>Historia del envío</h2><ol class=\"historia\">");
                foreach (EventoSeguimientoPublico evento in vista.Eventos)
                {
                    _ = cuerpo.Append("<li><span class=\"cuando\">").Append(H(FechaLarga(evento.Fecha, evento.ConHora))).Append("</span>")
                        .Append("<span class=\"que\">").Append(H(evento.Titulo)).Append("</span>");
                    if (!string.IsNullOrEmpty(evento.Detalle))
                    {
                        _ = cuerpo.Append("<span class=\"detalle\">").Append(H(evento.Detalle)).Append("</span>");
                    }
                    _ = cuerpo.Append("</li>");
                }
                _ = cuerpo.Append("</ol>");
            }

            _ = cuerpo.Append(Contacto(vista.MostrarContacto));
            return Documento("Seguimiento de su envío", cuerpo.ToString());
        }

        public static string HtmlNoEncontrado()
            => Documento("Envío no encontrado",
                "<div class=\"estado estado-pendiente\"><h1>No encontramos este envío</h1>" +
                "<p>El enlace no es correcto o ya no está disponible. Compruebe que lo ha copiado entero o póngase en contacto con nosotros " +
                "y le diremos cómo va su pedido.</p></div>" + Contacto(true));

        private static string Contacto(bool destacado)
            => "<div class=\"contacto" + (destacado ? " destacado" : string.Empty) + "\">" +
               "<p>" + (destacado ? "¿Necesita ayuda? Estamos a su disposición:" : "¿Alguna duda? Estamos a su disposición:") + "</p>" +
               "<p><a href=\"tel:" + TELEFONO_ENLACE + "\">" + TELEFONO + "</a> · <a href=\"" + WHATSAPP + "\">WhatsApp</a> · " +
               "<a href=\"mailto:" + CORREO + "\">" + CORREO + "</a></p></div>";

        private static string Documento(string titulo, string cuerpo)
            => "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
               "<meta name=\"robots\" content=\"noindex, nofollow\">" +
               "<meta name=\"referrer\" content=\"no-referrer\">" +
               "<meta name=\"color-scheme\" content=\"light dark\">" +
               "<title>" + H(titulo) + " · Nueva Visión</title><style>" + Estilos + "</style></head><body><main>" +
               "<header><span class=\"logo\"><img src=\"" + URL_LOGO + "\" alt=\"Nueva Visión\"></span></header>" +
               cuerpo +
               "<footer>Nueva Visión</footer></main></body></html>";

        private const string Estilos =
            ":root{--fondo:#f5f2f4;--tarjeta:#fff;--texto:#2b2630;--suave:#6f6578;--marca:#7b6e8e;--borde:#e4dde8;" +
            "--ok:#1e7d4f;--okf:#e5f4ec;--aviso:#a14b00;--avisof:#fdf0e3;--info:#5b4f7a;--infof:#efebf6;}" +
            "@media (prefers-color-scheme:dark){:root{--fondo:#16141a;--tarjeta:#221f28;--texto:#ece8f0;--suave:#aba3b4;--marca:#b9acd0;" +
            "--borde:#38323f;--ok:#7fd3a6;--okf:#183226;--aviso:#f3b27a;--avisof:#3a2717;--info:#c9bde3;--infof:#2c2738;}}" +
            "*{box-sizing:border-box}body{margin:0;background:var(--fondo);color:var(--texto);" +
            "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;font-size:16px;line-height:1.5}" +
            "main{max-width:520px;margin:0 auto;padding:16px}" +
            "header{text-align:center;padding:12px 0 16px}.logo{display:inline-block;background:#fff;border-radius:12px;padding:8px 16px}" +
            ".logo img{max-height:56px;max-width:100%;display:block}" +
            ".etiqueta{color:var(--suave);font-size:14px;margin:0 0 8px;text-transform:uppercase;letter-spacing:.05em}" +
            ".estado{border-radius:14px;padding:16px;margin-bottom:16px;background:var(--infof);border:1px solid var(--borde)}" +
            ".estado h1{font-size:22px;margin:0 0 6px;color:var(--info)}.estado p{margin:4px 0}" +
            ".estado-entregado{background:var(--okf)}.estado-entregado h1{color:var(--ok)}" +
            ".estado-incidencia,.estado-devuelto{background:var(--avisof)}.estado-incidencia h1,.estado-devuelto h1{color:var(--aviso)}" +
            ".resumen{display:flex;gap:12px;margin:0 0 16px}.resumen div{flex:1;background:var(--tarjeta);border:1px solid var(--borde);" +
            "border-radius:12px;padding:12px}.resumen dt{color:var(--suave);font-size:13px}.resumen dd{margin:0;font-size:20px;font-weight:600}" +
            "h2{font-size:16px;color:var(--marca);margin:20px 0 8px}" +
            ".historia{list-style:none;margin:0;padding:0;background:var(--tarjeta);border:1px solid var(--borde);border-radius:12px}" +
            ".historia li{padding:12px 16px;border-top:1px solid var(--borde);display:flex;flex-direction:column}" +
            ".historia li:first-child{border-top:0}.cuando{color:var(--suave);font-size:13px}.que{font-weight:600}.detalle{font-size:14px}" +
            ".contacto{margin-top:20px;padding:12px 16px;border-radius:12px;font-size:14px;color:var(--suave)}" +
            ".contacto.destacado{background:var(--tarjeta);border:1px solid var(--borde);color:var(--texto)}.contacto p{margin:4px 0}" +
            "a{color:var(--marca);font-weight:600;word-break:break-word}" +
            "footer{text-align:center;color:var(--suave);font-size:12px;padding:24px 0 8px}";

        private static string Clase(EstadoSeguimientoPublico estado)
        {
            switch (estado)
            {
                case EstadoSeguimientoPublico.Entregado: return "entregado";
                case EstadoSeguimientoPublico.Incidencia: return "incidencia";
                case EstadoSeguimientoPublico.Devuelto: return "devuelto";
                case EstadoSeguimientoPublico.EnReparto: return "reparto";
                case EstadoSeguimientoPublico.Cerrado: return "cerrado";
                default: return "pendiente";
            }
        }

        /// <summary>«jueves, 29 de octubre de 2026, 12:45» (sin hora si no la tiene).</summary>
        internal static string FechaLarga(DateTime fecha, bool conHora)
            => conHora
                ? fecha.ToString("dddd, d 'de' MMMM 'de' yyyy, HH:mm", Espanol)
                : fecha.ToString("dddd, d 'de' MMMM 'de' yyyy", Espanol);

        // Solo los caracteres que rompen el HTML: WebUtility.HtmlEncode de .NET Framework pasa las tildes a &#NNN; y la página
        // es UTF-8 (así el texto sigue legible en el código fuente y en los tests).
        private static string H(string texto)
            => (texto ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    }
}
