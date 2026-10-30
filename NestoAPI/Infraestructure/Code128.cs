using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#758: Code128 (juego B) para dibujarlo en los PDF de QuestPDF sin librería nueva (las etiquetas ZPL lo dibuja la
    /// Zebra con ^BC). Juego B: los caracteres imprimibles de ASCII (32-126), que es lo que llevan nuestros códigos (NE930123,
    /// NR930123-2, RP251400-1). Inicio B, los datos, el dígito de control (104 + Σ valor·posición, módulo 103) y la parada.
    /// </summary>
    public static class Code128
    {
        internal const int VALOR_INICIO_B = 104;
        internal const int VALOR_PARADA = 106;

        /// <summary>Anchos barra/espacio de los 107 símbolos (cada uno suma 11 módulos; la parada, 13).</summary>
        internal static readonly string[] PATRONES =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
        };

        /// <summary>Los valores de los símbolos: inicio B, datos, control y parada.</summary>
        /// <exception cref="ArgumentException">Vacío o con caracteres fuera del juego B.</exception>
        public static List<int> Valores(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                throw new ArgumentException("No hay nada que codificar en Code128.", nameof(texto));
            }
            var valores = new List<int> { VALOR_INICIO_B };
            int suma = VALOR_INICIO_B;
            for (int i = 0; i < texto.Length; i++)
            {
                char c = texto[i];
                if (c < 32 || c > 126)
                {
                    throw new ArgumentException($"El carácter «{c}» no se puede codificar en Code128 B.", nameof(texto));
                }
                int valor = c - 32;
                valores.Add(valor);
                suma += valor * (i + 1);
            }
            valores.Add(suma % 103);
            valores.Add(VALOR_PARADA);
            return valores;
        }

        /// <summary>El dígito de control de Code128 B.</summary>
        public static int DigitoControl(string texto)
        {
            List<int> valores = Valores(texto);
            return valores[valores.Count - 2];
        }

        /// <summary>Los módulos de izquierda a derecha (true = barra), sin la zona en blanco de los lados.</summary>
        public static bool[] Modulos(string texto)
        {
            var modulos = new List<bool>();
            foreach (int valor in Valores(texto))
            {
                bool barra = true;
                foreach (char ancho in PATRONES[valor])
                {
                    for (int k = 0; k < ancho - '0'; k++)
                    {
                        modulos.Add(barra);
                    }
                    barra = !barra;
                }
            }
            return modulos.ToArray();
        }

        /// <summary>Las barras y espacios seguidos como (esBarra, módulos), para dibujarlos como rectángulos.</summary>
        public static List<(bool EsBarra, int Modulos)> Tramos(string texto)
        {
            var tramos = new List<(bool, int)>();
            foreach (bool modulo in Modulos(texto))
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
    }
}
