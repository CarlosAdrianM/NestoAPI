using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Almacen
{
    /// <summary>Una falta neta del recorrido de un picking del periodo, en un hueco (PreparacionEscaneos.Hueco o, si no, el Motivo de la falta).</summary>
    public class FaltaConHuecoAlmacen
    {
        public int Picking { get; set; }
        public string Producto { get; set; }
        public string Hueco { get; set; }
        public string Usuario { get; set; }
        public int Faltas { get; set; }
        /// <summary>La última vez que se dio (lectura positiva).</summary>
        public DateTime Fecha { get; set; }
    }

    /// <summary>Una cogida del recorrido (cualquier salida: PICK o REPO) con el hueco guardado.</summary>
    public class CogidaConHuecoAlmacen
    {
        public string TipoOrigen { get; set; }
        public int NumeroOrigen { get; set; }
        public string Producto { get; set; }
        public string Hueco { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>La prueba de una falta que sí estaba: la cogida posterior en el mismo hueco.</summary>
    public class FaltaQueSiEstabaAlmacen
    {
        public FaltaConHuecoAlmacen Falta { get; set; }
        public CogidaConHuecoAlmacen Cogida { get; set; }
    }

    /// <summary>
    /// NestoAPI#914 (corte 2, 27/11/26): LA REGLA (pura) de «falta que sí estaba», versión SIMPLE: una falta dada en un hueco y, después,
    /// ese producto cogido de ESE MISMO hueco (por cualquiera, en otra salida) en las <see cref="HORAS"/> horas siguientes. Lo de «sin que entre
    /// medias haya entrado stock en el hueco» no se puede saber con lo que hay (las ubicaciones no guardan cuándo entró cada unidad en el hueco):
    /// por eso la ventana es corta. Informativo. Límite (Ariadna 2.48): en las salidas que no cambian la reserva, si el mozo lo coge de otro hueco
    /// la app no lo sabe y manda el de la parada; para quedarse corto antes que acusar en falso, no cuenta la cogida de la misma salida que la
    /// falta (eso es «FaltaDeshechaPorOtro») y la ventana es de pocas horas.
    /// </summary>
    public static class ReglaFaltaQueSiEstaba
    {
        public const int HORAS = 4;

        public static List<FaltaQueSiEstabaAlmacen> Evaluar(IEnumerable<FaltaConHuecoAlmacen> faltas, IEnumerable<CogidaConHuecoAlmacen> cogidas)
        {
            var resultado = new List<FaltaQueSiEstabaAlmacen>();
            Dictionary<string, List<CogidaConHuecoAlmacen>> porProductoYHueco = (cogidas ?? Enumerable.Empty<CogidaConHuecoAlmacen>())
                .Where(c => c != null && c.Cantidad > 0 && PlanificadorFaltasSalida.NormalizarHueco(c.Hueco) != null)
                .GroupBy(c => Clave(c.Producto, c.Hueco))
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Fecha).ToList());
            foreach (FaltaConHuecoAlmacen f in (faltas ?? Enumerable.Empty<FaltaConHuecoAlmacen>())
                .Where(f => f != null && f.Faltas > 0 && PlanificadorFaltasSalida.NormalizarHueco(f.Hueco) != null))
            {
                if (!porProductoYHueco.TryGetValue(Clave(f.Producto, f.Hueco), out List<CogidaConHuecoAlmacen> candidatas))
                {
                    continue;
                }
                CogidaConHuecoAlmacen cogida = candidatas.FirstOrDefault(c => c.Fecha > f.Fecha && c.Fecha <= f.Fecha.AddHours(HORAS)
                    && !(string.Equals(c.TipoOrigen?.Trim(), "PICK", StringComparison.OrdinalIgnoreCase) && c.NumeroOrigen == f.Picking));
                if (cogida != null)
                {
                    resultado.Add(new FaltaQueSiEstabaAlmacen { Falta = f, Cogida = cogida });
                }
            }
            return resultado;
        }

        private static string Clave(string producto, string hueco) =>
            (producto?.Trim().ToUpperInvariant() ?? string.Empty) + "|" + PlanificadorFaltasSalida.NormalizarHueco(hueco);
    }
}
