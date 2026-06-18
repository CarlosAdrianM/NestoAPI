using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Configuración del WebService DataTrans DTX de Innovatrans. El <c>identificador</c> (nº de
    /// cliente) se guarda en <c>AgenciaTransporte.Identificador</c> (como las demás agencias) y lo
    /// pasa el llamante. El resto no sensible (URL, empresa DTX, email) se lee de Web.config; la
    /// contraseña, de secretos.config (clave "InnovatransPassword"), y de ella se deriva la
    /// <c>clave</c> SOAP = MD5(password).
    /// </summary>
    public class ConfiguracionInnovatrans
    {
        public string Url { get; }
        public CredencialesDataTrans Credenciales { get; }

        public ConfiguracionInnovatrans(string identificador)
        {
            Url = ConfigurationManager.AppSettings["Innovatrans:Url"];
            Credenciales = new CredencialesDataTrans
            {
                Identificador = identificador,
                Empresa = ConfigurationManager.AppSettings["Innovatrans:Empresa"],
                Email = ConfigurationManager.AppSettings["Innovatrans:Email"],
                Clave = CalcularClave(ConfigurationManager.AppSettings["InnovatransPassword"])
            };
        }

        /// <summary>
        /// Constructor con valores explícitos (tests y composición fuera de Web.config). La
        /// <c>clave</c> de <paramref name="credenciales"/> ya debe ser el MD5 de la contraseña.
        /// </summary>
        public ConfiguracionInnovatrans(string url, CredencialesDataTrans credenciales)
        {
            Url = url;
            Credenciales = credenciales;
        }

        /// <summary>
        /// Deriva la <c>clave</c> de autenticación de DataTrans: MD5 de la contraseña, en
        /// hexadecimal de 32 caracteres en minúsculas. Devuelve cadena vacía si no hay contraseña.
        /// </summary>
        internal static string CalcularClave(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return string.Empty;
            }
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
