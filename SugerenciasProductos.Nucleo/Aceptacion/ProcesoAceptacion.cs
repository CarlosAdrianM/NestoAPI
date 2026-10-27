using System.Globalization;
using System.Text;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using ModeloSugerenciasProductos.Entrenamiento;

namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>NestoAPI#671 (corte 2e): LightGBM binario calibrado con peso por fila (las señales débiles pesan menos).</summary>
public static class EntrenadorAceptacion
{
    public const string ALGORITMO = "lightgbm-binario-ponderado";

    public static readonly string[] Categoricas =
    {
        nameof(ModeloAceptacionEntrada.Motivo),
        nameof(ModeloAceptacionEntrada.Vendedor),
        nameof(ModeloAceptacionEntrada.Aplicacion),
        nameof(ModeloAceptacionEntrada.Mes),
        nameof(ModeloAceptacionEntrada.EstadoCliente),
        nameof(ModeloAceptacionEntrada.GrupoSubgrupoProducto)
    };

    /// <summary>Todas las float de la entrada (por reflexión: una feature nueva entra sola, subiendo la versión).</summary>
    public static readonly string[] Numericas = typeof(ModeloAceptacionEntrada).GetProperties()
        .Where(p => p.PropertyType == typeof(float) && p.DeclaringType == typeof(ModeloAceptacionEntrada))
        .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public static IEstimator<ITransformer> Pipeline(MLContext ml, OpcionesAceptacion op)
        => ml.Transforms.Categorical.OneHotEncoding(Categoricas.Select(c => new InputOutputColumnPair(c + "Cod", c)).ToArray())
            .Append(ml.Transforms.Concatenate("Features", Categoricas.Select(c => c + "Cod").Concat(Numericas).ToArray()))
            .Append(ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
            {
                LabelColumnName = nameof(FilaAceptacion.Label),
                FeatureColumnName = "Features",
                ExampleWeightColumnName = nameof(FilaAceptacion.Peso),
                NumberOfLeaves = op.Hojas,
                NumberOfIterations = op.Iteraciones,
                MinimumExampleCountPerLeaf = op.MinimoEjemplosPorHoja,
                LearningRate = op.TasaAprendizaje,
                Seed = Entrenador.SEMILLA,
                NumberOfThreads = op.Hilos
            }));

    public static ITransformer Entrenar(MLContext ml, IEnumerable<FilaAceptacion> filas, OpcionesAceptacion op)
        => Pipeline(ml, op).Fit(ml.Data.LoadFromEnumerable(filas.ToList()));

    /// <summary>Probabilidad, puntuando con la clase de entrada (lo mismo que la API).</summary>
    public static List<float> Probabilidades(MLContext ml, ITransformer modelo, IEnumerable<ModeloAceptacionEntrada> entradas)
    {
        // Copia a la clase base: con la derivada, ML.NET vería también Label, Peso… (no estorban, pero así es igual que en la API)
        List<ModeloAceptacionEntrada> lista = entradas.Select(Base).ToList();
        IDataView datos = ml.Data.LoadFromEnumerable(lista);
        return ml.Data.CreateEnumerable<PrediccionAceptacion>(modelo.Transform(datos), reuseRowObject: false).Select(p => p.Probability).ToList();
    }

    public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
    {
        IDataView esquema = ml.Data.LoadFromEnumerable(Array.Empty<ModeloAceptacionEntrada>());
        string? carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (carpeta != null) Directory.CreateDirectory(carpeta);
        ml.Model.Save(modelo, esquema.Schema, ruta);
    }

    private static ModeloAceptacionEntrada Base(ModeloAceptacionEntrada e)
    {
        if (e.GetType() == typeof(ModeloAceptacionEntrada)) return e;
        var copia = new ModeloAceptacionEntrada();
        foreach (var p in typeof(ModeloAceptacionEntrada).GetProperties().Where(p => p.CanWrite))
        {
            p.SetValue(copia, p.GetValue(e));
        }
        return copia;
    }
}

/// <summary>Lo que se guarda junto al zip del modelo 2 (modelo_aceptacion_sugerencias_AAAAMMDD_HHmm.json).</summary>
public sealed class MetadatosModeloAceptacion
{
    public DateTime Fecha { get; set; }
    public string Fichero { get; set; } = "";
    public string Algoritmo { get; set; } = EntrenadorAceptacion.ALGORITMO;
    public int VersionFeatures { get; set; } = ModeloAceptacionEntrada.VERSION_FEATURES;
    public int Respondidas { get; set; }
    public int Anadidos { get; set; }
    public int Vendedores { get; set; }
    public int SenalesDebiles { get; set; }
    public int FilasEntrenamiento { get; set; }
    public int FilasValidacion { get; set; }
    public int PositivosValidacion { get; set; }
    public DateTime? Corte { get; set; }
    public double? Auc { get; set; }
    public double? AucActual { get; set; }
    public double? AucBayes { get; set; }
    public double? PrecisionPrimera { get; set; }
    public double? PrecisionPrimeraActual { get; set; }
    public bool PasaPuerta { get; set; }
    public string Motivo { get; set; } = "";
    public string Origen { get; set; } = "";
}

