using System.Diagnostics;
using System.Text;
using Microsoft.ML;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Evaluacion;
using ModeloSugerenciasProductos.Features;

namespace ModeloSugerenciasProductos.Entrenamiento;

/// <summary>
/// NestoAPI#671 (corte 2b): el entrenamiento entero salvo la lectura de la BD, para que la consola (y en el 2d el job de la API)
/// hagan lo mismo:
/// <list type="number">
/// <item>Instantáneas sin fuga (<see cref="ConstructorInstantaneas"/>) y partición temporal: entrenamiento, un mes de hueco y validación.</item>
/// <item>LightGBM binario calibrado (el que se guarda) y LambdaRank (alternativa del informe), entrenados con el entrenamiento.</item>
/// <item>En la validación, P@3, R@3, NDCG@3 y «al menos 1» del top-3 como lo ve el vendedor (NestoAPI#683: con o sin la
/// diversidad según <see cref="Diversidad.APLICAR"/>), frente a reglas-1, popularidad del perfil
/// y afinidad sola, desglosadas en habituales, ocasionales y nuevos.</item>
/// <item>Puerta: ganar a reglas-1 en P@3 por ≥ 2 puntos con NDCG@3 no peor.</item>
/// <item><see cref="EntrenarFinalYGuardar"/>: el zip se entrena con TODO (entrenamiento + validación).</item>
/// </list>
/// </summary>
public sealed class ProcesoEntrenamiento
{
    public const string MODELO_BINARIO = "Modelo (LightGBM binario)";
    public const string MODELO_LAMBDARANK = "LambdaRank (alternativa)";
    public const string BASE_REGLAS = "reglas-1";
    /// <summary>NestoAPI#683: el modelo con la diversidad contraria a la de Calcular (hoy, CON ella): solo informativo.</summary>
    public static readonly string MODELO_OTRA_DIVERSIDAD = Diversidad.APLICAR ? "Modelo sin diversidad (informativo)" : "Modelo con diversidad (informativo)";
    public const string BASE_REGLAS_DIVERSIDAD = "reglas-1 con la diversidad (informativo)";
    public const string BASE_POPULARIDAD = "Popularidad del perfil";
    public const string BASE_AFINIDAD = "Afinidad sola";
    /// <summary>NestoAPI#683: el mismo modelo con las features de la versión 1 (sin el estado), con la misma partición.</summary>
    public const string MODELO_SIN_ESTADO = "Modelo sin el estado (features v1, misma partición)";
    public static readonly string MODELO_SIN_ESTADO_OTRA_DIVERSIDAD = Diversidad.APLICAR
        ? "Modelo sin el estado, sin diversidad (informativo)" : "Modelo sin el estado, con diversidad (informativo)";
    public const string SEGMENTO_DISTRIBUIDORES = "Distribuidores (6/67)";
    public const string SEGMENTO_RESTO = "Resto";
    /// <summary>Versión de las features del modelo «sin el estado».</summary>
    public const int VERSION_SIN_ESTADO = 1;

    private readonly MLContext _ml;
    private readonly Action<string> _log;

