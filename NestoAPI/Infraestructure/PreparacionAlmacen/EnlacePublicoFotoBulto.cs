using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556 (Carlos, 30/09/26): la foto de un bulto se puede enseñar a quien no tiene usuario
    /// (el cliente que reclama, la agencia) con un enlace que no se puede adivinar. El enlace vale
    /// mientras exista la foto y va firmado con una clave propia, distinta de la de Azure: si hay que
    /// invalidar todos los enlaces repartidos, se cambia esta clave y las fotos siguen en su sitio.
    ///
    /// <para>El enlace no lleva la foto ni la dirección del almacenamiento: lleva el número del bulto
    /// y una firma. El API la comprueba y entonces da un acceso de unos minutos a la imagen.</para>
    /// </summary>
    public static class EnlacePublicoFotoBulto
    {
        public const string CLAVE_CONFIGURACION = "Almacen:ClaveEnlacesFotos";
        /// <summary>Con menos de esto la clave no protege nada: se trata como si no hubiera.</summary>
        public const int LONGITUD_MINIMA_CLAVE = 32;
        public const string RUTA = "api/Almacen/Fotos/";
        private const int CARACTERES_FIRMA = 40; // 160 bits

        public static bool ClaveValida(string clave) => !string.IsNullOrWhiteSpace(clave) && clave.Trim().Length >= LONGITUD_MINIMA_CLAVE;

        /// <summary>«{id}-{firma}». La firma depende también del IdCliente del bulto, que no se puede adivinar.</summary>
        public static string Token(string clave, int idBulto, Guid idCliente)
        {
            if (!ClaveValida(clave) || idBulto <= 0)
            {
                return null;
            }
            return idBulto.ToString(CultureInfo.InvariantCulture) + "-" + Firma(clave, idBulto, idCliente);
        }

        /// <summary>La ruta del enlace dentro del API (sin el servidor). Null si no hay clave.</summary>
        public static string Ruta(string clave, int idBulto, Guid idCliente)
        {
            string token = Token(clave, idBulto, idCliente);
            return token == null ? null : RUTA + token;
        }

        /// <summary>El número de bulto que dice el enlace. La firma se comprueba aparte, con el bulto ya leído.</summary>
        public static bool TryLeerId(string token, out int idBulto)
        {
            idBulto = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }
            int guion = token.IndexOf('-');
            return guion > 0
                && token.Length == guion + 1 + CARACTERES_FIRMA
                && int.TryParse(token.Substring(0, guion), NumberStyles.None, CultureInfo.InvariantCulture, out idBulto)
                && idBulto > 0;
        }

        public static bool EsValido(string clave, string token, int idBulto, Guid idCliente)
        {
            string esperado = Token(clave, idBulto, idCliente);
            return esperado != null && token != null && IgualesEnTiempoConstante(esperado, token.Trim().ToLowerInvariant());
        }

        // ===================================================================== NestoAPI#771: fotos del packing sin bulto

        /// <summary>NestoAPI#771: lo que distingue el token de una foto del packing («p17-…») del de un bulto («17-…»).</summary>
        public const string PREFIJO_FOTO_PACKING = "p";

        /// <summary>
        /// NestoAPI#771: «p{id}-{firma}» para una foto libre del packing (PreparacionFotosPacking). La misma clave que los bultos,
        /// con otro dominio en el texto firmado («packing|»): un token de bulto nunca vale para una foto del packing ni al revés.
        /// </summary>
        public static string TokenFotoPacking(string clave, int idFoto, Guid idCliente)
        {
            if (!ClaveValida(clave) || idFoto <= 0)
            {
                return null;
            }
            return PREFIJO_FOTO_PACKING + idFoto.ToString(CultureInfo.InvariantCulture) + "-" + Firma(clave, idFoto, idCliente, "packing|");
        }

        public static string RutaFotoPacking(string clave, int idFoto, Guid idCliente)
        {
            string token = TokenFotoPacking(clave, idFoto, idCliente);
            return token == null ? null : RUTA + token;
        }

        /// <summary>NestoAPI#771: ¿es el token de una foto del packing? Y, si lo es, su Id (la firma se comprueba aparte).</summary>
        public static bool TryLeerIdFotoPacking(string token, out int idFoto)
        {
            idFoto = 0;
            string limpio = token?.Trim();
            if (string.IsNullOrEmpty(limpio) || !limpio.StartsWith(PREFIJO_FOTO_PACKING, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return TryLeerId(limpio.Substring(PREFIJO_FOTO_PACKING.Length), out idFoto);
        }

        public static bool EsValidoFotoPacking(string clave, string token, int idFoto, Guid idCliente)
        {
            string esperado = TokenFotoPacking(clave, idFoto, idCliente);
            return esperado != null && token != null && IgualesEnTiempoConstante(esperado, token.Trim().ToLowerInvariant());
        }

        private static string Firma(string clave, int idBulto, Guid idCliente, string dominio = "bulto|")
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave.Trim())))
            {
                byte[] firma = hmac.ComputeHash(Encoding.UTF8.GetBytes(
                    dominio + idBulto.ToString(CultureInfo.InvariantCulture) + "|" + idCliente.ToString("N")));
                var texto = new StringBuilder(firma.Length * 2);
                foreach (byte b in firma)
                {
                    _ = texto.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return texto.ToString(0, CARACTERES_FIRMA);
            }
        }

        // Que comparar no tarde distinto según cuántos caracteres acierte quien prueba
        private static bool IgualesEnTiempoConstante(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            int diferencia = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diferencia |= a[i] ^ b[i];
            }
            return diferencia == 0;
        }
    }
}
