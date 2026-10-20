using ModeloSugerenciasProductos.Datos;

namespace ModeloSugerenciasProductos.Reglas;

/// <summary>De dónde sale una sugerencia (va tal cual en la columna Motivo de SugerenciasProductosMostradas).</summary>
public static class Fuentes
{
    public const string COMPRADO_JUNTO = "CompradoJunto";
    public const string LE_TOCA = "LeToca";
    public const string POTENCIAR = "Potenciar";
    public const string PARECIDOS = "Parecidos";
    /// <summary>Lo más comprado del perfil del cliente: candidato del modelo (corte 2c), no se enseña como motivo.</summary>
    public const string PERFIL = "Perfil";

    /// <summary>El orden en que reglas-1 coge las fuentes al mezclar (Parecidos, la última: sin afinidad, todo como en el corte 1).</summary>
    public static readonly string[] ORDEN = { COMPRADO_JUNTO, LE_TOCA, POTENCIAR, PARECIDOS };
}

/// <summary>Un par «comprados juntos» A → B (A en el carrito, B el que se sugiere).</summary>
public sealed class ParComprado
{
    public string ProductoA { get; set; } = "";
    public string ProductoB { get; set; } = "";
    public int VecesJuntos { get; set; }
    public decimal Soporte { get; set; }
    public decimal Confianza { get; set; }
    public decimal Lift { get; set; }
}

/// <summary>Un producto al que, por su ritmo, ya le toca al cliente.</summary>
public sealed class ResultadoLeToca
{
    public string Producto { get; set; } = "";
    /// <summary>Días desde la última compra ÷ mediana (redondeado a 4 decimales).</summary>
    public decimal Puntuacion { get; set; }
    public double Mediana { get; set; }
    public int DiasDesdeUltima { get; set; }
}

/// <summary>Cuántos clientes de un perfil (grupo y subgrupo en el que más compran) se han llevado un producto.</summary>
public sealed class ProductoDePerfil
{
    public string Grupo { get; set; } = "";
    public string SubGrupo { get; set; } = "";
    public string Producto { get; set; } = "";
    public int Clientes { get; set; }
}

/// <summary>
/// NestoAPI#670/#671: las reglas puras del corte 1 (sin BD ni textos), compartidas por la API y el entrenamiento offline para
/// que reglas-1 se mida exactamente como funciona.
/// </summary>
public static class ReglasSugerencias
{
    public const int MINIMO_COMPRAS = 3;
    public const double FACTOR_ABANDONO = 3;
    public const string GRUPO_MATERIAS_PRIMAS = "MTP";
    public const string SUBGRUPO_MUESTRAS = "MMP";
    /// <summary>Un «habitual» de un cliente: ≥ 3 días de compra en 24 meses (lo mismo que pide «le toca»).</summary>
    public const int COMPRAS_HABITUAL = 3;

    /// <summary>Lo que nunca se sugiere: de baja, ficticio, materias primas y muestras.</summary>
    public static bool EsSugerible(short estado, bool ficticio, string? grupo, string? subGrupo)
        => estado >= 0 && !ficticio
            && !string.Equals(grupo?.Trim(), GRUPO_MATERIAS_PRIMAS, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subGrupo?.Trim(), SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);

    public static bool EsSugerible(FichaProducto? ficha)
        => ficha != null && EsSugerible(ficha.Estado, ficha.Ficticio, ficha.Grupo, ficha.SubGrupo);

