using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#666 (Sugerencia 672 de Novedades, María José): cuando el seguimiento pasa un envío a
    /// INCIDENTADO, se avisa a los comerciales del pedido (estética y/o peluquería según sus líneas, con el
    /// mismo núcleo que el correo de retenidos, <see cref="ComercialesPedido"/>) por correo, con copia a
    /// logística (Carlos 20/10/26: las incidencias de agencia las lleva logística, no administración), y en la
    /// campana de Nesto (buzón + SignalR).
    /// </summary>
    public class AvisadorIncidenciasEnvio
    {
        /// <summary>Tipo del aviso en la campana de Nesto.</summary>
        public const string TIPO_NOTIFICACION = "IncidenciaEnvioAgencia";
        /// <summary>NestoAPI#699: tipo del aviso de envío DEVUELTO (¿lo sigue queriendo el cliente?), en la campana y en NestoApp.</summary>
        public const string TIPO_NOTIFICACION_DEVOLUCION = "DevolucionEnvioAgencia";
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";

        private readonly NVEntities db;
        private readonly ILectorComercialesPedido lector;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly IServicioNotificacionesPush notificaciones;

        private readonly Func<int, Task<bool>> yaDecidido;

        /// <param name="yaDecidido">NestoAPI#699: ¿alguien ha decidido ya qué hacer con el envío devuelto? (null = la tabla
        /// EnviosAgenciaReenvios; sin tabla, no).</param>
        public AvisadorIncidenciasEnvio(NVEntities db, ILectorComercialesPedido lector = null,
            IServicioCorreoElectronico servicioCorreo = null, IServicioNotificacionesPush notificaciones = null,
            Func<int, Task<bool>> yaDecidido = null)
        {
            this.db = db;
            this.lector = lector ?? new LectorComercialesPedido(db);
            this.servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.yaDecidido = yaDecidido ?? (async envio => await new RepositorioReenviosEnvio(db).LeerAsync(envio).ConfigureAwait(false) != null);
        }

        /// <summary>
        /// ¿Este cambio de estado es una incidencia NUEVA? Solo la transición (de cualquier otro estado a
        /// Incidentado) avisa: un envío que ya estaba incidentado no vuelve a avisar en cada pasada del job,
        /// aunque la agencia cambie el texto de la incidencia.
        /// </summary>
        public static bool PasaAIncidencia(short estadoAnterior, EnviosAgencia envio) =>
            envio != null
            && estadoAnterior != Constantes.Agencias.ESTADO_INCIDENTADO
            && envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO;

        /// <summary>
        /// NestoAPI#699: ¿el envío acaba de pasar a DEVUELTO? (de cualquier otro estado, incidentado incluido). La agencia lo
        /// devuelve y la caja tarda días en llegar: se pregunta YA al vendedor si el cliente lo sigue queriendo, para que el
        /// almacén lo sepa antes de que llegue (Alfredo, 04/11/26).
        /// </summary>
        public static bool PasaADevuelto(short estadoAnterior, EnviosAgencia envio) =>
            envio != null
            && estadoAnterior != Constantes.Agencias.ESTADO_DEVUELTO
            && envio.Estado == Constantes.Agencias.ESTADO_DEVUELTO;

        /// <summary>Incidencia nueva (#666) o devolución nueva (#699): las dos avisan a los comerciales.</summary>
        public static bool HayQueAvisar(short estadoAnterior, EnviosAgencia envio) =>
            PasaAIncidencia(estadoAnterior, envio) || PasaADevuelto(estadoAnterior, envio);

        /// <summary>
        /// Punto de entrada del job y de la actualización a demanda: con su propio contexto, para no mezclarse
        /// con el de la pasada. Nunca lanza.
        /// </summary>
        public static async Task AvisarConContextoPropioAsync(EnviosAgencia envio)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    await new AvisadorIncidenciasEnvio(db).AvisarAsync(envio).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Registrar(envio, ex);
            }
        }

        /// <summary>Correo + campana. Nunca lanza: un fallo al avisar no debe romper el seguimiento.</summary>
        public async Task AvisarAsync(EnviosAgencia envio)
        {
            if (envio == null)
            {
                return;
            }
            try
            {
                bool devuelto = envio.Estado == Constantes.Agencias.ESTADO_DEVUELTO;
                // NestoAPI#699: si ya se decidió (p. ej. se reenvió estando incidentado), no se pregunta otra vez.
                if (devuelto && await YaDecididoSinFallar(envio.Numero).ConfigureAwait(false))
                {
                    return;
                }
                ComercialesPedido comerciales = LeerComerciales(envio);
                var datosAgencia = db.AgenciasTransportes
                    .Where(a => a.Numero == envio.Agencia)
                    .Select(a => new { a.Nombre, a.Identificador }).FirstOrDefault();
                string agencia = datosAgencia?.Nombre?.Trim();

                MailMessage mail = devuelto
                    ? ComponerCorreoDevolucion(envio, comerciales, agencia, datosAgencia?.Identificador?.Trim())
                    : ComponerCorreo(envio, comerciales, agencia, datosAgencia?.Identificador?.Trim());
                _ = servicioCorreo.EnviarCorreoSMTP(mail);

                NotificacionPushDTO notificacion = devuelto
                    ? ComponerNotificacionDevolucion(envio, comerciales, agencia)
                    : ComponerNotificacion(envio, comerciales, agencia);
                foreach (string usuario in comerciales.UsuariosBuzon())
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                // Carlos 19/10/26: los vendedores casi no usan Nesto; la push les llega a NestoApp
                foreach (string vendedor in VendedoresNestoApp(comerciales))
                {
                    _ = await notificaciones.EnviarAVendedor(envio.Empresa?.Trim(), vendedor, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Registrar(envio, ex);
            }
        }

        /// <summary>
        /// NestoAPI#699: la agencia ha entregado un envío que se había decidido volver a enviar. Correo a los comerciales del
        /// pedido con copia a Logística (los mismos destinatarios que la devolución): si el reenvío se ha anulado, para que lo
        /// sepan; si ya estaba tramitado, para que lo resuelvan a mano. Nunca lanza.
        /// </summary>
        public Task AvisarReenvioEntregadoAsync(EnviosAgencia origen, RegistroReenvioEnvio fila, EnviosAgencia nuevo, bool anulado)
        {
            try
            {
                ComercialesPedido comerciales = LeerComerciales(origen);
                string agencia = db.AgenciasTransportes
                    .Where(a => a.Numero == origen.Agencia)
                    .Select(a => a.Nombre).FirstOrDefault()?.Trim();
                _ = servicioCorreo.EnviarCorreoSMTP(ComponerCorreoReenvioEntregado(origen, fila, nuevo, anulado, comerciales, agencia));
            }
            catch (Exception ex)
            {
                Registrar(origen, ex);
            }
            return Task.CompletedTask;
        }

        /// <summary>NestoAPI#699: el correo de <see cref="AvisarReenvioEntregadoAsync"/>.</summary>
        internal static MailMessage ComponerCorreoReenvioEntregado(EnviosAgencia origen, RegistroReenvioEnvio fila, EnviosAgencia nuevo,
            bool anulado, ComercialesPedido comerciales, string agencia)
        {
            string cliente = TextoCliente(origen, comerciales);
            string pedido = origen.Pedido.HasValue ? $" del pedido {origen.Pedido}" : string.Empty;
            int numeroNuevo = nuevo?.Numero ?? fila?.EnvioNuevo ?? 0;
            string nombreAgencia = agencia ?? origen.Agencia.ToString();
            string entregado =
                $"<p>La agencia <b>{Html(nombreAgencia)}</b> ha <b>entregado</b> el envío <b>{origen.Numero}</b>{Html(pedido)} de " +
                $"<b>{Html(cliente)}</b> (número de seguimiento {Html(origen.CodigoBarras?.Trim())}). Tenía una incidencia y se había " +
                $"decidido volver a enviarlo en el envío <b>{numeroNuevo}</b>" +
                (string.IsNullOrWhiteSpace(fila?.Usuario) ? string.Empty : $" ({Html(fila.Usuario.Trim())}, {fila.Fecha:dd/MM/yyyy})") + ".</p>";
            string cuerpo;
            string asunto;
            if (anulado)
            {
                asunto = $"La agencia entregó el envío {origen.Numero}{pedido} - c/ {cliente}: se anula el reenvío {numeroNuevo}";
                cuerpo = entregado +
                    $"<p>Como el reenvío {numeroNuevo} aún no tenía etiqueta, <b>se ha anulado</b>: no hay que mandar nada más. " +
                    "Si la caja ya estaba preparada para salir otra vez, no le pegue la etiqueta y devuelva la mercancía a su ubicación.</p>" +
                    (fila?.IncluirPortes == true
                        ? $"<p>El reenvío añadió una línea de portes al pedido{Html(pedido)}: quítela si no se debe cobrar.</p>"
                        : string.Empty);
            }
            else
            {
                asunto = $"La agencia entregó el envío {origen.Numero}{pedido} - c/ {cliente}, pero el reenvío {numeroNuevo} ya está tramitado: hay dos envíos";
                cuerpo = entregado +
                    $"<p>El reenvío <b>{numeroNuevo}</b> ya tiene etiqueta" +
                    (string.IsNullOrWhiteSpace(nuevo?.CodigoBarras) ? string.Empty : $" ({Html(nuevo.CodigoBarras.Trim())})") +
                    ", así que <b>hay dos envíos vivos del mismo pedido</b> y no se ha tocado nada. Resuélvalo a mano: si el reenvío " +
                    "aún no ha salido, anúlelo en la agencia y en Nesto › Agencias; si ya ha salido, hable con el cliente y con la " +
                    "agencia para que lo devuelvan.</p>";
            }
            var mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                Subject = asunto,
                IsBodyHtml = true,
                Body = cuerpo
            };
            AnadirDestinatarios(mail, origen, comerciales);
            return mail;
        }

        private async Task<bool> YaDecididoSinFallar(int envio)
        {
            try
            {
                return await yaDecidido(envio).ConfigureAwait(false);
            }
            catch
            {
                // Mejor preguntar de más que no preguntar.
                return false;
            }
        }

        private ComercialesPedido LeerComerciales(EnviosAgencia envio)
        {
            ComercialesPedido comerciales = envio.Pedido.HasValue ? lector.Leer(envio.Empresa, envio.Pedido.Value) : null;
            // Envío sin pedido (o pedido que ya no está): el vendedor del envío y, de respaldo, quien lo tramitó.
            return comerciales ?? lector.LeerDeVendedor(envio.Empresa, envio.Vendedor, envio.Usuario);
        }

        internal static MailMessage ComponerCorreo(EnviosAgencia envio, ComercialesPedido comerciales, string agencia,
            string identificadorAgencia = null)
        {
            string cliente = TextoCliente(envio, comerciales);
            // NestoAPI#697 (Carlos 23/10/26): la web de la agencia casi siempre explica mejor la incidencia que el
            // estado que nos llega (motivo, intentos, oficina...). Sin agencia registrada o sin datos, sin enlace.
            string enlace = EnlaceSeguimiento(envio, agencia, identificadorAgencia);
            string numeroSeguimiento = string.IsNullOrEmpty(enlace) || string.IsNullOrWhiteSpace(envio.CodigoBarras)
                ? $"<b>{Html(envio.CodigoBarras?.Trim())}</b>"
                : $"<a href=\"{Html(enlace)}\"><b>{Html(envio.CodigoBarras?.Trim())}</b></a>";
            string documento = envio.Pedido.HasValue ? $"el pedido {envio.Pedido}" : $"el envío {envio.Numero}";
            var mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                Subject = $"Incidencia de la agencia en {documento} - c/ {cliente}",
                IsBodyHtml = true,
                Body =
                    $"<p>La agencia <b>{Html(agencia ?? envio.Agencia.ToString())}</b> ha puesto una <b>incidencia</b> en " +
                    $"{Html(documento)} del cliente <b>{Html(cliente)}</b>.</p>" +
                    "<ul>" +
                    (envio.Pedido.HasValue ? $"<li>Pedido: <b>{envio.Pedido}</b></li>" : string.Empty) +
                    $"<li>Cliente: <b>{Html(cliente)}</b></li>" +
                    $"<li>Agencia: {Html(agencia ?? envio.Agencia.ToString())}</li>" +
                    $"<li>Número de seguimiento: {numeroSeguimiento} (envío {envio.Numero})</li>" +
                    $"<li>Incidencia: <b>{Html(DescripcionIncidencia(envio))}</b></li>" +
                    "</ul>" +
                    (string.IsNullOrEmpty(enlace) ? string.Empty
                        : $"<p><a href=\"{Html(enlace)}\">Ver el seguimiento en la web de la agencia</a>: " +
                          "suele explicar el motivo de la incidencia.</p>") +
                    "<p>Revise el envío en Nesto (Agencias, pestaña de incidentados) y, si hace falta, " +
                    "póngase en contacto con el cliente para solucionarlo cuanto antes.</p>"
            };
            AnadirDestinatarios(mail, envio, comerciales);
            return mail;
        }

        /// <summary>
        /// NestoAPI#699: la agencia ha DEVUELTO el envío. Se pregunta a los comerciales si el cliente lo sigue queriendo, ANTES
        /// de que llegue la caja: con la respuesta, el almacén la vuelve a poner con la etiqueta nueva o la abona y la ubica.
        /// Mismos destinatarios que la incidencia (#666, #716).
        /// </summary>
        internal static MailMessage ComponerCorreoDevolucion(EnviosAgencia envio, ComercialesPedido comerciales, string agencia,
            string identificadorAgencia = null)
        {
            string cliente = TextoCliente(envio, comerciales);
            string enlace = EnlaceSeguimiento(envio, agencia, identificadorAgencia);
            string pedido = envio.Pedido.HasValue ? $" del pedido {envio.Pedido}" : string.Empty;
            var mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                Subject = $"Envío devuelto{pedido} - c/ {cliente}: ¿lo sigue queriendo el cliente?",
                IsBodyHtml = true,
                Body =
                    $"<p>El envío <b>{envio.Numero}</b>{Html(pedido)} de <b>{Html(cliente)}</b> viene <b>devuelto</b> " +
                    $"({Html(DescripcionIncidencia(envio))}).</p>" +
                    "<ul>" +
                    (envio.Pedido.HasValue ? $"<li>Pedido: <b>{envio.Pedido}</b></li>" : string.Empty) +
                    $"<li>Cliente: <b>{Html(cliente)}</b></li>" +
                    $"<li>Agencia: {Html(agencia ?? envio.Agencia.ToString())}</li>" +
                    $"<li>Número de seguimiento: <b>{Html(envio.CodigoBarras?.Trim())}</b> (envío {envio.Numero})</li>" +
                    $"<li>Dirección: {Html(envio.Direccion?.Trim())}, {Html(envio.CodPostal?.Trim())} {Html(envio.Poblacion?.Trim())}</li>" +
                    "</ul>" +
                    (string.IsNullOrEmpty(enlace) ? string.Empty
                        : $"<p><a href=\"{Html(enlace)}\">Ver el seguimiento en la web de la agencia</a>.</p>") +
                    // NestoApp#273 (NestoApp 2.28.0): la devolución se contesta también desde el pedido en la app
                    "<p><b>¿Lo sigue queriendo el cliente?</b> Contéstelo en Nesto › Agencias › Devueltos o en NestoApp, en el pedido: " +
                    "«volver a enviar» (con la dirección corregida si hace falta) o «abonar». Si no puede, " +
                    "conteste a este correo o avise a Logística (va en copia) diciendo qué hacer. Así el almacén sabe qué hacer " +
                    "con la caja en cuanto llegue.</p>"
            };
            AnadirDestinatarios(mail, envio, comerciales);
            return mail;
        }

        private static void AnadirDestinatarios(MailMessage mail, EnviosAgencia envio, ComercialesPedido comerciales)
        {
            List<string> destinatarios = comerciales?.Correos() ?? new List<string>();
            // Novedad 716 (Laura, 22/10/26): las incidencias de los pedidos de la tienda online y de Amazon (y demás
            // canales externos) las llevan Laura y Enrique desde tiendaonline@. Se AÑADE a quien ya lo recibía
            // (comerciales o quien grabó el pedido; logística sigue en copia): no se le quita el aviso a nadie.
            if (EsPedidoTiendaOnline(envio, comerciales)
                && !destinatarios.Contains(Constantes.Correos.TIENDA_ONLINE, StringComparer.OrdinalIgnoreCase))
            {
                destinatarios.Insert(0, Constantes.Correos.TIENDA_ONLINE);
            }
            if (destinatarios.Any())
            {
                destinatarios.ForEach(d => mail.To.Add(new MailAddress(d)));
                mail.CC.Add(new MailAddress(Constantes.Correos.LOGISTICA));
            }
            else
            {
                mail.To.Add(new MailAddress(Constantes.Correos.LOGISTICA));
            }
        }

        /// <summary>
        /// Novedad 716: pedido de la tienda online o de un canal externo (forma de venta de sus líneas) o, si no
        /// hay pedido que leer, envío a los clientes ficticios de la tienda online o de Amazon.
        /// </summary>
        internal static bool EsPedidoTiendaOnline(EnviosAgencia envio, ComercialesPedido comerciales)
        {
            if (comerciales?.EsCanalExterno == true)
            {
                return true;
            }
            string cliente = (comerciales?.Cliente ?? envio?.Cliente)?.Trim();
            return cliente == Constantes.ClientesEspeciales.TIENDA_ONLINE || cliente == Constantes.ClientesEspeciales.AMAZON;
        }

        internal static NotificacionPushDTO ComponerNotificacion(EnviosAgencia envio, ComercialesPedido comerciales, string agencia)
        {
            string cliente = TextoCliente(envio, comerciales);
            var datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = envio.Empresa?.Trim(),
                ["envio"] = envio.Numero.ToString(),
                ["cliente"] = envio.Cliente?.Trim()
            };
            if (envio.Pedido.HasValue)
            {
                datos["pedido"] = envio.Pedido.Value.ToString();
            }
            return new NotificacionPushDTO
            {
                Titulo = envio.Pedido.HasValue
                    ? $"Incidencia de agencia en el pedido {envio.Pedido}"
                    : $"Incidencia de agencia en el envío {envio.Numero}",
                Cuerpo = $"Cliente {cliente}. {agencia ?? "La agencia"} ({envio.CodigoBarras?.Trim()}): {DescripcionIncidencia(envio)}",
                Tipo = TIPO_NOTIFICACION,
                Datos = datos
            };
        }

        /// <summary>NestoAPI#699: la campana de Nesto y la push de NestoApp del envío devuelto (Datos para contestar desde la app).</summary>
        internal static NotificacionPushDTO ComponerNotificacionDevolucion(EnviosAgencia envio, ComercialesPedido comerciales, string agencia)
        {
            NotificacionPushDTO notificacion = ComponerNotificacion(envio, comerciales, agencia);
            notificacion.Tipo = TIPO_NOTIFICACION_DEVOLUCION;
            notificacion.Datos["tipo"] = TIPO_NOTIFICACION_DEVOLUCION;
            notificacion.Titulo = envio.Pedido.HasValue
                ? $"Envío devuelto del pedido {envio.Pedido}: ¿lo sigue queriendo el cliente?"
                : $"Envío {envio.Numero} devuelto: ¿lo sigue queriendo el cliente?";
            notificacion.Cuerpo = $"Cliente {TextoCliente(envio, comerciales)}. {agencia ?? "La agencia"} lo devuelve " +
                $"({DescripcionIncidencia(envio)}). Contéstelo en Nesto › Agencias › Devueltos o en NestoApp, en el pedido.";
            return notificacion;
        }

        /// <summary>Los comerciales reales del aviso (no el vendedor general), una vez cada uno: a sus móviles con NestoApp.</summary>
        internal static List<string> VendedoresNestoApp(ComercialesPedido comerciales) =>
            (comerciales?.Comerciales ?? new List<ComercialPedido>())
                .Where(ComercialesPedido.EsComercialReal)
                .Select(c => c.Vendedor.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string DescripcionIncidencia(EnviosAgencia envio) =>
            string.IsNullOrWhiteSpace(envio.DetalleEstado) ? "la agencia no indica el motivo" : envio.DetalleEstado.Trim();

        private static string TextoCliente(EnviosAgencia envio, ComercialesPedido comerciales)
        {
            string numero = (comerciales?.Cliente ?? envio.Cliente)?.Trim();
            // Carlos 11/11/26: cliente/contacto (27593/0), no solo el cliente: un cliente con varios contactos no se
            // distingue por el número. El contacto es el del pedido o, si no se ha leído, el del envío.
            string contacto = (string.IsNullOrWhiteSpace(comerciales?.Contacto) ? envio.Contacto : comerciales.Contacto)?.Trim();
            if (!string.IsNullOrWhiteSpace(numero) && !string.IsNullOrWhiteSpace(contacto))
            {
                numero = $"{numero}/{contacto}";
            }
            string nombre = (comerciales?.NombreCliente ?? envio.Nombre)?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? numero : $"{numero} - {nombre}";
        }

        /// <summary>
        /// NestoAPI#697: el enlace de seguimiento de la agencia, o null si no se puede construir. Reparto propio
        /// (agencia 14): la página pública firmada (NestoAPI#744) con el número del envío, haya o no CodigoBarras; sin
        /// clave de firma configurada, sin enlace. El resto, por la estrategia de su nombre, con el albarán y el CP.
        /// </summary>
        internal static string EnlaceSeguimiento(EnviosAgencia envio, string agencia, string identificadorAgencia)
        {
            if (envio == null)
            {
                return null;
            }
            string codigo = envio.CodigoBarras?.Trim();
            if (envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                && (string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(agencia)))
            {
                return null;
            }
            try
            {
                return RegistroSeguimientoAgencias.UrlDeEnvio(envio.Numero, envio.Agencia, agencia, identificadorAgencia,
                    codigo, envio.CodPostal?.Trim(), envio.Cliente?.Trim(), envio.Pedido ?? 0);
            }
            catch
            {
                // Un enlace que no se puede construir no debe impedir el aviso: el correo sale como antes, sin enlace.
                return null;
            }
        }

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);

        private static void Registrar(EnviosAgencia envio, Exception ex)
        {
            try
            {
                ElmahHelper.Log(new Exception(
                    $"[Incidencia envío #666] No se pudo avisar de la incidencia del envío {envio?.Numero} (pedido {envio?.Pedido}): {ex.Message}", ex),
                    "Sistema (seguimiento de envíos)");
            }
            catch
            {
                // nunca romper el seguimiento
            }
        }
    }
}
