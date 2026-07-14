using System;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// #295: el concepto del enlace de pago acaba en el extracto del cliente (concepto contable
    /// "Pago TPV {Descripcion}"), así que debe identificar el pago y quedar persistido con un
    /// formato consistente. Esta clase concentra las dos reglas:
    /// - <see cref="EsGenericoOVacio"/>: detecta el asunto por defecto (o vacío), que solo es
    ///   aceptable cuando el enlace liquida efectos concretos.
    /// - <see cref="Normalizar"/>: corrige mayúsculas/minúsculas "evidentemente mal" (todo
    ///   mayúsculas, todo minúsculas, primera minúscula con el resto en mayúsculas) a tipo
    ///   oración; si el usuario mezcló mayúsculas y minúsculas razonablemente, se respeta su
    ///   formato (como mucho se capitaliza la primera letra).
    /// </summary>
    public static class FormateadorConcepto
    {
        public const string CONCEPTO_GENERICO = "Enlace de pago a Nueva Visión";

        private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-ES");

        public static bool EsGenericoOVacio(string concepto)
        {
            return string.IsNullOrWhiteSpace(concepto)
                || string.Equals(concepto.Trim(), CONCEPTO_GENERICO, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalizar(string concepto)
        {
            if (string.IsNullOrWhiteSpace(concepto))
            {
                return concepto;
            }

            string texto = concepto.Trim();
            var letras = texto.Where(char.IsLetter).ToList();
            if (letras.Count == 0)
            {
                return texto;
            }

            bool hayMayusculas = letras.Any(char.IsUpper);
            bool hayMinusculas = letras.Any(char.IsLower);
            var restoLetras = letras.Skip(1).ToList();
            bool restoTodoMayusculas = restoLetras.Any() && restoLetras.All(char.IsUpper);

            // Todo mayúsculas, todo minúsculas, o primera minúscula con el resto en mayúsculas:
            // formato "evidentemente mal" → tipo oración.
            if (!hayMinusculas || !hayMayusculas || (char.IsLower(letras[0]) && restoTodoMayusculas))
            {
                return CapitalizarPrimeraLetra(texto.ToLower(Cultura));
            }

            // Mixto con la primera letra en minúscula: solo se corrige esa; el resto se respeta.
            if (char.IsLower(letras[0]))
            {
                return CapitalizarPrimeraLetra(texto);
            }

            // Mixto razonable: se respeta tal cual.
            return texto;
        }

        private static string CapitalizarPrimeraLetra(string texto)
        {
            int indice = 0;
            while (indice < texto.Length && !char.IsLetter(texto[indice]))
            {
                indice++;
            }
            if (indice >= texto.Length)
            {
                return texto;
            }
            return texto.Substring(0, indice) + char.ToUpper(texto[indice], Cultura) + texto.Substring(indice + 1);
        }
    }
}
