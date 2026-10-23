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
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";

        private readonly NVEntities db;
        private readonly ILectorComercialesPedido lector;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly IServicioNotificacionesPush notificaciones;

        public AvisadorIncidenciasEnvio(NVEntities db, ILectorComercialesPedido lector = null,
            IServicioCorreoElectronico servicioCorreo = null, IServicioNotificacionesPush notificaciones = null)
        {
            this.db = db;
            this.lector = lector ?? new LectorComercialesPedido(db);
            this.servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
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
                ComercialesPedido comerciales = LeerComerciales(envio);
                var datosAgencia = db.AgenciasTransportes
                    .Where(a => a.Numero == envio.Agencia)
                    .Select(a => new { a.Nombre, a.Identificador }).FirstOrDefault();
                string agencia = datosAgencia?.Nombre?.Trim();

                MailMessage mail = ComponerCorreo(envio, comerciales, agencia, datosAgencia?.Identificador?.Trim());
                _ = servicioCorreo.EnviarCorreoSMTP(mail);

                NotificacionPushDTO notificacion = ComponerNotificacion(envio, comerciales, agencia);
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
            string numeroSeguimiento = string.IsNullOrEmpty(enlace)
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
            return mail;
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
            string nombre = (comerciales?.NombreCliente ?? envio.Nombre)?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? numero : $"{numero} - {nombre}";
        }

        /// <summary>NestoAPI#697: el enlace de seguimiento de la agencia, o null si no se puede construir.</summary>
        internal static string EnlaceSeguimiento(EnviosAgencia envio, string agencia, string identificadorAgencia)
        {
            string codigo = envio?.CodigoBarras?.Trim();
            if (string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(agencia))
            {
                return null;
            }
            string url = RegistroSeguimientoAgencias.ConstruirUrl(new DatosSeguimientoEnvio
            {
                AgenciaNombre = agencia,
                Identificador = identificadorAgencia,
                CodigoSeguimiento = codigo,
                CodigoPostal = envio.CodPostal?.Trim(),
                Cliente = envio.Cliente?.Trim(),
                Pedido = envio.Pedido ?? 0
            });
            return string.IsNullOrWhiteSpace(url) ? null : url;
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