    public OpcionesEntrenamiento Opciones { get; }
    public VentanaEntrenamiento Ventana { get; }
    public Dataset Dataset { get; }
    public bool DatosSuficientes { get; private set; }
    public List<ResultadoRanking> Resultados { get; } = new();
    public ResultadoRanking? Modelo => Resultados.FirstOrDefault(r => r.Modelo == MODELO_BINARIO);
    public ResultadoRanking? Reglas => Resultados.FirstOrDefault(r => r.Modelo == BASE_REGLAS);
    /// <summary>NestoAPI#683: el modelo con las features de la versión 1 (medido como <see cref="Modelo"/>: como lo ve el vendedor).</summary>
    public ResultadoRanking? SinEstado => Resultados.FirstOrDefault(r => r.Modelo == MODELO_SIN_ESTADO);
    /// <summary>NestoAPI#683: P@3 de cada modelo por grupo de estado (segmento → resultados en el orden de <see cref="ModelosPorEstado"/>).</summary>
    public List<KeyValuePair<string, List<ResultadoRanking>>> ResultadosPorEstado { get; } = new();
    public List<string> ModelosPorEstado { get; } = new();
    /// <summary>NestoAPI#683: si el estado de cada instantánea sale del historial (sin él, fuga y no pasa la puerta).</summary>
    public bool HistorialEstados { get; }
    public string? MotivoSinHistorialEstados { get; }
    /// <summary>NestoAPI#683: el primer cambio de estado apuntado (antes de él, el estado es aproximado).</summary>
    public DateTime? PrimerCambioEstado { get; }
    public DecisionPuerta Decision { get; private set; } = new(false, "");
    public int FilasEntrenamiento => Dataset.Entrenamiento.Sum(s => s.Filas.Count);
    public int FilasValidacion => Dataset.Validacion.Sum(s => s.Filas.Count);
    public int Positivos => Dataset.Todas.Sum(s => s.Filas.Count(f => f.Label));

    private ProcesoEntrenamiento(DatosCrudos datos, OpcionesEntrenamiento op, DateTime hoy, Action<string>? log)
    {
        Opciones = op;
        _log = log ?? (_ => { });
        _ml = Entrenador.CrearContexto();
        Ventana = VentanaEntrenamiento.Calcular(hoy, op);
        HistorialEstados = datos.HistorialEstados;
        MotivoSinHistorialEstados = datos.MotivoSinHistorialEstados;
        PrimerCambioEstado = datos.HistorialEstados && datos.CambiosEstado.Count > 0 ? datos.CambiosEstado.Min(c => c.Fecha) : null;
        Dataset = ConstructorInstantaneas.Construir(datos, Ventana, op, _log);
    }

    /// <summary>Construye el dataset, entrena, mide y decide (sin guardar nada).</summary>
    public static ProcesoEntrenamiento Evaluar(DatosCrudos datos, OpcionesEntrenamiento op, DateTime hoy, Action<string>? log = null)
    {
        var p = new ProcesoEntrenamiento(datos, op, hoy, log);
        p.EvaluarInterno();
        return p;
    }