/// <summary>
/// NestoAPI#671 (corte 2e): el entrenamiento del modelo 2 de principio a fin, sin BD (recibe las filas) y sin guardar hasta
/// <see cref="EntrenarFinalYGuardar"/>.
/// <list type="number">
/// <item>Umbral de datos (<see cref="UmbralDatosAceptacion"/>): si no llega, ni se entrena.</item>
/// <item>Features por orden de fecha: cada fila con las tasas de lo respondido antes (sin fuga), y su puntuación bayesiana.</item>
/// <item>Partición temporal por sesiones: las últimas <see cref="OpcionesAceptacion.FraccionValidacion"/> validan (solo sus
/// respuestas con P1); las anteriores entrenan (respuestas y, con menos peso, señales débiles).</item>
/// <item>Métricas fuera de muestra (AUC y precisión@1) del modelo, de P1 (el orden de hoy) y del bayesiano, y la puerta.</item>
/// </list>
/// </summary>
public sealed class ProcesoAceptacion
{
    public const string ACTUAL = "P1 (orden de hoy)";
    public const string BAYES = "P1 × tasa suavizada";
    public const string MODELO = "Modelo 2";

    private readonly OpcionesAceptacion op;
    private List<FilaAceptacion> todas = new();
    private List<MostradaAceptacion> respondidas = new();

    private ProcesoAceptacion(OpcionesAceptacion op)
    {
        this.op = op;
    }

    public UmbralDatosAceptacion.Resultado Umbral { get; private set; } = new();
    public bool DatosSuficientes => Umbral.Suficientes;
    public List<FilaAceptacion> Entrenamiento { get; private set; } = new();
    public List<FilaAceptacion> Validacion { get; private set; } = new();
    public DateTime? Corte { get; private set; }
    public int SenalesDebiles { get; private set; }
    public ResultadoAceptacion? Modelo { get; private set; }
    public ResultadoAceptacion? Actual { get; private set; }
    public ResultadoAceptacion? Bayes { get; private set; }
    public DecisionAceptacion Decision { get; private set; } = new() { Motivo = "sin evaluar" };

    public static ProcesoAceptacion Evaluar(IEnumerable<MostradaAceptacion> mostradas, OpcionesAceptacion? opciones = null, Action<string>? log = null)
    {
        var proceso = new ProcesoAceptacion(opciones ?? new OpcionesAceptacion());
        proceso.Ejecutar((mostradas ?? Enumerable.Empty<MostradaAceptacion>()).Where(m => m != null).ToList(), log);
        return proceso;
    }

    /// <summary>
    /// Las filas por orden de fecha, cada una con las tasas de lo respondido ANTES (sin fuga) y su puntuación bayesiana. Solo
    /// las respondidas y (si pesan algo) las señales débiles; lo demás no se ofreció.
    /// </summary>
    public static List<FilaAceptacion> ConstruirFilas(IEnumerable<MostradaAceptacion> mostradas, float pesoSenalDebil)
    {
        var tasas = new TasasAceptacion();
        var filas = new List<FilaAceptacion>();
        foreach (MostradaAceptacion m in mostradas.OrderBy(m => m.FechaMostrada).ThenBy(m => m.Id))
        {
            bool debil = m.EsSenalDebil && pesoSenalDebil > 0;
            if (!m.EsRespondida && !debil) continue;
            FilaAceptacion fila = CalculadoraFeaturesAceptacion.Calcular<FilaAceptacion>(m, InstantaneaFeatures.Leer(m.Features), tasas);
            fila.Label = m.EsRespondida ? m.Aceptada : true;
            fila.Peso = m.EsRespondida ? 1f : pesoSenalDebil;
            fila.Respondida = m.EsRespondida;
            fila.Sesion = m.SesionId ?? string.Empty;
            fila.Fecha = m.FechaMostrada;
            fila.VendedorFila = m.Vendedor?.Trim() ?? string.Empty;
            fila.PuntuacionActual = m.TieneP1 ? (double)m.Puntuacion : double.NaN;
            fila.PuntuacionBayes = m.TieneP1 ? ReordenadorBayesiano.Puntuar((float)m.Puntuacion, tasas, m.Producto, m.Motivo) : double.NaN;
            filas.Add(fila);
            tasas.Anadir(m);
        }
        return filas;
    }

