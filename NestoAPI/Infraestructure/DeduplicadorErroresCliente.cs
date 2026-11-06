using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#230 (06/11/26): los errores que mandan los clientes (POST api/Errores) se repetían sin fin en ELMAH (una PDA que
    /// no puede actualizarse, «Install Error(-10)…», 17 avisos iguales en una semana). Por (aplicación, usuario, mensaje
    /// normalizado) se loguea el primero; los siguientes en la ventana de 24 h solo se cuentan, y el primero después de la
    /// ventana sale con el contador. En memoria: un reciclaje del pool vuelve a avisar una vez (aceptable).
    /// </summary>
    public class DeduplicadorErroresCliente
    {
        public static readonly TimeSpan VENTANA = TimeSpan.FromHours(24);
        private const int MAXIMO_CLAVES = 5000;

        /// <summary>Mensajes que por sí solos no dicen dónde ha fallado: con ellos la clave lleva la primera línea de la pila.</summary>
        private static readonly string[] MENSAJES_GENERICOS =
        {
            "referencia a objeto no establecida",
            "object reference not set",
            "índice fuera de los límites",
            "index was outside the bounds",
            "la secuencia no contiene elementos",
            "sequence contains no elements",
            "el valor no puede ser nulo",
            "value cannot be null",
            "specified cast is not valid",
            "la conversión especificada no es válida"
        };

        private static readonly Regex FECHAS = new Regex(@"\d{1,4}[/\-.]\d{1,2}[/\-.]\d{1,4}([ T]\d{1,2}:\d{2}(:\d{2})?(\.\d+)?)?|\b\d{1,2}:\d{2}(:\d{2})?\b", RegexOptions.Compiled);
        private static readonly Regex NUMEROS_LARGOS = new Regex(@"\d{5,}", RegexOptions.Compiled);
        private static readonly Regex HEXADECIMALES = new Regex(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled);
        private static readonly Regex ESPACIOS = new Regex(@"\s+", RegexOptions.Compiled);

        private class Entrada
        {
            public DateTime InicioVentana;
            public int Repetidos;
        }

        private readonly Func<DateTime> ahora;
        private readonly Dictionary<string, Entrada> entradas = new Dictionary<string, Entrada>();
        private readonly object cerrojo = new object();

        public DeduplicadorErroresCliente(Func<DateTime> ahora = null)
        {
            this.ahora = ahora ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Registra la llegada del error. Null = repetido dentro de la ventana: no se loguea. Si no, se loguea, y el número
        /// es cuántas veces se calló en la ventana anterior (0 = primero, o sin repeticiones).
        /// </summary>
        public int? Registrar(string aplicacion, string usuario, string mensaje, string pila)
        {
            string clave = Clave(aplicacion, usuario, mensaje, pila);
            DateTime momento = ahora();
            lock (cerrojo)
            {
                if (entradas.TryGetValue(clave, out Entrada entrada) && momento - entrada.InicioVentana < VENTANA)
                {
                    entrada.Repetidos++;
                    return null;
                }
                int repetidosAntes = entrada?.Repetidos ?? 0;
                if (entrada == null && entradas.Count >= MAXIMO_CLAVES)
                {
                    Purgar(momento);
                }
                entradas[clave] = new Entrada { InicioVentana = momento };
                return repetidosAntes;
            }
        }

        /// <summary>« (repetido N veces en las últimas 24 h)» para el resumen, o vacío si no hubo repeticiones.</summary>
        public static string SufijoRepetidos(int repetidos)
            => repetidos > 0 ? $" (repetido {repetidos} {(repetidos == 1 ? "vez" : "veces")} en las últimas 24 h)" : string.Empty;

        internal static string Clave(string aplicacion, string usuario, string mensaje, string pila)
        {
            string mensajeNormalizado = Normalizar(mensaje);
            string clave = $"{aplicacion?.Trim().ToLowerInvariant()}|{usuario?.Trim().ToLowerInvariant()}|{mensajeNormalizado}";
            return EsGenerico(mensajeNormalizado) ? clave + "|" + Normalizar(PrimeraLineaDeLaPila(pila)) : clave;
        }

        /// <summary>La primera línea, sin fechas, horas, números largos ni direcciones hexadecimales, y en minúsculas.</summary>
        internal static string Normalizar(string texto)
        {
            string linea = PrimeraLinea(texto);
            linea = FECHAS.Replace(linea, "#");
            linea = HEXADECIMALES.Replace(linea, "#");
            linea = NUMEROS_LARGOS.Replace(linea, "#");
            return ESPACIOS.Replace(linea, " ").Trim().ToLowerInvariant();
        }

        internal static bool EsGenerico(string mensajeNormalizado)
            => mensajeNormalizado != null && MENSAJES_GENERICOS.Any(g => mensajeNormalizado.Contains(g));

        private static string PrimeraLinea(string texto)
            => (texto ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        /// <summary>La primera línea de la pila que dice dónde («en …»/«at …»), no la de la excepción.</summary>
        private static string PrimeraLineaDeLaPila(string pila)
        {
            List<string> lineas = (pila ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lineas.FirstOrDefault(l => l.StartsWith("en ", StringComparison.OrdinalIgnoreCase) || l.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
                ?? lineas.FirstOrDefault() ?? string.Empty;
        }

        private void Purgar(DateTime momento)
        {
            foreach (string clave in entradas.Where(e => momento - e.Value.InicioVentana >= VENTANA).Select(e => e.Key).ToList())
            {
                _ = entradas.Remove(clave);
            }
        }
    }
}