    /// <summary>
    /// Mediana de los días entre compras consecutivas (días distintos). Null si no hay <paramref name="minimo"/> días de compra.
    /// </summary>
    public static double? IntervaloMediano(IEnumerable<DateTime>? dias, int minimo = MINIMO_COMPRAS)
    {
        List<DateTime> ordenados = (dias ?? Enumerable.Empty<DateTime>())
            .Select(d => d.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (ordenados.Count < minimo || ordenados.Count < 2)
        {
            return null;
        }
        List<double> intervalos = ordenados
            .Zip(ordenados.Skip(1), (a, b) => (b - a).TotalDays)
            .OrderBy(x => x)
            .ToList();
        int n = intervalos.Count;
        return n % 2 == 1
            ? intervalos[n / 2]
            : (intervalos[(n / 2) - 1] + intervalos[n / 2]) / 2.0;
    }

    /// <summary>
    /// «Ya le toca»: productos con al menos 3 días de compra en los que los días desde la última superan la mediana de días
    /// entre compras (y no la triplican). Puntuación: días desde la última ÷ mediana. Solo cuenta lo comprado hasta
    /// <paramref name="hoy"/> (incluido).
    /// </summary>
    public static List<ResultadoLeToca> LeToca(IEnumerable<Compra>? compras, DateTime hoy)
    {
        var resultado = new List<ResultadoLeToca>();
        foreach (IGrouping<string, Compra> producto in (compras ?? Enumerable.Empty<Compra>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Producto) && c.Dia.Date <= hoy.Date)
            .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            double? mediana = IntervaloMediano(producto.Select(c => c.Dia));
            if (!mediana.HasValue || mediana.Value < 1)
            {
                continue;
            }
            int diasDesdeUltima = (int)(hoy.Date - producto.Max(c => c.Dia).Date).TotalDays;
            if (diasDesdeUltima <= mediana.Value || diasDesdeUltima >= mediana.Value * FACTOR_ABANDONO)
            {
                continue;
            }
            resultado.Add(new ResultadoLeToca
            {
                Producto = producto.Key,
                Puntuacion = Math.Round((decimal)(diasDesdeUltima / mediana.Value), 4),
                Mediana = mediana.Value,
                DiasDesdeUltima = diasDesdeUltima
            });
        }
        return resultado
            .OrderByDescending(c => c.Puntuacion)
            .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// «Comprados juntos»: por cada producto que no está en el carrito, el mejor par (más confianza y, a igualdad, más lift)
    /// con un producto del carrito como antecedente. De más a menos confianza.
    /// </summary>
    public static List<ParComprado> CompradoJunto(IEnumerable<ParComprado>? pares, ISet<string>? carrito)
    {
        ISet<string> enCarrito = carrito ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (pares ?? Enumerable.Empty<ParComprado>())
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductoA) && !string.IsNullOrWhiteSpace(p.ProductoB)
                && enCarrito.Contains(p.ProductoA.Trim()) && !enCarrito.Contains(p.ProductoB.Trim()))
            .GroupBy(p => p.ProductoB.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Confianza).ThenByDescending(p => p.Lift)
                .ThenBy(p => p.ProductoA, StringComparer.OrdinalIgnoreCase).First())
            .OrderByDescending(p => p.Confianza)
            .ThenBy(p => p.ProductoB.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// La mezcla de reglas-1: para llenar <paramref name="huecos"/>, primero la mejor de cada fuente que todavía no está a la
    /// vista (en <paramref name="ordenFuentes"/>), después, por turnos, la siguiente mejor de cada fuente. Se quitan los
    /// <paramref name="excluidos"/> y los repetidos.
    /// </summary>
    public static List<T> Elegir<T>(IEnumerable<T>? candidatos, Func<T, string> producto, Func<T, string> motivo, Func<T, decimal> puntuacion,
        IList<string> ordenFuentes, IEnumerable<string>? motivosVisibles, ISet<string>? excluidos, int huecos) where T : class
    {
        var elegidos = new List<T>();
        if (huecos <= 0)
        {
            return elegidos;
        }
        var usados = new HashSet<string>(excluidos ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        List<T> lista = (candidatos ?? Enumerable.Empty<T>()).ToList();
        Dictionary<string, Queue<T>> colas = ordenFuentes.Distinct().ToDictionary(f => f, f => new Queue<T>(
            lista.Where(c => motivo(c) == f && !string.IsNullOrWhiteSpace(producto(c)))
                .OrderByDescending(puntuacion)
                .ThenBy(producto, StringComparer.OrdinalIgnoreCase)));
        var visibles = new HashSet<string>(motivosVisibles ?? Enumerable.Empty<string>());

        T? Siguiente(string fuente)
        {
            Queue<T> cola = colas[fuente];
            while (cola.Count > 0)
            {
                T c = cola.Dequeue();
                if (usados.Add(producto(c).Trim()))
                {
                    return c;
                }
            }
            return null;
        }

        foreach (string fuente in ordenFuentes.Where(f => !visibles.Contains(f)))
        {
            if (elegidos.Count >= huecos)
            {
                return elegidos;
            }
            T? c = Siguiente(fuente);
            if (c != null)
            {
                elegidos.Add(c);
            }
        }
        bool quedan = true;
        while (elegidos.Count < huecos && quedan)
        {
            quedan = false;
            foreach (string fuente in ordenFuentes)
            {
                if (elegidos.Count >= huecos)
                {
                    break;
                }
                T? c = Siguiente(fuente);
                if (c != null)
                {
                    elegidos.Add(c);
                    quedan = true;
                }
            }
        }
        return elegidos;
    }

    /// <summary>
    /// El perfil de un cliente: el grupo y subgrupo (en mayúsculas) en el que más ha comprado (base imponible), con productos
    /// sugeribles. Null si no ha comprado nada con ficha o la base es ≤ 0.
    /// </summary>
    public static Tuple<string, string>? Perfil(IEnumerable<Compra>? compras, Func<string, FichaProducto?> ficha)
    {
        var porPerfil = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var claves = new Dictionary<string, Tuple<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Compra compra in compras ?? Enumerable.Empty<Compra>())
        {
            if (string.IsNullOrWhiteSpace(compra.Producto))
            {
                continue;
            }
            FichaProducto? f = ficha(compra.Producto.Trim());
            if (f == null || !EsSugerible(f) || string.IsNullOrWhiteSpace(f.Grupo))
            {
                continue;
            }
            string grupo = f.Grupo!.Trim().ToUpperInvariant();
            string subGrupo = (f.SubGrupo ?? string.Empty).Trim().ToUpperInvariant();
            string clave = grupo + "|" + subGrupo;
            porPerfil[clave] = (porPerfil.TryGetValue(clave, out decimal suma) ? suma : 0) + compra.BaseImponible;
            claves[clave] = Tuple.Create(grupo, subGrupo);
        }
        if (porPerfil.Count == 0)
        {
            return null;
        }
        string mejor = porPerfil.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).First().Key;
        return porPerfil[mejor] > 0 ? claves[mejor] : null;
    }

    public const int MESES_PERFIL = 12;
    public const int MINIMO_CLIENTES_PERFIL = 2;
    public const int MAXIMO_PRODUCTOS_POR_PERFIL = 500;

    /// <summary>
    /// Por perfil, cuántos clientes se han llevado cada producto en <see cref="MESES_PERFIL"/> meses (los
    /// <see cref="MAXIMO_PRODUCTOS_POR_PERFIL"/> primeros, con al menos <see cref="MINIMO_CLIENTES_PERFIL"/> clientes). El perfil
    /// de cada cliente se calcula con todas sus compras de la lista.
    /// </summary>
    public static List<ProductoDePerfil> PerfilesProductos(IEnumerable<Compra>? compras, Func<string, FichaProducto?> ficha, DateTime hoy)
    {
        DateTime desde = hoy.Date.AddMonths(-MESES_PERFIL);
        var cuenta = new Dictionary<string, ProductoDePerfil>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, Compra> cliente in (compras ?? Enumerable.Empty<Compra>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto))
            .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            Tuple<string, string>? perfil = Perfil(cliente, ficha);
            if (perfil == null)
            {
                continue;
            }
            foreach (string producto in cliente
                .Where(c => c.Dia >= desde)
                .Select(c => c.Producto.Trim())
                .Where(p => EsSugerible(ficha(p)))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string clave = perfil.Item1 + "|" + perfil.Item2 + "|" + producto;
                if (!cuenta.TryGetValue(clave, out ProductoDePerfil? fila))
                {
                    fila = new ProductoDePerfil { Grupo = perfil.Item1, SubGrupo = perfil.Item2, Producto = producto };
                    cuenta[clave] = fila;
                }
                fila.Clientes++;
            }
        }
        return cuenta.Values
            .Where(f => f.Clientes >= MINIMO_CLIENTES_PERFIL)
            .GroupBy(f => f.Grupo + "|" + f.SubGrupo)
            .SelectMany(g => g.OrderByDescending(f => f.Clientes).ThenBy(f => f.Producto, StringComparer.OrdinalIgnoreCase)
                .Take(MAXIMO_PRODUCTOS_POR_PERFIL))
            .ToList();
    }
}
