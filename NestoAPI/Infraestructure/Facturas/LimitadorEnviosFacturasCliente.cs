using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// TNV (07/10/26): tope sencillo de envíos de facturas por correo que un cliente de la tienda puede pedir por hora
    /// (en memoria, por proceso: si se recicla el pool, empieza de cero; basta para que nadie use la API para mandar
    /// correos sin parar). Los empleados no pasan por aquí.
    /// </summary>
    internal static class LimitadorEnviosFacturasCliente
    {
        internal const int MAXIMO_ENVIOS_POR_HORA = 10;
        private static readonly TimeSpan Ventana = TimeSpan.FromHours(1);
        private static readonly ConcurrentDictionary<string, Queue<DateTime>> envios =
            new ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Para los tests: la hora que se toma como «ahora».</summary>
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.UtcNow;

        /// <summary>Apunta un envío del cliente si no ha llegado al tope de la última hora. False si ya ha llegado.</summary>
        internal static bool Permitir(string cliente)
        {
            Queue<DateTime> cola = envios.GetOrAdd(cliente?.Trim() ?? string.Empty, _ => new Queue<DateTime>());
            DateTime ahora = Ahora();
            lock (cola)
            {
                while (cola.Count > 0 && ahora - cola.Peek() >= Ventana)
                {
                    _ = cola.Dequeue();
                }
                if (cola.Count >= MAXIMO_ENVIOS_POR_HORA)
                {
                    return false;
                }
                cola.Enqueue(ahora);
                return true;
            }
        }

        /// <summary>Para los tests: olvida los envíos apuntados y vuelve al reloj de verdad.</summary>
        internal static void Reiniciar()
        {
            envios.Clear();
            Ahora = () => DateTime.UtcNow;
        }
    }
}
