using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// Remitente fijo (nuestro almacén de Algete) para los envíos de CTT. Se configura en Web.config
    /// (claves CTT:Remitente:*), como el de Innovatrans.
    /// </summary>
    public class RemitenteCTT
    {
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Direccion { get; set; }
        public string Email { get; set; }
        public string Pais { get; set; } = "ES";
    }

    /// <summary>
    /// NestoAPI#493: configuración de la API REST de CTT Express. Lo NO sensible va en Web.config
    /// (CTT:Url = base de los endpoints, hoy https://api-test.cttexpress.com/integrations/ y en
    /// producción https://api.cttexpress.com/integrations/; CTT:ClientCenterCode = código de centro
    /// de cliente que nos dio CTT; CTT:Remitente:*). Las credenciales OAuth2 (client_credentials) van
    /// en secretos.config como "CTTClientId" y "CTTClientSecret".
    /// </summary>
    public class ConfiguracionCTT
    {
        public const string SCOPE = "urn:com:ctt-express:integration-clients:scopes:common/ALL";

        public string UrlBase { get; }
        public string ClientId { get; }
        public string ClientSecret { get; }
        public string ClientCenterCode { get; }
        public RemitenteCTT Remitente { get; }

        public ConfiguracionCTT()
            : this(
                ConfigurationManager.AppSettings["CTT:Url"],
                ConfigurationManager.AppSettings["CTTClientId"],
                ConfigurationManager.AppSettings["CTTClientSecret"],
                ConfigurationManager.AppSettings["CTT:ClientCenterCode"],
                new RemitenteCTT
                {
                    Nombre = ConfigurationManager.AppSettings["CTT:Remitente:Nombre"],
                    Telefono = ConfigurationManager.AppSettings["CTT:Remitente:Telefono"],
                    CodigoPostal = ConfigurationManager.AppSettings["CTT:Remitente:CodigoPostal"],
                    Poblacion = ConfigurationManager.AppSettings["CTT:Remitente:Poblacion"],
                    Direccion = ConfigurationManager.AppSettings["CTT:Remitente:Direccion"],
                    Email = ConfigurationManager.AppSettings["CTT:Remitente:Email"]
                })
        {
        }

        public ConfiguracionCTT(string urlBase, string clientId, string clientSecret, string clientCenterCode, RemitenteCTT remitente)
        {
            if (string.IsNullOrWhiteSpace(urlBase)) throw new ArgumentException("Falta CTT:Url en la configuración.", nameof(urlBase));
            UrlBase = urlBase.Trim().EndsWith("/") ? urlBase.Trim() : urlBase.Trim() + "/";
            ClientId = clientId?.Trim();
            ClientSecret = clientSecret?.Trim();
            ClientCenterCode = clientCenterCode?.Trim();
            Remitente = remitente ?? new RemitenteCTT();
        }

        public bool TieneCredenciales => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }
}
