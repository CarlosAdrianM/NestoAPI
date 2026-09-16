using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#479: el nombre de la ficha de Nesto va en MAYÚSCULAS por convención de la casa
    /// ("SILLON DE BARBERO CHECK GY") y, cuando el producto no tiene NombrePersonalizado con
    /// visto bueno, llegaba así a la tienda: al título, a la URL amigable y al &lt;title&gt;. Al
    /// SEO le penaliza y al cliente le grita. Esto lo pasa a FORMATO ORACIÓN ("Sillon de barbero
    /// Check GY") con tres únicas excepciones, ninguna adivinada:
    ///   1. La primera letra del nombre, siempre.
    ///   2. El nombre del modelo del fabricante: los tokens que coincidan (sin distinguir
    ///      mayúsculas) con un token de ProveedoresProducto.ReferenciaProv (Orden = 1) se
    ///      escriben COMO LOS ESCRIBE EL FABRICANTE ("Dave", "Check GY", "OKE 9 BR/W"). Sin lista
    ///      de modelos que mantener; si el fabricante lo escribe en mayúsculas, sale en mayúsculas.
    ///   3. Siglas y unidades de una lista blanca corta (LED, UV, ML...). Token completo: "LEDS"
    ///      no se parte.
    /// Las preposiciones y el resto ("de", "barbero", "con") caen solas en minúscula porque no
    /// están en ninguna de las tres. Sin tildes automáticas: "SILLON" -> "Sillon" (poner la tilde
    /// exige conocer la palabra; eso es la ficha SEO, no una función). Es presentación del
    /// mensaje: el nombre de Nesto no se toca y las fichas con NombrePersonalizado no pasan por aquí.
    /// </summary>
    public static class FormateadorNombreTienda
    {
        /// <summary>
        /// Siglas y unidades que se conservan tal cual (token completo, sin distinguir mayúsculas
        /// en la comparación; se escribe como está aquí). Conocimiento de negocio: vive en código
        /// con su comentario, como las listas de PuertaPublicacionTienda. La que falte sale en
        /// minúscula ("led"): se acepta, hoy están TODAS en mayúsculas, y se añade al detectarla.
        /// </summary>
        internal static readonly string[] SIGLAS =
        {
            "LED", "UV", "UVA", "UVB", "IPL", "RF", "EMS", "LCD", "RGB", "USB", "PVC", "ABS", "PU",
            "XS", "XL", "XXL", "L", "ML", "CL", "CM", "MM", "KG", "GR", "W", "V", "HZ", "SPF"
        };

        public static string FormatoOracion(string nombre, string referenciaProveedor)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return nombre;
            }

            Dictionary<string, string> fijos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sigla in SIGLAS)
            {
                fijos[sigla] = sigla;
            }
            // La referencia del fabricante manda sobre la lista: es su forma de escribirlo.
            foreach (string token in Tokens(referenciaProveedor))
            {
                fijos[token] = token;
            }

            string[] palabras = Tokens(nombre)
                .Select(t => fijos.TryGetValue(t, out string fijo) ? fijo : t.ToLowerInvariant())
                .ToArray();

            if (!fijos.ContainsKey(palabras[0]))
            {
                palabras[0] = char.ToUpperInvariant(palabras[0][0]) + palabras[0].Substring(1);
            }

            return string.Join(" ", palabras);
        }

        private static string[] Tokens(string texto)
        {
            return string.IsNullOrWhiteSpace(texto)
                ? new string[0]
                : texto.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
