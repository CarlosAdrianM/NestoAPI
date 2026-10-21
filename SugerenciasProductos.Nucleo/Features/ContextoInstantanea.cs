using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Reglas;

namespace ModeloSugerenciasProductos.Features;

/// <summary>
/// Las tablas del job nocturno tal y como estarían el día <see cref="Fecha"/>: pares, afinidad, popularidad y perfiles,
/// calculados SOLO con las compras de los 24 meses anteriores a esa fecha (las mismas funciones que usa el job de la API).
/// En el entrenamiento hay un contexto por instantánea; en la API (2c), las tablas leídas de la BD.
/// </summary>
public sealed class ContextoInstantanea
{
    public const int MESES_VENTANA = 24;

    public DateTime Fecha { get; private set; }
    public Dictionary<string, List<ParComprado>> ParesPorAntecedente { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<AfinidadProducto>> AfinidadPorCliente { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PopularidadDeProducto> Popularidad { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>«GRUPO|SUBGRUPO» → producto → clientes del perfil que lo compran (12 meses).</summary>
    public Dictionary<string, Dictionary<string, int>> ClientesPorPerfil { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>«GRUPO|SUBGRUPO» → productos del perfil de más a menos clientes.</summary>
    public Dictionary<string, List<ProductoDePerfil>> ProductosPorPerfil { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>NestoAPI#683: «ESTADO|TIPO|CLAVE» → clientes distintos (12 meses) de la popularidad por estado.</summary>
    public Dictionary<string, int> PopularidadEstado { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>NestoAPI#683: clientes de la popularidad por estado; 0 si no hay fila.</summary>
    public int ClientesEstado(string estado, string tipo, string clave)
        => PopularidadEstado.TryGetValue(ClaveEstado(estado, tipo, clave), out int n) ? n : 0;

    private static string ClaveEstado(string estado, string tipo, string clave)
        => (estado ?? "").Trim() + "|" + (tipo ?? "").Trim() + "|" + (clave ?? "").Trim();

    /// <summary>Con las tablas ya calculadas (la API, o los tests).</summary>
    /// <param name="popularidadEstado">NestoAPI#683: filas de la popularidad por estado (null: sin ellas, features a 0).</param>
    public static ContextoInstantanea Desde(DateTime fecha, IEnumerable<ParComprado>? pares, IEnumerable<AfinidadProducto>? afinidades,
        IEnumerable<PopularidadDeProducto>? popularidad, IEnumerable<ProductoDePerfil>? perfiles, IEnumerable<PopularidadPorEstado>? popularidadEstado = null)
    {
        var c = new ContextoInstantanea { Fecha = fecha.Date };
        foreach (IGrouping<string, ParComprado> g in (pares ?? Enumerable.Empty<ParComprado>()).GroupBy(p => p.ProductoA.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            c.ParesPorAntecedente[g.Key] = g.ToList();
        }
        foreach (IGrouping<string, AfinidadProducto> g in (afinidades ?? Enumerable.Empty<AfinidadProducto>()).GroupBy(a => a.Cliente.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            c.AfinidadPorCliente[g.Key] = g.OrderBy(a => a.Rango).ToList();
        }
        foreach (PopularidadDeProducto p in popularidad ?? Enumerable.Empty<PopularidadDeProducto>())
        {
            c.Popularidad[p.Producto.Trim()] = p;
        }
        foreach (IGrouping<string, ProductoDePerfil> g in (perfiles ?? Enumerable.Empty<ProductoDePerfil>())
            .GroupBy(p => p.Grupo.Trim().ToUpperInvariant() + "|" + p.SubGrupo.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase))
        {
            List<ProductoDePerfil> lista = g.OrderByDescending(p => p.Clientes).ThenBy(p => p.Producto, StringComparer.OrdinalIgnoreCase).ToList();
            c.ProductosPorPerfil[g.Key] = lista;
            c.ClientesPorPerfil[g.Key] = lista.GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Max(p => p.Clientes), StringComparer.OrdinalIgnoreCase);
        }
        foreach (PopularidadPorEstado p in popularidadEstado ?? Enumerable.Empty<PopularidadPorEstado>())
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Estado) || string.IsNullOrWhiteSpace(p.Tipo)) continue;
            c.PopularidadEstado[ClaveEstado(p.Estado, p.Tipo, p.Clave)] = p.Clientes;
        }
        return c;
    }

    /// <summary>
    /// Lo que habría calculado el job la madrugada del día <paramref name="fecha"/>: con las compras de
    /// [fecha − 24 meses, fecha). <paramref name="compras"/> puede traer más: se filtra aquí.
    /// </summary>
    /// <param name="estadoEn">
    /// NestoAPI#683: el estado de cada cliente en <paramref name="fecha"/> (<see cref="HistorialEstadosCliente.EstadoEn"/>), para
    /// la popularidad por estado. Null: sin ella.
    /// </param>
    public static ContextoInstantanea Calcular(IEnumerable<Compra> compras, Func<string, FichaProducto?> ficha, DateTime fecha,
        Func<string, short?>? estadoEn = null)
    {
        DateTime desde = fecha.Date.AddMonths(-MESES_VENTANA);
        List<Compra> ventana = compras.Where(c => c.Dia >= desde && c.Dia < fecha.Date).ToList();
        var sugeribles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool EsSugerible(string p)
        {
            if (!sugeribles.TryGetValue(p, out bool vale))
            {
                vale = ReglasSugerencias.EsSugerible(ficha(p));
                sugeribles[p] = vale;
            }
            return vale;
        }
        // Como el job: lee hasta ayer y calcula con «hoy» = la fecha (popularidad y perfiles miran hacia atrás desde ella).
        return Desde(fecha,
            TablasNocturnas.Pares(ventana, EsSugerible),
            TablasNocturnas.Afinidades(ventana, EsSugerible),
            TablasNocturnas.Popularidad(ventana, EsSugerible, fecha.Date),
            ReglasSugerencias.PerfilesProductos(ventana, ficha, fecha.Date),
            estadoEn == null ? null : TablasNocturnas.PopularidadEstado(ventana, ficha, cliente => EstadosCliente.Grupo(estadoEn(cliente)), fecha.Date));
    }
}
