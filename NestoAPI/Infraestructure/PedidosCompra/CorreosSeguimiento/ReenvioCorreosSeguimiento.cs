using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#865 (corte 3): qué ha hecho el job con un correo recién guardado.</summary>
    public enum DesenlaceCorreoSeguimiento
    {
        /// <summary>Guardado sin pasar por la fase 3 (falta Scripts/Issue857d o no se ha podido).</summary>
        SinAplicar,
        /// <summary>Se ha puesto su fecha en el pedido.</summary>
        FechaCambiada,
        /// <summary>Era de un pedido cuyas líneas ya tenían esa fecha.</summary>
        MismaFecha,
        /// <summary>Con 0 o varios pedidos posibles: Compras recibe el aviso en la campana.</summary>
        PorAsignar,
        /// <summary>Un envío nuestro de salida en copia: no se toca nada.</summary>
        SalidaNuestra,
        /// <summary>Su envío ya lo había descartado Compras.</summary>
        Descartado,
        /// <summary>El pedido de su envío ya se ha recibido.</summary>
        YaRecibido,
        /// <summary>Es de un pedido, pero el interruptor Compras.CorreosSeguimientoAplicar está apagado.</summary>
        AplicarApagado,
        /// <summary>De hace más de 3 días: no se aplica.</summary>
        Antiguo,
        /// <summary>El pedido ya no tiene líneas enviadas y sin recibir.</summary>
        NoEnElAire,
        /// <summary>No se ha podido aplicar (queda apuntado).</summary>
        Error,
        /// <summary>NestoAPI#857f: la agencia dice que ya está entregado: no se pone fecha (la recepción la marca el albarán).</summary>
        Entregado
    }

    /// <summary>NestoAPI#865 (corte 3): el desenlace de un correo recién guardado, para la cabecera del reenvío.</summary>
    public class ResultadoCorreoGuardado
    {
        public DesenlaceCorreoSeguimiento Desenlace { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        /// <summary>Las fechas que tenían las líneas cambiadas (null = sin fecha).</summary>
        public List<DateTime?> FechasAntes { get; set; } = new List<DateTime?>();
        public DateTime? FechaNueva { get; set; }
        public int LineasCambiadas { get; set; }
        /// <summary>Por asignar: cuántos pedidos posibles.</summary>
        public int Candidatos { get; set; }
        public string Detalle { get; set; }
        /// <summary>20/11/26: las líneas del aviso a las que se ha puesto la fecha (null = a todo el pedido).</summary>
        public List<LineaCasadaAviso> Lineas { get; set; }
    }

    /// <summary>
    /// NestoAPI#865 (corte 3): los textos del reenvío temporal a Compras (de usted, sin nombres de tablas). PUROS. El asunto lleva una marca
    /// propia para que el job no vuelva a captar el reenvío si llegara al buzón que lee (<see cref="EsReenvioNuestro"/>).
    /// </summary>
    public static class TextosReenvioCorreoSeguimiento
    {
        public const string MARCA_ASUNTO = "[Nesto: correo de seguimiento]";
        private static readonly CultureInfo es = new CultureInfo("es-ES");
        private static readonly Regex CUERPO_HTML = new Regex(@"<body[^>]*>(?<c>[\s\S]*)</body>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool EsReenvioNuestro(string asunto) =>
            !string.IsNullOrEmpty(asunto) && asunto.IndexOf(MARCA_ASUNTO, StringComparison.OrdinalIgnoreCase) >= 0;

        public static string Asunto(string asuntoOriginal)
        {
            string asunto = $"{MARCA_ASUNTO} {asuntoOriginal?.Trim()}".Trim();
            return asunto.Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>Lo que se ha hecho, en una frase.</summary>
        public static string QueSeHaHecho(ResultadoCorreoGuardado r)
        {
            string pedido = r?.Pedido.HasValue == true ? $"pedido de compra {r.Pedido}" : "pedido de compra";
            switch (r?.Desenlace ?? DesenlaceCorreoSeguimiento.SinAplicar)
            {
                case DesenlaceCorreoSeguimiento.FechaCambiada:
                    string antes = r.FechasAntes == null || !r.FechasAntes.Any()
                        ? "sin fecha"
                        : string.Join(" y ", r.FechasAntes.Select(f => f.HasValue ? Dia(f.Value) : "sin fecha"));
                    if (r.Lineas != null && r.Lineas.Any())
                    {
                        // 20/11/26: solo las líneas del aviso del proveedor
                        bool sinNinguna = r.FechasAntes == null || !r.FechasAntes.Any(f => f.HasValue);
                        string cambio = sinNinguna
                            ? $"{DiaCorto(r.FechaNueva)} (antes sin fecha)"
                            : $"del {string.Join(" y ", r.FechasAntes.Select(f => f.HasValue ? DiaCorto(f.Value) : "sin fecha"))} al {DiaCorto(r.FechaNueva)}";
                        string parcial = TextosLineasAviso.Parcial(r.Lineas);
                        return $"Fecha de recepción {cambio} en {r.LineasCambiadas} {(r.LineasCambiadas == 1 ? "línea" : "líneas")} del {pedido}: "
                            + $"{TextosLineasAviso.Lista(r.Lineas)}." + (parcial == null ? string.Empty : " " + parcial);
                    }
                    return $"Fecha del {pedido}: {antes} → {Dia(r.FechaNueva)} ({r.LineasCambiadas} {(r.LineasCambiadas == 1 ? "línea" : "líneas")}).";
                case DesenlaceCorreoSeguimiento.MismaFecha:
                    return $"Es del {pedido}, que ya tenía la fecha {Dia(r.FechaNueva)}: misma fecha, nada que cambiar.";
                case DesenlaceCorreoSeguimiento.PorAsignar:
                    return r.Candidatos == 0
                        ? "Por asignar: no se ha reconocido de qué pedido de compra es. Le llegará el aviso en la campana de Nesto."
                        : $"Por asignar: puede ser de {r.Candidatos} pedidos de compra distintos. Le llegará el aviso en la campana de Nesto.";
                case DesenlaceCorreoSeguimiento.SalidaNuestra:
                    return "Es un envío nuestro (sale de Nueva Visión), no de una compra: no se toca.";
                case DesenlaceCorreoSeguimiento.Descartado:
                    return "Su envío ya estaba descartado como «no es de ninguna compra»: no se toca.";
                case DesenlaceCorreoSeguimiento.YaRecibido:
                    return "El pedido de este envío ya se ha recibido: no se toca ninguna fecha.";
                case DesenlaceCorreoSeguimiento.AplicarApagado:
                    return $"Es del {pedido}, pero poner la fecha de los correos está apagado: no se ha cambiado.";
                case DesenlaceCorreoSeguimiento.Antiguo:
                    return "Es de hace más de 3 días: no se aplica (histórico).";
                case DesenlaceCorreoSeguimiento.NoEnElAire:
                    return $"Es del {pedido}, que ya no tiene nada enviado y sin recibir: no se ha cambiado.";
                case DesenlaceCorreoSeguimiento.Error:
                    return "No se ha podido aplicar por un error (queda apuntado).";
                case DesenlaceCorreoSeguimiento.Entregado:
                    return "La agencia dice que ya está entregado: no se pone ninguna fecha (la recepción la marca el albarán).";
                default:
                    return "Guardado; no se ha aplicado a ningún pedido.";
            }
        }

        /// <summary>
        /// NestoAPI#857e: de dónde ha salido el pedido de un aviso de salida del proveedor (remitente marcado sin agencia o con «leer el
        /// PDF»), en una frase; null si no es un aviso ni se ha leído ningún PDF.
        /// </summary>
        public static string OrigenAviso(CorreoSeguimientoFila fila)
        {
            if (fila == null || (fila.AvisoProveedor != true && fila.AdjuntoLeido == null))
            {
                return null;
            }
            string que = fila.AvisoProveedor == true ? "Aviso de salida del proveedor" : "Correo con PDF adjunto";
            if (fila.PedidoEnAdjunto.HasValue)
            {
                return $"{que}: pedido {fila.PedidoEnAdjunto} leído del PDF adjunto.";
            }
            switch (fila.AdjuntoLeido)
            {
                case nameof(EstadoPdfAdjunto.Leido):
                    return $"{que}: el PDF adjunto no dice ningún pedido nuestro en el aire (o dice varios).";
                case nameof(EstadoPdfAdjunto.SinTexto):
                    return $"{que}: el PDF adjunto no tiene texto (¿escaneado?); se sigue sin él.";
                case nameof(EstadoPdfAdjunto.Ilegible):
                    return $"{que}: el PDF adjunto no se ha podido leer; se sigue sin él.";
                case nameof(EstadoPdfAdjunto.Grande):
                    return $"{que}: el PDF adjunto es demasiado grande para leerlo; se sigue sin él.";
                case nameof(EstadoPdfAdjunto.NoDescargado):
                    return $"{que}: no se ha podido descargar el PDF adjunto; se sigue sin él.";
                case nameof(EstadoPdfAdjunto.SinPdf):
                    return $"{que}: no trae ningún PDF adjunto.";
                default:
                    return $"{que}.";
            }
        }

        /// <summary>La cabecera (HTML) con lo que se ha hecho y los datos del correo original.</summary>
        public static string Cabecera(CorreoSeguimientoFila fila, ResultadoCorreoGuardado r, string remitente, DateTime recibido)
        {
            var sb = new StringBuilder();
            sb.Append("<div style='font-family: Arial, sans-serif; font-size: 13px; border: 2px solid #1f6fb2; padding: 8px; margin-bottom: 12px; background: #eef5fb'>");
            string origen = OrigenAviso(fila);
            sb.Append($"<p style='margin:0 0 6px 0'><b>{Html(origen == null ? QueSeHaHecho(r) : origen + " " + QueSeHaHecho(r))}</b></p>");
            var datos = new List<string>();
            if (!string.IsNullOrWhiteSpace(fila?.Agencia))
            {
                datos.Add("Agencia: " + fila.Agencia.Trim());
            }
            if (!string.IsNullOrWhiteSpace(fila?.Codigo))
            {
                datos.Add("Seguimiento: " + fila.Codigo.Trim());
            }
            if (fila?.EntregaEstimadaDia != null)
            {
                datos.Add("Entrega que anuncia" + DeQuien(fila.OrigenEntrega) + ": " + Dia(fila.EntregaEstimadaDia));
            }
            string estado = TextoEstadoEnvio(fila?.EstadoEnvio);
            if (estado != null)
            {
                datos.Add("Estado según la agencia: " + estado);
            }
            if (!string.IsNullOrWhiteSpace(fila?.RemitentePaquete))
            {
                datos.Add("Lo envía: " + fila.RemitentePaquete.Trim());
            }
            // NestoAPI#897: los bultos y kilos que dice la agencia (solo se enseñan) y por qué se ha elegido el pedido con control de pendientes
            string bultos = BultosKilosCorreo.Texto(BultosKilosCorreo.De(fila?.Agencia, fila?.Extracto));
            if (bultos != null)
            {
                datos.Add("Bultos: " + bultos);
            }
            string porQue = ReglaControlPendientes.ExplicacionDe(fila);
            if (porQue != null)
            {
                datos.Add(porQue);
            }
            if (!string.IsNullOrWhiteSpace(r?.Detalle))
            {
                datos.Add(r.Detalle.Trim());
            }
            if (datos.Any())
            {
                sb.Append($"<p style='margin:0 0 6px 0'>{Html(string.Join(" · ", datos))}</p>");
            }
            // NestoAPI#857f: el enlace de seguimiento (el del correo o el que compone su agencia a partir del código)
            string enlace = !string.IsNullOrWhiteSpace(fila?.Url) ? fila.Url.Trim() : EnlaceSeguimientoAgencia.De(fila?.Agencia, fila?.Codigo);
            if (enlace != null && Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                sb.Append($"<p style='margin:0 0 6px 0'>Seguimiento en la web de la agencia: <a href='{Html(enlace)}'>{Html(enlace)}</a></p>");
            }
            sb.Append($"<p style='margin:0; color:#555'>Correo original de {Html(remitente)}, recibido el {recibido.ToString("dd/MM/yyyy HH:mm", es)}. " +
                "Este reenvío es temporal, para revisar lo que hace Nesto con los correos de seguimiento; no hace falta contestarlo.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>El cuerpo original: el de dentro de &lt;body&gt; si es HTML; si es texto, escapado.</summary>
        public static string CuerpoOriginal(string cuerpo, bool esHtml)
        {
            if (string.IsNullOrEmpty(cuerpo))
            {
                return "<p><i>(sin cuerpo)</i></p>";
            }
            if (!esHtml)
            {
                return $"<pre style='white-space: pre-wrap; font-family: inherit'>{Html(cuerpo)}</pre>";
            }
            Match m = CUERPO_HTML.Match(cuerpo);
            return m.Success ? m.Groups["c"].Value : cuerpo;
        }

        /// <summary>NestoAPI#857f: « (la agencia)» o « (el proveedor)» según de dónde sale la entrega; vacío si no se sabe.</summary>
        internal static string DeQuien(string origen)
        {
            if (string.IsNullOrWhiteSpace(origen))
            {
                return string.Empty;
            }
            if (string.Equals(origen, OrigenEntregaCorreo.CONSULTA, StringComparison.OrdinalIgnoreCase))
            {
                return " (consultada a la agencia)";
            }
            return OrigenEntregaCorreo.EsDeLaAgencia(origen) ? " (la agencia)" : " (el proveedor)";
        }

        /// <summary>NestoAPI#857f: el estado del envío en palabras; null si no hay.</summary>
        public static string TextoEstadoEnvio(string estado)
        {
            if (!Enum.TryParse(estado ?? string.Empty, true, out EstadoEnvioAgencia e))
            {
                return null;
            }
            switch (e)
            {
                case EstadoEnvioAgencia.EnCamino:
                    return "en camino";
                case EstadoEnvioAgencia.EnReparto:
                    return "en reparto";
                case EstadoEnvioAgencia.Reprogramada:
                    return "entrega reprogramada";
                default:
                    return "entregado";
            }
        }

        private static string Dia(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy", es) ?? "sin fecha";

        private static string DiaCorto(DateTime? fecha) => fecha?.ToString("dd/MM", es) ?? "sin fecha";

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
    }

    /// <summary>NestoAPI#865 (corte 3): reenvía a Compras los correos recién guardados por el job. Nunca lanza.</summary>
    public interface IReenviadorCorreosSeguimiento
    {
        /// <summary>Devuelve cuántos ha reenviado.</summary>
        int Reenviar(IEnumerable<(CorreoCompras Correo, CorreoSeguimientoFila Fila, ResultadoCorreoGuardado Resultado)> correos);
    }

    /// <summary>
    /// NestoAPI#865 (corte 3, TEMPORAL): cada correo captado y guardado por el job «compras-correos-seguimiento» se reenvía a compras@ por el
    /// SMTP de siempre (<see cref="IServicioCorreoElectronico"/>; Graph sigue siendo solo lectura: nada de /forward ni Mail.Send), con una
    /// cabecera de lo que se ha hecho y debajo el cuerpo original tal como lo ha leído Graph en esa pasada (nunca se guarda). El histórico y
    /// el reanálisis no reenvían (no pasan por aquí). Idempotente: solo llegan los que el job acaba de insertar (el INSERT es único por
    /// MensajeId). Interruptor ParametrosUsuario «(defecto)» <see cref="CLAVE_INTERRUPTOR"/>: SOLO «1» = encendido (sin fila, apagado).
    /// Un fallo no rompe el job: ELMAH, como mucho una vez al día.
    /// </summary>
    public class ReenviadorCorreosSeguimiento : IReenviadorCorreosSeguimiento
    {
        public const string CLAVE_INTERRUPTOR = "Compras.CorreosSeguimientoReenviar";
        private static DateTime? ultimoFalloApuntado;
        private static readonly object cerrojo = new object();

        private readonly IServicioCorreoElectronico correo;
        private readonly Func<bool> interruptor;
        private readonly Action<Exception> registrar;
        private readonly Func<DateTime> ahora;

        public ReenviadorCorreosSeguimiento(IServicioCorreoElectronico correo, Func<bool> interruptor = null, Action<Exception> registrar = null,
            Func<DateTime> ahora = null)
        {
            this.correo = correo ?? throw new ArgumentNullException(nameof(correo));
            this.registrar = registrar ?? (_ => { });
            this.interruptor = interruptor ?? (() => InterruptorEncendido(this.registrar));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Para los tests: que el fallo se vuelva a apuntar.</summary>
        internal static void OlvidarFalloApuntado()
        {
            lock (cerrojo)
            {
                ultimoFalloApuntado = null;
            }
        }

        internal static bool InterruptorEncendido(Action<Exception> registrar)
        {
            try
            {
                return ServicioAplicacionCorreosCompras.Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, ServicioAplicacionCorreosCompras.USUARIO_DEFECTO, CLAVE_INTERRUPTOR));
            }
            catch (Exception ex)
            {
                registrar?.Invoke(new Exception($"[Correos de seguimiento de compras #865] No se ha podido leer el interruptor {CLAVE_INTERRUPTOR}: no se reenvía. {ex.Message}", ex));
                return false;
            }
        }

        public int Reenviar(IEnumerable<(CorreoCompras Correo, CorreoSeguimientoFila Fila, ResultadoCorreoGuardado Resultado)> correos)
        {
            var lista = (correos ?? Enumerable.Empty<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>())
                .Where(c => c.Item1 != null && c.Item2 != null).ToList();
            if (!lista.Any())
            {
                return 0;
            }
            bool encendido;
            try
            {
                encendido = interruptor();
            }
            catch (Exception)
            {
                encendido = false;
            }
            if (!encendido)
            {
                return 0;
            }
            int reenviados = 0;
            int fallidos = 0;
            string primerError = null;
            foreach (var (original, fila, resultado) in lista)
            {
                try
                {
                    using (MailMessage mail = Construir(original, fila, resultado))
                    {
                        if (correo.EnviarCorreoSMTP(mail))
                        {
                            reenviados++;
                            continue;
                        }
                    }
                    fallidos++;
                    primerError = primerError ?? "el SMTP no lo ha aceptado";
                }
                catch (Exception ex)
                {
                    fallidos++;
                    primerError = primerError ?? ex.Message;
                }
            }
            if (fallidos > 0)
            {
                ApuntarFallo(fallidos, primerError);
            }
            return reenviados;
        }

        internal static MailMessage Construir(CorreoCompras original, CorreoSeguimientoFila fila, ResultadoCorreoGuardado resultado)
        {
            string remitente = string.IsNullOrWhiteSpace(original.RemitenteNombre)
                ? original.RemitenteDireccion
                : $"{original.RemitenteNombre} <{original.RemitenteDireccion}>";
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = TextosReenvioCorreoSeguimiento.Asunto(original.Asunto),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                Body = "<!DOCTYPE html><html><head><meta charset='utf-8'></head><body>" +
                    TextosReenvioCorreoSeguimiento.Cabecera(fila, resultado, remitente, original.Recibido) +
                    TextosReenvioCorreoSeguimiento.CuerpoOriginal(original.Cuerpo, original.CuerpoEsHtml) +
                    "</body></html>"
            };
            mail.To.Add(Constantes.Correos.COMPRAS);
            return mail;
        }

        private void ApuntarFallo(int fallidos, string error)
        {
            DateTime hoy = ahora().Date;
            lock (cerrojo)
            {
                if (ultimoFalloApuntado == hoy)
                {
                    return;
                }
                ultimoFalloApuntado = hoy;
            }
            try
            {
                registrar(new Exception($"[Correos de seguimiento de compras #865] No se han podido reenviar a Compras {fallidos} correo(s): {error} " +
                    "(no se vuelve a apuntar hasta mañana)."));
            }
            catch
            {
                // nada
            }
        }
    }
}
