using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    public static class GruposCalibracion
    {
        public const string EXT = "Acabó en EXT";
        public const string ESTADO_4 = "Estado 4";
        public const string SIN_VENTA = "Sin venta 12 meses";
        public const string VIVO = "Vivo";
    }

    /// <summary>Cómo estaba un producto 6, 9 y 12 meses antes de su «evento».</summary>
    public class FilaCalibracion
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        /// <summary>Mes del evento: entrada en EXT, último mes con venta (estado 4 y sin venta 12 meses) o el de referencia (vivos).</summary>
        public DateTime MesEvento { get; set; }
        /// <summary>Clave: meses antes del evento (6, 9, 12).</summary>
        public Dictionary<int, decimal?> Cobertura { get; set; } = new Dictionary<int, decimal?>();
        public Dictionary<int, decimal?> Pendiente { get; set; } = new Dictionary<int, decimal?>();
    }

    public class ResumenCalibracion
    {
        public string Grupo { get; set; }
        public int MesesAntes { get; set; }
        public int Productos { get; set; }
        public int SinVenta6Meses { get; set; }
        public decimal? MedianaCobertura { get; set; }
        public decimal? MedianaPendiente { get; set; }
    }

    /// <summary>Una regla candidata: qué % de muertos habría marcado y qué % de vivos (falsas alarmas).</summary>
    public class UmbralCalibracion
    {
        public string Regla { get; set; }
        public int MesesAntes { get; set; }
        public decimal PorcentajeMuertosMarcados { get; set; }
        public decimal PorcentajeVivosMarcados { get; set; }
        public int Muertos { get; set; }
        public int Vivos { get; set; }
    }

    public class ResultadoCalibracion
    {
        public List<FilaCalibracion> Detalle { get; set; } = new List<FilaCalibracion>();
        public List<ResumenCalibracion> Resumen { get; set; } = new List<ResumenCalibracion>();
        public List<UmbralCalibracion> Umbrales { get; set; } = new List<UmbralCalibracion>();
    }

    /// <summary>
    /// NestoAPI#630: backtest para calibrar los umbrales. Para los productos que en los últimos 3 años acabaron en EXT, en estado
    /// 4 o sin venta 12 meses, qué cobertura y pendiente relativa tenían 6, 9 y 12 meses antes, frente a los vivos (estado 0, con
    /// venta en los 3 últimos meses) medidos 6, 9 y 12 meses antes del mes de referencia.
    /// <para>Decidido aquí: la fecha del evento es la última entrada en EXT; para «sin venta» el último mes con venta (el momento
    /// en que dejó de venderse, con 12 meses de confirmación detrás); para estado 4, como no hay fecha del cambio de estado en la
    /// BD, también su último mes con venta dentro de los 36 meses (decisión de Carlos 16/10/26: medirlo desde el mes de referencia
    /// metía ~1.100 productos muertos hace años que salían iguales a 6, 9 y 12 meses); el estado 4 sin venta en esa ventana no
    /// entra. Los vivos se miden desde el mes de referencia. Las pendientes se desestacionalizan con los índices de hoy de la familia.</para>
    /// <para>Orden de los grupos: EXT (tiene fecha propia) → estado 4 → sin venta 12 meses → vivo; el estado 4 va antes que «sin
    /// venta» para que no se cuente como tal aunque lleve 12 meses sin vender.</para>
    /// </summary>
    public static class CalibracionTendencias
    {
        public static readonly int[] HORIZONTES = { 6, 9, 12 };
        private const int VENTANA_MESES = 36;

        public static string Grupo(DatosProductoTendencia d, DateTime mesReferencia, out int indiceEvento)
        {
            const int FIN = DatosProductoTendencia.ULTIMO;
            indiceEvento = FIN;
            DateTime fechaCorte = mesReferencia.AddMonths(1);
            if (d.StockExt > 0 && d.UltimaEntradaExt.HasValue && d.UltimaEntradaExt.Value < fechaCorte
                && d.UltimaEntradaExt.Value >= mesReferencia.AddMonths(-(VENTANA_MESES - 1)))
            {
                DateTime mes = new DateTime(d.UltimaEntradaExt.Value.Year, d.UltimaEntradaExt.Value.Month, 1);
                indiceEvento = FIN - ((mesReferencia.Year - mes.Year) * 12 + mesReferencia.Month - mes.Month);
                return GruposCalibracion.EXT;
            }
            int? ultimaVenta = CalculadoraSeries.UltimoMesConVenta(d.Ventas, FIN);
            if (d.Estado == 4)
            {
                // Sin fecha del cambio de estado en la BD: el evento es su último mes con venta dentro de la ventana. Sin venta en
                // la ventana no aporta información (lleva años muerto: 6, 9 y 12 meses antes saldría igual) y no entra.
                if (!ultimaVenta.HasValue || ultimaVenta.Value < FIN - (VENTANA_MESES - 1))
                {
                    return null;
                }
                indiceEvento = ultimaVenta.Value;
                return GruposCalibracion.ESTADO_4;
            }
            if (ultimaVenta.HasValue && ultimaVenta.Value <= FIN - 12 && ultimaVenta.Value >= FIN - 12 - (VENTANA_MESES - 1))
            {
                indiceEvento = ultimaVenta.Value;
                return GruposCalibracion.SIN_VENTA;
            }
            if (d.Estado == 0 && d.StockExt <= 0 && CalculadoraSeries.Suma(d.Ventas, FIN, 3) > 0)
            {
                return GruposCalibracion.VIVO;
            }
            return null;
        }

        /// <summary>Cobertura (meses) en la posición <paramref name="i"/>: stock a fin de ese mes ÷ venta media de los 6 que acaban ahí.</summary>
        public static decimal? CoberturaEn(DatosProductoTendencia d, int i)
        {
            decimal media = CalculadoraSeries.Media(d.Ventas, i, 6);
            return media > 0 ? Math.Round(d.StockFinMes[i] / media, 1) : (decimal?)null;
        }

        public static ResultadoCalibracion Calcular(IEnumerable<DatosProductoTendencia> productos, Func<string, decimal[]> indicesFamilia,
            DateTime mesReferencia, ParametrosTendencias p)
        {
            p = p ?? new ParametrosTendencias();
            ResultadoCalibracion resultado = new ResultadoCalibracion();
            foreach (DatosProductoTendencia d in productos ?? Enumerable.Empty<DatosProductoTendencia>())
            {
                string grupo = Grupo(d, mesReferencia, out int evento);
                if (grupo == null)
                {
                    continue;
                }
                FilaCalibracion fila = new FilaCalibracion
                {
                    Producto = d.Producto?.Trim(),
                    Nombre = d.Nombre?.Trim(),
                    Grupo = grupo,
                    MesEvento = mesReferencia.AddMonths(evento - DatosProductoTendencia.ULTIMO)
                };
                decimal[] indices = indicesFamilia?.Invoke(d.Familia?.Trim());
                bool alguno = false;
                foreach (int h in HORIZONTES)
                {
                    int i = evento - h;
                    if (i - 11 < 0)
                    {
                        continue;
                    }
                    alguno = true;
                    fila.Cobertura[h] = CoberturaEn(d, i);
                    fila.Pendiente[h] = CalculadoraSeries.PendienteRelativa(d.Ventas, i, indices, mesReferencia.AddMonths(i - DatosProductoTendencia.ULTIMO));
                }
                if (alguno)
                {
                    resultado.Detalle.Add(fila);
                }
            }

            foreach (string grupo in new[] { GruposCalibracion.EXT, GruposCalibracion.ESTADO_4, GruposCalibracion.SIN_VENTA, GruposCalibracion.VIVO })
            {
                foreach (int h in HORIZONTES)
                {
                    List<FilaCalibracion> filas = resultado.Detalle.Where(f => f.Grupo == grupo && f.Cobertura.ContainsKey(h)).ToList();
                    resultado.Resumen.Add(new ResumenCalibracion
                    {
                        Grupo = grupo,
                        MesesAntes = h,
                        Productos = filas.Count,
                        SinVenta6Meses = filas.Count(f => !f.Cobertura[h].HasValue),
                        MedianaCobertura = Mediana(filas.Select(f => f.Cobertura[h])),
                        MedianaPendiente = Mediana(filas.Select(f => f.Pendiente[h]))
                    });
                }
            }

            // Reglas candidatas: «sin venta en 6 meses» cuenta como cobertura infinita (marcado).
            var reglas = new List<(string nombre, Func<decimal?, decimal?, bool> marca)>
            {
                ("Cobertura > 6 meses", (c, s) => !c.HasValue || c.Value > 6),
                ("Cobertura > 9 meses", (c, s) => !c.HasValue || c.Value > 9),
                ("Cobertura > 12 meses", (c, s) => !c.HasValue || c.Value > 12),
                ("Pendiente <= -2 %/mes", (c, s) => s.HasValue && s.Value <= -2),
                ("Pendiente <= " + p.PendienteBaja.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + " %/mes (umbral actual)", (c, s) => s.HasValue && s.Value <= p.PendienteBaja),
                ("Pendiente <= -5 %/mes", (c, s) => s.HasValue && s.Value <= -5),
                ("Cobertura > " + p.MesesCoberturaEstado1 + " meses y pendiente <= umbral", (c, s) => (!c.HasValue || c.Value > p.MesesCoberturaEstado1) && s.HasValue && s.Value <= p.PendienteBaja)
            };
            foreach (int h in HORIZONTES)
            {
                List<FilaCalibracion> muertos = resultado.Detalle.Where(f => f.Grupo != GruposCalibracion.VIVO && f.Cobertura.ContainsKey(h)).ToList();
                List<FilaCalibracion> vivos = resultado.Detalle.Where(f => f.Grupo == GruposCalibracion.VIVO && f.Cobertura.ContainsKey(h)).ToList();
                foreach (var regla in reglas)
                {
                    resultado.Umbrales.Add(new UmbralCalibracion
                    {
                        Regla = regla.nombre,
                        MesesAntes = h,
                        Muertos = muertos.Count,
                        Vivos = vivos.Count,
                        PorcentajeMuertosMarcados = Porcentaje(muertos.Count(f => regla.marca(f.Cobertura[h], f.Pendiente[h])), muertos.Count),
                        PorcentajeVivosMarcados = Porcentaje(vivos.Count(f => regla.marca(f.Cobertura[h], f.Pendiente[h])), vivos.Count)
                    });
                }
            }
            return resultado;
        }

        internal static decimal? Mediana(IEnumerable<decimal?> valores)
        {
            List<decimal> lista = valores.Where(v => v.HasValue).Select(v => v.Value).OrderBy(v => v).ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            int medio = lista.Count / 2;
            return lista.Count % 2 == 1 ? lista[medio] : Math.Round((lista[medio - 1] + lista[medio]) / 2m, 2);
        }

        private static decimal Porcentaje(int parte, int total) => total == 0 ? 0m : Math.Round(100m * parte / total, 1);
    }
}
