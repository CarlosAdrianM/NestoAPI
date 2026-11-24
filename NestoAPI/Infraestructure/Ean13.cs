using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#884: EAN-13 para dibujarlo en los PDF de QuestPDF sin librería nueva, como <see cref="Code128"/> (en las etiquetas
    /// lo dibuja la impresora con ^BE / E30). 95 módulos: guarda 101, seis cifras de la izquierda en los juegos L o G según la
    /// primera cifra, guarda central 01010, seis cifras en el juego R y guarda 101. Sin las zonas de silencio (11 y 7 módulos),
    /// que pone quien lo dibuja.
    /// </summary>
    public static class Ean13
    {
        public const int MODULOS = 95;
        public const int SILENCIO_IZQUIERDA = 11;
        public const int SILENCIO_DERECHA = 7;

        /// <summary>Juego L (impar) de cada cifra; el G es el R al revés y el R es el L invertido.</summary>
        internal static readonly string[] JUEGO_L =
        {
            "0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"
        };

        /// <summary>Qué juego (L o G) lleva cada una de las seis cifras de la izquierda, según la primera cifra.</summary>
        internal static readonly string[] PARIDAD =
        {
            "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"
        };

        /// <summary>13 cifras con el dígito de control bien.</summary>
        public static bool EsValido(string codigo)
        {
            string limpio = codigo?.Trim();
            return limpio != null && limpio.Length == 13 && limpio.All(c => c >= '0' && c <= '9')
                && Productos.ValidadorGtin.EsCodigoValido(limpio);
        }

        /// <summary>Los 95 módulos (true = barra).</summary>
        /// <exception cref="ArgumentException">No es un EAN-13 válido.</exception>
        public static bool[] Modulos(string codigo)
        {
            if (!EsValido(codigo))
            {
                throw new ArgumentException($"«{codigo}» no es un EAN-13 válido.", nameof(codigo));
            }
            string cifras = codigo.Trim();
            string paridad = PARIDAD[cifras[0] - '0'];
            var bits = new System.Text.StringBuilder("101");
            for (int i = 1; i <= 6; i++)
            {
                string l = JUEGO_L[cifras[i] - '0'];
                bits.Append(paridad[i - 1] == 'L' ? l : JuegoG(l));
            }
            bits.Append("01010");
            for (int i = 7; i <= 12; i++)
            {
                bits.Append(JuegoR(JUEGO_L[cifras[i] - '0']));
            }
            bits.Append("101");
            return bits.ToString().Select(c => c == '1').ToArray();
        }

        /// <summary>Las barras y espacios seguidos como (esBarra, módulos), para dibujarlos como rectángulos.</summary>
        public static List<(bool EsBarra, int Modulos)> Tramos(string codigo)
        {
            var tramos = new List<(bool, int)>();
            foreach (bool modulo in Modulos(codigo))
            {
                if (tramos.Count > 0 && tramos[tramos.Count - 1].Item1 == modulo)
                {
                    tramos[tramos.Count - 1] = (modulo, tramos[tramos.Count - 1].Item2 + 1);
                }
                else
                {
                    tramos.Add((modulo, 1));
                }
            }
            return tramos;
        }

        private static string JuegoR(string l) => new string(l.Select(c => c == '1' ? '0' : '1').ToArray());

        private static string JuegoG(string l) => new string(JuegoR(l).Reverse().ToArray());
    }
}