    private void EvaluarInterno()
    {
        var reloj = Stopwatch.StartNew();
        List<FilaEntrenamiento> entrenamiento = Dataset.Entrenamiento.SelectMany(s => s.Filas).ToList();
        List<FilaEntrenamiento> validacion = Dataset.Validacion.SelectMany(s => s.Filas).ToList();
        _log($"Dataset: {Dataset.Entrenamiento.Count} sesiones de entrenamiento ({entrenamiento.Count} filas, {entrenamiento.Count(f => f.Label)} +), " +
             $"{Dataset.Validacion.Count} de validación ({validacion.Count} filas, {validacion.Count(f => f.Label)} +).");
        if (entrenamiento.Count == 0 || validacion.Count == 0 || entrenamiento.All(f => f.Label) || entrenamiento.All(f => !f.Label))
        {
            DatosSuficientes = false;
            Decision = new DecisionPuerta(false, "no hay datos suficientes para entrenar y validar");
            _log("No hay datos suficientes para entrenar y validar.");
            return;
        }
        DatosSuficientes = true;

        ITransformer binario = Entrenador.EntrenarBinario(_ml, entrenamiento, Opciones);
        List<float> p = Entrenador.Probabilidades(_ml, binario, validacion);
        Dictionary<FilaEntrenamiento, float> puntuacion = new(ReferenceEqualityComparer.Instancia);
        for (int i = 0; i < validacion.Count; i++) puntuacion[validacion[i]] = p[i];
        _log($"Modelo binario entrenado y puntuado en {reloj.Elapsed.TotalSeconds:0.0} s.");

        // NestoAPI#683: la puerta mide el top-3 como lo ve el vendedor (Diversidad.APLICAR, la misma constante que Calcular)
        Func<Sesion, IReadOnlyList<string>> topModelo = s => Diversidad.TopComoLaApi(s.Filas, f => puntuacion[f]);
        Resultados.Add(Metricas.Evaluar(MODELO_BINARIO, Dataset.Validacion, topModelo));

        // NestoAPI#683: el mismo modelo sin las features del estado (versión 1), con la misma partición: lo que aportan
        ITransformer sinEstado = Entrenador.EntrenarBinario(_ml, entrenamiento, Opciones, VERSION_SIN_ESTADO);
        List<float> p1 = Entrenador.Probabilidades(_ml, sinEstado, validacion);
        Dictionary<FilaEntrenamiento, float> puntuacionSinEstado = new(ReferenceEqualityComparer.Instancia);
        for (int i = 0; i < validacion.Count; i++) puntuacionSinEstado[validacion[i]] = p1[i];
        Func<Sesion, IReadOnlyList<string>> topSinEstado = s => Diversidad.TopComoLaApi(s.Filas, f => puntuacionSinEstado[f]);
        Resultados.Add(Metricas.Evaluar(MODELO_SIN_ESTADO, Dataset.Validacion, topSinEstado));
        _log($"Modelo sin el estado (features v1) entrenado y puntuado ({reloj.Elapsed.TotalSeconds:0.0} s).");
        if (Opciones.EvaluarLambdaRank)
        {
            try
            {
                ITransformer ranking = Entrenador.EntrenarRanking(_ml, entrenamiento, Opciones);
                List<float> pr = Entrenador.PuntuacionesRanking(_ml, ranking, validacion);
                Dictionary<FilaEntrenamiento, float> puntuacionRanking = new(ReferenceEqualityComparer.Instancia);
                for (int i = 0; i < validacion.Count; i++) puntuacionRanking[validacion[i]] = pr[i];
                Resultados.Add(Metricas.Evaluar(MODELO_LAMBDARANK, Dataset.Validacion, s => Diversidad.TopComoLaApi(s.Filas, f => puntuacionRanking[f])));
                _log($"LambdaRank entrenado y puntuado ({reloj.Elapsed.TotalSeconds:0.0} s).");
            }
            catch (Exception ex)
            {
                _log($"LambdaRank no se ha podido entrenar ({ex.Message}); el informe sigue sin él.");
            }
        }
        Func<Sesion, IReadOnlyList<string>> topReglas = s => s.Reglas1;
        Func<Sesion, IReadOnlyList<string>> topOtraDiversidad = s => OtraDiversidad(s, puntuacion);
        Func<Sesion, IReadOnlyList<string>> topSinEstadoOtraDiversidad = s => OtraDiversidad(s, puntuacionSinEstado);
        Resultados.Add(Metricas.Evaluar(BASE_REGLAS, Dataset.Validacion, topReglas));
        Resultados.Add(Metricas.Evaluar(MODELO_OTRA_DIVERSIDAD, Dataset.Validacion, topOtraDiversidad));
        Resultados.Add(Metricas.Evaluar(MODELO_SIN_ESTADO_OTRA_DIVERSIDAD, Dataset.Validacion, topSinEstadoOtraDiversidad));
        Resultados.Add(Metricas.Evaluar(BASE_REGLAS_DIVERSIDAD, Dataset.Validacion, s =>
        {
            Dictionary<string, FilaEntrenamiento> filas = s.Filas.GroupBy(f => f.Producto, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            return Diversidad.Aplicar(s.Reglas1Orden, p => filas.TryGetValue(p, out FilaEntrenamiento? f) && f.EsHabitual > 0,
                p => filas.TryGetValue(p, out FilaEntrenamiento? f) ? f.Familia : null);
        }));
        Resultados.Add(Metricas.Evaluar(BASE_POPULARIDAD, Dataset.Validacion,
            s => Diversidad.TopComoLaApi(s.Filas, f => f.ClientesPerfil + f.Clientes12Meses / 1e6)));
        Resultados.Add(Metricas.Evaluar(BASE_AFINIDAD, Dataset.Validacion,
            s => Diversidad.TopComoLaApi(s.Filas, f => f.Afinidad + f.Clientes12Meses / 1e9)));

        // NestoAPI#683: P@3 por estado del cliente (al menos distribuidores frente al resto)
        var tops = new List<KeyValuePair<string, Func<Sesion, IReadOnlyList<string>>>>
        {
            new(MODELO_BINARIO, topModelo),
            new(MODELO_SIN_ESTADO, topSinEstado),
            new(MODELO_OTRA_DIVERSIDAD, topOtraDiversidad),
            new(MODELO_SIN_ESTADO_OTRA_DIVERSIDAD, topSinEstadoOtraDiversidad),
            new(BASE_REGLAS, topReglas)
        };
        ModelosPorEstado.AddRange(tops.Select(t => t.Key));
        var segmentos = new List<KeyValuePair<string, Func<Sesion, bool>>>
        {
            new(SEGMENTO_DISTRIBUIDORES, s => EstadosCliente.EsDistribuidor(s.EstadoCliente)),
            new(SEGMENTO_RESTO, s => !EstadosCliente.EsDistribuidor(s.EstadoCliente))
        };
        foreach (string grupo in Dataset.Validacion.Where(s => s.Positivos.Count > 0 && !EstadosCliente.EsDistribuidor(s.EstadoCliente))
            .GroupBy(s => s.EstadoCliente).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key))
        {
            string g = grupo;
            segmentos.Add(new("Estado " + g, s => s.EstadoCliente == g));
        }
        foreach (KeyValuePair<string, Func<Sesion, bool>> segmento in segmentos)
        {
            List<Sesion> sesiones = Dataset.Validacion.Where(segmento.Value).ToList();
            ResultadosPorEstado.Add(new(segmento.Key, tops.Select(t => Metricas.Evaluar(t.Key, sesiones, t.Value)).ToList()));
        }

        Decision = PuertaCalidad.Decidir(Modelo!, Reglas!, Opciones.MinimaMejoraP3, SinEstado);
        if (!HistorialEstados)
        {
            Decision = new DecisionPuerta(false, $"sin el historial de estados del cliente (SeguimientoCambiosEstadoCliente: {MotivoSinHistorialEstados}) " +
                $"el estado de cada instantánea sería el de hoy y habría fuga; {Decision.Motivo}");
        }
        _log("");
        _log($"Validación ({Ventana.Corte:MM/yyyy} – {Ventana.InstantaneasValidacion.LastOrDefault():MM/yyyy}), top-3 {(Diversidad.APLICAR ? "con" : "sin")} la diversidad, como lo ve el vendedor:");
        _log(Informe.Tabla(Resultados));
        _log("Por tipo de producto para el cliente:");
        _log(Informe.TablaSegmentos(Resultados));
        _log("P@3 por estado del cliente (sesiones con algún positivo entre paréntesis):");
        _log(Informe.TablaEstados(ModelosPorEstado, ResultadosPorEstado));
        _log($"Puerta: {(Decision.Pasa ? "PASA" : "NO PASA")} — {Decision.Motivo}.");
        _log($"Evaluación terminada en {reloj.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>
    /// Entrena con TODO el dataset, guarda el zip con el esquema de <see cref="ModeloSugerenciaEntrada"/> y comprueba la ida y
    /// vuelta (cargarlo y puntuar unas filas con la clase de la API). Devuelve esas probabilidades de prueba.
    /// </summary>
    public List<float> EntrenarFinalYGuardar(string ruta)
    {
        if (!DatosSuficientes) throw new InvalidOperationException("No hay datos suficientes para entrenar el modelo final");
        List<FilaEntrenamiento> todas = Dataset.Todas.SelectMany(s => s.Filas).ToList();
        // Siempre con la versión actual de las features (la que se apunta en el json)
        ITransformer final = Entrenador.EntrenarBinario(_ml, todas, Opciones);
        Entrenador.Guardar(_ml, final, ruta);
        ITransformer cargado = Entrenador.Cargar(_ml, ruta);
        List<float> prueba = Entrenador.Probabilidades(_ml, cargado, todas.Take(5));
        if (prueba.Count == 0 || prueba.Any(float.IsNaN))
            throw new InvalidOperationException($"El modelo guardado en {ruta} no puntúa bien en la ida y vuelta");
        _log($"Modelo guardado en {ruta} (ida y vuelta OK: {string.Join("; ", prueba.Select(x => x.ToString("0.000")))}).");
        return prueba;
    }

    /// <summary>El informe de métricas frente a los baselines (markdown).</summary>
    public string InformeMetricas()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Sugerencias de productos: modelo frente a reglas-1 — {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"- Alcance: {Opciones.Alcance}. Compras del {Ventana.DatosDesde:dd/MM/yyyy} al {Ventana.DatosHasta.AddDays(-1):dd/MM/yyyy}.");
        sb.AppendLine($"- Instantáneas de entrenamiento: {Ventana.InstantaneasEntrenamiento.Count} ({Fecha(Ventana.InstantaneasEntrenamiento.FirstOrDefault())} – " +
                      $"{Fecha(Ventana.InstantaneasEntrenamiento.LastOrDefault())}); validación: {Ventana.InstantaneasValidacion.Count} " +
                      $"({Fecha(Ventana.InstantaneasValidacion.FirstOrDefault())} – {Fecha(Ventana.InstantaneasValidacion.LastOrDefault())}), con un mes de hueco.");
        sb.AppendLine($"- Sesiones: {Dataset.Entrenamiento.Count} de entrenamiento ({FilasEntrenamiento} filas) y {Dataset.Validacion.Count} de validación ({FilasValidacion} filas). Positivos: {Positivos}.");
        sb.AppendLine();
        sb.AppendLine("## Puerta");
        sb.AppendLine();
        sb.AppendLine($"**{(Decision.Pasa ? "PASA" : "NO PASA")}** — {Decision.Motivo}.");
        sb.AppendLine();
        sb.AppendLine($"Regla: ganar a reglas-1 en P@3 por al menos {Informe.Num(Opciones.MinimaMejoraP3 * 100, "0.#")} puntos, con la NDCG@3 no peor, " +
                      $"y no perder más de {Informe.Num(PuertaCalidad.MAXIMA_PERDIDA_FRENTE_ANTERIOR * 100, "0.0#")} puntos de P@3 frente al mismo modelo sin las features nuevas (NestoAPI#683). " +
                      $"Versión de las features: {ModeloSugerenciaEntrada.VERSION_FEATURES}.");
        sb.AppendLine();
        if (!DatosSuficientes)
        {
            sb.AppendLine("No hay datos suficientes para entrenar y validar.");
            return sb.ToString();
        }
        sb.AppendLine("## Validación (top-3 que vería el vendedor)");
        sb.AppendLine();
        sb.AppendLine(Informe.Tabla(Resultados));
        sb.AppendLine("Solo cuentan las sesiones con algún positivo. Cobertura = positivos que están entre los candidatos (el techo de la R@3).");
        sb.AppendLine();
        sb.AppendLine("## Por tipo de producto para el cliente");
        sb.AppendLine();
        sb.AppendLine("Habituales: ≥ 3 días de compra en 24 meses; ocasionales: 1 o 2; nuevos: ninguno.");
        sb.AppendLine();
        sb.AppendLine(Informe.TablaSegmentos(Resultados));
        sb.AppendLine("## Por estado del cliente (NestoAPI#683)");
        sb.AppendLine();
        sb.AppendLine($"P@3 de cada modelo según el estado del cliente el día del pedido (grupos de `EstadosCliente`; sesiones con algún positivo entre paréntesis). " +
                      $"«Sin el estado» es el mismo LightGBM con las features de la versión {VERSION_SIN_ESTADO}, entrenado con la misma partición: lo que aportan las features nuevas.");
        sb.AppendLine();
        sb.AppendLine(Informe.TablaEstados(ModelosPorEstado, ResultadosPorEstado));
        sb.AppendLine(HistorialEstados
            ? $"Estado de cada instantánea: el de hoy deshaciendo los cambios de SeguimientoCambiosEstadoCliente posteriores{(PrimerCambioEstado.HasValue ? $" (apuntados desde el {PrimerCambioEstado:dd/MM/yyyy}; antes, aproximado)" : "")}."
            : $"**SIN historial de estados** ({MotivoSinHistorialEstados}): el estado de cada instantánea es el de hoy (fuga), así que el modelo no pasa la puerta.");
        sb.AppendLine();
        sb.AppendLine("## Cómo se mide");
        sb.AppendLine();
        sb.AppendLine("- Sesión = el primer pedido del mes de cada cliente. Carrito simulado: la mitad de las líneas del pedido (al azar, semilla fija); en el 30 % de las sesiones, vacío.");
        sb.AppendLine("- Positivo = el producto está en la otra mitad del pedido o el cliente se lo lleva en los 30 días siguientes.");
        sb.AppendLine("- Todo se calcula con lo anterior a cada instantánea (tablas del job) o al día del pedido (historial del cliente): sin fuga.");
        sb.AppendLine(Diversidad.APLICAR
            ? "- Modelo, LambdaRank, popularidad y afinidad: top-3 con la diversidad, como en Calcular (la mejor siempre; como mucho 1 habitual y 1 por familia; si no hay con qué llenar, se relaja)."
            : "- Modelo, LambdaRank, popularidad y afinidad: top-3 SIN la diversidad, como en Calcular (Diversidad.APLICAR = false): las tres mejores por puntuación.");
        sb.AppendLine("- La puerta (y la comparación con el modelo sin el estado) mide el modelo como lo ve el vendedor (sigue a Diversidad.APLICAR, la misma constante que Calcular) " +
                      "frente a reglas-1 como lo ve hoy (su propia mezcla). Las filas «informativo» dan la otra variante de la diversidad.");
        sb.AppendLine("- reglas-1: la mezcla del corte 1 (una de cada fuente y después por turnos, 8 candidatos por fuente) con CompradoJunto y LeToca. " +
                      "«Potenciar» (ofertas, novedades, «Potenciar hasta») no se puede reconstruir hacia atrás, ni los filtros de stock, pendientes, rechazos y grupo del vendedor: " +
                      "afectan igual a todos los modelos.");
        return sb.ToString();
    }

    /// <param name="rutaModelo">Zip guardado (null si no se guardó).</param>
    /// <param name="comoEntrenar">Sección final con cómo lanzarlo.</param>
    public string Readme(string? rutaModelo, string comoEntrenar)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Modelo de sugerencias de productos — {DateTime.Now:dd/MM/yyyy HH:mm} (NestoAPI#671, corte 2b)");
        sb.AppendLine();
        if (rutaModelo != null)
            sb.AppendLine($"- Fichero: `{Path.GetFileName(rutaModelo)}` — LightGBM binario calibrado, entrenado con todas las sesiones (entrenamiento + validación).");
        sb.AppendLine($"- Qué predice: P(el cliente se lleva el producto en el pedido del día d o en los {Opciones.DiasEtiqueta} días siguientes).");
        sb.AppendLine($"- Puerta: **{(Decision.Pasa ? "pasa" : "no pasa")}** — {Decision.Motivo}.");
        sb.AppendLine("- La API (corte 2c) usa el último zip que pasa la puerta, con caché (como ModeloContacto); sin modelo, reglas-1. Se apaga con el parámetro ModeloSugerenciasActivo = 0 de «(defecto)».");
        sb.AppendLine();
        sb.AppendLine(InformeMetricas().Replace("# Sugerencias de productos", "## Informe: sugerencias de productos"));
        sb.AppendLine("## Entrada del modelo");
        sb.AppendLine();
        sb.AppendLine("Clase `ModeloSugerenciaEntrada` del núcleo (NestoAPI/SugerenciasProductos.Nucleo/Features), la MISMA que usará la API: " +
                      "nombres y tipos exactos (ML.NET enlaza por nombre). Cálculo en `CalculadoraFeatures` con el contexto de las tablas del job " +
                      "(`ContextoInstantanea`) y las compras del cliente de los 24 meses anteriores.");
        sb.AppendLine();
        sb.AppendLine($"Versión de las features: **{ModeloSugerenciaEntrada.VERSION_FEATURES}** (va en el json como `VersionFeatures`; la API solo usa zips de versiones que sabe calcular).");
        sb.AppendLine();
        sb.AppendLine($"Categóricas: {string.Join(", ", Entrenador.Categoricas)}. Numéricas: {string.Join(", ", Entrenador.Numericas)}.");
        sb.AppendLine();
        sb.AppendLine("Salida: `PredictedLabel` (bool), `Score` (float), `Probability` (float).");
        sb.AppendLine();
        sb.AppendLine("## Cómo reentrenar");
        sb.AppendLine();
        sb.AppendLine(comoEntrenar);
        return sb.ToString();
    }

    public MetadatosModelo Metadatos(DateTime fecha, string origen, string? rutaModelo) => new()
    {
        Fecha = fecha,
        Fichero = rutaModelo == null ? "" : Path.GetFileName(rutaModelo),
        DatosDesde = Ventana.DatosDesde,
        DatosHasta = Ventana.DatosHasta,
        PrimeraInstantanea = Ventana.InstantaneasEntrenamiento.FirstOrDefault(),
        Corte = Ventana.Corte,
        UltimaInstantanea = Ventana.InstantaneasValidacion.LastOrDefault(),
        SesionesEntrenamiento = Dataset.Entrenamiento.Count,
        SesionesValidacion = Dataset.Validacion.Count,
        Filas = FilasEntrenamiento + FilasValidacion,
        Positivos = Positivos,
        Alcance = Opciones.Alcance,
        P3 = MetadatosModelo.Numero(Modelo?.P3),
        R3 = MetadatosModelo.Numero(Modelo?.R3),
        Ndcg3 = MetadatosModelo.Numero(Modelo?.Ndcg3),
        AlMenos1 = MetadatosModelo.Numero(Modelo?.AlMenos1),
        P3Reglas = MetadatosModelo.Numero(Reglas?.P3),
        Ndcg3Reglas = MetadatosModelo.Numero(Reglas?.Ndcg3),
        PasaPuerta = Decision.Pasa,
        Motivo = Decision.Motivo,
        Origen = origen,
        VersionFeatures = ModeloSugerenciaEntrada.VERSION_FEATURES,
        P3SinEstado = MetadatosModelo.Numero(SinEstado?.P3),
        P3Distribuidores = MetadatosModelo.Numero(P3Segmento(SEGMENTO_DISTRIBUIDORES)),
        P3Resto = MetadatosModelo.Numero(P3Segmento(SEGMENTO_RESTO)),
        HistorialEstados = HistorialEstados,
        Diversidad = Evaluacion.Diversidad.APLICAR
    };

    private double? P3Segmento(string segmento)
        => ResultadosPorEstado.FirstOrDefault(r => r.Key == segmento).Value?.FirstOrDefault(r => r.Modelo == MODELO_BINARIO)?.P3;

    /// <summary>La variante de la diversidad que NO usa Calcular (solo informativa).</summary>
    private static List<string> OtraDiversidad(Sesion s, Dictionary<FilaEntrenamiento, float> puntuacion)
        => Diversidad.APLICAR ? Diversidad.SinDiversidad(s.Filas, f => puntuacion[f], Metricas.K) : Diversidad.Top(s.Filas, f => puntuacion[f]);

    private static string Fecha(DateTime d) => d == default ? "—" : d.ToString("MM/yyyy");

    private sealed class ReferenceEqualityComparer : IEqualityComparer<FilaEntrenamiento>
    {
        public static readonly ReferenceEqualityComparer Instancia = new();
        public bool Equals(FilaEntrenamiento? x, FilaEntrenamiento? y) => ReferenceEquals(x, y);
        public int GetHashCode(FilaEntrenamiento obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>
/// Lo que se deja en <c>modelo_sugerencias_productos.json</c> junto al zip. Sin serializador aquí (netstandard2.0 sin
/// dependencias): la consola lo escribe con System.Text.Json; los nombres de las propiedades son el contrato.
/// </summary>
public sealed class MetadatosModelo
{
    public const string NOMBRE_FICHERO = "modelo_sugerencias_productos.json";
    public const string NOMBRE_MODELO = "modelo_sugerencias_productos.zip";

    public DateTime Fecha { get; set; }
    public string Fichero { get; set; } = "";
    public DateTime DatosDesde { get; set; }
    /// <summary>Fin (excluido) de las compras leídas.</summary>
    public DateTime DatosHasta { get; set; }
    public DateTime PrimeraInstantanea { get; set; }
    /// <summary>Primera instantánea de validación.</summary>
    public DateTime Corte { get; set; }
    public DateTime UltimaInstantanea { get; set; }
    public int SesionesEntrenamiento { get; set; }
    public int SesionesValidacion { get; set; }
    public int Filas { get; set; }
    public int Positivos { get; set; }
    public string Alcance { get; set; } = "";
    /// <summary>Métricas del modelo en la validación (fracción; null si no hay).</summary>
    public double? P3 { get; set; }
    public double? R3 { get; set; }
    public double? Ndcg3 { get; set; }
    public double? AlMenos1 { get; set; }
    public double? P3Reglas { get; set; }
    public double? Ndcg3Reglas { get; set; }
    public bool PasaPuerta { get; set; }
    public string Motivo { get; set; } = "";
    public string Origen { get; set; } = "";
    /// <summary>
    /// NestoAPI#683: versión del esquema de features con la que se entrenó (<see cref="ModeloSugerenciaEntrada.VERSION_FEATURES"/>).
    /// Los json de antes no la llevan: al leerlos queda <see cref="ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL"/>.
    /// </summary>
    public int VersionFeatures { get; set; } = ModeloSugerenciaEntrada.VERSION_FEATURES_INICIAL;
    /// <summary>NestoAPI#683: P@3 del mismo modelo con las features de la versión 1 (misma partición).</summary>
    public double? P3SinEstado { get; set; }
    /// <summary>NestoAPI#683: P@3 del modelo en las sesiones de distribuidores (estados 6 y 67).</summary>
    public double? P3Distribuidores { get; set; }
    /// <summary>NestoAPI#683: P@3 del modelo en el resto de sesiones.</summary>
    public double? P3Resto { get; set; }
    /// <summary>NestoAPI#683: si el estado de cada instantánea salió del historial de cambios.</summary>
    public bool HistorialEstados { get; set; }
    /// <summary>
    /// NestoAPI#683: si las métricas (y la puerta) se midieron con la diversidad. Null en los json de antes de este cambio, que
    /// se midieron CON ella.
    /// </summary>
    public bool? Diversidad { get; set; }

    public static double? Numero(double? v) => v == null || double.IsNaN(v.Value) ? null : Math.Round(v.Value, 6);
}
