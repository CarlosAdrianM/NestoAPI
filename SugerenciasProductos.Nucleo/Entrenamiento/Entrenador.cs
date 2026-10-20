using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using ModeloSugerenciasProductos.Features;

namespace ModeloSugerenciasProductos.Entrenamiento;

/// <summary>
/// NestoAPI#671 (corte 2b): LightGBM binario calibrado (el modelo que se guarda) y LambdaRank (alternativa que solo sale en el
/// informe). One-hot de las categóricas → Concatenate → árbol. Semilla fija.
/// </summary>
public static class Entrenador
{
    public const int SEMILLA = 671;
    public const string BINARIO = "lightgbm-binario";
    public const string LAMBDARANK = "lightgbm-lambdarank";

    public static readonly string[] Categoricas =
    {
        nameof(ModeloSugerenciaEntrada.Mes),
        nameof(ModeloSugerenciaEntrada.GrupoSubgrupoProducto),
        nameof(ModeloSugerenciaEntrada.PerfilCliente)
    };

    public static readonly string[] Numericas =
    {
        nameof(ModeloSugerenciaEntrada.EsCompradoJunto),
        nameof(ModeloSugerenciaEntrada.ConfianzaCompradoJunto),
        nameof(ModeloSugerenciaEntrada.LiftCompradoJunto),
        nameof(ModeloSugerenciaEntrada.AntecedentesEnCarrito),
        nameof(ModeloSugerenciaEntrada.EsLeToca),
        nameof(ModeloSugerenciaEntrada.EsParecidos),
        nameof(ModeloSugerenciaEntrada.Afinidad),
        nameof(ModeloSugerenciaEntrada.RangoAfinidad),
        nameof(ModeloSugerenciaEntrada.EsPerfil),
        nameof(ModeloSugerenciaEntrada.ClientesPerfil),
        nameof(ModeloSugerenciaEntrada.NumeroFuentes),
        nameof(ModeloSugerenciaEntrada.ComprasProducto24Meses),
        nameof(ModeloSugerenciaEntrada.DiasDesdeUltimaCompraProducto),
        nameof(ModeloSugerenciaEntrada.IntervaloMedianoProducto),
        nameof(ModeloSugerenciaEntrada.RatioLeToca),
        nameof(ModeloSugerenciaEntrada.EsHabitual),
        nameof(ModeloSugerenciaEntrada.ImporteProducto24Meses),
        nameof(ModeloSugerenciaEntrada.ComprasFamilia24Meses),
        nameof(ModeloSugerenciaEntrada.ComprasSubgrupo24Meses),
        nameof(ModeloSugerenciaEntrada.Clientes12Meses),
        nameof(ModeloSugerenciaEntrada.Clientes3Meses),
        nameof(ModeloSugerenciaEntrada.TendenciaPopularidad),
        nameof(ModeloSugerenciaEntrada.PedidosCliente12Meses),
        nameof(ModeloSugerenciaEntrada.ImporteCliente12Meses),
        nameof(ModeloSugerenciaEntrada.DiasDesdeUltimoPedidoCliente),
        nameof(ModeloSugerenciaEntrada.ProductosDistintosCliente),
        nameof(ModeloSugerenciaEntrada.TamanoCarrito),
        nameof(ModeloSugerenciaEntrada.CarritoVacio),
        nameof(ModeloSugerenciaEntrada.MismoSubgrupoEnCarrito),
        nameof(ModeloSugerenciaEntrada.MismaFamiliaEnCarrito)
    };

    /// <summary>
    /// Contexto con semilla fija y el ensamblado de LightGBM registrado en el catálogo: en .NET Framework (la API) ML.NET no lo
    /// descubre solo y, sin registrarlo, no reconoce el cargador del zip.
    /// </summary>
    public static MLContext CrearContexto()
    {
        var ml = new MLContext(seed: SEMILLA);
        ml.ComponentCatalog.RegisterAssembly(typeof(LightGbmBinaryModelParameters).Assembly);
        return ml;
    }

