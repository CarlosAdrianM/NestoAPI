using NestoAPI.Models.Direcciones;
using System;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#874 (Carlos 17/11/26, cliente 42143): Google da a veces la calle SIN número («Calle Sor Livia Alcorta») y la
    /// comercial lo escribía en el texto libre, sin que nadie lo comprobara. Ahora el número va en su campo y se vuelve a
    /// preguntar a Google con calle + número + CP. Reglas puras:
    /// <list type="bullet">
    /// <item>En «Número» SOLO va el número del portal: cifras con letra o «BIS» opcionales («99», «13 B», «13 BIS») o «S/N».
    /// «Nº 99», «99 local 6» o «Edificio Rojo» no valen NUNCA (400): lo demás va en «¿Qué es?».</item>
    /// <item>Número válido que Google no encuentra en esa calle y CP: NO se bloquea, se pregunta al usuario (Carlos 17/11/26) y,
    /// si confirma (<c>ClienteCrear.NumeroConfirmadoSinGoogle</c>), el alta sigue y se apunta en Modificaciones
    /// (<see cref="TABLA_MODIFICACIONES"/>) para medirlo y revisarlo.</item>
    /// </list>
    /// </summary>
    public static class NumeroPropioDireccion
    {
        public const string SIN_NUMERO = "S/N";
        /// <summary>Tabla (columna Tabla de Modificaciones) del rastro de los números confirmados sin Google.</summary>
        public const string TABLA_MODIFICACIONES = "ClienteNumeroSinGoogle";

        private static readonly Regex FORMATO = new Regex(@"^\d{1,5}(BIS|[A-Z])?$", RegexOptions.Compiled);
        private static readonly Regex SIN_NUMERO_ESCRITO = new Regex(@"^(S\s*[/.\-]?\s*N(º|O|\.)?|SIN\s+N(Ú|U)MERO)\.?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CIFRAS_INICIALES = new Regex(@"^\d+", RegexOptions.Compiled);

        public const string MOTIVO_FORMATO = "En «Número» solo va el número del portal (por ejemplo, 99) o S/N. " +
            "Lo demás (piso, local, edificio, rótulo…) va en «¿Qué es?».";

        /// <summary>«13», «13 b» → «13B»; «13 bis» → «13BIS»; «s/n», «sn», «sin número» → «S/N»; vacío → null. Lo demás, sin espacios y en mayúsculas.</summary>
        public static string Normalizar(string numero)
        {
            if (string.IsNullOrWhiteSpace(numero))
            {
                return null;
            }
            string limpio = numero.Trim();
            if (SIN_NUMERO_ESCRITO.IsMatch(limpio))
            {
                return SIN_NUMERO;
            }
            return Regex.Replace(limpio, @"\s+", string.Empty).ToUpperInvariant();
        }

        public static bool EsSinNumero(string numero) => Normalizar(numero) == SIN_NUMERO;

        /// <summary>¿Es un número de portal? Cifras con, como mucho, una letra o «BIS» detrás; o «S/N».</summary>
        public static bool FormatoValido(string numero)
        {
            string n = Normalizar(numero);
            return n == SIN_NUMERO || (n != null && FORMATO.IsMatch(n));
        }

        /// <summary>Mismo número de portal: cuentan las cifras («13» casa con «13B»: Google rara vez sabe la letra).</summary>
        public static bool MismoNumero(string deGoogle, string tecleado)
        {
            string a = CifrasIniciales(deGoogle), b = CifrasIniciales(Normalizar(tecleado));
            return a != null && b != null && a.TrimStart('0') == b.TrimStart('0');
        }

        /// <summary>
        /// ¿Google confirma el número? Tiene que devolver un resultado con ESE número de portal y, si trae código postal,
        /// el mismo que el de la ficha (sin espacios ni guiones). Con la calle sola (sin street_number) no confirma.
        /// </summary>
        public static bool Confirmado(DireccionDetalleDTO google, string numero, string codigoPostal)
        {
            if (google == null || !MismoNumero(google.Numero, numero))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(google.CodigoPostal) || string.IsNullOrWhiteSpace(codigoPostal))
            {
                return true;
            }
            return string.Equals(SoloAlfanumerico(google.CodigoPostal), SoloAlfanumerico(codigoPostal), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>La pregunta al usuario cuando Google no encuentra un número válido (de usted).</summary>
        public static string PreguntaNoEncontrado(string calle, string numero, string codigoPostal)
        {
            string n = Normalizar(numero);
            // En el alta llega la dirección ya con el número («C/ LA GRANJA, 99»): se nombra solo la calle.
            string soloCalle = Regex.Replace(calle?.Trim() ?? string.Empty, @",\s*" + Regex.Escape(n ?? string.Empty) + "$", string.Empty, RegexOptions.IgnoreCase);
            return $"Google Maps no encuentra el número {n} en {soloCalle.ToUpper()} ({codigoPostal?.Trim()}). " +
                   "Es posible que el repartidor de la agencia no lo encuentre. ¿Quiere guardarlo igualmente?";
        }

        public static string MotivoNumeroNoEstaEnLaDireccion(string direccion, string numero)
            => $"La dirección «{direccion?.Trim()}» no lleva el número {Normalizar(numero)} que se ha comprobado con Google. " +
               "Vuelva a «Datos generales» y compruebe la dirección.";

        /// <summary>¿La dirección ya formateada lleva ese número (como palabra, no dentro de otro)?</summary>
        public static bool DireccionLlevaElNumero(string direccion, string numero)
        {
            string n = Normalizar(numero);
            if (string.IsNullOrWhiteSpace(direccion) || n == null)
            {
                return false;
            }
            if (n == SIN_NUMERO)
            {
                return direccion.ToUpperInvariant().Contains(SIN_NUMERO);
            }
            string cifras = CifrasIniciales(n);
            return cifras != null && Regex.IsMatch(direccion, $@"(?<!\d){cifras}(?!\d)");
        }

        private static string CifrasIniciales(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            Match m = CIFRAS_INICIALES.Match(texto.Trim());
            return m.Success ? m.Value : null;
        }

        private static string SoloAlfanumerico(string texto) => Regex.Replace(texto ?? string.Empty, @"[^0-9A-Za-z]", string.Empty);
    }

    /// <summary>NestoAPI#874: lo que decide el alta sobre el número propio.</summary>
    public sealed class ResultadoNumeroPropio
    {
        /// <summary>Por qué no sigue el alta (400, de usted); null si sigue.</summary>
        public string Motivo { get; set; }
        /// <summary>El usuario confirmó un número que Google no encuentra: hay que dejar rastro tras guardar.</summary>
        public bool ConfirmadoSinGoogle { get; set; }

        internal static readonly ResultadoNumeroPropio Sigue = new ResultadoNumeroPropio();
    }
}
