namespace ModeloSugerenciasProductos.Features;

/// <summary>
/// NestoAPI#671 (corte 2b): ENTRADA DEL MODELO 1 (propensión de compra). Una fila = un cliente en un momento d (el día del
/// pedido), con un carrito, y un producto candidato. Es la MISMA clase que usará la API para puntuar en el 2c: no se puede
/// renombrar ni cambiar el tipo de nada sin reentrenar (ML.NET enlaza por nombre de columna). La calcula
/// <see cref="CalculadoraFeatures"/>, SIEMPRE con lo anterior a d (sin fuga):
/// <list type="bullet">
/// <item>las tablas del job nocturno (pares, afinidad, popularidad, perfiles) calculadas con las compras de los 24 meses anteriores;</item>
/// <item>el historial del cliente: sus compras de los 24 meses anteriores a d;</item>
/// <item>el carrito que lleva el vendedor en ese momento.</item>
/// </list>
/// Todo lo que lee está en las tablas del 2a o en la consulta de compras de un cliente que ya usa la API (#661).
/// Valores «no hay»: <see cref="SIN_DATO"/> en los días; 0 en lo demás.
/// <para>NestoAPI#683: VERSIÓN DEL ESQUEMA (<see cref="VERSION_FEATURES"/>). Cada zip guarda en su json la versión con la
/// que se entrenó (MetadatosModelo.VersionFeatures; sin ella, 1) y la API solo usa zips de versiones que sabe calcular. Añadir
/// columnas no rompe los zips anteriores (ML.NET coge solo las suyas), pero un zip nuevo no se puede puntuar sin sus
/// columnas: por eso, cada vez que se añada o cambie una feature (o un grupo de <see cref="EstadosCliente"/>), subir la versión.</para>
/// <list type="table">
/// <item><term>1</term><description>NestoAPI#671 (20/10/26): fuentes, cliente, producto, popularidad y carrito.</description></item>
/// <item><term>2</term><description>NestoAPI#683 (21/10/26): estado del cliente y popularidad del producto y de su familia entre los clientes del mismo estado.</description></item>
/// </list>
/// </summary>
public class ModeloSugerenciaEntrada
{
    public const float SIN_DATO = 1000f;
    /// <summary>La versión del esquema de features que calcula este núcleo.</summary>
    public const int VERSION_FEATURES = 2;
    /// <summary>La de los zips sin VersionFeatures en el json (los de antes de NestoAPI#683).</summary>
    public const int VERSION_FEATURES_INICIAL = 1;

    // ---- No son features (para cruzar el resultado) ----
    public string Cliente { get; set; } = "";
    public string Producto { get; set; } = "";

    // ---- Categóricas ----
    /// <summary>Mes de d, "1".."12".</summary>
    public string Mes { get; set; } = "";
    /// <summary>Grupo + subgrupo del producto («COSCRE»); "NADA" si no hay ficha.</summary>
    public string GrupoSubgrupoProducto { get; set; } = "";
    /// <summary>Perfil del cliente: grupo + subgrupo en el que más compra (base imponible, 24 meses); "NADA" si no hay.</summary>
    public string PerfilCliente { get; set; } = "";

    // ---- Fuentes que lo proponen ----
    public float EsCompradoJunto { get; set; }
    /// <summary>Confianza del mejor par con un producto del carrito como antecedente; 0 si no hay.</summary>
    public float ConfianzaCompradoJunto { get; set; }
    /// <summary>Lift de ese par; 0 si no hay.</summary>
    public float LiftCompradoJunto { get; set; }
    /// <summary>Productos del carrito con un par hacia este.</summary>
    public float AntecedentesEnCarrito { get; set; }
    /// <summary>1 si «le toca» con la regla del corte 1 (pasado de la mediana, sin triplicarla).</summary>
    public float EsLeToca { get; set; }
    public float EsParecidos { get; set; }
    /// <summary>Afinidad item-kNN (suma de similitudes); 0 si no está en el top 50 del cliente.</summary>
    public float Afinidad { get; set; }
    /// <summary>Rango de la afinidad (1 = el más afín); 51 si no está.</summary>
    public float RangoAfinidad { get; set; }
    public float EsPerfil { get; set; }
    /// <summary>Clientes del perfil del cliente que lo han comprado en 12 meses (tabla de perfiles); 0 si no hay.</summary>
    public float ClientesPerfil { get; set; }
    /// <summary>Cuántas fuentes lo proponen.</summary>
    public float NumeroFuentes { get; set; }

