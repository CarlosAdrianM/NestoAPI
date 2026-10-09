using Microsoft.ML;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;
using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Entrenamiento;

public static class Entrenador
{
    public const string LIGHTGBM = "lightgbm";
    public const string FASTTREE = "fasttree";

    public static readonly string[] Categoricas =
    {
        nameof(ModeloContactoEntrada.TipoInteraccion),
        nameof(ModeloContactoEntrada.Mes),
        nameof(ModeloContactoEntrada.DiaSemana),
        nameof(ModeloContactoEntrada.GrupoSubgrupoMasVendido)
    };

    public static readonly string[] Numericas =
    {
        nameof(ModeloContactoEntrada.EsPorLaTarde),
        nameof(ModeloContactoEntrada.Pedidos12Meses),
        nameof(ModeloContactoEntrada.Importe12Meses),
        nameof(ModeloContactoEntrada.DiasDesdeUltimoPedido),
        nameof(ModeloContactoEntrada.DiasEntrePedidos),
        nameof(ModeloContactoEntrada.PedidosMismoMesAnnoAnterior),
        nameof(ModeloContactoEntrada.ImporteMedioPedido),
        nameof(ModeloContactoEntrada.TendenciaImporte),
        nameof(ModeloContactoEntrada.TasaConversionCliente),
        nameof(ModeloContactoEntrada.ContactosPrevios),
        nameof(ModeloContactoEntrada.SinHistorial)
    };

    /// <summary>
    /// Contexto con semilla fija y los ensamblados de LightGBM y FastTree registrados en el catálogo: en .NET Framework
    /// (la API) ML.NET no los descubre solo y sin registrarlos no reconoce el cargador del zip.
    /// </summary>
    public static MLContext CrearContexto()
    {
        var ml = new MLContext(seed: 603);
        ml.ComponentCatalog.RegisterAssembly(typeof(LightGbmBinaryModelParameters).Assembly);
        ml.ComponentCatalog.RegisterAssembly(typeof(FastTreeBinaryModelParameters).Assembly);
        return ml;
    }

    /// <summary>Carga un zip compartiendo la lectura (la API puede estar leyéndolo a la vez para puntuar).</summary>
    public static ITransformer Cargar(MLContext ml, string ruta)
    {
        using var fichero = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return ml.Model.Load(fichero, out _);
    }

    /// <summary>One-hot de las categóricas → Concatenate → NormalizeMinMax (ANTES del trainer) → árbol binario calibrado.</summary>
    public static IEstimator<ITransformer> Pipeline(MLContext ml, string trainer, bool conPesos)
    {
        string? peso = conPesos ? nameof(FilaEntrenamiento.Peso) : null;
        IEstimator<ITransformer> arbol = trainer switch
        {
            FASTTREE => ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = nameof(FilaEntrenamiento.Label),
                FeatureColumnName = "Features",
                ExampleWeightColumnName = peso,
                NumberOfLeaves = 16,
                NumberOfTrees = 200,
                MinimumExampleCountPerLeaf = 30,
                LearningRate = 0.05
            }),
            _ => ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
            {
                LabelColumnName = nameof(FilaEntrenamiento.Label),
                FeatureColumnName = "Features",
                ExampleWeightColumnName = peso,
                NumberOfLeaves = 16,
                NumberOfIterations = 200,
                MinimumExampleCountPerLeaf = 30,
                LearningRate = 0.05,
                Seed = 603
            })
        };

        string[] codificadas = Categoricas.Select(c => c + "Cod").ToArray();
        return ml.Transforms.Categorical.OneHotEncoding(
                Categoricas.Select(c => new InputOutputColumnPair(c + "Cod", c)).ToArray())
            .Append(ml.Transforms.Concatenate("Features", codificadas.Concat(Numericas).ToArray()))
            .Append(ml.Transforms.NormalizeMinMax("Features"))
            .Append(arbol);
    }

    /// <summary>Pesos de clase: los positivos pesan negativos/positivos (sin duplicar filas). Solo con --pesos.</summary>
    public static void AsignarPesos(IList<FilaEntrenamiento> filas, bool conPesos)
    {
        int positivos = filas.Count(f => f.Label);
        int negativos = filas.Count - positivos;
        float pesoPositivo = conPesos && positivos > 0 ? (float)negativos / positivos : 1f;
        foreach (FilaEntrenamiento f in filas) f.Peso = f.Label ? pesoPositivo : 1f;
    }

    public static ITransformer Entrenar(MLContext ml, IEnumerable<FilaEntrenamiento> filas, string trainer, bool conPesos)
    {
        IDataView datos = ml.Data.LoadFromEnumerable(filas);
        return Pipeline(ml, trainer, conPesos).Fit(datos);
    }

    public static List<float> Probabilidades(MLContext ml, ITransformer modelo, IEnumerable<ModeloContactoEntrada> entradas)
    {
        // Se proyecta a la clase base: así se prueba lo mismo que hará la API (sin Label ni Peso).
        IDataView datos = ml.Data.LoadFromEnumerable(entradas.Select(Copiar));
        return ml.Data.CreateEnumerable<PrediccionContacto>(modelo.Transform(datos), reuseRowObject: false)
            .Select(p => p.Probability).ToList();
    }

    /// <summary>Guarda el modelo con el esquema de entrada de <see cref="ModeloContactoEntrada"/> (lo que recibirá la API).</summary>
    public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
    {
        IDataView esquema = ml.Data.LoadFromEnumerable(Array.Empty<ModeloContactoEntrada>());
        ml.Model.Save(modelo, esquema.Schema, ruta);
    }

    public static ModeloContactoEntrada Copiar(ModeloContactoEntrada f) => new()
    {
        ClienteId = f.ClienteId,
        TipoInteraccion = f.TipoInteraccion,
        Mes = f.Mes,
        DiaSemana = f.DiaSemana,
        GrupoSubgrupoMasVendido = f.GrupoSubgrupoMasVendido,
        EsPorLaTarde = f.EsPorLaTarde,
        Pedidos12Meses = f.Pedidos12Meses,
        Importe12Meses = f.Importe12Meses,
        DiasDesdeUltimoPedido = f.DiasDesdeUltimoPedido,
        DiasEntrePedidos = f.DiasEntrePedidos,
        PedidosMismoMesAnnoAnterior = f.PedidosMismoMesAnnoAnterior,
        ImporteMedioPedido = f.ImporteMedioPedido,
        TendenciaImporte = f.TendenciaImporte,
        TasaConversionCliente = f.TasaConversionCliente,
        ContactosPrevios = f.ContactosPrevios,
        SinHistorial = f.SinHistorial
    };
}
