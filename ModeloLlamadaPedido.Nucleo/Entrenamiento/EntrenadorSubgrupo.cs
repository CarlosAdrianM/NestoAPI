using Microsoft.ML;
using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Entrenamiento;

/// <summary>
/// NestoAPI#618: entrenamiento y puntuación del modelo por subgrupo (<see cref="ModeloContactoSubgrupoEntrada"/>). Mismo
/// pipeline y trainer que el general (<see cref="Entrenador.Pipeline(MLContext, string, bool, IReadOnlyList{string}, IReadOnlyList{string})"/>)
/// con las columnas de relación cliente↔subgrupo añadidas.
/// </summary>
public static class EntrenadorSubgrupo
{
    public static readonly string[] Categoricas = Entrenador.Categoricas
        .Concat(new[] { nameof(ModeloContactoSubgrupoEntrada.GrupoSubgrupo) }).ToArray();

    public static readonly string[] Numericas = Entrenador.Numericas.Concat(new[]
    {
        nameof(ModeloContactoSubgrupoEntrada.PedidosSubgrupo12Meses),
        nameof(ModeloContactoSubgrupoEntrada.PedidosSubgrupo24Meses),
        nameof(ModeloContactoSubgrupoEntrada.DiasDesdeUltimoPedidoSubgrupo),
        nameof(ModeloContactoSubgrupoEntrada.FrecuenciaSubgrupo),
        nameof(ModeloContactoSubgrupoEntrada.CuotaImporteSubgrupo),
        nameof(ModeloContactoSubgrupoEntrada.SubgrupoMismoMesAnnoAnterior),
        nameof(ModeloContactoSubgrupoEntrada.TendenciaSubgrupo),
        nameof(ModeloContactoSubgrupoEntrada.EsSubgrupoMasVendido),
        nameof(ModeloContactoSubgrupoEntrada.SubgrupoNuncaComprado)
    }).ToArray();

    public static void AsignarPesos(IList<FilaEntrenamientoSubgrupo> filas, bool conPesos)
    {
        int positivos = filas.Count(f => f.Label);
        int negativos = filas.Count - positivos;
        float pesoPositivo = conPesos && positivos > 0 ? (float)negativos / positivos : 1f;
        foreach (FilaEntrenamientoSubgrupo f in filas) f.Peso = f.Label ? pesoPositivo : 1f;
    }

    public static ITransformer Entrenar(MLContext ml, IEnumerable<FilaEntrenamientoSubgrupo> filas, string trainer, bool conPesos)
    {
        IDataView datos = ml.Data.LoadFromEnumerable(filas);
        return Entrenador.Pipeline(ml, trainer, conPesos, Categoricas, Numericas).Fit(datos);
    }

    /// <summary>Probabilidad del modelo por subgrupo; se proyecta a la clase de entrada exacta, como hará la API.</summary>
    public static List<float> Probabilidades(MLContext ml, ITransformer modelo, IEnumerable<ModeloContactoSubgrupoEntrada> entradas)
    {
        IDataView datos = ml.Data.LoadFromEnumerable(entradas.Select(Copiar));
        return ml.Data.CreateEnumerable<PrediccionContacto>(modelo.Transform(datos), reuseRowObject: false)
            .Select(p => p.Probability).ToList();
    }

    /// <summary>
    /// Lo que se hace hoy (antes de #618) al elegir un subgrupo: el modelo GENERAL con el subgrupo pisando
    /// GrupoSubgrupoMasVendido (ModeloContacto.ConGrupoSubgrupo de la API). Es con lo que se compara mientras no haya modelo por subgrupo.
    /// </summary>
    public static List<float> ProbabilidadesGeneralConSubgrupoPisado(MLContext ml, ITransformer general, IEnumerable<ModeloContactoSubgrupoEntrada> entradas)
        => Entrenador.Probabilidades(ml, general, entradas.Select(e =>
        {
            ModeloContactoEntrada copia = Entrenador.Copiar(e);
            copia.GrupoSubgrupoMasVendido = e.GrupoSubgrupo;
            return copia;
        }));

    /// <summary>El modelo general con las features reales del cliente (sin pisar nada): P(pedido).</summary>
    public static List<float> ProbabilidadesGeneral(MLContext ml, ITransformer general, IEnumerable<ModeloContactoSubgrupoEntrada> entradas)
        => Entrenador.Probabilidades(ml, general, entradas);

    /// <summary>Guarda el modelo con el esquema de <see cref="ModeloContactoSubgrupoEntrada"/> (lo que recibirá la API).</summary>
    public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
    {
        IDataView esquema = ml.Data.LoadFromEnumerable(Array.Empty<ModeloContactoSubgrupoEntrada>());
        ml.Model.Save(modelo, esquema.Schema, ruta);
    }

    public static ModeloContactoSubgrupoEntrada Copiar(ModeloContactoSubgrupoEntrada f)
    {
        var r = new ModeloContactoSubgrupoEntrada();
        CalculadoraFeaturesSubgrupo.CopiarGenerales(f, r);
        r.GrupoSubgrupo = f.GrupoSubgrupo;
        r.PedidosSubgrupo12Meses = f.PedidosSubgrupo12Meses;
        r.PedidosSubgrupo24Meses = f.PedidosSubgrupo24Meses;
        r.DiasDesdeUltimoPedidoSubgrupo = f.DiasDesdeUltimoPedidoSubgrupo;
        r.FrecuenciaSubgrupo = f.FrecuenciaSubgrupo;
        r.CuotaImporteSubgrupo = f.CuotaImporteSubgrupo;
        r.SubgrupoMismoMesAnnoAnterior = f.SubgrupoMismoMesAnnoAnterior;
        r.TendenciaSubgrupo = f.TendenciaSubgrupo;
        r.EsSubgrupoMasVendido = f.EsSubgrupoMasVendido;
        r.SubgrupoNuncaComprado = f.SubgrupoNuncaComprado;
        return r;
    }
}