    public static ITransformer Cargar(MLContext ml, string ruta)
    {
        using var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return ml.Model.Load(fichero, out _);
    }

    private static IEstimator<ITransformer> Preparar(MLContext ml)
        => ml.Transforms.Categorical.OneHotEncoding(Categoricas.Select(c => new InputOutputColumnPair(c + "Cod", c)).ToArray())
            .Append(ml.Transforms.Concatenate("Features", Categoricas.Select(c => c + "Cod").Concat(Numericas).ToArray()));

    public static IEstimator<ITransformer> PipelineBinario(MLContext ml, OpcionesEntrenamiento op)
        => Preparar(ml).Append(ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
        {
            LabelColumnName = nameof(FilaEntrenamiento.Label),
            FeatureColumnName = "Features",
            NumberOfLeaves = op.Hojas,
            NumberOfIterations = op.Iteraciones,
            MinimumExampleCountPerLeaf = op.MinimoEjemplosPorHoja,
            LearningRate = op.TasaAprendizaje,
            Seed = SEMILLA,
            NumberOfThreads = op.Hilos
        }));

    public static IEstimator<ITransformer> PipelineRanking(MLContext ml, OpcionesEntrenamiento op)
        => ml.Transforms.Conversion.MapValueToKey("SesionClave", nameof(FilaEntrenamiento.Sesion))
            .Append(Preparar(ml))
            .Append(ml.Ranking.Trainers.LightGbm(new LightGbmRankingTrainer.Options
            {
                LabelColumnName = nameof(FilaEntrenamiento.Relevancia),
                FeatureColumnName = "Features",
                RowGroupColumnName = "SesionClave",
                NumberOfLeaves = op.Hojas,
                NumberOfIterations = op.Iteraciones,
                MinimumExampleCountPerLeaf = op.MinimoEjemplosPorHoja,
                LearningRate = op.TasaAprendizaje,
                Seed = SEMILLA,
                NumberOfThreads = op.Hilos
            }));

    public static ITransformer EntrenarBinario(MLContext ml, IEnumerable<FilaEntrenamiento> filas, OpcionesEntrenamiento op)
        => PipelineBinario(ml, op).Fit(ml.Data.LoadFromEnumerable(filas));

    /// <summary>LambdaRank necesita las filas de cada sesión juntas.</summary>
    public static ITransformer EntrenarRanking(MLContext ml, IEnumerable<FilaEntrenamiento> filas, OpcionesEntrenamiento op)
        => PipelineRanking(ml, op).Fit(ml.Data.LoadFromEnumerable(filas.OrderBy(f => f.Sesion, StringComparer.Ordinal).ToList()));

    /// <summary>Probabilidad calibrada, puntuando con la clase de entrada (lo mismo que hará la API: sin Label ni datos de evaluación).</summary>
    public static List<float> Probabilidades(MLContext ml, ITransformer modelo, IEnumerable<ModeloSugerenciaEntrada> entradas)
    {
        IDataView datos = ml.Data.LoadFromEnumerable<ModeloSugerenciaEntrada>(entradas.ToList());
        return ml.Data.CreateEnumerable<PrediccionSugerencia>(modelo.Transform(datos), reuseRowObject: false)
            .Select(p => p.Probability).ToList();
    }

    public static List<float> PuntuacionesRanking(MLContext ml, ITransformer modelo, IEnumerable<FilaEntrenamiento> filas)
    {
        IDataView datos = ml.Data.LoadFromEnumerable(filas.ToList());
        return ml.Data.CreateEnumerable<PrediccionRanking>(modelo.Transform(datos), reuseRowObject: false)
            .Select(p => p.Score).ToList();
    }

    /// <summary>Guarda el modelo con el esquema de entrada de <see cref="ModeloSugerenciaEntrada"/> (lo que recibirá la API).</summary>
    public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
    {
        IDataView esquema = ml.Data.LoadFromEnumerable(Array.Empty<ModeloSugerenciaEntrada>());
        string? carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (carpeta != null) Directory.CreateDirectory(carpeta);
        ml.Model.Save(modelo, esquema.Schema, ruta);
    }
}
