using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>Lo que hace falta para abrir un cobro de Aplázame del carrito (todo calculado por el servidor).</summary>
    public class SolicitudCobroAplazame
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Correo { get; set; }
        public string Usuario { get; set; }
        public decimal Importe { get; set; }
        public IEnumerable<LineaPedidoVentaDTO> Lineas { get; set; }
        public DireccionCheckout Facturacion { get; set; }
        public DireccionCheckout Envio { get; set; }
        public string NombreEnvio { get; set; }
        public bool RecogidaEnTienda { get; set; }
    }

    /// <summary>Respuesta de <c>POST api/Pedidos/Cliente/Carrito/Aplazame</c> (contrato con la app).</summary>
    public class CobroAplazameIniciadoDTO
    {
        public int IdPago { get; set; }
        public string CheckoutId { get; set; }
        public string UrlCheckout { get; set; }
        public decimal Importe { get; set; }
    }

    /// <summary>Respuesta de <c>GET api/Pedidos/Cliente/Carrito/Aplazame/{idPago}</c> (contrato con la app).</summary>
    public class EstadoCobroAplazameDTO
    {
        /// <summary>Pendiente | Confirmado | Rechazado | Cancelado.</summary>
        public string Estado { get; set; }
        public decimal Importe { get; set; }
    }

    /// <summary>Lo que se le contesta a Aplázame en la notificación: código HTTP y, si es 200, el status del cuerpo.</summary>
    public class RespuestaNotificacionAplazame
    {
        public int CodigoHttp { get; set; }
        public string Status { get; set; }

        public static RespuestaNotificacionAplazame Ok() => new RespuestaNotificacionAplazame { CodigoHttp = 200, Status = "ok" };
        public static RespuestaNotificacionAplazame Ko() => new RespuestaNotificacionAplazame { CodigoHttp = 200, Status = "ko" };
        public static RespuestaNotificacionAplazame Codigo(int codigo) => new RespuestaNotificacionAplazame { CodigoHttp = codigo };
    }

    /// <summary>Lo que ha hecho una pasada del job de capturas (para el log y los tests).</summary>
    public class ResultadoCapturasAplazame
    {
        public List<int> Capturados { get; } = new List<int>();
        public List<int> YaCapturados { get; } = new List<int>();
        public List<int> Avisados { get; } = new List<int>();
        public List<int> Fallidos { get; } = new List<int>();
    }

    public interface IServicioCobrosAplazame
    {
        ConfiguracionAplazame Configuracion { get; }

        Task<CobroAplazameIniciadoDTO> IniciarCobro(SolicitudCobroAplazame solicitud);

        /// <summary>El estado del cobro, solo si es de Aplázame y de ese cliente; si no, null.</summary>
        Task<EstadoCobroAplazameDTO> Estado(int idPago, string empresa, string cliente);

        Task<RespuestaNotificacionAplazame> ProcesarNotificacion(string cabeceraAutorizacion, string cuerpo);

        /// <summary>Cancela en Aplázame un cobro sin pedido. True si al cliente no se le va a cobrar nada.</summary>
        Task<bool> Cancelar(int idPago, string motivo);

        Task<ResultadoCapturasAplazame> CapturarServidos(DateTime ahora);

        Task AvisarAprobacionSiSigueSinPedido(int idPago);
    }

    /// <summary>
    /// NestoAPI#663: el cobro con Aplázame del carrito de la app, de punta a punta: abrir el checkout, la confirmación que
    /// manda Aplázame (como <c>confirm.php</c> del módulo de PrestaShop), la cancelación cuando el pedido no llega a
    /// existir y la captura cuando se sirve.
    ///
    /// <para><b>Lo que nunca puede pasar</b>: que un pedido nazca pagado sin que Aplázame lo haya aprobado. El cobro solo
    /// pasa a Autorizado cuando Aplázame nos pide la confirmación (<c>pending</c> + <c>confirmation_required</c>) con el
    /// importe EXACTO del cobro, y <see cref="ServicioPagos.ReservarCobroCarrito"/> solo crea el pedido con un cobro
    /// Autorizado.</para>
    /// </summary>
    public class ServicioCobrosAplazame : IServicioCobrosAplazame
    {
        internal const string URL_NOTIFICACION = ServicioPagos.URL_BASE_API + "/api/Aplazame/Notificacion";
        internal const string URL_PAGINA_CHECKOUT = ServicioPagos.URL_BASE_API + "/aplazame/checkout.html";
        internal const string ESQUEMA_APP = "nestotiendas://aplazame/";

        /// <summary>Aplázame exige capturar como mucho 15 días después de confirmar: a los 12 se avisa.</summary>
        internal const int DIAS_AVISO_CAPTURA = 12;

        /// <summary>Si a los 2 minutos de aprobarse el cobro sigue sin pedido, la app no se ha enterado: push.</summary>
        internal const int MINUTOS_AVISO_APROBACION = 2;

        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        private readonly IRepositorioCobrosAplazame repositorio;
        private readonly IClienteApiAplazame api;
        private readonly Lazy<ConfiguracionAplazame> configuracion;
        private readonly IServicioCorreoElectronico correo;

        public ServicioCobrosAplazame(IRepositorioCobrosAplazame repositorio, IClienteApiAplazame api,
            Func<ConfiguracionAplazame> configuracion, IServicioCorreoElectronico correo)
        {
            this.repositorio = repositorio;
            this.api = api;
            this.configuracion = new Lazy<ConfiguracionAplazame>(configuracion);
            this.correo = correo;
        }

        public static IServicioCobrosAplazame CrearPorDefecto()
        {
            Lazy<ConfiguracionAplazame> config = new Lazy<ConfiguracionAplazame>(() => ConfiguracionAplazame.Leer(new LectorParametrosUsuario()));
            return new ServicioCobrosAplazame(
                new RepositorioCobrosAplazame(),
                new ClienteApiPerezoso(() => new ClienteApiAplazame(config.Value.ClavePrivada)),
                () => config.Value,
                new ServicioCorreoElectronico());
        }

        public ConfiguracionAplazame Configuracion => configuracion.Value;

        /// <summary>Errores a ELMAH. Sustituible en tests.</summary>
        internal Action<Exception> RegistrarError { get; set; } = ElmahHelper.Log;

        /// <summary>Programa la push de «ya está aprobado» (Hangfire, a los 2 minutos). Sustituible en tests.</summary>
        internal Action<int> ProgramarAvisoAprobacion { get; set; } = AplazameJobsService.ProgramarAvisoAprobacion;

        /// <summary>Las push a los clientes de NestoTiendas. Se crea solo cuando hay que avisar. Sustituible en tests.</summary>
        internal Func<IServicioNotificacionesPush> CrearNotificaciones { get; set; } = () => new ServicioNotificacionesPush();

        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        #region Abrir el cobro

        public async Task<CobroAplazameIniciadoDTO> IniciarCobro(SolicitudCobroAplazame solicitud)
        {
            if (solicitud == null || solicitud.Importe <= 0)
            {
                throw new ArgumentException("El importe del cobro de Aplázame debe ser mayor que cero");
            }
            if (string.IsNullOrWhiteSpace(solicitud.Correo))
            {
                throw new ArgumentException("Aplázame necesita el correo del cliente");
            }
            ConfiguracionAplazame config = Configuracion;
            if (config.Modo == ModoAplazame.Apagado || !config.TieneClaves)
            {
                throw new InvalidOperationException("Aplázame está apagado o sin claves");
            }

            PagoTPV pago = new PagoTPV
            {
                // Provisional y único hasta tener el Id: el mid definitivo es APP-{Id}
                NumeroOrden = "T" + Guid.NewGuid().ToString("N").Substring(0, 11).ToUpperInvariant(),
                Tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME,
                Empresa = solicitud.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Cliente = solicitud.Cliente?.Trim(),
                Contacto = solicitud.Contacto?.Trim(),
                Importe = solicitud.Importe,
                Descripcion = "PEDIDO EN NUEVA VISIÓN (APLÁZAME)",
                Correo = solicitud.Correo?.Trim(),
                Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                FechaCreacion = Ahora(),
                Usuario = solicitud.Usuario,
                TokenAcceso = Guid.NewGuid(),
                Sandbox = config.Sandbox
            };
            int idPago = await repositorio.Crear(pago).ConfigureAwait(false);
            string mid = Mid(idPago);
            await repositorio.GuardarReferencias(idPago, mid, null).ConfigureAwait(false);

            CheckoutAplazame checkout = ConstructorCheckoutAplazame.Construir(
                mid, solicitud.Correo, solicitud.Cliente, solicitud.Facturacion, solicitud.Envio, solicitud.NombreEnvio,
                solicitud.RecogidaEnTienda, solicitud.Lineas, solicitud.Importe, URL_NOTIFICACION, ESQUEMA_APP);

            string checkoutId;
            try
            {
                checkoutId = await api.CrearCheckout(checkout, config.Sandbox).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Sin checkout no hay nada que aprobar: el cobro no vale para ningún pedido
                _ = await repositorio.CambiarEstado(idPago, new[] { Constantes.EstadosPagoTPV.PENDIENTE },
                    Constantes.EstadosPagoTPV.CANCELADO).ConfigureAwait(false);
                throw;
            }
            await repositorio.GuardarReferencias(idPago, null, checkoutId).ConfigureAwait(false);

            return new CobroAplazameIniciadoDTO
            {
                IdPago = idPago,
                CheckoutId = checkoutId,
                UrlCheckout = UrlCheckout(checkoutId, config.ClavePublica, config.Sandbox),
                Importe = solicitud.Importe
            };
        }

        /// <summary>El identificador del cobro en Aplázame (order.id = mid), estable: con él se confirma, captura y cancela.</summary>
        internal static string Mid(int idPago) => "APP-" + idPago.ToString(CultureInfo.InvariantCulture);

        internal static string UrlCheckout(string checkoutId, string clavePublica, bool sandbox)
        {
            return $"{URL_PAGINA_CHECKOUT}?id={Uri.EscapeDataString(checkoutId ?? string.Empty)}" +
                $"&pk={Uri.EscapeDataString(clavePublica ?? string.Empty)}&sandbox={(sandbox ? "true" : "false")}";
        }

        #endregion

        #region Estado para la app

        public async Task<EstadoCobroAplazameDTO> Estado(int idPago, string empresa, string cliente)
        {
            PagoTPV pago = await repositorio.Leer(idPago).ConfigureAwait(false);
            if (!ProveedorCobroCarrito.EsAplazame(pago)
                || !string.Equals(pago.Cliente?.Trim(), cliente?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(pago.Empresa?.Trim(), (empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return new EstadoCobroAplazameDTO { Estado = EstadoParaLaApp(pago.Estado), Importe = pago.Importe };
        }

        internal static string EstadoParaLaApp(string estado)
        {
            switch (estado?.Trim())
            {
                case Constantes.EstadosPagoTPV.AUTORIZADO:
                    return "Confirmado";
                case Constantes.EstadosPagoTPV.DENEGADO:
                    return "Rechazado";
                case Constantes.EstadosPagoTPV.CANCELADO:
                case Constantes.EstadosPagoTPV.DEVUELTO:
                    return "Cancelado";
                default:
                    return "Pendiente";
            }
        }

        #endregion

        #region Notificación de Aplázame

        public async Task<RespuestaNotificacionAplazame> ProcesarNotificacion(string cabeceraAutorizacion, string cuerpo)
        {
            ConfiguracionAplazame config = Configuracion;
            if (string.IsNullOrWhiteSpace(config.ClavePrivada) || !AutorizacionValida(cabeceraAutorizacion, config.ClavePrivada))
            {
                RegistrarError(new Exception("[Aplázame #663] Notificación con una autorización que no es la nuestra: rechazada (403)."));
                return RespuestaNotificacionAplazame.Codigo(403);
            }

            JObject datos;
            try
            {
                datos = JObject.Parse(cuerpo ?? string.Empty);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Aplázame #663] Notificación ilegible: {ex.Message}", ex));
                return RespuestaNotificacionAplazame.Codigo(400);
            }

            string mid = datos.Value<string>("mid")?.Trim();
            string status = datos.Value<string>("status")?.Trim().ToLowerInvariant();
            string motivo = datos.Value<string>("status_reason")?.Trim().ToLowerInvariant();
            long? importe = datos["total_amount"]?.Type == JTokenType.Integer ? datos.Value<long>("total_amount") : (long?)null;
            bool? sandbox = datos["sandbox"]?.Type == JTokenType.Boolean ? datos.Value<bool>("sandbox") : (bool?)null;

            if (string.IsNullOrWhiteSpace(mid))
            {
                RegistrarError(new Exception("[Aplázame #663] Notificación sin mid."));
                return RespuestaNotificacionAplazame.Codigo(400);
            }

            PagoTPV pago = await repositorio.LeerPorMid(mid).ConfigureAwait(false);
            if (pago == null)
            {
                RegistrarError(new Exception($"[Aplázame #663] Notificación de un mid que no conocemos: {mid} ({status}/{motivo})."));
                return RespuestaNotificacionAplazame.Codigo(404);
            }

            if (sandbox.HasValue && sandbox.Value != pago.Sandbox)
            {
                RegistrarError(new Exception($"[Aplázame #663] La notificación de {mid} dice sandbox={sandbox} y el cobro se abrió " +
                    $"con sandbox={pago.Sandbox}: rechazada."));
                return RespuestaNotificacionAplazame.Codigo(400);
            }

            string estado = pago.Estado?.Trim();
            bool importeExacto = importe.HasValue && importe.Value == ConstructorCheckoutAplazame.Centimos(pago.Importe);

            if (status == "pending" && motivo == "confirmation_required")
            {
                return await Confirmar(pago, estado, importeExacto, importe).ConfigureAwait(false);
            }

            if (status == "ok")
            {
                // Estado final: ya lo habíamos confirmado nosotros. Idempotente.
                if (!importeExacto)
                {
                    RegistrarError(new Exception($"[Aplázame #663] {mid}: Aplázame da por bueno un importe ({importe}) que no es el " +
                        $"del cobro ({pago.Importe:N2} EUR). Revisar en el panel de Aplázame."));
                }
                if (estado == Constantes.EstadosPagoTPV.PENDIENTE)
                {
                    // No nos llegó (o no contestamos) la confirmación, pero Aplázame lo da por cerrado
                    if (importeExacto && await repositorio.CambiarEstado(pago.Id, new[] { Constantes.EstadosPagoTPV.PENDIENTE },
                        Constantes.EstadosPagoTPV.AUTORIZADO, Ahora()).ConfigureAwait(false))
                    {
                        ProgramarAviso(pago.Id);
                    }
                }
                else if (estado == Constantes.EstadosPagoTPV.CANCELADO || estado == Constantes.EstadosPagoTPV.DENEGADO)
                {
                    RegistrarError(new Exception($"[Aplázame #663] {mid}: Aplázame lo da por pagado y aquí está {estado}. " +
                        "Revisarlo en el panel de Aplázame (puede que haya que cancelarlo allí)."));
                }
                return RespuestaNotificacionAplazame.Ok();
            }

            if (status == "ko")
            {
                await Rechazar(pago, estado, motivo).ConfigureAwait(false);
                return RespuestaNotificacionAplazame.Ok();
            }

            // pending + challenge_required (validación de identidad) u otro estado intermedio: nada que hacer todavía
            return RespuestaNotificacionAplazame.Ok();
        }

        private async Task<RespuestaNotificacionAplazame> Confirmar(PagoTPV pago, string estado, bool importeExacto, long? importe)
        {
            bool vivo = estado == Constantes.EstadosPagoTPV.PENDIENTE || estado == Constantes.EstadosPagoTPV.AUTORIZADO;
            if (!vivo)
            {
                // Cancelado (caducó, o el pedido no salió) o rechazado: no se confirma
                return RespuestaNotificacionAplazame.Ko();
            }
            if (!importeExacto)
            {
                // Como el módulo de PrestaShop: un importe distinto del que se cobró es fraude o error, nunca un pedido
                RegistrarError(new Exception($"[Aplázame #663] {pago.NumeroOrden}: Aplázame pide confirmar {importe} céntimos y el cobro " +
                    $"es de {pago.Importe:N2} EUR. NO se confirma."));
                _ = await repositorio.CambiarEstado(pago.Id, new[] { Constantes.EstadosPagoTPV.PENDIENTE },
                    Constantes.EstadosPagoTPV.DENEGADO).ConfigureAwait(false);
                return RespuestaNotificacionAplazame.Ko();
            }
            if (estado == Constantes.EstadosPagoTPV.AUTORIZADO)
            {
                return RespuestaNotificacionAplazame.Ok(); // reintento de Aplázame
            }
            bool confirmado = await repositorio.CambiarEstado(pago.Id, new[] { Constantes.EstadosPagoTPV.PENDIENTE },
                Constantes.EstadosPagoTPV.AUTORIZADO, Ahora()).ConfigureAwait(false);
            if (confirmado)
            {
                ProgramarAviso(pago.Id);
                return RespuestaNotificacionAplazame.Ok();
            }
            // Ha cambiado entre la lectura y la escritura: se decide con lo que hay ahora
            PagoTPV ahora = await repositorio.Leer(pago.Id).ConfigureAwait(false);
            return ahora?.Estado?.Trim() == Constantes.EstadosPagoTPV.AUTORIZADO
                ? RespuestaNotificacionAplazame.Ok()
                : RespuestaNotificacionAplazame.Ko();
        }

        private async Task Rechazar(PagoTPV pago, string estado, string motivo)
        {
            if (estado == Constantes.EstadosPagoTPV.CANCELADO || estado == Constantes.EstadosPagoTPV.DENEGADO)
            {
                return; // ya estaba: idempotente
            }
            bool tienePedido = TienePedido(pago);
            _ = await repositorio.CambiarEstado(pago.Id,
                new[] { Constantes.EstadosPagoTPV.PENDIENTE, Constantes.EstadosPagoTPV.AUTORIZADO },
                Constantes.EstadosPagoTPV.DENEGADO).ConfigureAwait(false);
            if (tienePedido || estado == Constantes.EstadosPagoTPV.AUTORIZADO)
            {
                string mensaje = $"[Aplázame #663] {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente {pago.Cliente?.Trim()}) pasa a " +
                    $"rechazado ({motivo}) DESPUÉS de haberlo confirmado" +
                    (tienePedido ? $", y el pedido {pago.Documento?.Trim()} ya existe con su prepago: NO SERVIRLO sin cobrarlo de otra forma." : ".");
                RegistrarError(new Exception(mensaje));
                if (tienePedido)
                {
                    EnviarCorreoInformatica($"Aplázame rechaza el cobro del pedido {pago.Documento?.Trim()}", mensaje);
                }
            }
        }

        private void ProgramarAviso(int idPago)
        {
            try
            {
                ProgramarAvisoAprobacion(idPago);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Aplázame #663] No se pudo programar el aviso de aprobación del cobro {idPago}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Compara la cabecera con «Bearer &lt;clave privada&gt;» en tiempo constante (sobre los hash, para no filtrar
        /// ni la longitud). Internal para los tests.
        /// </summary>
        internal static bool AutorizacionValida(string cabecera, string clavePrivada)
        {
            if (string.IsNullOrWhiteSpace(cabecera) || string.IsNullOrWhiteSpace(clavePrivada))
            {
                return false;
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] recibida = sha.ComputeHash(Encoding.UTF8.GetBytes(cabecera.Trim()));
                byte[] esperada = sha.ComputeHash(Encoding.UTF8.GetBytes("Bearer " + clavePrivada.Trim()));
                int diferencia = 0;
                for (int i = 0; i < esperada.Length; i++)
                {
                    diferencia |= recibida[i] ^ esperada[i];
                }
                return diferencia == 0;
            }
        }

        #endregion

        #region Push cuando la app no se ha enterado

        /// <summary>
        /// Si a los 2 minutos de aprobarse el cobro sigue sin pedido, la app no volvió con «ok» (volvió con «pending»
        /// porque Aplázame pidió validar la identidad, o se cerró): se le dice al cliente que ya puede terminar.
        /// </summary>
        public async Task AvisarAprobacionSiSigueSinPedido(int idPago)
        {
            PagoTPV pago = await repositorio.Leer(idPago).ConfigureAwait(false);
            if (!ProveedorCobroCarrito.EsAplazame(pago)
                || pago.Estado?.Trim() != Constantes.EstadosPagoTPV.AUTORIZADO
                || !string.IsNullOrWhiteSpace(pago.Documento)
                || string.IsNullOrWhiteSpace(pago.Cliente))
            {
                return; // ya tiene pedido (o se está creando), o ya no está aprobado
            }
            NotificacionPushDTO notificacion = new NotificacionPushDTO
            {
                Titulo = "Pago aprobado",
                Cuerpo = "Aplázame ha aprobado tu pago. Abre el carrito para terminar el pedido",
                Tipo = "carrito",
                Datos = new Dictionary<string, string>
                {
                    { "tipo", "carrito" },
                    { "idPago", pago.Id.ToString(CultureInfo.InvariantCulture) }
                }
            };
            try
            {
                _ = await CrearNotificaciones().EnviarACliente(pago.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    pago.Cliente.Trim(), notificacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Aplázame #663] No se pudo avisar por push al cliente {pago.Cliente?.Trim()} de que " +
                    $"el cobro {pago.NumeroOrden} está aprobado: {ex.Message}", ex));
            }
        }

        #endregion

        #region Cancelar

        public async Task<bool> Cancelar(int idPago, string motivo)
        {
            PagoTPV pago = await repositorio.Leer(idPago).ConfigureAwait(false);
            if (!ProveedorCobroCarrito.EsAplazame(pago))
            {
                return false;
            }
            string estado = pago.Estado?.Trim();
            if (estado == Constantes.EstadosPagoTPV.CANCELADO || estado == Constantes.EstadosPagoTPV.DENEGADO
                || estado == Constantes.EstadosPagoTPV.DEVUELTO)
            {
                return true; // no hay nada retenido
            }
            if (TienePedido(pago))
            {
                // Un cobro que ya paga un pedido no se cancela por aquí: sería dejar el pedido sin pagar
                RegistrarError(new Exception($"[Aplázame #663] Se ha pedido cancelar {pago.NumeroOrden} ({motivo}) pero paga el pedido " +
                    $"{pago.Documento?.Trim()}: NO se cancela."));
                return false;
            }

            if (estado == Constantes.EstadosPagoTPV.PENDIENTE)
            {
                // Aplázame no lo ha aprobado: allí no hay nada que cancelar (solo se cancela lo confirmado). Se cierra aquí
                // para que, si la aprobación llegara después, se le conteste «ko».
                bool cerrado = await repositorio.CambiarEstado(pago.Id, new[] { Constantes.EstadosPagoTPV.PENDIENTE },
                    Constantes.EstadosPagoTPV.CANCELADO).ConfigureAwait(false);
                if (cerrado)
                {
                    return true;
                }
                pago = await repositorio.Leer(idPago).ConfigureAwait(false);
                estado = pago?.Estado?.Trim();
                if (estado != Constantes.EstadosPagoTPV.AUTORIZADO)
                {
                    return estado == Constantes.EstadosPagoTPV.CANCELADO || estado == Constantes.EstadosPagoTPV.DENEGADO;
                }
            }

            try
            {
                await api.Cancelar(pago.NumeroOrden.Trim(), pago.Sandbox).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Aplázame #663] NO se ha podido cancelar {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                    $"{pago.Cliente?.Trim()}) después de que {motivo}: {ex.Message}. Cancelarlo en el panel de Aplázame.", ex));
                return false;
            }
            _ = await repositorio.CambiarEstado(pago.Id, new[] { Constantes.EstadosPagoTPV.AUTORIZADO },
                Constantes.EstadosPagoTPV.CANCELADO).ConfigureAwait(false);
            RegistrarError(new Exception($"[Aplázame #663] Cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                $"{pago.Cliente?.Trim()}) CANCELADO en Aplázame: {motivo}."));
            return true;
        }

        private static bool TienePedido(PagoTPV pago) => int.TryParse(pago?.Documento?.Trim(), out int numero) && numero > 0;

        #endregion

        #region Captura

        /// <summary>
        /// Aplázame exige capturar al enviar, y como mucho a los 15 días de confirmar. Se captura en cuanto TODAS las
        /// líneas del pedido están en albarán o factura, por lo servido (nunca más de lo autorizado), y una sola vez: antes
        /// de capturar se pregunta a Aplázame qué hay capturado, como hace el módulo de PrestaShop. A los 12 días sin
        /// capturar, correo a Informática.
        /// </summary>
        public async Task<ResultadoCapturasAplazame> CapturarServidos(DateTime ahora)
        {
            ResultadoCapturasAplazame resultado = new ResultadoCapturasAplazame();
            List<PagoTPV> cobros = await repositorio.LeerConfirmadosSinCapturar().ConfigureAwait(false);

            foreach (PagoTPV pago in cobros)
            {
                bool capturado = false;
                try
                {
                    capturado = await CapturarSiEstaServido(pago, ahora, resultado).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    resultado.Fallidos.Add(pago.Id);
                    RegistrarError(new Exception($"[Aplázame #663] No se ha podido capturar {pago.NumeroOrden} (pedido " +
                        $"{pago.Documento?.Trim()}): {ex.Message}", ex));
                }

                if (!capturado && pago.FechaAvisoCaptura == null && pago.FechaConfirmacion.HasValue
                    && pago.FechaConfirmacion.Value.AddDays(DIAS_AVISO_CAPTURA) <= ahora)
                {
                    try
                    {
                        AvisarCapturaAtrasada(pago);
                        await repositorio.MarcarAvisoCaptura(pago.Id, ahora).ConfigureAwait(false);
                        resultado.Avisados.Add(pago.Id);
                    }
                    catch (Exception ex)
                    {
                        RegistrarError(new Exception($"[Aplázame #663] No se pudo avisar de la captura atrasada de {pago.NumeroOrden}: {ex.Message}", ex));
                    }
                }
            }
            return resultado;
        }

        private async Task<bool> CapturarSiEstaServido(PagoTPV pago, DateTime ahora, ResultadoCapturasAplazame resultado)
        {
            int pedido = int.Parse(pago.Documento.Trim());
            string empresa = pago.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<LineaPedidoServida> lineas = await repositorio.LeerLineasPedido(empresa, pedido).ConfigureAwait(false);
            if (lineas == null || !lineas.Any() || lineas.Any(l => l.Estado < Constantes.EstadosLineaVenta.ALBARAN))
            {
                return false; // todavía no ha salido entero
            }

            string mid = pago.NumeroOrden.Trim();
            CapturasAplazame capturas = await api.ConsultarCapturas(mid, pago.Sandbox).ConfigureAwait(false);
            long yaCapturado = capturas?.Capturas?.Sum(c => c.Importe) ?? 0;
            if (yaCapturado > 0 || (capturas != null && capturas.PendienteDeCapturar <= 0))
            {
                // Ya está capturado (por otra pasada, o a mano en el panel): se apunta y no se vuelve a capturar
                DateTime fecha = capturas?.Capturas?.Where(c => c.Fecha.HasValue).Select(c => c.Fecha.Value.LocalDateTime)
                    .DefaultIfEmpty(ahora).Max() ?? ahora;
                await repositorio.GuardarCaptura(pago.Id, fecha, yaCapturado / 100M).ConfigureAwait(false);
                resultado.YaCapturados.Add(pago.Id);
                return true;
            }

            long autorizado = ConstructorCheckoutAplazame.Centimos(pago.Importe);
            long servido = ConstructorCheckoutAplazame.Centimos(RoundingHelper.DosDecimalesRound(lineas.Sum(l => l.Total)));
            long importe = Math.Min(Math.Min(servido, autorizado), capturas?.PendienteDeCapturar ?? autorizado);
            if (importe <= 0)
            {
                RegistrarError(new Exception($"[Aplázame #663] {mid}: el pedido {pedido} está servido pero no hay nada que capturar " +
                    $"(servido {servido / 100M:N2} EUR, autorizado {pago.Importe:N2} EUR). Revisarlo."));
                return false;
            }

            await api.Capturar(mid, importe, pago.Sandbox).ConfigureAwait(false);
            await repositorio.GuardarCaptura(pago.Id, ahora, importe / 100M).ConfigureAwait(false);
            resultado.Capturados.Add(pago.Id);

            if (importe < autorizado)
            {
                RegistrarError(new Exception($"[Aplázame #663] {mid}: capturados {importe / 100M:N2} EUR de {pago.Importe:N2} EUR autorizados " +
                    $"(el pedido {pedido} ha salido por menos). Revisar en el panel de Aplázame si hay que anular (void) el resto."));
            }
            return true;
        }

        private void AvisarCapturaAtrasada(PagoTPV pago)
        {
            string limite = pago.FechaConfirmacion.Value.AddDays(15).ToString("dd/MM/yyyy", Es);
            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Aplázame: el pedido {pago.Documento?.Trim()} sigue sin capturar",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                Body = $"<p>El cobro de Aplázame <b>{pago.NumeroOrden?.Trim()}</b> del pedido <b>{pago.Documento?.Trim()}</b> " +
                    $"(cliente {pago.Cliente?.Trim()}, {pago.Importe.ToString("N2", Es)} €) se confirmó el " +
                    $"{pago.FechaConfirmacion.Value.ToString("dd/MM/yyyy HH:mm", Es)} y todavía no se ha capturado.</p>" +
                    $"<p>Aplázame solo admite la captura hasta el <b>{limite}</b>. Se captura sola en cuanto todas las líneas del " +
                    "pedido estén en albarán o factura; si no va a salir a tiempo, hay que capturarlo o cancelarlo en el panel de Aplázame.</p>" +
                    (pago.Sandbox ? "<p>(Cobro de PRUEBAS, entorno sandbox.)</p>" : string.Empty)
            };
            mail.To.Add(Constantes.Correos.INFORMATICA);
            if (correo == null || !correo.EnviarCorreoSMTP(mail))
            {
                throw new InvalidOperationException("No se ha podido enviar el correo");
            }
        }

        private void EnviarCorreoInformatica(string asunto, string texto)
        {
            try
            {
                MailMessage mail = new MailMessage
                {
                    From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                    Subject = asunto,
                    IsBodyHtml = false,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8,
                    Body = texto
                };
                mail.To.Add(Constantes.Correos.INFORMATICA);
                _ = correo?.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Aplázame #663] No se pudo enviar el correo «{asunto}»: {ex.Message}", ex));
            }
        }

        #endregion

        /// <summary>El cliente HTTP se crea al usarlo: así un servicio sin claves no revienta hasta que de verdad llama a Aplázame.</summary>
        private class ClienteApiPerezoso : IClienteApiAplazame
        {
            private readonly Lazy<IClienteApiAplazame> cliente;

            public ClienteApiPerezoso(Func<IClienteApiAplazame> crear)
            {
                cliente = new Lazy<IClienteApiAplazame>(crear);
            }

            public Task<string> CrearCheckout(CheckoutAplazame checkout, bool sandbox) => cliente.Value.CrearCheckout(checkout, sandbox);
            public Task<CapturasAplazame> ConsultarCapturas(string mid, bool sandbox) => cliente.Value.ConsultarCapturas(mid, sandbox);
            public Task Capturar(string mid, long centimos, bool sandbox) => cliente.Value.Capturar(mid, centimos, sandbox);
            public Task Cancelar(string mid, bool sandbox) => cliente.Value.Cancelar(mid, sandbox);
        }
    }
}
