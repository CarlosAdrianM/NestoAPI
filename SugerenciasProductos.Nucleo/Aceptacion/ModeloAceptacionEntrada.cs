using ModeloSugerenciasProductos.Features;

namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>
/// NestoAPI#671 (corte 2e): ENTRADA DEL MODELO 2 (aceptación: P(el cliente dice «Añadir» | el vendedor se la ofreció)). La
/// MISMA clase en el entrenamiento (job semanal) y en la API (sombra y, si se activa, orden): ML.NET enlaza por nombre de
/// columna, así que cambiar un nombre o un tipo obliga a reentrenar y a subir <see cref="VERSION_FEATURES"/>. La calcula
/// <see cref="CalculadoraFeaturesAceptacion"/>. Stacking: P1 (el modelo 1) es una feature más.
/// <para>Rasgos de #671: motivo y fuentes, lift de la pareja, días desde la última compra frente a la mediana, frecuencia del
/// cliente, carrito, si es novedad u oferta (fuente Potenciar), estado del cliente, vendedor, mes y las tasas históricas de
/// aceptación del producto, del motivo, del vendedor y del cliente. Los del modelo 1 salen de la instantánea guardada al
/// enseñarla (SugerenciasProductosSombra); en las filas de antes de la sombra, <see cref="TieneInstantanea"/> = 0 y van a 0.</para>
/// <para>Posición: en el entrenamiento, el hueco en que se enseñó (corrige el sesgo de posición); en la API, siempre 1 (se
/// puntúa «como si fuera la primera»).</para>
/// </summary>
public class ModeloAceptacionEntrada
{
    public const int VERSION_FEATURES = 1;

    // ---- No son features ----
    public string Producto { get; set; } = "";

    // ---- Categóricas ----
    public string Motivo { get; set; } = "";
    public string Vendedor { get; set; } = "";
    public string Aplicacion { get; set; } = "";
    public string Mes { get; set; } = "";
    public string EstadoCliente { get; set; } = "";
    public string GrupoSubgrupoProducto { get; set; } = "";

    // ---- Del modelo 1 ----
    /// <summary>P1 si se enseñó con el modelo 1; 0 si no.</summary>
    public float P1 { get; set; }
    public float TieneP1 { get; set; }

    // ---- Cómo se enseñó ----
    public float Posicion { get; set; }
    public float Exploracion { get; set; }
    public float TamanoCarrito { get; set; }
    public float DiaSemana { get; set; }
    public float NumeroFuentes { get; set; }
    public float FuenteCompradoJunto { get; set; }
    public float FuenteLeToca { get; set; }
    public float FuentePotenciar { get; set; }
    public float FuenteParecidos { get; set; }
    public float FuentePerfil { get; set; }

    // ---- Instantánea del modelo 1 ----
    public float TieneInstantanea { get; set; }
    public float LiftCompradoJunto { get; set; }
    public float ConfianzaCompradoJunto { get; set; }
    public float RatioLeToca { get; set; }
    public float DiasDesdeUltimaCompraProducto { get; set; }
    public float ComprasProducto24Meses { get; set; }
    public float EsHabitual { get; set; }
    public float PedidosCliente12Meses { get; set; }
    public float ProductosDistintosCliente { get; set; }
    public float Clientes12Meses { get; set; }
    public float TendenciaPopularidad { get; set; }
    public float IndiceEstado { get; set; }

    // ---- Tasas históricas (solo respuestas anteriores) ----
    public float TasaProducto { get; set; }
    public float RespuestasProducto { get; set; }
    public float TasaMotivo { get; set; }
    public float TasaVendedor { get; set; }
    public float TasaCliente { get; set; }
    public float RespuestasCliente { get; set; }
}

/// <summary>Una fila del entrenamiento: la entrada + etiqueta, peso y lo que hace falta para partir y medir.</summary>
public sealed class FilaAceptacion : ModeloAceptacionEntrada
{
    public bool Label { get; set; }
    /// <summary>1 las respondidas; <see cref="OpcionesAceptacion.PesoSenalDebil"/> las señales débiles.</summary>
    public float Peso { get; set; } = 1f;
    public string Sesion { get; set; } = "";
    public DateTime Fecha { get; set; }
    public bool Respondida { get; set; }
    public string VendedorFila { get; set; } = "";
    /// <summary>La puntuación con que ordena hoy Calcular (P1), para la comparación; NaN si se enseñó con las reglas.</summary>
    public double PuntuacionActual { get; set; } = double.NaN;
    /// <summary>La del reordenador bayesiano (con las tasas de antes de la fila).</summary>
    public double PuntuacionBayes { get; set; } = double.NaN;
}

public sealed class PrediccionAceptacion
{
    public bool PredictedLabel { get; set; }
    public float Score { get; set; }
    public float Probability { get; set; }
}

