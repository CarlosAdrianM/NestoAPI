using System;
using System.Configuration;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.PreparacionAlmacen;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#744: el enlace público de seguimiento de un envío del reparto propio (agencia 14):
    /// <c>https://api.nuevavision.es/seguimiento/{token}</c>.
    ///
    /// <para>El token es opaco y estable SIN tabla: Base64Url de [nº de envío (4 bytes)][HMAC-SHA256 truncado a 128 bits].
    /// La firma depende de una clave secreta del Web.config, así que no se puede fabricar el token de otro envío
    /// (no es enumerable) aunque el número del envío viaje dentro. El mismo envío da siempre el mismo enlace, que
    /// se puede mandar por correo, a la app o a Nesto sin guardarlo en ninguna parte.</para>
    ///
    /// <para>Clave: <see cref="CLAVE_CONFIGURACION"/> si está; si no, la de los enlaces de las fotos de los bultos
    /// (<see cref="EnlacePublicoFotoBulto.CLAVE_CONFIGURACION"/>, ya en secretos.config), separada por dominio (el texto
    /// firmado empieza por «seguimiento-rp|», nunca coincide con el de las fotos). Para invalidar todos los enlaces
    /// de seguimiento sin tocar los de las fotos, basta con dar de alta la clave propia.</para>
    /// </summary>
    public static class EnlaceSeguimientoRepartoPropio
    {
        public const string CLAVE_CONFIGURACION = "Agencias:ClaveSeguimientoRepartoPropio";
        public const string RUTA = "seguimiento/";
        private const string DOMINIO = "seguimiento-rp|";
        private const int BYTES_FIRMA = 16; // 128 bits
        private const int BYTES_TOKEN = 4 + BYTES_FIRMA;

        /// <summary>La clave con la que se firma (sustituible en los tests). Null si no hay ninguna válida.</summary>
        internal static Func<string> LeerClave { get; set; } = ClaveConfigurada;

        /// <summary>La URL pública de la API, sin barra final (sustituible en los tests).</summary>
        internal static Func<string> LeerUrlBase { get; set; } = () => MiniaturasChequeRegalo.UrlPublicaApi;

        internal static string ClaveConfigurada()
        {
            string propia = ConfigurationManager.AppSettings[CLAVE_CONFIGURACION];
            if (EnlacePublicoFotoBulto.ClaveValida(propia))
            {
                return propia.Trim();
            }
            string fotos = ConfigurationManager.AppSettings[EnlacePublicoFotoBulto.CLAVE_CONFIGURACION];
            return EnlacePublicoFotoBulto.ClaveValida(fotos) ? fotos.Trim() : null;
        }

        /// <summary>El token del envío con la clave configurada. Null si no hay clave o el envío no es válido.</summary>
        public static string Token(int envio) => Token(LeerClave(), envio);

        public static string Token(string clave, int envio)
        {
            if (!EnlacePublicoFotoBulto.ClaveValida(clave) || envio <= 0)
            {
                return null;
            }
            byte[] datos = new byte[BYTES_TOKEN];
            datos[0] = (byte)(envio >> 24);
            datos[1] = (byte)(envio >> 16);
            datos[2] = (byte)(envio >> 8);
            datos[3] = (byte)envio;
            Buffer.BlockCopy(Firma(clave, envio), 0, datos, 4, BYTES_FIRMA);
            return Base64Url(datos);
        }

        /// <summary>La URL absoluta de la página de seguimiento del envío, o null si no hay clave.</summary>
        public static string Url(int envio)
        {
            string token = Token(envio);
            return token == null ? null : (LeerUrlBase() ?? string.Empty).Trim().TrimEnd('/') + "/" + RUTA + token;
        }

        /// <summary>El envío del token, solo si la firma es buena (con la clave configurada).</summary>
        public static bool TryLeerEnvio(string token, out int envio) => TryLeerEnvio(LeerClave(), token, out envio);

        public static bool TryLeerEnvio(string clave, string token, out int envio)
        {
            envio = 0;
            if (!EnlacePublicoFotoBulto.ClaveValida(clave) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }
            byte[] datos = DeBase64Url(token.Trim());
            if (datos == null || datos.Length != BYTES_TOKEN)
            {
                return false;
            }
            int numero = (datos[0] << 24) | (datos[1] << 16) | (datos[2] << 8) | datos[3];
            if (numero <= 0)
            {
                return false;
            }
            // En tiempo constante: que comparar no tarde distinto según cuántos bytes acierte quien prueba
            byte[] esperada = Firma(clave, numero);
            int diferencia = 0;
            for (int i = 0; i < BYTES_FIRMA; i++)
            {
                diferencia |= esperada[i] ^ datos[4 + i];
            }
            if (diferencia != 0)
            {
                return false;
            }
            envio = numero;
            return true;
        }

        private static byte[] Firma(string clave, int envio)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave.Trim())))
            {
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(DOMINIO + envio.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static string Base64Url(byte[] datos)
            => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] DeBase64Url(string texto)
        {
            foreach (char c in texto)
            {
                bool valido = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!valido)
                {
                    return null;
                }
            }
            string base64 = texto.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 1: return null;
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
