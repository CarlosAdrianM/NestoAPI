using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.PedidosVenta.Retencion
{
    /// <summary>
    /// NestoAPI#722 (Carlos, 06/11/26): aplazar un pedido con picking cuyo envío ya tiene la agencia. Si la agencia admite el
    /// cambio de fecha en tránsito (hoy CTT, NestoAPI#726), se le cambia con la MISMA lógica que POST api/EnviosAgencias/{id}/
    /// FechaEntrega; si no, el PUT se rechaza y Nesto ofrece mandar un correo a logística.
    /// </summary>
    public interface ICambioFechaEnvioAgencia
    {
        /// <summary>Lo que admite el envío en la agencia (GET api/EnviosAgencias/{id}/Gestiones); null si no se ha podido saber.</summary>
        Task<GestionesEnvioDTO> Gestiones(int envio);

        /// <summary>El cambio de fecha en la agencia (el núcleo de POST api/EnviosAgencias/{id}/FechaEntrega).</summary>
        Task<ResultadoCambioFechaEnAgencia> CambiarFecha(int envio, DateTime fecha);
    }

    /// <summary>
    /// El de producción: un EnviosAgenciasController con su propio contexto (la historia y la fecha del envío no se mezclan con
    /// el guardado del pedido) y fuera de cualquier transacción de fuera (UnirPedidos): lo que haga la agencia no se deshace.
    /// </summary>
    public class CambioFechaEnvioAgencia : ICambioFechaEnvioAgencia
    {
        private readonly Func<EnviosAgenciasController> crearController;

        public CambioFechaEnvioAgencia(IPrincipal usuario) : this(() => new EnviosAgenciasController(new NVEntities()) { User = usuario })
        {
        }

        /// <summary>Para los tests: el controlador con su BD y su fábrica de agencias falsas.</summary>
        internal CambioFechaEnvioAgencia(Func<EnviosAgenciasController> crearController)
        {
            this.crearController = crearController ?? throw new ArgumentNullException(nameof(crearController));
        }

        public async Task<GestionesEnvioDTO> Gestiones(int envio)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            using (EnviosAgenciasController controller = crearController())
            {
                return (await controller.GetGestionesEnvio(envio).ConfigureAwait(true) as OkNegotiatedContentResult<GestionesEnvioDTO>)?.Content;
            }
        }

        public async Task<ResultadoCambioFechaEnAgencia> CambiarFecha(int envio, DateTime fecha)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            using (EnviosAgenciasController controller = crearController())
            {
                return (await controller.CambiarFechaEntregaEnAgencia(envio, fecha).ConfigureAwait(true)).Resultado;
            }
        }
    }

    /// <summary>Cuerpo de POST api/PedidosVenta/{empresa}/{numero}/AvisarLogisticaAplazamiento.</summary>
    public class AvisarLogisticaAplazamientoRequest
    {
        /// <summary>La fecha de entrega que pedía el usuario.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>El envío que ya tiene la agencia (el «envio» del rechazo); si no viene, el último tramitado del pedido.</summary>
        public int? Envio { get; set; }
        /// <summary>Quien lo pide (como en el PUT); si no viene, el usuario autenticado.</summary>
        public string Usuario { get; set; }
        /// <summary>Lo que quiera añadir el usuario (opcional).</summary>
        public string Comentario { get; set; }
    }

    /// <summary>Respuesta de POST api/PedidosVenta/{empresa}/{numero}/AvisarLogisticaAplazamiento.</summary>
    public class AvisarLogisticaAplazamientoRespuesta
    {
        public string Mensaje { get; set; }
        public List<string> Destinatarios { get; set; } = new List<string>();
    }

    public static class AplazamientoEnvioEnAgencia
    {
        /// <summary>El PUT se rechaza porque la agencia ya tiene el pedido y no admite cambiarle la fecha: Nesto ofrece el correo.</summary>
        public const string CODIGO_YA_EN_AGENCIA = "PEDIDO_YA_EN_AGENCIA";
        /// <summary>La agencia admite el cambio pero ha rechazado esta fecha (pasada, no laborable, 403 por el estado…).</summary>
        public const string CODIGO_AGENCIA_RECHAZA_FECHA = "AGENCIA_RECHAZA_FECHA_ENTREGA";
        /// <summary>La agencia no responde (502), como en Anular y Modificar.</summary>
        public const string CODIGO_AGENCIA_NO_RESPONDE = "AGENCIA_NO_RESPONDE";
        /// <summary>Tipo del aviso del PUT cuando la fecha se ha cambiado también en la agencia.</summary>
        public const string TIPO_AVISO_CAMBIADA_EN_AGENCIA = "FechaCambiadaEnAgencia";
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static string MensajeYaEnAgencia(int pedido, int envio)
            => $"El pedido {pedido} ya lo tiene la agencia (envío {envio}): no llegamos a tiempo para retrasar la entrega. " +
               "La única opción es que el cliente lo rechace. Si quiere, mandamos un correo a logística para que intenten hablar con la agencia.";

        public static string MensajeCambiadaEnAgencia(ResultadoCambioFechaEnAgencia resultado)
            => $"La fecha de entrega se ha cambiado también en {NombreAgencia(resultado?.NombreAgencia)} (envío {resultado?.Envio}): " +
               $"la entregará el {resultado?.Fecha.ToString("dd/MM/yyyy", castellano)}.";

        /// <summary>403 con código PEDIDO_YA_EN_AGENCIA: la agencia ya lo tiene y no se le puede cambiar la fecha.</summary>
        public static NestoBusinessException ExcepcionYaEnAgencia(string empresa, int pedido, int envio, DateTime fechaPedida, string usuario)
            => new NestoBusinessException(MensajeYaEnAgencia(pedido, envio), new ErrorContext
            {
                ErrorCode = CODIGO_YA_EN_AGENCIA,
                Empresa = empresa?.Trim(),
                Pedido = pedido,
                Usuario = usuario,
                AdditionalData = new Dictionary<string, object>
                {
                    ["envio"] = envio,
                    ["fechaEntrega"] = fechaPedida.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["puedeAvisarLogistica"] = true
                }
            })
            { StatusCode = HttpStatusCode.Forbidden };

        /// <summary>La agencia admitía el cambio pero no lo ha hecho: 403 con su motivo, o 502 si no responde. El pedido no se guarda.</summary>
        public static NestoBusinessException ExcepcionAgenciaNoLoHace(string empresa, int pedido, ResultadoCambioFechaEnAgencia resultado, string usuario)
        {
            string motivo = string.IsNullOrWhiteSpace(resultado?.Mensaje) ? "La agencia ha rechazado el cambio." : resultado.Mensaje.Trim();
            bool noResponde = resultado?.AgenciaNoResponde == true;
            return new NestoBusinessException(
                $"No se ha guardado el pedido: no se ha podido cambiar la fecha de entrega en {NombreAgencia(resultado?.NombreAgencia)} (envío {resultado?.Envio}). {motivo}",
                new ErrorContext
                {
                    ErrorCode = noResponde ? CODIGO_AGENCIA_NO_RESPONDE : CODIGO_AGENCIA_RECHAZA_FECHA,
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    Usuario = usuario,
                    AdditionalData = new Dictionary<string, object>
                    {
                        ["envio"] = resultado?.Envio ?? 0,
                        ["fechaEntrega"] = (resultado?.Fecha ?? DateTime.MinValue).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["motivoAgencia"] = motivo
                    }
                })
            {
                StatusCode = noResponde ? HttpStatusCode.BadGateway : HttpStatusCode.Forbidden,
                RegistrarEnLog = noResponde
            };
        }

        /// <summary>
        /// Al PUT: con el envío que ya tiene la agencia, ¿se le puede cambiar la fecha? Si la agencia no tiene gestión en tránsito, el
        /// estado no lo admite o no se puede saber, se rechaza (lo conservador). Devuelve el envío a cambiar en la agencia.
        /// </summary>
        public static async Task<int> EnvioACambiarOExcepcion(ICambioFechaEnvioAgencia cambio, string empresa, int pedido, int envio, DateTime fechaPedida, string usuario)
        {
            GestionesEnvioDTO gestiones = null;
            try
            {
                gestiones = cambio == null ? null : await cambio.Gestiones(envio).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Aplazar con envío en la agencia #722] Pedido {pedido}, envío {envio}: no se ha podido saber si la agencia admite cambiar la fecha: {ex.Message}", ex));
            }
            if (gestiones?.CambiarFecha != true)
            {
                throw ExcepcionYaEnAgencia(empresa, pedido, envio, fechaPedida, usuario);
            }
            return envio;
        }

        /// <summary>El correo a logística (con copia al vendedor del pedido): pedido, envío, fecha pedida y quién lo pide, sin promesas.</summary>
        public static MailMessage ComponerCorreoLogistica(string empresa, int pedido, string cliente, string nombreCliente, int? envio,
            string albaran, string agencia, DateTime fechaPedida, string usuario, string comentario, string correoVendedor)
        {
            string fecha = fechaPedida.ToString("dddd dd/MM/yyyy", castellano);
            string quien = TextosRetencion.NombreUsuario(usuario);
            var mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                Subject = $"Aplazar la entrega del pedido {pedido} (ya lo tiene {NombreAgencia(agencia)}) - c/ {cliente?.Trim()}",
                IsBodyHtml = true,
                Body =
                    $"<p><b>{Html(quien)}</b> quiere aplazar al <b>{Html(fecha)}</b> la entrega del pedido <b>{pedido}</b>, " +
                    $"pero el envío ya lo tiene {Html(NombreAgencia(agencia))} y desde Nesto no se puede cambiar la fecha.</p>" +
                    "<p>Si es posible, intenten hablar con la agencia para que lo retrase. No le hemos prometido nada al cliente: " +
                    "la otra opción es que el cliente rechace la entrega.</p>" +
                    "<ul>" +
                    $"<li>Pedido: <b>{pedido}</b> (empresa {Html(empresa?.Trim())})</li>" +
                    $"<li>Cliente: <b>{Html(cliente?.Trim())}</b>{(string.IsNullOrWhiteSpace(nombreCliente) ? string.Empty : " - " + Html(nombreCliente.Trim()))}</li>" +
                    (envio.HasValue ? $"<li>Envío: {envio}{(string.IsNullOrWhiteSpace(albaran) ? string.Empty : " - albarán <b>" + Html(albaran.Trim()) + "</b>")}</li>" : string.Empty) +
                    $"<li>Fecha de entrega pedida: <b>{Html(fecha)}</b></li>" +
                    $"<li>Lo pide: {Html(usuario)}</li>" +
                    (string.IsNullOrWhiteSpace(comentario) ? string.Empty : $"<li>Comentario: {Html(comentario.Trim())}</li>") +
                    "</ul>"
            };
            mail.To.Add(new MailAddress(Constantes.Correos.LOGISTICA));
            if (!string.IsNullOrWhiteSpace(correoVendedor))
            {
                try
                {
                    mail.CC.Add(new MailAddress(correoVendedor.Trim()));
                }
                catch (FormatException)
                {
                    // Correo del vendedor mal escrito en su ficha: el aviso a logística sale igual.
                }
            }
            return mail;
        }

        private static string NombreAgencia(string agencia) => string.IsNullOrWhiteSpace(agencia) ? "la agencia" : agencia.Trim();

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
    }
}
