using NestoAPI.Models.Direcciones;
using System;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#874 (Carlos 17/11/26, cliente 42143): Google da a veces la calle SIN número («Calle Sor Livia Alcorta») y la
    /// comercial lo escribía en el texto libre, sin que nadie lo comprobara. Ahora el número va en su campo (cifras o «S/N»)
    /// y se vuelve a preguntar a Google con calle + número + CP: si lo encuentra, queda verificado. Reglas puras.
    /// </summary>
    public static class NumeroPropioDireccion
    {
        public const string SIN_NUMERO = "S/N";

        private static readonly Regex FORMATO = new Regex(@"^\d{1,5}(BIS|[A-Z])?$", RegexOptions.Compiled);
        private static readonly Regex SIN_NUMERO_ESCRITO = new Regex(@"^(S\s*[/.\-]?\s*N(º|O|\.)?|SIN\s+N(Ú|U)MERO)\.?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CIFRAS_INICIALES = new Regex(@"^\d+", RegexOptions.Compiled);

        public const string MOTIVO_FORMATO = "El número de la calle tiene que ser solo cifras (13, 13 B) o «S/N» si la calle no tiene número.";

        /// <summary>«13», «13b» → «13B»; «13 bis» → «13BIS»; «s/n», «sn», «sin número» → «S/N»; vacío → null.</summary>
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
            return Regex.Replace(limpio, @"\s+", string.Empty).TrimStart('N', 'n', 'º', '.').ToUpperInvariant();
        }

        public static bool EsSinNumero(string numero) => Normalizar(numero) == SIN_NUMERO;

        /// <summary>Normalizado: cifras con, como mucho, una letra o «BIS» detrás; o «S/N».</summary>
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

        public static string MotivoNoEncontrado(string calle, string numero, string codigoPostal)
            => $"Google no encuentra el número {Normalizar(numero)} en {calle?.Trim().ToUpper()} ({codigoPostal?.Trim()}). " +
               "Compruebe el número; si la calle no tiene número, escriba «S/N».";

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
}
