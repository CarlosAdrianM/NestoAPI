using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Configuración del WebService DataTrans DTX de Innovatrans. Lo NO sensible (URL,
    /// identificador, empresa, email) se lee de Web.config; la contraseña, de secretos.config
    /// (clave "InnovatransPassword"), y de ella se deriva la <c>clave</c> SOAP = MD5(password).
    /// </summary>
    public class ConfiguracionInnovatrans
    {
        public string Url { get; }
        public CredencialesDataTrans Credenciales { get; }

        public ConfiguracionInnovatrans()
        {
            Url = ConfigurationManager.AppSettings["Innovatrans:Url"];
            Credenciales = new CredencialesDataTrans
            {
                Identificador = ConfigurationManager.AppSettings["Innovatrans:Identificador"],
                Empresa = ConfigurationManager.AppSettings["Innovatrans:Empresa"],
                Email = ConfigurationManager.AppSettings["Innovatrans:Email"],
                Clave = CalcularClave(ConfigurationManager.AppSettings["InnovatransPassword"])
            };
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