    // ---- Cliente y producto ----
    /// <summary>Días distintos en que el cliente lo ha comprado en 24 meses.</summary>
    public float ComprasProducto24Meses { get; set; }
    /// <summary>Días desde la última compra del producto; <see cref="SIN_DATO"/> si no lo ha comprado.</summary>
    public float DiasDesdeUltimaCompraProducto { get; set; }
    /// <summary>Mediana de días entre compras (≥ 3 compras); <see cref="SIN_DATO"/> si no hay.</summary>
    public float IntervaloMedianoProducto { get; set; }
    /// <summary>Días desde la última ÷ mediana; 0 si no hay mediana.</summary>
    public float RatioLeToca { get; set; }
    /// <summary>1 si ≥ 3 días de compra en 24 meses (lo que la diversidad llama «habitual»).</summary>
    public float EsHabitual { get; set; }
    /// <summary>Base imponible del producto en 24 meses.</summary>
    public float ImporteProducto24Meses { get; set; }
    /// <summary>Días distintos en que ha comprado algo de la misma familia (marca) en 24 meses.</summary>
    public float ComprasFamilia24Meses { get; set; }
    /// <summary>Días distintos en que ha comprado algo del mismo grupo y subgrupo en 24 meses.</summary>
    public float ComprasSubgrupo24Meses { get; set; }

    // ---- Producto ----
    /// <summary>Clientes distintos que lo compran en 12 meses (popularidad).</summary>
    public float Clientes12Meses { get; set; }
    /// <summary>Ídem en 3 meses.</summary>
    public float Clientes3Meses { get; set; }
    /// <summary>Clientes3Meses × 4 ÷ Clientes12Meses; 0 si no hay. &gt; 1 = sube.</summary>
    public float TendenciaPopularidad { get; set; }

    // ---- Cliente ----
    /// <summary>Días de compra del cliente en 12 meses.</summary>
    public float PedidosCliente12Meses { get; set; }
    public float ImporteCliente12Meses { get; set; }
    /// <summary>Días desde su última compra (de lo que sea); <see cref="SIN_DATO"/> si no hay.</summary>
    public float DiasDesdeUltimoPedidoCliente { get; set; }
    /// <summary>Productos distintos que ha comprado en 24 meses.</summary>
    public float ProductosDistintosCliente { get; set; }

    // ---- Carrito ----
    /// <summary>Productos distintos en el carrito.</summary>
    public float TamanoCarrito { get; set; }
    public float CarritoVacio { get; set; }
    /// <summary>Productos del carrito del mismo grupo y subgrupo.</summary>
    public float MismoSubgrupoEnCarrito { get; set; }
    /// <summary>Productos del carrito de la misma familia (marca).</summary>
    public float MismaFamiliaEnCarrito { get; set; }

    // ---- Estado del cliente (versión 2, NestoAPI#683) ----
    /// <summary>Grupo del estado del cliente (<see cref="EstadosCliente.Grupo"/>): «6» distribuidores, «0», «9»…; "NADA" sin ficha.</summary>
    public string EstadoCliente { get; set; } = "";
    /// <summary>Clientes de su mismo grupo de estado que lo han comprado en 12 meses (tabla del job); 0 si no hay.</summary>
    public float ClientesEstado12Meses { get; set; }
    /// <summary>ClientesEstado12Meses ÷ clientes del grupo con alguna compra en 12 meses; 0 si no hay.</summary>
    public float ProporcionEstado12Meses { get; set; }
    /// <summary>Clientes del grupo que han comprado algo de la familia (marca) en 12 meses ÷ clientes del grupo; 0 si no hay.</summary>
    public float ProporcionFamiliaEstado12Meses { get; set; }
    /// <summary>ProporcionEstado12Meses ÷ la misma proporción entre todos los clientes (&gt; 1 = su estado lo compra más que la media); 0 si no hay.</summary>
    public float IndiceEstado { get; set; }
    /// <summary>Lo mismo con la familia: «a los distribuidores, esta marca no» (≪ 1); 0 si no hay.</summary>
    public float IndiceFamiliaEstado { get; set; }
}

/// <summary>Una fila del dataset: la entrada del modelo + etiqueta y datos de la evaluación (no son features).</summary>
public sealed class FilaEntrenamiento : ModeloSugerenciaEntrada
{
    /// <summary>Se lo llevó: en la otra mitad del pedido o en los 30 días siguientes.</summary>
    public bool Label { get; set; }
    /// <summary>Label en float, para LambdaRank.</summary>
    public float Relevancia { get; set; }
    /// <summary>La sesión (cliente y día): el grupo de LambdaRank y de las métricas.</summary>
    public string Sesion { get; set; } = "";
    public string Familia { get; set; } = "";
    /// <summary>Puntuación de reglas-1 para su fuente (confianza, ratio de «le toca»); 0 si reglas-1 no lo propone.</summary>
    public float PuntuacionReglas { get; set; }
    /// <summary>La fuente con la que lo propone reglas-1 (CompradoJunto o LeToca); "" si no lo propone.</summary>
    public string FuenteReglas { get; set; } = "";
}

/// <summary>Salida del modelo binario calibrado.</summary>
public sealed class PrediccionSugerencia
{
    public bool PredictedLabel { get; set; }
    public float Score { get; set; }
    public float Probability { get; set; }
}

/// <summary>Salida de LambdaRank.</summary>
public sealed class PrediccionRanking
{
    public float Score { get; set; }
}
