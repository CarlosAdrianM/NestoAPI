using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Error al comunicar con el WebService DataTrans DTX (transporte HTTP/SOAP).
    /// No representa un error de negocio de DataTrans (esos vienen en codError/respuesta
    /// dentro del XML), sino un fallo de conexión, HTTP o de parseo de la respuesta.
    /// </summary>
    public class DataTransException : Exception
    {
        public DataTransException(string message) : base(message) { }
        public DataTransException(string message, Exception inner) : base(message, inner) { }
    }

    public interface IClienteSoapDataTrans
    {
        /// <summary>
        /// Ejecuta una operación SOAP de DataTrans y devuelve la respuesta como XML.
        /// </summary>
        /// <param name="servicio">Servicio que cuelga de la URL base (Poblaciones, Envios, Estados, Etiquetas...).</param>
        /// <param name="operacion">Nombre de la operación (BuscarPoblacion, ConsultarEstados...). Se usa para
        /// el cuerpo <c>{operacion}TypeIn</c> y para el header <c>SOAPAction: urn:{operacion}</c>.</param>
        /// <param name="parametros">Parámetros de la operación (en el namespace mes), aparte de la autenticación.</param>
        Task<XDocument> EjecutarAsync(string servicio, string operacion, params XElement[] parametros);
    }

    /// <summary>
    /// Cliente SOAP genérico del WebService DataTrans DTX de Innovatrans. Monta el sobre SOAP con
    /// el bloque de autenticación (clave = MD5 de la contraseña), aplica el control de tasa, hace
    /// el POST con <c>SOAPAction: urn:{operacion}</c> y devuelve el XML de respuesta sin interpretar.
    /// Es el ladrillo de transporte: las operaciones concretas (lectura de estados/poblaciones,
    /// inserción de envíos, etc.) se construyen encima.
    /// </summary>
    public class ClienteSoapDataTrans : IClienteSoapDataTrans
    {
        internal static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
        internal static readonly XNamespace Mes = "http://messagein.dtx.sw";
        internal static readonly XNamespace Com = "http://complexType.dtx.sw";

        // El HttpClient y el control de tasa son compartidos por proceso: el límite (50/5min) es
        // por credencial, no por petición. Inyectables para los tests.
        private static readonly HttpClient ClienteCompartido = new HttpClient();
        private static readonly ControlTasaDataTrans ControlCompartido = new ControlTasaDataTrans();

        private readonly ConfiguracionInnovatrans _config;
        private readonly ControlTasaDataTrans _control;
        private readonly HttpClient _http;
        private readonly RegistroIntercambiosRemotos _registro;

        public ClienteSoapDataTrans(ConfiguracionInnovatrans config, ControlTasaDataTrans control = null,
            HttpClient http = null, RegistroIntercambiosRemotos registro = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _control = control ?? ControlCompartido;
            _http = http ?? ClienteCompartido;
            _registro = registro; // opcional: si es null, no se auditan los intercambios
        }

        public async Task<XDocument> EjecutarAsync(string servicio, string operacion, params XElement[] parametros)
        {
            if (string.IsNullOrWhiteSpace(servicio)) throw new ArgumentNullException(nameof(servicio));
            if (string.IsNullOrWhiteSpace(operacion)) throw new ArgumentNullException(nameof(operacion));

            string url = (_config.Url ?? string.Empty).TrimEnd('/') + "/" + servicio;
            string envelope = ConstruirEnvelope(operacion, parametros);

            await _control.EsperarTurnoAsync().ConfigureAwait(false);

            using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
                req.Headers.TryAddWithoutValidation("SOAPAction", "urn:" + operacion);

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    _registro?.Registrar(operacion, url, envelope, "[sin respuesta] " + ex.Message);
                    throw new DataTransException($"No se pudo conectar con DataTrans para {operacion}: {ex.Message}", ex);
                }

                using (resp)
                {
                    string cuerpo = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    _registro?.Registrar(operacion, url, envelope, cuerpo);
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new DataTransException($"DataTrans {operacion} devolvió HTTP {(int)resp.StatusCode}: {cuerpo}");
                    }
                    try
                    {
                        return XDocument.Parse(cuerpo);
                    }
                    catch (XmlException ex)
                    {
                        throw new DataTransException($"La respuesta de DataTrans {operacion} no es XML válido: {cuerpo}", ex);
                    }
                }
            }
        }

        private string ConstruirEnvelope(string operacion, XElement[] parametros)
        {
            XElement autenticacion = new XElement(Mes + "autenticacion",
                new XElement(Com + "identificador", _config.Credenciales?.Identificador),
                new XElement(Com + "empresa", _config.Credenciales?.Empresa),
                new XElement(Com + "email", _config.Credenciales?.Email),
                new XElement(Com + "clave", _config.Credenciales?.Clave));

            XElement cuerpoOperacion = new XElement(Mes + (operacion + "TypeIn"), autenticacion);
            if (parametros != null)
            {
                foreach (XElement p in parametros)
                {
                    cuerpoOperacion.Add(p);
                }
            }

            XElement envelope = new XElement(Soapenv + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", Soapenv.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "mes", Mes.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "com", Com.NamespaceName),
                new XElement(Soapenv + "Header"),
                new XElement(Soapenv + "Body", cuerpoOperacion));

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + envelope;
        }
    }
}
