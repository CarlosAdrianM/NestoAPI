using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#864: Aplázame como PRIMERA implementación de <see cref="IProveedorPagoAPlazos"/>. Todo lo que es de Aplázame en
    /// el pago a plazos de un pedido está aquí (y en lo que ya había de #663: <see cref="IClienteApiAplazame"/>, el
    /// constructor del checkout, las claves y los límites de <see cref="ConfiguracionAplazame"/>): el núcleo no lo conoce.
    /// <list type="bullet">
    /// <item><description>Solicitud = un checkout (<c>POST /checkout</c>); nuestra referencia es su <c>order.id</c> (mid) y su
    /// id, la referencia externa.</description></item>
    /// <item><description>El cliente lo termina en <c>aplazame/checkout.html</c> (la misma página que la app de #663): desde la
    /// app vuelve por <c>nestotiendas://aplazame/…</c>; por enlace (<c>volver=web</c>) el resultado se le enseña en la página.</description></item>
    /// <item><description>Notificación: <c>Authorization: Bearer &lt;clave privada&gt;</c>; <c>pending</c> +
    /// <c>confirmation_required</c> pide confirmar (se contesta <c>{"status":"ok"|"ko"}</c>); <c>ok</c> / <c>ko</c> son el final.</description></item>
    /// <item><description>Hay que capturar al enviar, como mucho 15 días después de confirmar.</description></item>
    /// </list>
    /// El interruptor de la app (AplazameApp) NO cuenta aquí: este canal tiene el suyo (ConfiguracionPagoAPlazos).
    /// </summary>
    public class ProveedorPagoAPlazosAplazame : IProveedorPagoAPlazos
    {
        public const string CODIGO = "Aplazame";

        /// <summary>Aplázame exige capturar como mucho 15 días después de confirmar.</summary>
        internal const int DIAS_PLAZO_CAPTURA = 15;

        /// <summary>A lo que se le añade ok / pending / ko / dismiss cuando el cliente paga por enlace (en el navegador).</summary>
        internal const string PREFIJO_RETORNO_WEB = ServicioCobrosAplazame.URL_PAGINA_CHECKOUT + "?volver=web&resultado=";

        private readonly IClienteApiAplazame api;
        private readonly Func<ConfiguracionAplazame> configuracion;

        public ProveedorPagoAPlazosAplazame(IClienteApiAplazame api, Func<ConfiguracionAplazame> configuracion)
        {
            this.api = api;
            this.configuracion = configuracion;
        }

        /// <summary>El de verdad. No toca la red ni la base de datos hasta que se usa.</summary>
        public static ProveedorPagoAPlazosAplazame CrearPorDefecto()
        {
            Func<ConfiguracionAplazame> leer = () => ConfiguracionAplazame.Leer(new LectorParametrosUsuario());
            return new ProveedorPagoAPlazosAplazame(
                new ServicioCobrosAplazame.ClienteApiPerezoso(() => new ClienteApiAplazame(leer().ClavePrivada)),
                leer);
        }

        public string Codigo => CODIGO;

        public string Nombre => "Aplázame";

        public CondicionesProveedorPagoAPlazos Condiciones()
        {
            ConfiguracionAplazame config = configuracion();
            return new CondicionesProveedorPagoAPlazos
            {
                Operativo = config != null && config.TieneClaves,
                // Los límites de nuestra cuenta en Aplázame (los mismos parámetros de #663)
                ImporteMinimo = config?.ImporteMinimo,
                ImporteMaximo = config?.ImporteMaximo,
                DiasPlazoCaptura = DIAS_PLAZO_CAPTURA,
                // Como los pedidos de PrestaShop y de la app pagados con Aplázame: la conciliación de Bancos no cambia
                CuentaPrepago = ProveedorCobroCarrito.Aplazame.CuentaPrepago,
                PrefijoConceptoPrepago = ProveedorCobroCarrito.Aplazame.PrefijoConceptoPrepago
            };
        }

        public async Task<string> CrearSolicitud(PeticionSolicitudProveedor peticion)
        {
            if (peticion == null || peticion.Importe <= 0)
            {
                throw new ArgumentException("El importe que se financia con Aplázame debe ser mayor que cero");
            }
            if (string.IsNullOrWhiteSpace(peticion.Correo))
            {
                throw new ArgumentException("Aplázame necesita el correo del cliente");
            }
            bool enApp = CanalesPagoAPlazos.Normalizar(peticion.Canal) == CanalesPagoAPlazos.APP;
            CheckoutAplazame checkout = ConstructorCheckoutAplazame.Construir(
                peticion.Referencia, peticion.Correo, peticion.Cliente,
                Direccion(peticion.Facturacion), Direccion(peticion.Envio),
                peticion.RecogidaEnTienda ? "Recogida en tienda" : "Envío", peticion.RecogidaEnTienda,
                (peticion.Lineas ?? new System.Collections.Generic.List<LineaPagoAPlazos>()).Where(l => l != null).Select(l => new LineaCheckout
                {
                    EsProducto = l.EsProducto,
                    Producto = l.Producto,
                    Texto = l.Texto,
                    Cantidad = l.Cantidad,
                    BaseImponible = l.BaseImponible,
                    Impuestos = l.Impuestos
                }),
                peticion.Importe, peticion.UrlNotificacion,
                enApp ? ServicioCobrosAplazame.ESQUEMA_APP : PREFIJO_RETORNO_WEB);
            return await api.CrearCheckout(checkout, peticion.Pruebas).ConfigureAwait(false);
        }

        private static DireccionCheckout Direccion(DireccionPagoAPlazos origen) => origen == null ? null : new DireccionCheckout
        {
            Nombre = origen.Nombre,
            Direccion = origen.Direccion,
            CodigoPostal = origen.CodigoPostal,
            Poblacion = origen.Poblacion,
            Provincia = origen.Provincia,
            Pais = origen.Pais,
            Telefono = origen.Telefono
        };

        public string UrlPago(ReferenciaSolicitudProveedor solicitud, string canal)
        {
            string url = ServicioCobrosAplazame.UrlCheckout(solicitud?.ReferenciaExterna, configuracion()?.ClavePublica, solicitud != null && solicitud.Pruebas);
            return CanalesPagoAPlazos.Normalizar(canal) == CanalesPagoAPlazos.APP ? url : url + "&volver=web";
        }

        public NotificacionPagoAPlazos InterpretarNotificacion(string cabeceraAutorizacion, string cuerpo)
        {
            string clavePrivada = configuracion()?.ClavePrivada;
            if (string.IsNullOrWhiteSpace(clavePrivada) || !ServicioCobrosAplazame.AutorizacionValida(cabeceraAutorizacion, clavePrivada))
            {
                return new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.NoAutorizada };
            }
            JObject datos;
            try
            {
                datos = JObject.Parse(cuerpo ?? string.Empty);
            }
            catch (Exception)
            {
                return new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.Ilegible };
            }
            string mid = Texto(datos, "mid");
            if (string.IsNullOrWhiteSpace(mid))
            {
                return new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.Ilegible };
            }
            string status = Texto(datos, "status")?.ToLowerInvariant();
            string motivo = Texto(datos, "status_reason")?.ToLowerInvariant();
            TipoNotificacionPagoAPlazos tipo =
                status == "pending" && motivo == "confirmation_required" ? TipoNotificacionPagoAPlazos.PideConfirmacion
                : status == "ok" ? TipoNotificacionPagoAPlazos.Aprobada
                : status == "ko" ? TipoNotificacionPagoAPlazos.Rechazada
                : TipoNotificacionPagoAPlazos.Intermedia; // pending + challenge_required (validación de identidad) y demás
            return new NotificacionPagoAPlazos
            {
                Tipo = tipo,
                Referencia = mid,
                // Aplázame da los importes en céntimos
                Importe = datos["total_amount"]?.Type == JTokenType.Integer ? datos.Value<long>("total_amount") / 100M : (decimal?)null,
                Pruebas = datos["sandbox"]?.Type == JTokenType.Boolean ? datos.Value<bool>("sandbox") : (bool?)null,
                Motivo = motivo
            };
        }

        private static string Texto(JObject datos, string campo)
        {
            JToken valor = datos[campo];
            return valor == null || valor.Type == JTokenType.Null || valor is JContainer ? null : valor.ToString().Trim();
        }

        public RespuestaNotificacionProveedor ResponderNotificacion(DecisionNotificacionPagoAPlazos decision)
        {
            switch (decision)
            {
                case DecisionNotificacionPagoAPlazos.Aceptar:
                    return new RespuestaNotificacionProveedor { CodigoHttp = 200, Cuerpo = "{\"status\":\"ok\"}" };
                case DecisionNotificacionPagoAPlazos.Rechazar:
                    return new RespuestaNotificacionProveedor { CodigoHttp = 200, Cuerpo = "{\"status\":\"ko\"}" };
                case DecisionNotificacionPagoAPlazos.NoAutorizada:
                    return new RespuestaNotificacionProveedor { CodigoHttp = 403 };
                case DecisionNotificacionPagoAPlazos.Desconocida:
                    return new RespuestaNotificacionProveedor { CodigoHttp = 404 };
                case DecisionNotificacionPagoAPlazos.Reintentar:
                    // Con un 500 Aplázame reintenta, que es lo que se quiere si algo ha fallado aquí
                    return new RespuestaNotificacionProveedor { CodigoHttp = 500 };
                default:
                    return new RespuestaNotificacionProveedor { CodigoHttp = 400 };
            }
        }

        public async Task<EstadoCapturaProveedor> ConsultarCaptura(ReferenciaSolicitudProveedor solicitud)
        {
            CapturasAplazame capturas = await api.ConsultarCapturas(solicitud.Referencia, solicitud.Pruebas).ConfigureAwait(false);
            if (capturas == null)
            {
                return new EstadoCapturaProveedor();
            }
            return new EstadoCapturaProveedor
            {
                Capturado = (capturas.Capturas?.Sum(c => c.Importe) ?? 0) / 100M,
                PendienteDeCapturar = capturas.PendienteDeCapturar / 100M,
                FechaUltimaCaptura = capturas.Capturas?.Where(c => c.Fecha.HasValue).Select(c => (DateTime?)c.Fecha.Value.LocalDateTime).Max()
            };
        }

        public Task Capturar(ReferenciaSolicitudProveedor solicitud, decimal importe)
            => api.Capturar(solicitud.Referencia, ConstructorCheckoutAplazame.Centimos(importe), solicitud.Pruebas);

        public Task Cancelar(ReferenciaSolicitudProveedor solicitud)
            => api.Cancelar(solicitud.Referencia, solicitud.Pruebas);

        public Task Reembolsar(ReferenciaSolicitudProveedor solicitud, decimal importe)
            => api.Reembolsar(solicitud.Referencia, ConstructorCheckoutAplazame.Centimos(importe), solicitud.Pruebas);
    }
}
