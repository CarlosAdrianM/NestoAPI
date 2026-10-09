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
            MaxPaginasSeguimiento = LeerMaxPaginasSeguimiento(ConfigurationManager.AppSettings[CLAVE_MAX_PAGINAS_SEGUIMIENTO]);
        }

        /// <summary>
        /// NestoAPI#602: clave OPCIONAL de Web.config con el tope de páginas (de 50 envíos) por consulta del
        /// listado de seguimiento por fechas. No hace falta ponerla: sin ella vale
        /// <see cref="MAX_PAGINAS_SEGUIMIENTO_POR_DEFECTO"/> (y lo que se añade a mano en el servidor se pierde
        /// al publicar).
        /// </summary>
        public const string CLAVE_MAX_PAGINAS_SEGUIMIENTO = "CTT:MaxPaginasSeguimiento";

        /// <summary>
        /// NestoAPI#602: 10 páginas x 50 = 500 envíos, unas dos semanas y media de CTT (~30 envíos al día).
        /// Cada página es una llamada a la API de CTT, que corta por cupo (429); antes eran hasta 40 y el
        /// poll pedía todas las del rango en cada pasada (~7 cada media hora).
        /// </summary>
        public const int MAX_PAGINAS_SEGUIMIENTO_POR_DEFECTO = 10;

        /// <summary>Tope de páginas del listado de seguimiento por consulta (ver <see cref="CLAVE_MAX_PAGINAS_SEGUIMIENTO"/>).</summary>
        public int MaxPaginasSeguimiento { get; set; } = MAX_PAGINAS_SEGUIMIENTO_POR_DEFECTO;

        /// <summary>El valor de la clave si es un entero positivo (sin pasar de <see cref="AgenciaRemotaCTT.MAX_PAGINAS"/>); si no, el de por defecto.</summary>
        internal static int LeerMaxPaginasSeguimiento(string valor)
        {
            return int.TryParse(valor?.Trim(), out int paginas) && paginas > 0
                ? Math.Min(paginas, AgenciaRemotaCTT.MAX_PAGINAS)
                : MAX_PAGINAS_SEGUIMIENTO_POR_DEFECTO;
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

        /// <summary>
        /// NestoAPI#494: interruptor de las recogidas y retornos por CTT (parámetro de usuario
        /// CTTRetornosActivos de la empresa por defecto, «(defecto)»; lo pone el perfil al componer la
        /// agencia). Apagado, un envío de CTT con tipo de retorno se RECHAZA con un mensaje claro: nunca
        /// sale como envío normal (el paquete iría al cliente en vez de recogerse en su casa).
        /// </summary>
        public bool RetornosActivos { get; set; }
    }
}
