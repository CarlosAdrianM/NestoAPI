using System;
using System.Text;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#428/#429: comparación de secretos en TIEMPO CONSTANTE.
    ///
    /// El operador == y String.Equals cortan en cuanto encuentran el primer carácter distinto, así
    /// que el tiempo de respuesta depende de cuántos caracteres se acertaron. Sobre un endpoint
    /// anónimo, eso permite adivinar un secreto carácter a carácter en vez de a fuerza bruta:
    /// se prueban los 10 dígitos, se mira cuál tarda un pelo más, se fija, y se pasa al siguiente.
    /// Un código de 6 dígitos pasa de 900.000 intentos a unas decenas.
    ///
    /// .NET Framework 4.8 no tiene CryptographicOperations.FixedTimeEquals (llegó con .NET Core
    /// 2.1), así que se implementa aquí.
    /// </summary>
    internal static class ComparacionSegura
    {
        /// <summary>
        /// Compara dos secretos sin que el tiempo empleado revele en qué se parecen.
        ///
        /// Un nulo NUNCA es igual a nada, ni siquiera a otro nulo: aquí eso siempre significa
        /// "falta el secreto", y ese caso tiene que fallar en cerrado. Es la misma trampa del
        /// #429 (setting sin definir + cabecera ausente = null == null = pasa).
        /// </summary>
        internal static bool SonIguales(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return SonIguales(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }

        /// <summary>
        /// Privado a propósito: con las dos sobrecargas visibles, una llamada con dos nulos es
        /// ambigua para el compilador, y el caso de los nulos es justo el que hay que poder
        /// escribir sin ceremonias en los tests. Fuera de aquí solo se comparan cadenas.
        /// </summary>
        private static bool SonIguales(byte[] a, byte[] b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            // La LONGITUD sí se compara de golpe, y es correcto: no es un dato secreto (un código
            // son siempre 6 dígitos y una firma HMAC siempre 64 caracteres). Lo que no puede
            // filtrarse es el CONTENIDO, y para eso el bucle recorre siempre el array entero
            // acumulando diferencias con XOR, sin salir antes de tiempo.
            if (a.Length != b.Length)
            {
                return false;
            }

            int diferencias = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diferencias |= a[i] ^ b[i];
            }

            return diferencias == 0;
        }
    }
}
