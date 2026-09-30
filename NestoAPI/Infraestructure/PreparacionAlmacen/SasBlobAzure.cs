using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: enlaces firmados (SAS de servicio) para un blob de Azure Storage, hechos a mano
    /// con HMAC-SHA256 en vez de con el SDK de Azure. El SDK arrastra media docena de dependencias
    /// (Azure.Core, System.Text.Json, System.Memory…) con sus redirecciones de ensamblado, y en un
    /// proyecto web de .NET Framework eso es justo lo que rompe un despliegue. Lo que necesitamos es
    /// una firma: caben en esta clase la subida (con un enlace de escritura) y el enlace de lectura
    /// de 15 minutos que se manda al cliente.
    ///
    /// <para>Formato de la cadena a firmar: el de la versión 2020-12-06 del servicio para un blob
    /// (sr=b). Si se cambia la versión hay que revisar los campos: cada versión añade alguno.</para>
    /// </summary>
    public static class SasBlobAzure
    {
        public const string VERSION_SERVICIO = "2020-12-06";
        public const string PERMISO_LECTURA = "r";
        /// <summary>Crear y escribir: lo justo para subir un blob nuevo.</summary>
        public const string PERMISO_SUBIDA = "cw";

        public class CuentaAlmacenamiento
        {
            public string Nombre { get; set; }
            /// <summary>La clave de la cuenta, en Base64 tal como la da el portal.</summary>
            public string Clave { get; set; }
            public string SufijoEndpoint { get; set; } = "core.windows.net";

            public string UrlBase => $"https://{Nombre}.blob.{SufijoEndpoint}";
        }

        /// <summary>
        /// Lee la cadena de conexión del portal (DefaultEndpointsProtocol=https;AccountName=…;
        /// AccountKey=…;EndpointSuffix=…). Null si está vacía o le falta la cuenta o la clave.
        /// </summary>
        public static CuentaAlmacenamiento LeerCadenaDeConexion(string cadena)
        {
            if (string.IsNullOrWhiteSpace(cadena))
            {
                return null;
            }

            // La clave es Base64 y puede acabar en «=»: solo se parte por el primer «=»
            Dictionary<string, string> campos = cadena.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(parte => parte.Split(new[] { '=' }, 2))
                .Where(par => par.Length == 2)
                .GroupBy(par => par[0].Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

            if (!campos.TryGetValue("AccountName", out string nombre) || string.IsNullOrWhiteSpace(nombre) ||
                !campos.TryGetValue("AccountKey", out string clave) || string.IsNullOrWhiteSpace(clave))
            {
                return null;
            }

            var cuenta = new CuentaAlmacenamiento { Nombre = nombre, Clave = clave };
            if (campos.TryGetValue("EndpointSuffix", out string sufijo) && !string.IsNullOrWhiteSpace(sufijo))
            {
                cuenta.SufijoEndpoint = sufijo;
            }
            return cuenta;
        }

        /// <summary>
        /// La cadena que se firma. Dieciséis campos separados por salto de línea, en este orden;
        /// los que no se usan van vacíos. Pública para poder probarla contra el ejemplo de la documentación.
        /// </summary>
        public static string CadenaAFirmar(string cuenta, string contenedor, string rutaBlob, string permisos,
            DateTime? desdeUtc, DateTime caducaUtc)
        {
            return string.Join("\n", new[]
            {
                permisos,                                   // sp
                desdeUtc.HasValue ? Fecha(desdeUtc.Value) : string.Empty, // st
                Fecha(caducaUtc),                           // se
                $"/blob/{cuenta}/{contenedor}/{rutaBlob}",  // recurso canónico (sin codificar)
                string.Empty,                               // si  (directiva de acceso almacenada)
                string.Empty,                               // sip (intervalo de IP)
                "https",                                    // spr
                VERSION_SERVICIO,                           // sv
                "b",                                        // sr  (un blob)
                string.Empty,                               // instantánea
                string.Empty,                               // ámbito de cifrado
                string.Empty,                               // rscc
                string.Empty,                               // rscd
                string.Empty,                               // rsce
                string.Empty,                               // rscl
                string.Empty                                // rsct
            });
        }

        public static string Firmar(string claveBase64, string cadenaAFirmar)
        {
            using (var hmac = new HMACSHA256(Convert.FromBase64String(claveBase64)))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(cadenaAFirmar)));
            }
        }

        /// <summary>
        /// La dirección completa del blob con su firma. Quien la tenga puede hacer lo que digan los
        /// permisos hasta que caduque, sin ninguna otra credencial.
        /// </summary>
        public static Uri UrlFirmada(CuentaAlmacenamiento cuenta, string contenedor, string rutaBlob, string permisos,
            DateTime caducaUtc, DateTime? desdeUtc = null)
        {
            if (cuenta == null)
            {
                throw new ArgumentNullException(nameof(cuenta));
            }

            string firma = Firmar(cuenta.Clave, CadenaAFirmar(cuenta.Nombre, contenedor, rutaBlob, permisos, desdeUtc, caducaUtc));
            var consulta = new List<string>
            {
                "sv=" + VERSION_SERVICIO,
                "spr=https"
            };
            if (desdeUtc.HasValue)
            {
                consulta.Add("st=" + Uri.EscapeDataString(Fecha(desdeUtc.Value)));
            }
            consulta.Add("se=" + Uri.EscapeDataString(Fecha(caducaUtc)));
            consulta.Add("sr=b");
            consulta.Add("sp=" + permisos);
            consulta.Add("sig=" + Uri.EscapeDataString(firma));

            string rutaCodificada = string.Join("/", rutaBlob.Split('/').Select(Uri.EscapeDataString));
            return new Uri($"{cuenta.UrlBase}/{contenedor}/{rutaCodificada}?{string.Join("&", consulta)}");
        }

        private static string Fecha(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
    }
}
