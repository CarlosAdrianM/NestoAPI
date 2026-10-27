using ModeloLlamadaPedido.Datos;

namespace ModeloLlamadaPedido.Features;

/// <summary>NestoAPI#618: lo que el cliente ha comprado de un subgrupo antes del día del contacto.</summary>
public sealed class EstadisticaSubgrupo
{
    public static readonly EstadisticaSubgrupo Vacia = new();

    /// <summary>Días con el subgrupo en 12 meses.</summary>
    public int Pedidos12 { get; internal set; }
    /// <summary>Días con el subgrupo en 24 meses.</summary>
    public int Pedidos24 { get; internal set; }
    /// <summary>Último día con el subgrupo (24 meses); null si no hay.</summary>
    public DateTime? Ultimo { get; internal set; }
    public decimal Importe12 { get; internal set; }
    /// <summary>Importe en [d − 6 m, d).</summary>
    public decimal ImporteA { get; internal set; }
    /// <summary>Importe en [d − 12 m, d − 6 m).</summary>
    public decimal ImporteB { get; internal set; }
    public int MismoMes { get; internal set; }
}

/// <summary>NestoAPI#618: todo lo comprado por subgrupo antes de un día (se calcula una vez por contacto y vale para todos sus subgrupos).</summary>
public sealed class ResumenSubgrupos
{
    public ResumenSubgrupos(DateTime dia, IReadOnlyDictionary<string, EstadisticaSubgrupo> porSubgrupo, decimal importeTotal12)
    {
        Dia = dia;
        PorSubgrupo = porSubgrupo;
        ImporteTotal12 = importeTotal12;
    }

    public DateTime Dia { get; }
    public IReadOnlyDictionary<string, EstadisticaSubgrupo> PorSubgrupo { get; }
    /// <summary>Base imponible de todos los subgrupos en 12 meses (denominador de la cuota).</summary>
    public decimal ImporteTotal12 { get; }

    public EstadisticaSubgrupo De(string grupoSubgrupo)
        => PorSubgrupo.TryGetValue(CalculadoraFeaturesSubgrupo.Normalizar(grupoSubgrupo), out EstadisticaSubgrupo? e) ? e : EstadisticaSubgrupo.Vacia;
}

/// <summary>
/// NestoAPI#618: features de relación cliente↔subgrupo y etiqueta del modelo por subgrupo, SIN FUGA (solo pedidos anteriores
/// al día del contacto). Una sola definición para el entrenamiento y la API (la API referencia el núcleo).
/// </summary>
public static class CalculadoraFeaturesSubgrupo
{
    /// <summary>Por contacto, como mucho los N subgrupos que más ha comprado en 24 meses (acota el dataset con los clientes grandes).</summary>
    public const int MAX_COMPRADOS_POR_CONTACTO = 15;
    /// <summary>Por contacto, N subgrupos que no ha comprado en 24 meses, al azar (determinista) del catálogo: venta cruzada.</summary>
    public const int NO_COMPRADOS_POR_CONTACTO = 2;
    /// <summary>Un subgrupo entra en el catálogo de no comprados si aparece en al menos N días-cliente del histórico.</summary>
    public const int MINIMO_DIAS_CATALOGO = 30;

    /// <summary>Recortado y en mayúsculas. Si ya lo está, devuelve la misma cadena (un millón de filas comparten unas pocas).</summary>
    public static string Normalizar(string? grupoSubgrupo)
    {
        string t = (grupoSubgrupo ?? "").Trim();
        foreach (char c in t)
        {
            if (char.IsLower(c)) return t.ToUpperInvariant();
        }
        return t;
    }