/// <summary>NestoAPI#671 (corte 2e): de una sugerencia (y su instantánea) a la entrada del modelo 2. La usan el job y la API.</summary>
public static class CalculadoraFeaturesAceptacion
{
    public const string NADA = "NADA";

    /// <param name="instantanea">Las features del modelo 1 de ese candidato (null si no hay).</param>
    /// <param name="tasas">Las tasas con lo respondido ANTES (null: sin historia).</param>
    public static T Calcular<T>(MostradaAceptacion m, ModeloSugerenciaEntrada? instantanea, TasasAceptacion? tasas) where T : ModeloAceptacionEntrada, new()
    {
        if (m == null) throw new ArgumentNullException(nameof(m));
        tasas ??= new TasasAceptacion();
        HashSet<string> fuentes = Fuentes(m.Fuentes, m.Motivo);
        bool tieneP1 = m.TieneP1;
        var e = new T
        {
            Producto = (m.Producto ?? string.Empty).Trim(),
            Motivo = Categoria(m.Motivo),
            Vendedor = Categoria(m.Vendedor),
            Aplicacion = Categoria(m.Aplicacion),
            Mes = m.FechaMostrada == default ? NADA : m.FechaMostrada.Month.ToString(System.Globalization.CultureInfo.InvariantCulture),
            EstadoCliente = Categoria(instantanea?.EstadoCliente),
            GrupoSubgrupoProducto = Categoria(instantanea?.GrupoSubgrupoProducto),
            P1 = tieneP1 ? (float)m.Puntuacion : 0f,
            TieneP1 = tieneP1 ? 1f : 0f,
            Posicion = m.Posicion == 0 ? 1f : m.Posicion,
            Exploracion = m.Exploracion ? 1f : 0f,
            TamanoCarrito = m.TamanoCarrito ?? instantanea?.TamanoCarrito ?? 0f,
            DiaSemana = m.FechaMostrada == default ? 0f : (float)(int)m.FechaMostrada.DayOfWeek,
            NumeroFuentes = fuentes.Count,
            FuenteCompradoJunto = fuentes.Contains(Reglas.Fuentes.COMPRADO_JUNTO) ? 1f : 0f,
            FuenteLeToca = fuentes.Contains(Reglas.Fuentes.LE_TOCA) ? 1f : 0f,
            FuentePotenciar = fuentes.Contains(Reglas.Fuentes.POTENCIAR) ? 1f : 0f,
            FuenteParecidos = fuentes.Contains(Reglas.Fuentes.PARECIDOS) ? 1f : 0f,
            FuentePerfil = fuentes.Contains(Reglas.Fuentes.PERFIL) ? 1f : 0f,
            TasaProducto = (float)tasas.Producto(m.Producto, m.Motivo),
            RespuestasProducto = tasas.RespondidasProducto(m.Producto),
            TasaMotivo = (float)tasas.Motivo(m.Motivo),
            TasaVendedor = (float)tasas.Vendedor(m.Vendedor),
            TasaCliente = (float)tasas.Cliente(m.Cliente),
            RespuestasCliente = tasas.RespondidasCliente(m.Cliente)
        };
        if (instantanea != null)
        {
            e.TieneInstantanea = 1f;
            e.LiftCompradoJunto = instantanea.LiftCompradoJunto;
            e.ConfianzaCompradoJunto = instantanea.ConfianzaCompradoJunto;
            e.RatioLeToca = instantanea.RatioLeToca;
            e.DiasDesdeUltimaCompraProducto = instantanea.DiasDesdeUltimaCompraProducto;
            e.ComprasProducto24Meses = instantanea.ComprasProducto24Meses;
            e.EsHabitual = instantanea.EsHabitual;
            e.PedidosCliente12Meses = instantanea.PedidosCliente12Meses;
            e.ProductosDistintosCliente = instantanea.ProductosDistintosCliente;
            e.Clientes12Meses = instantanea.Clientes12Meses;
            e.TendenciaPopularidad = instantanea.TendenciaPopularidad;
            e.IndiceEstado = instantanea.IndiceEstado;
        }
        else
        {
            e.DiasDesdeUltimaCompraProducto = ModeloSugerenciaEntrada.SIN_DATO;
        }
        return e;
    }

    /// <summary>Las fuentes de la fila (si no se registraron, la del motivo).</summary>
    internal static HashSet<string> Fuentes(string? fuentes, string? motivo)
    {
        var resultado = new HashSet<string>(
            (fuentes ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).Where(f => f.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (resultado.Count == 0 && !string.IsNullOrWhiteSpace(motivo))
        {
            resultado.Add(motivo!.Trim());
        }
        return resultado;
    }

    private static string Categoria(string? valor) => string.IsNullOrWhiteSpace(valor) ? NADA : valor!.Trim().ToUpperInvariant();
}
