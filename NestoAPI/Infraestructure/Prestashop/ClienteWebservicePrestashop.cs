using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Prestashop
{
    /// <summary>
    /// NestoAPI#614: el webservice de la tienda PrestaShop de Nueva Visión. Rutas relativas a la URL del webservice
    /// («cart_rules/12», «customers?filter[email]=[…]»); devuelve el XML tal cual.
    /// </summary>
    public interface IClienteWebservicePrestashop
    {
        Task<string> Leer(string recurso);
        /// <summary>POST: crea el recurso y devuelve el XML con lo creado (lleva el id).</summary>
        Task<string> Crear(string recurso, string xml);
        /// <summary>PUT: el recurso entero, con su id.</summary>
        Task<string> Modificar(string recurso, string xml);
    }

    /// <summary>La tienda ha contestado con error (o no ha contestado: <see cref="Codigo"/> null).</summary>
    public class PrestashopWebserviceException : Exception
    {
        public PrestashopWebserviceException(string mensaje, HttpStatusCode? codigo = null, Exception interna = null) : base(mensaje, interna)
        {
            Codigo = codigo;
        }

        public HttpStatusCode? Codigo { get; }
    }

    /// <summary>
    /// NestoAPI#614: cliente del webservice de PrestaShop. Es el mismo acceso que ya usaba la API para leer las fichas de la tienda
    /// (<c>ProductoDTO.LeerUrlTiendaOnline</c>, correos post-compra #74): la clave <c>PrestashopWebserviceKeyNV</c> de
    /// secretos.config como usuario de la autenticación básica. Sin la clave en la URL, así que las rutas se pueden escribir en
    /// los mensajes de error y en ELMAH.
    /// </summary>
    public class ClienteWebservicePrestashop : IClienteWebservicePrestashop
    {
        /// <summary>La clave del webservice (secretos.config), la misma de la lectura de fichas.</summary>
        public const string CLAVE_WEBSERVICE = "PrestashopWebserviceKeyNV";
        public const string CLAVE_URL = "Prestashop:UrlWebservice";
        public const string URL_POR_DEFECTO = "https://www.productosdeesteticaypeluqueriaprofesional.com/api";
        public static readonly TimeSpan TIEMPO_POR_DEFECTO = TimeSpan.FromSeconds(15);

        private readonly Func<HttpMessageHandler> crearHandler;
        private readonly string urlBase;
        private readonly TimeSpan tiempo;

        public ClienteWebservicePrestashop(TimeSpan? tiempoMaximo = null) : this(null, null, tiempoMaximo)
        {
        }

        internal ClienteWebservicePrestashop(Func<HttpMessageHandler> crearHandler, string urlBase, TimeSpan? tiempoMaximo)
        {
            this.crearHandler = crearHandler ?? (() => CrearHandler(LeerClave()));
            string url = urlBase ?? ConfigurationManager.AppSettings[CLAVE_URL];
            this.urlBase = (string.IsNullOrWhiteSpace(url) ? URL_POR_DEFECTO : url.Trim()).TrimEnd('/');
            tiempo = tiempoMaximo ?? TIEMPO_POR_DEFECTO;
        }

        /// <summary>La clave como usuario de la autenticación básica (así la quiere PrestaShop), mandada desde la primera petición.</summary>
        public static HttpClientHandler CrearHandler(string clave) =>
            new HttpClientHandler { Credentials = new NetworkCredential { UserName = clave }, PreAuthenticate = true };

        private static string LeerClave()
        {
            string clave = ConfigurationManager.AppSettings[CLAVE_WEBSERVICE];
            if (string.IsNullOrWhiteSpace(clave))
            {
                throw new PrestashopWebserviceException($"Falta la clave {CLAVE_WEBSERVICE} del webservice de PrestaShop en secretos.config.");
            }
            return clave.Trim();
        }

        public Task<string> Leer(string recurso) => Enviar(HttpMethod.Get, recurso, null);

        public Task<string> Crear(string recurso, string xml) => Enviar(HttpMethod.Post, recurso, xml);

        public Task<string> Modificar(string recurso, string xml) => Enviar(HttpMethod.Put, recurso, xml);

        private async Task<string> Enviar(HttpMethod metodo, string recurso, string xml)
        {
            string ruta = (recurso ?? string.Empty).TrimStart('/');
            using (var cliente = new HttpClient(crearHandler(), disposeHandler: true) { Timeout = tiempo })
            using (var peticion = new HttpRequestMessage(metodo, $"{urlBase}/{ruta}"))
            {
                if (xml != null)
                {
                    peticion.Content = new StringContent(xml, Encoding.UTF8, "application/xml");
                }
                HttpResponseMessage respuesta;
                try
                {
                    respuesta = await cliente.SendAsync(peticion).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    throw new PrestashopWebserviceException($"La tienda PrestaShop no ha contestado al {metodo} {RutaSinFiltros(ruta)}: {ex.Message}", null, ex);
                }
                using (respuesta)
                {
                    string cuerpo = respuesta.Content == null ? string.Empty : await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!respuesta.IsSuccessStatusCode)
                    {
                        string detalle = MensajeDeError(cuerpo);
                        throw new PrestashopWebserviceException(
                            $"La tienda PrestaShop ha respondido {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}) al {metodo} {RutaSinFiltros(ruta)}" +
                            (string.IsNullOrEmpty(detalle) ? "." : $": {detalle}"), respuesta.StatusCode);
                    }
                    return cuerpo;
                }
            }
        }

        /// <summary>Solo el recurso («cart_rules/12»), sin los filtros: llevan el correo del cliente.</summary>
        internal static string RutaSinFiltros(string ruta)
        {
            string r = ruta ?? string.Empty;
            int interrogacion = r.IndexOf('?');
            return interrogacion >= 0 ? r.Substring(0, interrogacion) : r;
        }

        /// <summary>El primer mensaje de &lt;errors&gt; del XML de error de PrestaShop (vacío si no lo trae).</summary>
        internal static string MensajeDeError(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return string.Empty;
            }
            try
            {
                XElement error = XDocument.Parse(xml.Trim()).Root?.Element("errors")?.Element("error");
                string mensaje = error?.Element("message")?.Value?.Trim();
                string codigo = error?.Element("code")?.Value?.Trim();
                return string.IsNullOrEmpty(mensaje) ? string.Empty : string.IsNullOrEmpty(codigo) ? mensaje : $"{mensaje} (código {codigo})";
            }
            catch (Exception)
            {
                return xml.Length > 200 ? xml.Substring(0, 200) : xml;
            }
        }
    }
}
