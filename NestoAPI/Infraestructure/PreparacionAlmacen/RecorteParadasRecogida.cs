using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#713: una parada de la recogida tal como sale de SQL_LINEAS_PICKING / SQL_LINEAS_REPOSICION_SALIDA, con lo
    /// reservado y lo que piden sus líneas para poder recortarla. Viaja como <see cref="LineaPickingAlmacenDTO"/>: los dos
    /// números de más no salen en el JSON.
    /// </summary>
    public class FilaParadaRecogida : LineaPickingAlmacenDTO
    {
        /// <summary>Lo reservado en el hueco (Ubicaciones), en positivo. Null si la parada no tiene reserva.</summary>
        [JsonIgnore]
        public int? Reservado { get; set; }
        /// <summary>Lo que piden las líneas de la parada (Cantidad − Recoger, cada línea una vez), o lo que sale del producto en una reposición.</summary>
        [JsonIgnore]
        public int PendienteLineas { get; set; }
    }

    /// <summary>
    /// NestoAPI#713 (incidencia 785, picking 99941, 27/10/26): la cantidad de una parada NUNCA supera lo que piden sus
    /// líneas. Si las reservas dicen más, se recorta (una parada que se queda en 0 desaparece) y se deja aviso en ELMAH, como
    /// mucho una vez por salida y producto mientras viva el proceso: el recorrido se lee a cada rato y un aviso por lectura
    /// convertiría ELMAH en un log de tráfico.
    /// </summary>
    public static class RecorteParadasRecogida
    {
        private static readonly ConcurrentDictionary<string, byte> avisados = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Para los tests: olvida los avisos dados.</summary>
        internal static void OlvidarAvisos()
        {
            avisados.Clear();
        }

        /// <param name="tipoOrigen">PICK o REPO.</param>
        /// <param name="registrar">Dónde se deja el aviso; null = ELMAH.</param>
        public static List<LineaPickingAlmacenDTO> Aplicar(string empresa, string tipoOrigen, int numero,
            IEnumerable<FilaParadaRecogida> filas, Action<Exception> registrar = null)
        {
            var resultado = new List<LineaPickingAlmacenDTO>();
            var recortadas = new List<(FilaParadaRecogida Fila, int Antes)>();
            foreach (FilaParadaRecogida fila in filas ?? Enumerable.Empty<FilaParadaRecogida>())
            {
                if (fila == null)
                {
                    continue;
                }
                int maximo = Math.Max(0, fila.PendienteLineas);
                if (fila.Reservado.HasValue && fila.Cantidad > maximo)
                {
                    recortadas.Add((fila, fila.Cantidad));
                    fila.Cantidad = maximo;
                }
                if (fila.Cantidad > 0)
                {
                    resultado.Add(fila);
                }
            }
            if (recortadas.Count > 0)
            {
                Avisar(empresa, tipoOrigen, numero, recortadas, registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (recogida Ariadna)")));
            }
            return resultado;
        }

        private static void Avisar(string empresa, string tipoOrigen, int numero, List<(FilaParadaRecogida Fila, int Antes)> recortadas,
            Action<Exception> registrar)
        {
            foreach (var producto in recortadas.GroupBy(r => r.Fila.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                string clave = $"{empresa?.Trim()}|{tipoOrigen}|{numero}|{producto.Key}";
                if (!avisados.TryAdd(clave, 0))
                {
                    continue;
                }
                string huecos = string.Join("; ", producto.Select(r =>
                    $"hueco {Hueco(r.Fila)}: reservas {r.Fila.Reservado}, líneas {r.Fila.PendienteLineas}, se pide {r.Fila.Cantidad} (en vez de {r.Antes})"));
                try
                {
                    registrar(new Exception(
                        $"NestoAPI#713: la recogida de {tipoOrigen} {numero} (empresa {empresa?.Trim()}) pedía del producto {producto.Key} más de lo que " +
                        $"piden sus líneas y se ha recortado. {huecos}. Revisar las reservas (Ubicaciones) de ese producto."));
                }
                catch
                {
                    // El aviso nunca tumba la recogida
                }
            }
        }

        private static string Hueco(LineaPickingAlmacenDTO fila)
        {
            return string.IsNullOrWhiteSpace(fila.Pasillo) && string.IsNullOrWhiteSpace(fila.Fila) && string.IsNullOrWhiteSpace(fila.Columna)
                ? "(sin hueco)"
                : $"{fila.Pasillo?.Trim()}/{fila.Fila?.Trim()}/{fila.Columna?.Trim()}";
        }
    }
}