    /// <param name="lineas">Lo comprado por día y subgrupo del cliente, ORDENADO por día.</param>
    /// <param name="fechaContacto">Día del contacto (se ignora la hora).</param>
    public static ResumenSubgrupos Resumir(IReadOnlyList<PedidoSubgrupoDia> lineas, DateTime fechaContacto)
    {
        DateTime d = fechaContacto.Date;
        DateTime hace6 = d.AddMonths(-6), hace11 = d.AddMonths(-11), hace12 = d.AddMonths(-12), hace24 = d.AddMonths(-CalculadoraFeatures.MESES_HISTORIAL);
        var porSubgrupo = new Dictionary<string, EstadisticaSubgrupo>(StringComparer.Ordinal);
        var ultimoDiaContado = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        decimal total12 = 0;
        foreach (PedidoSubgrupoDia l in lineas)
        {
            DateTime dia = l.Dia.Date;
            if (dia >= d) break; // ordenadas: nada del día del contacto en adelante
            if (dia < hace24) continue;
            string gs = Normalizar(l.GrupoSubgrupo);
            if (gs.Length == 0) continue;
            if (!porSubgrupo.TryGetValue(gs, out EstadisticaSubgrupo? e))
            {
                e = new EstadisticaSubgrupo();
                porSubgrupo[gs] = e;
            }
            // Un pedido = un día: si vienen varias filas del mismo día y subgrupo, cuentan una vez.
            bool diaNuevo = !ultimoDiaContado.TryGetValue(gs, out DateTime anterior) || anterior != dia;
            ultimoDiaContado[gs] = dia;
            if (diaNuevo) e.Pedidos24++;
            e.Ultimo = dia;
            if (dia >= hace12)
            {
                if (diaNuevo)
                {
                    e.Pedidos12++;
                    if (dia < hace11) e.MismoMes++;
                }
                e.Importe12 += l.Importe;
                total12 += l.Importe;
                if (dia >= hace6) e.ImporteA += l.Importe; else e.ImporteB += l.Importe;
            }
        }
        return new ResumenSubgrupos(d, porSubgrupo, total12);
    }

    /// <summary>
    /// Entrada del modelo por subgrupo: copia de las features generales de <paramref name="general"/> (calculadas para el
    /// mismo contacto) más las de relación con <paramref name="grupoSubgrupo"/>.
    /// </summary>
    public static T Calcular<T>(ModeloContactoEntrada general, ResumenSubgrupos resumen, string grupoSubgrupo) where T : ModeloContactoSubgrupoEntrada, new()
    {
        string gs = Normalizar(grupoSubgrupo);
        EstadisticaSubgrupo e = resumen.De(gs);
        var r = new T();
        CopiarGenerales(general, r);
        r.GrupoSubgrupo = gs;
        r.PedidosSubgrupo12Meses = e.Pedidos12;
        r.PedidosSubgrupo24Meses = e.Pedidos24;
        r.DiasDesdeUltimoPedidoSubgrupo = e.Ultimo.HasValue ? (float)(resumen.Dia - e.Ultimo.Value).TotalDays : CalculadoraFeatures.SIN_PEDIDO_DIAS;
        r.FrecuenciaSubgrupo = Frecuencia(e.Pedidos12, general.Pedidos12Meses);
        r.CuotaImporteSubgrupo = resumen.ImporteTotal12 > 0 ? (float)(e.Importe12 / resumen.ImporteTotal12) : 0f;
        r.SubgrupoMismoMesAnnoAnterior = e.MismoMes;
        r.TendenciaSubgrupo = e.ImporteA + e.ImporteB > 0 ? (float)((e.ImporteA - e.ImporteB) / (e.ImporteA + e.ImporteB)) : 0f;
        r.EsSubgrupoMasVendido = string.Equals(Normalizar(general.GrupoSubgrupoMasVendido), gs, StringComparison.Ordinal) ? 1f : 0f;
        r.SubgrupoNuncaComprado = e.Pedidos24 == 0 ? 1f : 0f;
        return r;
    }

    /// <summary>(pedidos con el subgrupo en 12 m + 0,5) / (pedidos en 12 m + 1).</summary>
    public static float Frecuencia(float pedidosSubgrupo12, float pedidos12) => (pedidosSubgrupo12 + 0.5f) / (pedidos12 + 1f);

    /// <summary>Subgrupos con algún pedido en [día, día + 7] (la etiqueta del modelo por subgrupo).</summary>
    public static HashSet<string> SubgruposEnVentana(IReadOnlyList<PedidoSubgrupoDia> lineas, DateTime fechaContacto)
    {
        DateTime dia = fechaContacto.Date;
        DateTime limite = dia.AddDays(CalculadoraFeatures.VENTANA_POSITIVO_DIAS);
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        foreach (PedidoSubgrupoDia l in lineas)
        {
            if (l.Dia.Date > limite) break; // ordenadas
            if (l.Dia.Date >= dia) resultado.Add(Normalizar(l.GrupoSubgrupo));
        }
        return resultado;
    }