    /// <summary>Partición temporal por sesiones: el corte es la fecha de la primera sesión del último tramo.</summary>
    public static (List<FilaAceptacion> entrenamiento, List<FilaAceptacion> validacion, DateTime? corte) Partir(List<FilaAceptacion> filas, double fraccionValidacion)
    {
        List<KeyValuePair<string, DateTime>> sesiones = filas.Where(f => f.Respondida)
            .GroupBy(f => f.Sesion, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, DateTime>(g.Key, g.Min(f => f.Fecha)))
            .OrderBy(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
        int validar = (int)Math.Round(sesiones.Count * fraccionValidacion, MidpointRounding.AwayFromZero);
        if (sesiones.Count < 2 || validar <= 0)
        {
            return (filas.ToList(), new List<FilaAceptacion>(), null);
        }
        DateTime corte = sesiones[sesiones.Count - validar].Value;
        var deValidacion = new HashSet<string>(sesiones.Skip(sesiones.Count - validar).Select(s => s.Key), StringComparer.Ordinal);
        // Al entrenamiento, lo de antes del corte que no es de una sesión de validación; a la validación, sus respuestas
        List<FilaAceptacion> entrenamiento = filas.Where(f => !deValidacion.Contains(f.Sesion) && f.Fecha < corte).ToList();
        List<FilaAceptacion> validacion = filas.Where(f => deValidacion.Contains(f.Sesion) && f.Respondida).ToList();
        return (entrenamiento, validacion, corte);
    }

    private void Ejecutar(List<MostradaAceptacion> mostradas, Action<string>? log)
    {
        respondidas = mostradas.Where(m => m.EsRespondida).ToList();
        Umbral = UmbralDatosAceptacion.Comprobar(mostradas, op);
        log?.Invoke($"Umbral: {Umbral.Motivo}.");
        if (!Umbral.Suficientes)
        {
            Decision = new DecisionAceptacion { Motivo = Umbral.Motivo };
            return;
        }

        todas = ConstruirFilas(mostradas, op.PesoSenalDebil);
        SenalesDebiles = todas.Count(f => !f.Respondida);
        (List<FilaAceptacion> entrenamiento, List<FilaAceptacion> validacion, DateTime? corte) = Partir(todas, op.FraccionValidacion);
        Entrenamiento = entrenamiento;
        Corte = corte;
        // Solo se comparan las que tienen P1 (el orden de hoy): las de las reglas no tienen una puntuación comparable
        Validacion = validacion.Where(f => !double.IsNaN(f.PuntuacionActual)).ToList();
        log?.Invoke($"Filas: {todas.Count} ({SenalesDebiles} señales débiles). Entrenamiento {Entrenamiento.Count}, validación {Validacion.Count} (desde {corte:dd/MM/yyyy}).");
        if (Entrenamiento.Count(f => f.Label) == 0 || Entrenamiento.Count(f => !f.Label) == 0 || Validacion.Count == 0)
        {
            Decision = new DecisionAceptacion { Motivo = "la partición temporal deja sin positivos, sin negativos o sin validación" };
            return;
        }

        MLContext ml = Entrenador.CrearContexto();
        ITransformer modelo = EntrenadorAceptacion.Entrenar(ml, Entrenamiento, op);
        List<float> probabilidades = EntrenadorAceptacion.Probabilidades(ml, modelo, Validacion);
        var p2 = new Dictionary<FilaAceptacion, double>();
        for (int i = 0; i < Validacion.Count; i++) p2[Validacion[i]] = probabilidades[i];

        Modelo = MetricasAceptacion.Evaluar(MODELO, Validacion, f => p2[f]);
        Actual = MetricasAceptacion.Evaluar(ACTUAL, Validacion, f => f.PuntuacionActual);
        Bayes = MetricasAceptacion.Evaluar(BAYES, Validacion, f => f.PuntuacionBayes);
        Decision = MetricasAceptacion.Decidir(Modelo, Actual, Bayes, op);
        log?.Invoke($"Puerta: {(Decision.Pasa ? "PASA" : "no pasa")} ({Decision.Motivo}).");
    }

    /// <summary>Entrena con TODO (entrenamiento y validación) y guarda el zip. Solo con datos suficientes.</summary>
    public void EntrenarFinalYGuardar(string ruta)
    {
        if (!DatosSuficientes || todas.Count == 0) throw new InvalidOperationException("No hay datos suficientes para entrenar el modelo de aceptación.");
        MLContext ml = Entrenador.CrearContexto();
        ITransformer modelo = EntrenadorAceptacion.Entrenar(ml, todas, op);
        EntrenadorAceptacion.Guardar(ml, modelo, ruta);
    }

    public MetadatosModeloAceptacion Metadatos(DateTime fecha, string origen, string rutaModelo) => new()
    {
        Fecha = fecha,
        Fichero = Path.GetFileName(rutaModelo),
        Respondidas = Umbral.Respondidas,
        Anadidos = Umbral.Anadidos,
        Vendedores = Umbral.Vendedores,
        SenalesDebiles = SenalesDebiles,
        FilasEntrenamiento = Entrenamiento.Count,
        FilasValidacion = Validacion.Count,
        PositivosValidacion = Validacion.Count(f => f.Label),
        Corte = Corte,
        Auc = Numero(Modelo?.Auc),
        AucActual = Numero(Actual?.Auc),
        AucBayes = Numero(Bayes?.Auc),
        PrecisionPrimera = Numero(Modelo?.PrecisionPrimera),
        PrecisionPrimeraActual = Numero(Actual?.PrecisionPrimera),
        PasaPuerta = Decision.Pasa,
        Motivo = Decision.Motivo,
        Origen = origen
    };

    private static double? Numero(double? v) => v == null || double.IsNaN(v.Value) ? null : v;

    /// <summary>Informe en Markdown: umbral, métricas frente a lo de hoy, decisión y tasas por motivo y vendedor.</summary>
    public string Informe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Modelo 2 de sugerencias: aceptación (NestoAPI#671, corte 2e)");
        sb.AppendLine();
        sb.AppendLine($"- Datos: {Umbral.Respondidas} respuestas («Añadido» {Umbral.Anadidos}), {Umbral.Vendedores} vendedores. Umbral: ≥ {op.MinimoRespondidas} respuestas, ≥ {op.MinimoAnadidos} «Añadido» y ≥ {op.MinimoVendedores} vendedores → {(Umbral.Suficientes ? "SE ENTRENA" : "NO se entrena")} ({Umbral.Motivo}).");
        if (DatosSuficientes)
        {
            sb.AppendLine($"- Partición temporal por sesiones: entrenamiento {Entrenamiento.Count} filas ({SenalesDebiles} señales débiles con peso {op.PesoSenalDebil.ToString(CultureInfo.InvariantCulture)}), validación {Validacion.Count} respuestas con P1 desde {Corte:dd/MM/yyyy}.");
            sb.AppendLine();
            sb.AppendLine("| Orden | Respuestas | «Añadido» | AUC | Sesiones (P@1) | Precisión@1 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (ResultadoAceptacion? r in new[] { Modelo, Actual, Bayes })
            {
                if (r == null) continue;
                sb.AppendLine($"| {r.Nombre} | {r.Filas} | {r.Positivos} | {MetricasAceptacion.F(r.Auc)} | {r.SesionesPrecision} | {MetricasAceptacion.P(r.PrecisionPrimera)} |");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"**Puerta:** {(Decision.Pasa ? "PASA" : "NO pasa")} — {Decision.Motivo}. AUC del modelo ≥ AUC de P1 + {MetricasAceptacion.F(op.MinimaMejoraAuc)}, no peor que el bayesiano y, con ≥ {op.MinimoSesionesPrecision} sesiones, precisión@1 no peor que P1; con ≥ {op.MinimoFilasValidacion} respuestas y ≥ {op.MinimoPositivosValidacion} «Añadido» en la validación.");
        sb.AppendLine();
        sb.AppendLine("Pasar la puerta no cambia nada visible por sí solo: el orden solo lo usa Calcular con `ModeloSugerencias:RankingAceptacion` = `Activo` en Web.config (por defecto, `Sombra`: se calcula y se registra en SugerenciasProductosSombra).");
        sb.AppendLine();
        sb.AppendLine("## Tasa de aceptación (todas las respuestas)");
        sb.AppendLine();
        AppendTasas(sb, "Motivo", respondidas.GroupBy(m => m.Motivo?.Trim() ?? "—", StringComparer.OrdinalIgnoreCase));
        sb.AppendLine();
        AppendTasas(sb, "Vendedor", respondidas.GroupBy(m => string.IsNullOrWhiteSpace(m.Vendedor) ? "—" : m.Vendedor!.Trim(), StringComparer.OrdinalIgnoreCase));
        return sb.ToString();
    }

    private static void AppendTasas(StringBuilder sb, string titulo, IEnumerable<IGrouping<string, MostradaAceptacion>> grupos)
    {
        sb.AppendLine($"| {titulo} | Respuestas | «Añadido» | Tasa |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (IGrouping<string, MostradaAceptacion> g in grupos.OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            int n = g.Count(), a = g.Count(m => m.Aceptada);
            sb.AppendLine($"| {g.Key} | {n} | {a} | {MetricasAceptacion.P(n == 0 ? double.NaN : a / (double)n)} |");
        }
    }
}
