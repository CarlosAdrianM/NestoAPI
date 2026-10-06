using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Direcciones
{
    /// <summary>
    /// NestoAPI#596: el ÚNICO sitio que entiende un código postal. El usuario lo teclea como quiere
    /// («4480 670», «4480670», «4480-670», «8850») y todo lo demás (parsers de agencias, tarifas,
    /// portes, altas de clientes y envíos) pregunta aquí en vez de llevar su propia regex.
    ///
    /// Formato canónico:
    ///   - España (país "ES" o vacío): 5 cifras; si vienen 4 se rellena el cero («8850» → «08850»).
    ///   - Portugal ("PT", o formato portugués de 7 cifras con cualquier país o sin él): «dddd-ddd»;
    ///     si solo hay 4 cifras y el país es PT, «dddd».
    ///   - Resto de países: recortado y en mayúsculas.
    /// Nunca rechaza: lo que no reconoce lo devuelve recortado (y en mayúsculas).
    ///
    /// Ojo: un CP de 4 cifras SIN país es ambiguo (España sin el cero o Portugal/Bélgica/Suiza). En
    /// <see cref="Normalizar"/> manda España (como pide la issue), pero <see cref="EsPortugues"/>
    /// solo lo da por portugués con país PT; <see cref="TieneFormatoPortugues"/> conserva la regla
    /// antigua de los perfiles de agencia (4 cifras = Portugal) para los que no conocen el país.
    /// </summary>
    public static class CodigoPostal
    {
        public const string ESPANA = "ES";
        public const string PORTUGAL = "PT";

        // 4 cifras + 3 cifras con separador opcional (guion o espacios, incluso «4480 - 670»).
        private static readonly Regex PortugalCompleto = new Regex(@"^([1-9]\d{3})\s*-?\s*(\d{3})$");
        private static readonly Regex PortugalCorto = new Regex(@"^[1-9]\d{3}$");
        private static readonly Regex CuatroCifras = new Regex(@"^\d{4}$");
        private static readonly Regex CincoCifras = new Regex(@"^\d{5}$");

        /// <summary>
        /// País ISO-2 a partir de lo que guarde cada tabla: "ES"/"PT" (Clientes.Pais, ISO-2), "ESP"/"PRT"
        /// (DataTrans), o los numéricos de EnviosAgencia.Pais (34/724 España, 351/620 Portugal).
        /// Cualquier otro texto se devuelve recortado y en mayúsculas; null o vacío, cadena vacía.
        /// </summary>
        public static string PaisIso(string pais)
        {
            string p = (pais ?? string.Empty).Trim().ToUpperInvariant();
            switch (p)
            {
                case "ES":
                case "ESP":
                case "34":
                case "724":
                    return ESPANA;
                case "PT":
                case "PRT":
                case "351":
                case "620":
                    return PORTUGAL;
                default:
                    return p;
            }
        }

        /// <summary>
        /// EnviosAgencia.Pais (numérico) → ISO-2. 34/724 = ES, 351/620 = PT; 0 (sin informar) = "" (se deduce
        /// del CP); cualquier otro número se devuelve tal cual: es otro país y su CP no se toca.
        /// </summary>
        public static string PaisIso(int pais)
            => pais == 0 ? string.Empty : PaisIso(pais.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>Solo las cifras del texto ("" si es null).</summary>
        public static string Digitos(string texto)
            => string.IsNullOrEmpty(texto) ? string.Empty : new string(texto.Where(char.IsDigit).ToArray());

        /// <summary>
        /// ¿Tiene forma de CP portugués, sin saber el país? 4 cifras o 4+3 con o sin separador. Es la regla
        /// de los perfiles de agencia (DefaultsEnvio), que solo ven el CP.
        /// </summary>
        public static bool TieneFormatoPortugues(string texto)
        {
            string t = Limpiar(texto);
            return PortugalCompleto.IsMatch(t) || PortugalCorto.IsMatch(t);
        }

        /// <summary>
        /// ¿Es un CP portugués? Con país PT, si tiene forma portuguesa (4 o 7 cifras). Con España o sin
        /// país, solo si tiene 7 cifras (un CP español nunca las tiene). Con otro país, no.
        /// </summary>
        public static bool EsPortugues(string texto, string paisIso = null)
        {
            string iso = PaisIso(paisIso);
            string t = Limpiar(texto);
            if (iso == PORTUGAL)
            {
                return PortugalCompleto.IsMatch(t) || PortugalCorto.IsMatch(t);
            }
            if (iso.Length == 0 || iso == ESPANA)
            {
                return PortugalCompleto.IsMatch(t);
            }
            return false;
        }

        /// <summary>¿Es un CP español (01000-52999) una vez normalizado? Con otro país explícito, no.</summary>
        public static bool EsEspanol(string texto, string paisIso = null)
        {
            string iso = PaisIso(paisIso);
            if (iso.Length != 0 && iso != ESPANA)
            {
                return false;
            }
            string cp = Normalizar(texto, ESPANA);
            return cp != null && CincoCifras.IsMatch(cp)
                && int.TryParse(cp, out int numero) && numero >= 1000 && numero <= 52999;
        }

        /// <summary>
        /// Formato canónico para guardar (ver la cabecera). null se queda null; lo que no se reconoce,
        /// recortado y en mayúsculas.
        /// </summary>
        public static string Normalizar(string texto, string paisIso = null)
        {
            if (texto == null)
            {
                return null;
            }
            string iso = PaisIso(paisIso);
            string t = Limpiar(texto);

            if (iso.Length == 0 || iso == ESPANA || iso == PORTUGAL)
            {
                Match completo = PortugalCompleto.Match(t);
                if (completo.Success)
                {
                    return completo.Groups[1].Value + "-" + completo.Groups[2].Value;
                }
            }
            if (iso.Length == 0 || iso == ESPANA)
            {
                if (CuatroCifras.IsMatch(t) && t[0] != '0')
                {
                    return "0" + t;
                }
            }
            return t.ToUpperInvariant();
        }

        /// <summary>
        /// Lo que se manda a CTT (recipient_postal_code / sender_postal_code): el canónico. Portugal con
        /// guion (es lo que usa su propia colección Postman) y España con 5 cifras.
        /// </summary>
        public static string ParaCTT(string texto, string paisIso = null)
            => Normalizar(texto, paisIso) ?? string.Empty;

        // Trim de todo blanco (los char de la BD vienen rellenados, y llegan NBSP de autocompletados)
        // y los blancos interiores repetidos se quedan en uno.
        private static string Limpiar(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return string.Empty;
            }
            string t = texto.Replace(' ', ' ').Trim();
            return Regex.Replace(t, @"\s+", " ");
        }
    }
}
