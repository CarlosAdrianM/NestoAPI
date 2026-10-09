using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#544 (d) / #593: el nombre de pila con el que saludar en un correo («Hola, Pepita:»), reutilizando la
    /// decisión persona/empresa del correo post-compra: el modelo (<see cref="GeneradorContenidoCorreoPostCompra.GenerarSaludosAsync"/>)
    /// decide, la guarda determinista (<see cref="GeneradorContenidoCorreoPostCompra.SanearSaludo"/>) quita siglas de
    /// sociedad y nombres de más de dos palabras, y de lo que queda solo se usa el nombre de pila
    /// (<see cref="GeneradorContenidoCorreoPostCompra.NombreDelSaludo"/>). La fórmula la pone cada correo. Lo usan el aviso
    /// de facturas vencidas y el correo del cheque regalo.
    /// </summary>
    internal static class SaludoPorNombreDePila
    {
        /// <summary>A quién saludar: la persona de contacto si la hay (mejor que la razón social); si no, el cliente. Null si ninguno.</summary>
        internal static string NombreParaSaludar(string nombrePersonaContacto, string nombreCliente)
            => !string.IsNullOrWhiteSpace(nombrePersonaContacto) ? nombrePersonaContacto.Trim()
                : !string.IsNullOrWhiteSpace(nombreCliente) ? nombreCliente.Trim() : null;

        /// <summary>
        /// Nombre → nombre de pila («» si es una empresa o un centro). Nunca lanza: si el modelo falla, no está
        /// configurado o pasa del <paramref name="tope"/> (null = sin tope), se apunta en <paramref name="registrarError"/>
        /// y vuelve vacío, y cada correo saluda sin nombre. Con tope, la llamada va en otro hilo para que ni siquiera
        /// una parte síncrona del cliente HTTP pueda pasar de él.
        /// </summary>
        internal static async Task<Dictionary<string, string>> NombresDePila(IEnumerable<string> nombres,
            Func<List<string>, Task<Dictionary<string, string>>> generarSaludos, TimeSpan? tope,
            Action<Exception> registrarError, string contexto)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (nombres ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!lista.Any() || generarSaludos == null)
            {
                return resultado;
            }

            Dictionary<string, string> saludos;
            try
            {
                if (tope == null)
                {
                    saludos = await generarSaludos(lista).ConfigureAwait(false);
                }
                else
                {
                    Task<Dictionary<string, string>> llamada = Task.Run(() => generarSaludos(lista));
                    if (await Task.WhenAny(llamada, Task.Delay(tope.Value)).ConfigureAwait(false) != llamada)
                    {
                        // Que la excepción tardía no quede sin observar
                        _ = llamada.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        throw new TimeoutException($"El modelo no ha contestado en {tope.Value.TotalSeconds:0.#} s.");
                    }
                    saludos = await llamada.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                registrarError?.Invoke(new Exception($"{contexto}: {ex.Message}", ex));
                return resultado;
            }

            foreach (string nombre in lista)
            {
                if (saludos != null && saludos.TryGetValue(nombre, out string saludo))
                {
                    resultado[nombre] = GeneradorContenidoCorreoPostCompra.NombreDelSaludo(
                        GeneradorContenidoCorreoPostCompra.SanearSaludo(nombre, saludo));
                }
            }
            return resultado;
        }
    }
}
