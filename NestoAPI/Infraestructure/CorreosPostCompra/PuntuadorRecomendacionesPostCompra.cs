using NestoAPI.Infraestructure.SugerenciasProductos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#707 (corte 2): los datos de las sugerencias de la plantilla (#669) con los que se ordenan los recomendados
    /// de un correo post-compra. Cualquiera de las listas puede venir vacía (job sin calcular o tabla sin crear): entonces
    /// ese componente vale 0 y, si todos valen 0, se queda el orden de la consulta.
    /// </summary>
    public class DatosPuntuacionRecomendados
    {
        /// <summary>Los productos principales del correo (lo que ha comprado esta semana y sale en el correo).</summary>
        public IEnumerable<string> ProductosComprados { get; set; }
        /// <summary>SugerenciasProductosPares con ProductoA entre los comprados (CalculadoraParesCompradosJuntos).</summary>
        public IEnumerable<ParCompradoJunto> Pares { get; set; }
        /// <summary>SugerenciasProductosAfinidad del cliente (item-kNN: productos parecidos a lo que compra y que no compra).</summary>
        public IEnumerable<AfinidadCliente> Afinidad { get; set; }
        /// <summary>SugerenciasProductosPopularidad de los candidatos.</summary>
        public IEnumerable<PopularidadProducto> Popularidad { get; set; }
    }

    /// <summary>NestoAPI#707: la puntuación de un candidato y la regla que más ha pesado (se guarda en cada fila enviada).</summary>
    public class PuntuacionCandidato
    {
        public string Regla { get; set; }
        /// <summary>Entre 0 y 1, con 4 decimales. Null con <see cref="PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL"/>.</summary>
        public decimal? Puntuacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#707 (corte 2): ordena los candidatos a recomendado del correo post-compra con lo que ya calcula el job de las
    /// sugerencias de la plantilla (#669), en vez de dejarlos en el orden de la consulta.
    /// <para>Puntuacion = 0,5 × pares + 0,3 × afinidad + 0,2 × popularidad, cada componente entre 0 y 1:</para>
    /// <list type="bullet">
    /// <item><b>pares</b>: la mayor Confianza (P(candidato | comprado)) de SugerenciasProductosPares entre los productos que ha
    /// comprado y el candidato: «quien compra esto, compra también aquello».</item>
    /// <item><b>afinidad</b>: la Afinidad del cliente con el candidato (SugerenciasProductosAfinidad), dividida por la mayor de
    /// los candidatos de ese correo.</item>
    /// <item><b>popularidad</b>: log(1 + Clientes12Meses) del candidato dividido por el mayor de los candidatos de ese correo.</item>
    /// </list>
    /// <para>La regla de cada fila es el componente que más aporta («pares», «afinidad» o «popularidad»); si todos valen 0,
    /// «orden_original» (sin puntuación) y esos candidatos van detrás, en el orden de la consulta. A igualdad de puntuación,
    /// también manda el orden de la consulta.</para>
    /// <para>El corte 3 (ML.NET) sustituirá esta fórmula por un modelo: por eso cada fila lleva <see cref="VERSION_MODELO"/>.</para>
    /// </summary>
    public static class PuntuadorRecomendacionesPostCompra
    {
        public const string VERSION_MODELO = "reglas-1";

        public const string REGLA_PARES = "pares";
        public const string REGLA_AFINIDAD = "afinidad";
        public const string REGLA_POPULARIDAD = "popularidad";
        public const string REGLA_ORDEN_ORIGINAL = "orden_original";

        internal const decimal PESO_PARES = 0.5m;
        internal const decimal PESO_AFINIDAD = 0.3m;
        internal const decimal PESO_POPULARIDAD = 0.2m;

        /// <summary>
        /// La puntuación de cada candidato (clave sin espacios, sin distinguir mayúsculas). Nunca null: sin datos, todos
        /// «orden_original».
        /// </summary>
        public static Dictionary<string, PuntuacionCandidato> Puntuar(IEnumerable<string> candidatos, DatosPuntuacionRecomendados datos)
        {
            var resultado = new Dictionary<string, PuntuacionCandidato>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (candidatos ?? Enumerable.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (lista.Count == 0)
            {
                return resultado;
            }

            var comprados = new HashSet<string>(
                (datos?.ProductosComprados ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);

            // pares: la mayor confianza desde cualquiera de los comprados
            var pares = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (ParCompradoJunto par in datos?.Pares ?? Enumerable.Empty<ParCompradoJunto>())
            {
                if (par == null || string.IsNullOrWhiteSpace(par.ProductoA) || string.IsNullOrWhiteSpace(par.ProductoB)
                    || !comprados.Contains(par.ProductoA.Trim()))
                {
                    continue;
                }
                string b = par.ProductoB.Trim();
                decimal confianza = Math.Max(0m, Math.Min(1m, par.Confianza));
                if (!pares.TryGetValue(b, out decimal actual) || confianza > actual)
                {
                    pares[b] = confianza;
                }
            }

            var afinidad = (datos?.Afinidad ?? Enumerable.Empty<AfinidadCliente>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Producto) && a.Afinidad > 0)
                .GroupBy(a => a.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(a => a.Afinidad), StringComparer.OrdinalIgnoreCase);
            decimal maxAfinidad = lista.Select(c => afinidad.TryGetValue(c, out decimal a) ? a : 0m).DefaultIfEmpty(0m).Max();

            var popularidad = (datos?.Popularidad ?? Enumerable.Empty<PopularidadProducto>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto) && p.Clientes12Meses > 0)
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Math.Log(1 + g.Max(p => p.Clientes12Meses)), StringComparer.OrdinalIgnoreCase);
            double maxPopularidad = lista.Select(c => popularidad.TryGetValue(c, out double p) ? p : 0d).DefaultIfEmpty(0d).Max();

            foreach (string candidato in lista)
            {
                decimal componentePares = pares.TryGetValue(candidato, out decimal cp) ? cp : 0m;
                decimal componenteAfinidad = maxAfinidad > 0 && afinidad.TryGetValue(candidato, out decimal ca) ? ca / maxAfinidad : 0m;
                decimal componentePopularidad = maxPopularidad > 0 && popularidad.TryGetValue(candidato, out double cpop)
                    ? (decimal)(cpop / maxPopularidad) : 0m;

                decimal aportePares = PESO_PARES * componentePares;
                decimal aporteAfinidad = PESO_AFINIDAD * componenteAfinidad;
                decimal aportePopularidad = PESO_POPULARIDAD * componentePopularidad;
                decimal total = aportePares + aporteAfinidad + aportePopularidad;

                if (total <= 0)
                {
                    resultado[candidato] = new PuntuacionCandidato { Regla = REGLA_ORDEN_ORIGINAL, Puntuacion = null };
                    continue;
                }

                string regla = aportePares >= aporteAfinidad && aportePares >= aportePopularidad ? REGLA_PARES
                    : aporteAfinidad >= aportePopularidad ? REGLA_AFINIDAD
                    : REGLA_POPULARIDAD;
                resultado[candidato] = new PuntuacionCandidato
                {
                    Regla = regla,
                    Puntuacion = Math.Round(total, 4, MidpointRounding.AwayFromZero)
                };
            }
            return resultado;
        }

        /// <summary>
        /// Los candidatos de mayor a menor puntuación; los «orden_original» detrás. A igualdad, el orden en que llegan (orden
        /// estable: OrderBy de LINQ lo es).
        /// </summary>
        public static List<T> Ordenar<T>(IList<T> candidatos, Func<T, string> producto, IDictionary<string, PuntuacionCandidato> puntuaciones)
        {
            if (candidatos == null)
            {
                return new List<T>();
            }
            if (puntuaciones == null || puntuaciones.Count == 0)
            {
                return candidatos.ToList();
            }
            return candidatos
                .Select((c, i) => new { Candidato = c, Indice = i, Puntuacion = Leer(puntuaciones, producto(c))?.Puntuacion })
                .OrderByDescending(x => x.Puntuacion.HasValue)
                .ThenByDescending(x => x.Puntuacion ?? 0m)
                .ThenBy(x => x.Indice)
                .Select(x => x.Candidato)
                .ToList();
        }

        internal static PuntuacionCandidato Leer(IDictionary<string, PuntuacionCandidato> puntuaciones, string producto)
            => puntuaciones != null && !string.IsNullOrWhiteSpace(producto) && puntuaciones.TryGetValue(producto.Trim(), out PuntuacionCandidato p)
                ? p : null;
    }
}
