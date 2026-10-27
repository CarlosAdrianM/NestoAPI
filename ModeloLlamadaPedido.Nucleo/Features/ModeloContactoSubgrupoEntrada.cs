namespace ModeloLlamadaPedido.Features;

/// <summary>
/// NestoAPI#618: ENTRADA DEL MODELO POR SUBGRUPO (<c>modelo_llamadas_subgrupo.zip</c>). Pregunta «¿hará este cliente en los
/// próximos 7 días un pedido que incluya el subgrupo <see cref="GrupoSubgrupo"/> si le llamo hoy?», en vez de pisar
/// <see cref="ModeloContactoEntrada.GrupoSubgrupoMasVendido"/> (que respondía «cómo convierten los clientes cuyo subgrupo
/// estrella es X»). Lleva las features generales de hoy (hereda <see cref="ModeloContactoEntrada"/>, mismo cálculo) más las de
/// relación cliente↔subgrupo, que generalizan entre subgrupos aunque cada uno tenga pocos positivos.
/// <para>La MISMA clase la usan el entrenamiento y la API: no se puede renombrar ni cambiar el tipo de nada sin reentrenar
/// (ML.NET enlaza por nombre de columna). Cálculo en <see cref="CalculadoraFeaturesSubgrupo"/> (d = día del contacto; todo
/// ANTES de d).</para>
/// </summary>
public class ModeloContactoSubgrupoEntrada : ModeloContactoEntrada
{
    /// <summary>Grupo+SubGrupo por el que se pregunta (p. ej. "COS007"), recortado y en mayúsculas. Categórica.</summary>
    public string GrupoSubgrupo { get; set; } = "";
    /// <summary>Pedidos (días) con alguna línea del subgrupo en [d − 12 meses, d).</summary>
    public float PedidosSubgrupo12Meses { get; set; }
    /// <summary>Lo mismo en [d − 24 meses, d).</summary>
    public float PedidosSubgrupo24Meses { get; set; }
    /// <summary>Días desde el último pedido con el subgrupo (24 meses); 1000 si no hay.</summary>
    public float DiasDesdeUltimoPedidoSubgrupo { get; set; }
    /// <summary>(PedidosSubgrupo12Meses + 0,5) / (Pedidos12Meses + 1): qué parte de sus pedidos lleva el subgrupo (suavizado; el «interino» de la issue).</summary>
    public float FrecuenciaSubgrupo { get; set; }
    /// <summary>Base imponible del subgrupo / base imponible de todos los subgrupos del cliente en 12 meses; 0 si no compró nada.</summary>
    public float CuotaImporteSubgrupo { get; set; }
    /// <summary>Pedidos con el subgrupo en [d − 12 meses, d − 11 meses) (el mismo mes del año anterior).</summary>
    public float SubgrupoMismoMesAnnoAnterior { get; set; }
    /// <summary>(A − B) / (A + B) con A = importe del subgrupo en [d − 6 m, d) y B = en [d − 12 m, d − 6 m); 0 si A + B = 0.</summary>
    public float TendenciaSubgrupo { get; set; }
    /// <summary>1 si el subgrupo es <see cref="ModeloContactoEntrada.GrupoSubgrupoMasVendido"/>.</summary>
    public float EsSubgrupoMasVendido { get; set; }
    /// <summary>1 si no hay ningún pedido con el subgrupo en 24 meses (venta cruzada).</summary>
    public float SubgrupoNuncaComprado { get; set; }
}

/// <summary>NestoAPI#618: una fila del dataset por subgrupo (entrada + etiqueta, peso y datos de evaluación).</summary>
public sealed class FilaEntrenamientoSubgrupo : ModeloContactoSubgrupoEntrada, IFilaEvaluable
{
    /// <summary>Pedido del cliente con alguna línea del subgrupo en [día, día + 7].</summary>
    public bool Label { get; set; }
    public float Peso { get; set; } = 1f;
    public string Vendedor { get; set; } = "";
    public DateTime Fecha { get; set; }
    public int AnnoMes { get; set; }
    /// <summary>La precision@k se mide por vendedor, mes y subgrupo: es la lista que ve el vendedor al elegir el subgrupo.</summary>
    string IFilaEvaluable.Segmento => GrupoSubgrupo;
}
