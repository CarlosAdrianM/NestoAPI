using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881: lo que necesita el cálculo del fin de ruta, ya con los tramos del motor (o de la línea recta).</summary>
    public class EntradaFinRuta
    {
        public DateTime Salida { get; set; }
        /// <summary>
        /// Minutos de conducción SIN colchón de cada tramo: del origen a la 1.ª parada, de cada parada a la siguiente y de la última al
        /// final (n + 1 tramos con n paradas). Si la ruta pasa por Algete antes de un destino personalizado, el último tramo llega a
        /// Algete y el de Algete al destino va en <see cref="MinutosAlgeteADestino"/>.
        /// </summary>
        public IReadOnlyList<double> MinutosTramos { get; set; }
        /// <summary>Kilómetros de cada tramo (los mismos que <see cref="MinutosTramos"/>).</summary>
        public IReadOnlyList<double> KmTramos { get; set; }
        /// <summary>Los minutos de cada parada (n), ya con <see cref="MinutosParadaReparto.Recorrido(IReadOnlyList{ParadaParaTiempo}, ConstantesTiemposReparto)"/>.</summary>
        public IReadOnlyList<double> MinutosParada { get; set; }
        public double FactorConduccion { get; set; } = 1;
        /// <summary>La ruta pasa por Algete (acaba allí, o acaba en otro sitio pero lleva algo para Algete): se suma la descarga.</summary>
        public bool PasaPorAlgete { get; set; }
        public double MinutosDescargaAlgete { get; set; }
        /// <summary>Solo si pasa por Algete y el destino es otro: el tramo de Algete al destino.</summary>
        public double? MinutosAlgeteADestino { get; set; }
        public double? KmAlgeteADestino { get; set; }
        /// <summary>El cierre de ese día (<see cref="HorarioCierreReparto"/>); null = no hay con qué comparar.</summary>
        public DateTime? HoraCierre { get; set; }
    }

    public class ResultadoFinRuta
    {
        public DateTime FinEstimado { get; set; }
        public DateTime LlegadaDestino { get; set; }
        /// <summary>A qué hora se llega a cada parada (minutos enteros), en orden.</summary>
        public List<DateTime> Llegadas { get; set; } = new List<DateTime>();
        public double MinutosConduccion { get; set; }
        public double MinutosParadas { get; set; }
        public double MinutosDescarga { get; set; }
        public double Km { get; set; }
        public bool SeComparaConCierre { get; set; }
        public bool PasaDelCierre { get; set; }
        public int MinutosDeMas { get; set; }
    }

    /// <summary>
    /// NestoAPI#881: el fin previsto de una ruta. PURO. Conducción (minutos del motor × (1 + colchón)) + minutos de cada parada + descarga en
    /// Algete si la ruta pasa por allí. Sin esperas por el horario preferible (eso es cosa del orden sugerido, #769). Se compara con el
    /// cierre solo si hay cierre ese día y la ruta pasa por Algete (si acaba en casa del repartidor sin nada para Algete, no).
    /// </summary>
    public static class CalculoFinRuta
    {
        public static ResultadoFinRuta Calcular(EntradaFinRuta e)
        {
            if (e == null)
            {
                throw new ArgumentNullException(nameof(e));
            }
            IReadOnlyList<double> paradas = e.MinutosParada ?? new double[0];
            IReadOnlyList<double> tramos = e.MinutosTramos ?? new double[0];
            IReadOnlyList<double> km = e.KmTramos ?? new double[0];
            double factor = e.FactorConduccion > 0 ? e.FactorConduccion : 1;
            var resultado = new ResultadoFinRuta();

            double reloj = 0;
            for (int i = 0; i < paradas.Count; i++)
            {
                double tramo = Tramo(tramos, i) * factor;
                resultado.MinutosConduccion += tramo;
                reloj += tramo;
                resultado.Llegadas.Add(Minuto(e.Salida.AddMinutes(reloj)));
                double enParada = Math.Max(0, paradas[i]);
                resultado.MinutosParadas += enParada;
                reloj += enParada;
            }
            double vuelta = Tramo(tramos, paradas.Count) * factor;
            resultado.MinutosConduccion += vuelta;
            reloj += vuelta;
            resultado.Km = Enumerable.Range(0, paradas.Count + 1).Sum(i => Tramo(km, i));

            if (e.PasaPorAlgete)
            {
                resultado.MinutosDescarga = Math.Max(0, e.MinutosDescargaAlgete);
                reloj += resultado.MinutosDescarga;
            }
            resultado.FinEstimado = Redondear(e.Salida, reloj);
            if (e.PasaPorAlgete && e.MinutosAlgeteADestino.HasValue)
            {
                double aCasa = Math.Max(0, e.MinutosAlgeteADestino.Value) * factor;
                resultado.MinutosConduccion += aCasa;
                resultado.Km += Math.Max(0, e.KmAlgeteADestino ?? 0);
                resultado.LlegadaDestino = Redondear(e.Salida, reloj + aCasa);
            }
            else
            {
                // Acaba en Algete (llega antes de descargar) o en el destino sin pasar por Algete.
                resultado.LlegadaDestino = e.PasaPorAlgete ? Redondear(e.Salida, reloj - resultado.MinutosDescarga) : resultado.FinEstimado;
            }

            resultado.SeComparaConCierre = e.HoraCierre.HasValue && e.PasaPorAlgete;
            if (resultado.SeComparaConCierre && resultado.FinEstimado > e.HoraCierre.Value)
            {
                resultado.PasaDelCierre = true;
                resultado.MinutosDeMas = (int)Math.Ceiling((resultado.FinEstimado - e.HoraCierre.Value).TotalMinutes - 1e-9);
            }
            return resultado;
        }

        private static double Tramo(IReadOnlyList<double> tramos, int i)
            => i < tramos.Count && !double.IsNaN(tramos[i]) && !double.IsInfinity(tramos[i]) ? Math.Max(0, tramos[i]) : 0;

        /// <summary>Al minuto mÃ¡s cercano (medio minuto, hacia arriba).</summary>
        private static DateTime Redondear(DateTime salida, double minutos) => Minuto(salida.AddMinutes(minutos).AddSeconds(30));

        private static DateTime Minuto(DateTime hora) => new DateTime(hora.Year, hora.Month, hora.Day, hora.Hour, hora.Minute, 0);
    }

    /// <summary>NestoAPI#881: los textos del fin de ruta (de usted). PURO.</summary>
    public static class TextosFinRuta
    {
        /// <summary>«Fin previsto 17:40 · 40 min después del cierre» o «Fin previsto 15:10».</summary>
        public static string Aviso(DateTime fin, bool pasaDelCierre, int minutosDeMas)
        {
            string texto = $"Fin previsto {fin:H:mm}";
            return pasaDelCierre && minutosDeMas > 0 ? $"{texto} · {Duracion(minutosDeMas)} después del cierre" : texto;
        }

        /// <summary>«40 min», «1 h», «1 h 15 min».</summary>
        public static string Duracion(int minutos)
        {
            if (minutos < 60)
            {
                return $"{minutos} min";
            }
            int horas = minutos / 60;
            int resto = minutos % 60;
            return resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
        }
    }
}