    /// <summary>Positivo = pedido del cliente con alguna línea del subgrupo en [día, día + 7]. La marca Pedido del rapport no dice de qué subgrupo, así que no cuenta.</summary>
    public static bool EsPositivo(IReadOnlyList<PedidoSubgrupoDia> lineas, string grupoSubgrupo, DateTime fechaContacto)
        => SubgruposEnVentana(lineas, fechaContacto).Contains(Normalizar(grupoSubgrupo));

    /// <summary>
    /// Subgrupos de las filas de un contacto: los <paramref name="maxComprados"/> que más ha comprado en 24 meses (más días;
    /// empate por orden) y <paramref name="noComprados"/> del <paramref name="catalogo"/> que no ha comprado, elegidos con una
    /// semilla estable de cliente y día (mismo dataset en cada ejecución, y en .NET Framework y .NET 8).
    /// </summary>
    public static List<string> Candidatos(ResumenSubgrupos resumen, IReadOnlyList<string> catalogo, string clienteId, DateTime fechaContacto,
        int maxComprados = MAX_COMPRADOS_POR_CONTACTO, int noComprados = NO_COMPRADOS_POR_CONTACTO)
    {
        List<string> comprados = resumen.PorSubgrupo
            .Where(kv => kv.Value.Pedidos24 > 0)
            .OrderByDescending(kv => kv.Value.Pedidos24).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(maxComprados)
            .Select(kv => kv.Key)
            .ToList();
        List<string> libres = catalogo.Where(g => !resumen.PorSubgrupo.ContainsKey(g)).ToList();
        var azar = new Random(SemillaEstable($"{clienteId}|{fechaContacto:yyyyMMdd}"));
        for (int i = 0; i < noComprados && libres.Count > 0; i++)
        {
            int j = azar.Next(libres.Count);
            comprados.Add(libres[j]);
            libres.RemoveAt(j);
        }
        return comprados;
    }

    /// <summary>Catálogo de subgrupos para la muestra de no comprados: los que salen en al menos <paramref name="minimoDias"/> días-cliente, por orden.</summary>
    public static List<string> Catalogo(IEnumerable<PedidoSubgrupoDia> lineas, int minimoDias = MINIMO_DIAS_CATALOGO)
        => lineas
            .Select(l => (Gs: Normalizar(l.GrupoSubgrupo), l.ClienteId, Dia: l.Dia.Date))
            .Where(x => x.Gs.Length > 0)
            .Distinct()
            .GroupBy(x => x.Gs)
            .Where(g => g.Count() >= minimoDias)
            .Select(g => g.Key)
            .OrderBy(g => g, StringComparer.Ordinal)
            .ToList();

    /// <summary>FNV-1a de 32 bits (string.GetHashCode cambia en cada proceso de .NET 8).</summary>
    public static int SemillaEstable(string texto)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in texto)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    public static void CopiarGenerales(ModeloContactoEntrada f, ModeloContactoEntrada destino)
    {
        destino.ClienteId = f.ClienteId;
        destino.TipoInteraccion = f.TipoInteraccion;
        destino.Mes = f.Mes;
        destino.DiaSemana = f.DiaSemana;
        destino.GrupoSubgrupoMasVendido = f.GrupoSubgrupoMasVendido;
        destino.EsPorLaTarde = f.EsPorLaTarde;
        destino.Pedidos12Meses = f.Pedidos12Meses;
        destino.Importe12Meses = f.Importe12Meses;
        destino.DiasDesdeUltimoPedido = f.DiasDesdeUltimoPedido;
        destino.DiasEntrePedidos = f.DiasEntrePedidos;
        destino.PedidosMismoMesAnnoAnterior = f.PedidosMismoMesAnnoAnterior;
        destino.ImporteMedioPedido = f.ImporteMedioPedido;
        destino.TendenciaImporte = f.TendenciaImporte;
        destino.TasaConversionCliente = f.TasaConversionCliente;
        destino.ContactosPrevios = f.ContactosPrevios;
        destino.SinHistorial = f.SinHistorial;
    }
}
