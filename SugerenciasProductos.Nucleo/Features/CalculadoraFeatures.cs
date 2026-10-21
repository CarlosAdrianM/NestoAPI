using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Reglas;

namespace ModeloSugerenciasProductos.Features;

/// <summary>Los candidatos de un cliente en un momento, con sus features, y lo que habría enseñado reglas-1.</summary>
public sealed class ResultadoCandidatos
{
    public List<FilaEntrenamiento> Filas { get; } = new();
    /// <summary>Los productos que enseñaría reglas-1 (en su orden), con las fuentes que se pueden reconstruir offline.</summary>
    public List<string> Reglas1 { get; } = new();
    /// <summary>Todos los candidatos de reglas-1 en el orden de su mezcla (para medirla también con la diversidad).</summary>
    public List<string> Reglas1Orden { get; } = new();
    /// <summary>Producto → días de compra en 24 meses (para separar habituales, ocasionales y nuevos).</summary>
    public Dictionary<string, int> ComprasPorProducto { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// NestoAPI#671 (corte 2b): candidatos y features de <see cref="ModeloSugerenciaEntrada"/>, SIN FUGA: el contexto (tablas del
/// job) es de antes de d y el historial del cliente son sus compras de [d − 24 meses, d). Las mismas cuentas en el
/// entrenamiento y, en el 2c, en la API.
/// <para>Candidatos: las cuatro fuentes que se pueden reconstruir con compras (CompradoJunto, LeToca, Parecidos y lo más
/// comprado del perfil), como mucho <c>maximoPorFuente</c> de cada una, sin lo del carrito ni lo que no se puede sugerir.
/// «Potenciar» (ofertas, novedades y «Potenciar hasta») no se puede reconstruir hacia atrás y no entra.</para>
/// </summary>
public static class CalculadoraFeatures
{
    public const int MESES_HISTORIAL = 24;
    public const int MESES_CLIENTE = 12;
    /// <summary>Lo que reglas-1 mira de cada fuente (MotorSugerenciasProductos.CANDIDATOS_POR_FUENTE).</summary>
    public const int CANDIDATOS_POR_FUENTE_REGLAS = 8;
    public const int SUGERENCIAS_VISIBLES = 3;
    public const string NADA = "NADA";

    /// <param name="contexto">Tablas del job anteriores a <paramref name="dia"/>.</param>
    /// <param name="cliente">El cliente.</param>
    /// <param name="historial">Compras del cliente; solo cuentan las de [d − 24 meses, d).</param>
    /// <param name="carrito">Productos que lleva el carrito.</param>
    /// <param name="ficha">Ficha de un producto (null si no hay).</param>
    /// <param name="dia">d.</param>
    /// <param name="maximoPorFuente">Candidatos de cada fuente.</param>
    /// <param name="adicionales">
    /// NestoAPI#671 (corte 2c): productos que la API también quiere puntuar (las de «Potenciar», que no se pueden reconstruir
    /// hacia atrás, y las que ya están a la vista). Se añaden al final de los candidatos SIN apuntarles ninguna fuente: sus
    /// features salen exactamente como saldrían en el entrenamiento para un producto que ninguna fuente del modelo propone
    /// (lo de «comprado junto» y «le toca» se mira igual con todos los pares y todo el historial). Lo que no se puede sugerir
    /// o está en el carrito no entra. Null en el entrenamiento: el resultado es el de siempre.
    /// </param>
    /// <param name="estadoCliente">
    /// NestoAPI#683: Clientes.Estado del contacto principal en d (en el entrenamiento, <see cref="HistorialEstadosCliente.EstadoEn"/>;
    /// en Calcular, el de hoy). Se agrupa aquí con <see cref="EstadosCliente.Grupo"/>; null = «NADA». Las features de
    /// popularidad por estado salen de <see cref="ContextoInstantanea.PopularidadEstado"/> (0 si no hay).
    /// </param>
    public static ResultadoCandidatos Calcular(ContextoInstantanea contexto, string cliente, IEnumerable<Compra> historial,
        ISet<string> carrito, Func<string, FichaProducto?> ficha, DateTime dia, int maximoPorFuente, IEnumerable<string>? adicionales = null,
        short? estadoCliente = null)
    {
        DateTime d = dia.Date;
        DateTime desde = d.AddMonths(-MESES_HISTORIAL);
        DateTime desde12 = d.AddMonths(-MESES_CLIENTE);
        List<Compra> compras = historial.Where(c => c.Dia >= desde && c.Dia < d && !string.IsNullOrWhiteSpace(c.Producto)).ToList();
        var resultado = new ResultadoCandidatos();
        var enCarrito = new HashSet<string>(carrito.Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);

        // Lo del cliente
        var diasProducto = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var importeProducto = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var diasFamilia = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var diasSubgrupo = new Dictionary<string, HashSet<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var diasCliente12 = new HashSet<DateTime>();
        decimal importe12 = 0;
        DateTime? ultimoPedido = null;
        foreach (Compra c in compras)
        {
            string p = c.Producto.Trim();
            Anadir(diasProducto, p, c.Dia.Date);
            importeProducto[p] = (importeProducto.TryGetValue(p, out decimal i) ? i : 0) + c.BaseImponible;
            FichaProducto? f = ficha(p);
            if (!string.IsNullOrWhiteSpace(f?.Familia)) Anadir(diasFamilia, f!.Familia!.Trim(), c.Dia.Date);
            string sub = GrupoSubgrupo(f);
            if (sub != NADA) Anadir(diasSubgrupo, sub, c.Dia.Date);
            if (c.Dia >= desde12)
            {
                diasCliente12.Add(c.Dia.Date);
                importe12 += c.BaseImponible;
            }
            if (ultimoPedido == null || c.Dia > ultimoPedido) ultimoPedido = c.Dia.Date;
        }
        foreach (KeyValuePair<string, HashSet<DateTime>> p in diasProducto) resultado.ComprasPorProducto[p.Key] = p.Value.Count;
        Tuple<string, string>? perfil = ReglasSugerencias.Perfil(compras, ficha);
        string clavePerfil = perfil == null ? "" : perfil.Item1 + "|" + perfil.Item2;

        bool Vale(string producto) => !enCarrito.Contains(producto) && ReglasSugerencias.EsSugerible(ficha(producto));

        // Fuentes
        var pares = new List<ParComprado>();
        foreach (string a in enCarrito)
        {
            if (contexto.ParesPorAntecedente.TryGetValue(a, out List<ParComprado>? lista)) pares.AddRange(lista);
        }
        List<ParComprado> juntos = ReglasSugerencias.CompradoJunto(pares, enCarrito).Where(p => Vale(p.ProductoB.Trim())).ToList();
        Dictionary<string, int> antecedentes = pares.Where(p => !enCarrito.Contains(p.ProductoB.Trim()))
            .GroupBy(p => p.ProductoB.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ProductoA.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(), StringComparer.OrdinalIgnoreCase);
        List<ResultadoLeToca> letoca = ReglasSugerencias.LeToca(compras, d).Where(r => Vale(r.Producto)).ToList();
        List<AfinidadProducto> parecidos = (contexto.AfinidadPorCliente.TryGetValue(cliente.Trim(), out List<AfinidadProducto>? af) ? af : new List<AfinidadProducto>())
            .Where(a => !diasProducto.ContainsKey(a.Producto.Trim()) && Vale(a.Producto.Trim())).ToList();
        List<ProductoDePerfil> delPerfil = (perfil != null && contexto.ProductosPorPerfil.TryGetValue(clavePerfil, out List<ProductoDePerfil>? pp) ? pp : new List<ProductoDePerfil>())
            .Where(p => Vale(p.Producto.Trim())).ToList();

        var fuentes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var orden = new List<string>();
        void Fuente(string producto, string fuente)
        {
            if (!fuentes.TryGetValue(producto, out HashSet<string>? f))
            {
                f = new HashSet<string>();
                fuentes[producto] = f;
                orden.Add(producto);
            }
            f.Add(fuente);
        }
        foreach (ParComprado p in juntos.Take(maximoPorFuente)) Fuente(p.ProductoB.Trim(), Fuentes.COMPRADO_JUNTO);
        foreach (ResultadoLeToca r in letoca.Take(maximoPorFuente)) Fuente(r.Producto, Fuentes.LE_TOCA);
        foreach (AfinidadProducto a in parecidos.Take(maximoPorFuente)) Fuente(a.Producto.Trim(), Fuentes.PARECIDOS);
        foreach (ProductoDePerfil p in delPerfil.Take(maximoPorFuente)) Fuente(p.Producto.Trim(), Fuentes.PERFIL);
        foreach (string extra in adicionales ?? Enumerable.Empty<string>())
        {
            string producto = extra?.Trim() ?? "";
            if (producto.Length == 0 || fuentes.ContainsKey(producto) || !Vale(producto)) continue;
            fuentes[producto] = new HashSet<string>();
            orden.Add(producto);
        }

        // reglas-1 (la mezcla del corte 1 con lo reconstruible: CompradoJunto y LeToca)
        var reglas = juntos.Take(CANDIDATOS_POR_FUENTE_REGLAS)
            .Select(p => new CandidatoRegla(p.ProductoB.Trim(), Fuentes.COMPRADO_JUNTO, p.Confianza))
            .Concat(letoca.Take(CANDIDATOS_POR_FUENTE_REGLAS).Select(r => new CandidatoRegla(r.Producto, Fuentes.LE_TOCA, r.Puntuacion)))
            .ToList();
        resultado.Reglas1Orden.AddRange(ReglasSugerencias.Elegir(reglas, c => c.Producto, c => c.Motivo, c => c.Puntuacion, Fuentes.ORDEN,
            null, enCarrito, int.MaxValue).Select(c => c.Producto));
        resultado.Reglas1.AddRange(resultado.Reglas1Orden.Take(SUGERENCIAS_VISIBLES));
        Dictionary<string, CandidatoRegla> reglaPorProducto = reglas.GroupBy(r => r.Producto, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ParComprado> mejorPar = juntos.GroupBy(p => p.ProductoB.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var afinidadPorProducto = new Dictionary<string, AfinidadProducto>(StringComparer.OrdinalIgnoreCase);
        foreach (AfinidadProducto a in af ?? new List<AfinidadProducto>())
        {
            if (!afinidadPorProducto.ContainsKey(a.Producto.Trim())) afinidadPorProducto[a.Producto.Trim()] = a;
        }
        Dictionary<string, int> clientesPerfil = perfil != null && contexto.ClientesPorPerfil.TryGetValue(clavePerfil, out Dictionary<string, int>? cp)
            ? cp : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var subgruposCarrito = enCarrito.Select(p => GrupoSubgrupo(ficha(p))).ToList();
        var familiasCarrito = enCarrito.Select(p => ficha(p)?.Familia?.Trim() ?? "").ToList();
        string mes = d.Month.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string perfilCliente = perfil == null ? NADA : perfil.Item1 + perfil.Item2;
        string estado = EstadosCliente.Grupo(estadoCliente);
        int clientesEstado = contexto.ClientesEstado(estado, PopularidadPorEstado.TOTAL, "");
        int clientesTodos = contexto.ClientesEstado(EstadosCliente.TODOS, PopularidadPorEstado.TOTAL, "");

        foreach (string producto in orden)
        {
            FichaProducto? f = ficha(producto);
            HashSet<string> deDonde = fuentes[producto];
            string sub = GrupoSubgrupo(f);
            string familia = f?.Familia?.Trim() ?? "";
            int comprasProducto = diasProducto.TryGetValue(producto, out HashSet<DateTime>? dp) ? dp.Count : 0;
            double? mediana = dp == null ? null : ReglasSugerencias.IntervaloMediano(dp);
            int? diasUltima = dp == null ? null : (int)(d - dp.Max()).TotalDays;
            contexto.Popularidad.TryGetValue(producto, out PopularidadDeProducto? pop);
            mejorPar.TryGetValue(producto, out ParComprado? par);
            afinidadPorProducto.TryGetValue(producto, out AfinidadProducto? afinidad);
            reglaPorProducto.TryGetValue(producto, out CandidatoRegla? regla);
            int productoEstado = contexto.ClientesEstado(estado, PopularidadPorEstado.PRODUCTO, producto);
            int productoTodos = contexto.ClientesEstado(EstadosCliente.TODOS, PopularidadPorEstado.PRODUCTO, producto);
            int familiaEstado = familia.Length == 0 ? 0 : contexto.ClientesEstado(estado, PopularidadPorEstado.FAMILIA, familia);
            int familiaTodos = familia.Length == 0 ? 0 : contexto.ClientesEstado(EstadosCliente.TODOS, PopularidadPorEstado.FAMILIA, familia);

            resultado.Filas.Add(new FilaEntrenamiento
            {
                Cliente = cliente.Trim(),
                Producto = producto,
                Familia = familia,
                Mes = mes,
                GrupoSubgrupoProducto = sub,
                PerfilCliente = perfilCliente,
                EsCompradoJunto = deDonde.Contains(Fuentes.COMPRADO_JUNTO) || par != null ? 1 : 0,
                ConfianzaCompradoJunto = par == null ? 0 : (float)par.Confianza,
                LiftCompradoJunto = par == null ? 0 : (float)par.Lift,
                AntecedentesEnCarrito = antecedentes.TryGetValue(producto, out int n) ? n : 0,
                EsLeToca = letoca.Any(r => string.Equals(r.Producto, producto, StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
                EsParecidos = deDonde.Contains(Fuentes.PARECIDOS) ? 1 : 0,
                Afinidad = afinidad == null || comprasProducto > 0 ? 0 : (float)afinidad.Afinidad,
                RangoAfinidad = afinidad == null || comprasProducto > 0 ? TablasNocturnas.MAXIMO_AFINIDADES_POR_CLIENTE + 1 : afinidad.Rango,
                EsPerfil = deDonde.Contains(Fuentes.PERFIL) ? 1 : 0,
                ClientesPerfil = clientesPerfil.TryGetValue(producto, out int clientesDelPerfil) ? clientesDelPerfil : 0,
                NumeroFuentes = deDonde.Count,
                ComprasProducto24Meses = comprasProducto,
                DiasDesdeUltimaCompraProducto = diasUltima ?? ModeloSugerenciaEntrada.SIN_DATO,
                IntervaloMedianoProducto = mediana.HasValue ? (float)mediana.Value : ModeloSugerenciaEntrada.SIN_DATO,
                RatioLeToca = mediana.HasValue && mediana.Value >= 1 && diasUltima.HasValue ? (float)(diasUltima.Value / mediana.Value) : 0,
                EsHabitual = comprasProducto >= ReglasSugerencias.COMPRAS_HABITUAL ? 1 : 0,
                ImporteProducto24Meses = importeProducto.TryGetValue(producto, out decimal imp) ? (float)imp : 0,
                ComprasFamilia24Meses = familia.Length > 0 && diasFamilia.TryGetValue(familia, out HashSet<DateTime>? df) ? df.Count : 0,
                ComprasSubgrupo24Meses = sub != NADA && diasSubgrupo.TryGetValue(sub, out HashSet<DateTime>? ds) ? ds.Count : 0,
                Clientes12Meses = pop?.Clientes12Meses ?? 0,
                Clientes3Meses = pop?.Clientes3Meses ?? 0,
                TendenciaPopularidad = pop == null || pop.Clientes12Meses == 0 ? 0 : pop.Clientes3Meses * 4f / pop.Clientes12Meses,
                PedidosCliente12Meses = diasCliente12.Count,
                ImporteCliente12Meses = (float)importe12,
                DiasDesdeUltimoPedidoCliente = ultimoPedido.HasValue ? (float)(d - ultimoPedido.Value).TotalDays : ModeloSugerenciaEntrada.SIN_DATO,
                ProductosDistintosCliente = diasProducto.Count,
                TamanoCarrito = enCarrito.Count,
                CarritoVacio = enCarrito.Count == 0 ? 1 : 0,
                MismoSubgrupoEnCarrito = sub == NADA ? 0 : subgruposCarrito.Count(s => s == sub),
                MismaFamiliaEnCarrito = familia.Length == 0 ? 0 : familiasCarrito.Count(x => string.Equals(x, familia, StringComparison.OrdinalIgnoreCase)),
                EstadoCliente = estado,
                ClientesEstado12Meses = productoEstado,
                ProporcionEstado12Meses = Proporcion(productoEstado, clientesEstado),
                ProporcionFamiliaEstado12Meses = Proporcion(familiaEstado, clientesEstado),
                IndiceEstado = Indice(productoEstado, clientesEstado, productoTodos, clientesTodos),
                IndiceFamiliaEstado = Indice(familiaEstado, clientesEstado, familiaTodos, clientesTodos),
                PuntuacionReglas = regla == null ? 0 : (float)regla.Puntuacion,
                FuenteReglas = regla?.Motivo ?? ""
            });
        }
        return resultado;
    }

    /// <summary>NestoAPI#683: a ÷ b; 0 si b es 0.</summary>
    public static float Proporcion(int a, int b) => b <= 0 ? 0f : (float)((double)a / b);

    /// <summary>NestoAPI#683: (a ÷ b) ÷ (c ÷ d), la proporción del grupo frente a la de todos; 0 si falta algún denominador.</summary>
    public static float Indice(int a, int b, int c, int d) => b <= 0 || c <= 0 || d <= 0 ? 0f : (float)((double)a / b / ((double)c / d));

    /// <summary>Grupo + subgrupo en mayúsculas («COSCRE»); <see cref="NADA"/> si no hay ficha o grupo.</summary>
    public static string GrupoSubgrupo(FichaProducto? f)
        => string.IsNullOrWhiteSpace(f?.Grupo) ? NADA : (f!.Grupo!.Trim() + (f.SubGrupo ?? "").Trim()).ToUpperInvariant();

    private static void Anadir(Dictionary<string, HashSet<DateTime>> mapa, string clave, DateTime dia)
    {
        if (!mapa.TryGetValue(clave, out HashSet<DateTime>? s))
        {
            s = new HashSet<DateTime>();
            mapa[clave] = s;
        }
        s.Add(dia);
    }

    internal sealed class CandidatoRegla
    {
        public CandidatoRegla(string producto, string motivo, decimal puntuacion)
        {
            Producto = producto;
            Motivo = motivo;
            Puntuacion = puntuacion;
        }

        public string Producto { get; }
        public string Motivo { get; }
        public decimal Puntuacion { get; }
    }
}
