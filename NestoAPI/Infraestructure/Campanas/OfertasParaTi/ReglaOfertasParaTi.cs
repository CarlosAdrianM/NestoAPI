using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// «Ofertas para ti» (cualquier campaña): LA REGLA, pura. De las filas vigentes de una campaña saca las
    /// candidatas y, para un cliente, las ordena y elige como mucho N (por defecto 5, de 1 a 10).
    /// <list type="bullet">
    /// <item>Candidata = fila de tarifa por producto con Campana, vigente HOY (<see cref="Vigencia"/>), audiencia de profesional (1 o 2,
    /// lo que la tienda publica al profesional: <c>ProductoDTO.CalcularDescuentosPorAudiencia</c>; el 0 es «no se anuncia» y el 3 está
    /// prohibido), ficha con Estado ≥ 0, ni muestra (MMP) ni materia prima (MTP) y con descuento. Un producto en dos campañas: la de más.</item>
    /// <item>Fuera lo que el cliente ya ha comprado desde que empezó la campaña y lo que tiene en un pedido pendiente.</item>
    /// <item>Orden de la regla: 1) habituales que le tocan (≥ 2 días de compra en 12 meses y, desde la última, ≥ 60 % de su ciclo),
    /// 2) los demás habituales, 3) de las familias que más compra (24 meses) por descuento y popularidad, 4) lo demás por popularidad y
    /// descuento. Si hay probabilidades del modelo 1, mandan ellas (las que no tiene, detrás en el orden de la regla).</item>
    /// <item>Diversidad: como mucho 2 de la misma familia entre los N si hay alternativas.</item>
    /// </list>
    /// El stock (disponible &gt; 0 en ALG+REI+ALC de las empresas 1 y 3) lo filtra el servicio entre la evaluación y la elección
    /// (<see cref="ConStock"/>), porque se lee por tandas y solo de lo que hace falta.
    /// </summary>
    public static class ReglaOfertasParaTi
    {
        public const int MAXIMO_POR_DEFECTO = 5;
        public const int MAXIMO_TOPE = 10;
        public const int MESES_HABITUAL = 12;
        public const int MESES_FAMILIAS = 24;
        public const int DIAS_MINIMOS_HABITUAL = 2;
        public const decimal FRACCION_CICLO = 0.6m;
        public const int MAXIMO_POR_FAMILIA = 2;
        /// <summary>Las familias que cuentan como «sus marcas»: las de más importe en 24 meses.</summary>
        public const int FAMILIAS_QUE_CUENTAN = 5;
        /// <summary>Las que se le pasan al modelo (las primeras de la regla con stock).</summary>
        public const int CANDIDATOS_MODELO = 25;
        /// <summary>Desde cuántos clientes en 12 meses un producto es «de lo más pedido».</summary>
        public const int CLIENTES_POPULAR = 20;

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>1..10; sin dato o fuera de rango, el más cercano (por defecto 5).</summary>
        public static int Maximo(int? pedido)
            => pedido == null ? MAXIMO_POR_DEFECTO : Math.Max(1, Math.Min(MAXIMO_TOPE, pedido.Value));

        /// <summary>Lo que la tienda publica al profesional (1 = solo profesionales, 2 = los dos).</summary>
        public static bool AudienciaParaProfesional(byte audiencia) => audiencia == 1 || audiencia == 2;

        /// <summary>El descuento de la fila en tanto por uno: Descuento o, con precio fijo, 1 − Precio/PVP (entre 0 y 1).</summary>
        public static decimal DescuentoDeFila(FilaCampanaOferta fila)
        {
            if (fila == null)
            {
                return 0;
            }
            decimal descuento = fila.Descuento > 0
                ? fila.Descuento
                : (fila.Precio > 0 && fila.PVP > 0 ? 1 - fila.Precio.Value / fila.PVP.Value : 0);
            return Math.Max(0, Math.Min(1, descuento));
        }

        /// <summary>Si la fila puede dar una oferta hoy (sin mirar al cliente ni el stock).</summary>
        public static bool FilaValida(FilaCampanaOferta fila, DateTime hoy)
        {
            return fila != null
                && !string.IsNullOrWhiteSpace(fila.Producto)
                && !string.IsNullOrWhiteSpace(fila.Campana)
                && Vigencia.EsVigente(fila.FechaDesde, fila.FechaHasta, hoy.Date, 0)
                && AudienciaParaProfesional(fila.AudienciaOferta)
                && fila.Estado.HasValue && fila.Estado.Value >= 0
                && !Igual(fila.Grupo, Constantes.Productos.GRUPO_MATERIAS_PRIMAS)
                && !Igual(fila.SubGrupo, Constantes.Productos.SUBGRUPO_MUESTRAS)
                && DescuentoDeFila(fila) > 0;
        }

        /// <summary>
        /// Las candidatas de la campaña (o de todas las vigentes si <paramref name="campana"/> va vacía), una por producto (la de más
        /// descuento; a igualdad, la que acaba antes y luego por nombre de campaña). <paramref name="popularidad"/>: producto → clientes
        /// en 12 meses.
        /// </summary>
        public static List<CandidataOferta> Candidatas(IEnumerable<FilaCampanaOferta> filas, string campana, DateTime hoy,
            IDictionary<string, int> popularidad = null)
        {
            string laCampana = campana?.Trim();
            return (filas ?? Enumerable.Empty<FilaCampanaOferta>())
                .Where(f => FilaValida(f, hoy))
                .Where(f => string.IsNullOrEmpty(laCampana) || Igual(f.Campana, laCampana))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(DescuentoDeFila)
                    .ThenBy(f => f.FechaHasta ?? DateTime.MaxValue)
                    .ThenBy(f => f.Campana, StringComparer.OrdinalIgnoreCase)
                    .First())
                .Select(f => new CandidataOferta
                {
                    Producto = f.Producto.Trim(),
                    Nombre = f.Nombre?.Trim(),
                    Familia = f.Familia?.Trim(),
                    NombreFamilia = f.NombreFamilia?.Trim(),
                    Campana = f.Campana.Trim(),
                    Descuento = Math.Round(DescuentoDeFila(f), 4, MidpointRounding.AwayFromZero),
                    FechaDesde = f.FechaDesde?.Date,
                    FechaHasta = f.FechaHasta?.Date,
                    FechaHastaExacta = f.FechaHasta,
                    Popularidad = popularidad != null && popularidad.TryGetValue(f.Producto.Trim(), out int p) ? p : 0
                })
                .OrderBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// La campaña que da título a la respuesta cuando no se pide ninguna: la de más candidatas (a igualdad, la que acaba antes y
        /// luego por nombre). Null si no hay ninguna.
        /// </summary>
        public static string CampanaPrincipal(IEnumerable<CandidataOferta> candidatas)
            => (candidatas ?? Enumerable.Empty<CandidataOferta>())
                .GroupBy(c => c.Campana, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Min(c => c.FechaHasta ?? DateTime.MaxValue))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key)
                .FirstOrDefault();

        /// <summary>El fin de una campaña: el más tardío de sus filas; null si alguna no tiene fin (o no hay filas).</summary>
        public static DateTime? FinDeCampana(IEnumerable<CandidataOferta> candidatas, string campana)
        {
            List<CandidataOferta> suyas = (candidatas ?? Enumerable.Empty<CandidataOferta>()).Where(c => Igual(c.Campana, campana)).ToList();
            if (suyas.Count == 0 || suyas.Any(c => c.FechaHasta == null))
            {
                return null;
            }
            return suyas.Max(c => c.FechaHasta.Value);
        }

        /// <summary>
        /// Las candidatas que valen para este cliente, en el orden de la regla, con su motivo, su texto (de tú) y la cantidad sugerida.
        /// Fuera lo comprado desde el inicio de su campaña (día de albarán ≥ FechaDesde) y lo que tiene pendiente.
        /// </summary>
        public static List<CandidataEvaluada> Evaluar(IEnumerable<CandidataOferta> candidatas, HistorialClienteOfertas historial, DateTime hoy)
        {
            DateTime dia = hoy.Date;
            HistorialClienteOfertas h = historial ?? new HistorialClienteOfertas();
            List<CompraDiaReposicion> compras = (h.Compras ?? new List<CompraDiaReposicion>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto) && c.Dia.Date < dia.AddDays(1))
                .ToList();
            Dictionary<string, List<CompraDiaReposicion>> porProducto = compras
                .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            ISet<string> pendientes = new HashSet<string>((h.Pendientes ?? new HashSet<string>()).Where(p => p != null).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> rangoFamilia = RangoFamilias(compras, h.FamiliaDe, dia);

            var evaluadas = new List<Tuple<CandidataEvaluada, decimal>>();
            foreach (CandidataOferta c in candidatas ?? Enumerable.Empty<CandidataOferta>())
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Producto) || pendientes.Contains(c.Producto.Trim()))
                {
                    continue;
                }
                porProducto.TryGetValue(c.Producto.Trim(), out List<CompraDiaReposicion> suyas);
                suyas = suyas ?? new List<CompraDiaReposicion>();
                if (c.FechaDesde.HasValue && suyas.Any(s => s.Dia.Date >= c.FechaDesde.Value.Date))
                {
                    continue; // ya lo ha pedido desde que empezó la campaña
                }
                RitmoCompra ritmo = Ritmo(suyas, dia);
                var evaluada = new CandidataEvaluada { Candidata = c };
                decimal clave;
                if (ritmo.Habitual)
                {
                    evaluada.CantidadSugerida = Math.Max(1, ritmo.CantidadHabitual);
                    if (ritmo.LeToca)
                    {
                        evaluada.Grupo = 1;
                        evaluada.Motivo = MotivosOfertaParaTi.TE_TOCA;
                        evaluada.Texto = $"Lo pides cada {ritmo.Ciclo} días y llevas {ritmo.DiasDesdeUltima} sin pedirlo";
                        clave = ritmo.Ciclo > 0 ? (decimal)ritmo.DiasDesdeUltima / ritmo.Ciclo : ritmo.DiasDesdeUltima;
                    }
                    else
                    {
                        evaluada.Grupo = 2;
                        evaluada.Motivo = MotivosOfertaParaTi.LO_SUELES_PEDIR;
                        evaluada.Texto = $"Lo has pedido {ritmo.DiasUltimoAnio} veces en el último año";
                        clave = ritmo.DiasUltimoAnio;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(c.Familia) && rangoFamilia.ContainsKey(c.Familia.Trim()))
                {
                    evaluada.Grupo = 3;
                    evaluada.Motivo = MotivosOfertaParaTi.DE_TUS_MARCAS;
                    string familia = FormatoTitulo(string.IsNullOrWhiteSpace(c.NombreFamilia) ? c.Familia : c.NombreFamilia);
                    evaluada.Texto = $"De {familia}, que sueles comprar";
                    clave = 0;
                }
                else
                {
                    evaluada.Grupo = 4;
                    evaluada.Motivo = MotivosOfertaParaTi.TE_PUEDE_INTERESAR;
                    evaluada.Texto = c.Popularidad >= CLIENTES_POPULAR
                        ? "De lo más pedido por otros profesionales"
                        : "Una oferta que te puede interesar";
                    clave = 0;
                }
                evaluadas.Add(Tuple.Create(evaluada, clave));
            }

            return evaluadas
                .OrderBy(e => e.Item1.Grupo)
                // 1 y 2: el que más le toca / el más habitual primero; 3 y 4 empatan aquí
                .ThenByDescending(e => e.Item2)
                .ThenByDescending(e => e.Item1.Grupo == 4 ? e.Item1.Candidata.Popularidad : 0)
                .ThenByDescending(e => e.Item1.Candidata.Descuento)
                .ThenByDescending(e => e.Item1.Candidata.Popularidad)
                .ThenBy(e => e.Item1.Candidata.Producto, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Item1)
                .ToList();
        }

        /// <summary>
        /// Las primeras <paramref name="maximo"/> en orden que tienen disponible &gt; 0 (<paramref name="disponibles"/>: producto →
        /// unidades; un producto que no está se trata como sin stock).
        /// </summary>
        public static List<CandidataEvaluada> ConStock(IEnumerable<CandidataEvaluada> ordenadas, IDictionary<string, int> disponibles, int maximo)
            => (ordenadas ?? Enumerable.Empty<CandidataEvaluada>())
                .Where(e => disponibles != null && disponibles.TryGetValue(e.Candidata.Producto.Trim(), out int d) && d > 0)
                .Take(Math.Max(0, maximo))
                .ToList();

        /// <summary>
        /// El orden final: con probabilidades (modelo 1), las que tienen primero de más a menos y detrás las demás en el orden de la
        /// regla; sin ellas, el de la regla. Luego la diversidad (como mucho <see cref="MAXIMO_POR_FAMILIA"/> por familia mientras haya
        /// alternativas; las que se apartan van detrás) y se cogen <paramref name="maximo"/>.
        /// </summary>
        public static List<OfertaElegida> Elegir(IReadOnlyList<CandidataEvaluada> ordenadas, int maximo, IDictionary<string, float> probabilidades = null)
            => Ordenar(ordenadas, probabilidades).Take(Math.Max(0, maximo)).ToList();

        /// <summary>Como <see cref="Elegir"/> pero entero (para quien todavía puede descartar alguna, como el endpoint por el precio).</summary>
        public static List<OfertaElegida> Ordenar(IReadOnlyList<CandidataEvaluada> ordenadas, IDictionary<string, float> probabilidades = null)
        {
            List<CandidataEvaluada> lista = (ordenadas ?? new List<CandidataEvaluada>()).Where(e => e?.Candidata != null).ToList();
            bool conModelo = probabilidades != null && probabilidades.Count > 0;
            List<OfertaElegida> enOrden = lista
                .Select((e, i) => new
                {
                    e,
                    i,
                    p = conModelo && probabilidades.TryGetValue(e.Candidata.Producto.Trim(), out float prob) ? (float?)prob : null
                })
                .OrderBy(x => x.p.HasValue ? 0 : 1)
                .ThenByDescending(x => x.p ?? 0)
                .ThenBy(x => x.i)
                .Select(x => new OfertaElegida
                {
                    Candidata = x.e.Candidata,
                    Motivo = x.e.Motivo,
                    Texto = x.e.Texto,
                    CantidadSugerida = x.e.CantidadSugerida,
                    Habitual = x.e.Habitual,
                    Probabilidad = x.p
                })
                .ToList();
            return Diversificar(enOrden);
        }

        /// <summary>Como mucho 2 por familia delante; las que sobran, detrás en su orden (solo salen si no hay alternativas).</summary>
        public static List<OfertaElegida> Diversificar(IReadOnlyList<OfertaElegida> enOrden)
        {
            var delante = new List<OfertaElegida>();
            var detras = new List<OfertaElegida>();
            var porFamilia = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (OfertaElegida o in enOrden ?? new List<OfertaElegida>())
            {
                string familia = o.Candidata.Familia?.Trim() ?? string.Empty;
                porFamilia.TryGetValue(familia, out int n);
                if (familia.Length > 0 && n >= MAXIMO_POR_FAMILIA)
                {
                    detras.Add(o);
                    continue;
                }
                porFamilia[familia] = n + 1;
                delante.Add(o);
            }
            delante.AddRange(detras);
            return delante;
        }

        /// <summary>Lo que se sabe de cómo compra un producto (solo días anteriores a hoy).</summary>
        public class RitmoCompra
        {
            public bool Habitual { get; set; }
            public bool LeToca { get; set; }
            public int Ciclo { get; set; }
            public int DiasDesdeUltima { get; set; }
            public int DiasUltimoAnio { get; set; }
            public int CantidadHabitual { get; set; } = 1;
        }

        /// <summary>
        /// Habitual = 2 días de compra o más en 12 meses. Ciclo = mediana de días entre compras de esos 12 meses (redondeada); le toca
        /// si desde la última han pasado ≥ 60 % del ciclo. Cantidad habitual = mediana de lo que se lleva cada día.
        /// </summary>
        public static RitmoCompra Ritmo(IEnumerable<CompraDiaReposicion> compras, DateTime hoy)
        {
            DateTime dia = hoy.Date;
            DateTime desde = dia.AddMonths(-MESES_HABITUAL);
            List<IGrouping<DateTime, CompraDiaReposicion>> dias = (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => c != null && c.Dia.Date >= desde && c.Dia.Date < dia)
                .GroupBy(c => c.Dia.Date)
                .OrderBy(g => g.Key)
                .ToList();
            var ritmo = new RitmoCompra { DiasUltimoAnio = dias.Count };
            if (dias.Count < DIAS_MINIMOS_HABITUAL)
            {
                return ritmo;
            }
            ritmo.Habitual = true;
            List<double> intervalos = new List<double>();
            for (int i = 1; i < dias.Count; i++)
            {
                intervalos.Add((dias[i].Key - dias[i - 1].Key).TotalDays);
            }
            ritmo.Ciclo = Math.Max(1, (int)Math.Round(Mediana(intervalos), MidpointRounding.AwayFromZero));
            ritmo.DiasDesdeUltima = (int)(dia - dias.Last().Key).TotalDays;
            ritmo.LeToca = ritmo.DiasDesdeUltima >= FRACCION_CICLO * ritmo.Ciclo;
            ritmo.CantidadHabitual = Math.Max(1, (int)Math.Round(Mediana(dias.Select(d => (double)d.Sum(c => c.Cantidad)).ToList()),
                MidpointRounding.AwayFromZero));
            return ritmo;
        }

        /// <summary>Familia → puesto (0 = la que más) de las <see cref="FAMILIAS_QUE_CUENTAN"/> de más importe en 24 meses.</summary>
        public static Dictionary<string, int> RangoFamilias(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, string> familiaDe, DateTime hoy)
        {
            DateTime desde = hoy.Date.AddMonths(-MESES_FAMILIAS);
            return (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => c != null && c.Dia.Date >= desde && !string.IsNullOrWhiteSpace(c.Producto))
                .Select(c => new
                {
                    Familia = familiaDe != null && familiaDe.TryGetValue(c.Producto.Trim(), out string f) ? f?.Trim() : null,
                    c.BaseImponible
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Familia))
                .GroupBy(x => x.Familia, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { g.Key, Importe = g.Sum(x => x.BaseImponible) })
                .Where(x => x.Importe > 0)
                .OrderByDescending(x => x.Importe)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(FAMILIAS_QUE_CUENTAN)
                .Select((x, i) => new { x.Key, i })
                .ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>El nombre de la campaña sin el año («Black Friday 2026» → «Black Friday»). Vacío → null.</summary>
        public static string Titulo(string campana)
        {
            if (string.IsNullOrWhiteSpace(campana))
            {
                return null;
            }
            string sinAnio = Regex.Replace(campana, @"(?<!\d)(19|20)\d{2}(?!\d)", " ");
            string limpio = Regex.Replace(sinAnio, @"\s+", " ").Trim(' ', '-', '–', '·', '/', '_', ',');
            return limpio.Length == 0 ? campana.Trim() : limpio;
        }

        /// <summary>«Hasta el domingo 30 de noviembre» (con el año si no es el de hoy). Null sin fecha.</summary>
        public static string TextoHasta(DateTime? hasta, DateTime hoy)
        {
            if (hasta == null)
            {
                return null;
            }
            DateTime d = hasta.Value.Date;
            string formato = d.Year == hoy.Year ? "dddd d 'de' MMMM" : "dddd d 'de' MMMM 'de' yyyy";
            return "Hasta el " + d.ToString(formato, ES);
        }

        private static readonly HashSet<string> PALABRAS_MENORES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "del", "la", "las", "el", "los", "y", "e", "o", "con", "sin", "para", "por", "en", "al", "a"
        };

        /// <summary>
        /// Nombres de Nesto en mayúsculas («SICUREZZA BB CREAM 50 ML») en formato título («Sicurezza BB Cream 50 ml»): las palabras sin
        /// vocales se quedan en mayúsculas (BB, SPF), las unidades y lo que lleva cifras en minúscula y los artículos y preposiciones en
        /// minúscula salvo al principio. Si ya trae minúsculas, se deja como viene (sin espacios de más).
        /// </summary>
        public static string FormatoTitulo(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return texto?.Trim();
            }
            string[] palabras = texto.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (texto.Any(char.IsLower))
            {
                return string.Join(" ", palabras);
            }
            var resultado = new List<string>();
            for (int i = 0; i < palabras.Length; i++)
            {
                string p = palabras[i];
                string minuscula = p.ToLower(ES);
                if (!p.Any(char.IsLetter))
                {
                    resultado.Add(p);
                }
                else if (p.Any(char.IsDigit) || EsUnidad(minuscula))
                {
                    resultado.Add(minuscula);
                }
                else if (!minuscula.Any(EsVocal))
                {
                    resultado.Add(p);
                }
                else if (i > 0 && PALABRAS_MENORES.Contains(minuscula))
                {
                    resultado.Add(minuscula);
                }
                else
                {
                    resultado.Add(char.ToUpper(minuscula[0], ES) + minuscula.Substring(1));
                }
            }
            return string.Join(" ", resultado);
        }

        private static bool EsUnidad(string palabra)
            => palabra == "ml" || palabra == "gr" || palabra == "g" || palabra == "kg" || palabra == "l" || palabra == "cl" || palabra == "ud" || palabra == "uds";

        private static bool EsVocal(char c) => "aeiouáéíóúüy".IndexOf(c) >= 0;

        internal static double Mediana(IList<double> valores)
        {
            if (valores == null || valores.Count == 0)
            {
                return 0;
            }
            List<double> orden = valores.OrderBy(v => v).ToList();
            int medio = orden.Count / 2;
            return orden.Count % 2 == 1 ? orden[medio] : (orden[medio - 1] + orden[medio]) / 2;
        }

        internal static bool Igual(string a, string b)
            => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
