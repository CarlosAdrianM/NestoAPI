namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>
/// NestoAPI#671 (corte 2e): una sugerencia enseñada, tal y como la ve el modelo 2 (aceptación). En el entrenamiento es una fila
/// de SugerenciasProductosMostradas (con su sesión y, si la hay, la instantánea de features de SugerenciasProductosSombra); en
/// la API, el candidato que se va a enseñar (sin respuesta y con <see cref="Posicion"/> = 1). Los nombres coinciden con las
/// columnas de la consulta del job (EF los enlaza por nombre).
/// </summary>
public sealed class MostradaAceptacion
{
    public int Id { get; set; }
    /// <summary>La sesión de la plantilla (Guid en texto): las sesiones no se parten entre entrenamiento y validación.</summary>
    public string SesionId { get; set; } = "";
    public string Producto { get; set; } = "";
    /// <summary>La fuente que se enseñó al vendedor (CompradoJunto, LeToca, Potenciar, Parecidos).</summary>
    public string Motivo { get; set; } = "";
    /// <summary>Todas las fuentes que lo propusieron, separadas por comas (null antes del script Issue671a).</summary>
    public string? Fuentes { get; set; }
    /// <summary>Con el modelo 1 (VersionMotor «m1-»/«m2-»), P1; con las reglas, la puntuación de su fuente (no comparable).</summary>
    public decimal Puntuacion { get; set; }
    public byte Posicion { get; set; }
    public string VersionMotor { get; set; } = "";
    public DateTime FechaMostrada { get; set; }
    /// <summary>Anadido | NoLeInteresa | DescartadoVendedor | Sustituida | null (no tocada).</summary>
    public string? Respuesta { get; set; }
    public bool? EntroEnPedido { get; set; }
    public int? TamanoCarrito { get; set; }
    public bool Exploracion { get; set; }
    public string? Vendedor { get; set; }
    public string? Aplicacion { get; set; }
    public string? Cliente { get; set; }
    /// <summary>Instantánea de las features del modelo 1 cuando se enseñó (<see cref="InstantaneaFeatures"/>); null si no hay.</summary>
    public string? Features { get; set; }

    /// <summary>Respondida por el cliente: «Anadido» (1) o «NoLeInteresa» (0). «DescartadoVendedor» y «Sustituida», no.</summary>
    public bool EsRespondida => RespuestasAceptacion.EsRespondida(Respuesta);

    public bool Aceptada => RespuestasAceptacion.EsAceptada(Respuesta);

    /// <summary>Señal débil: nadie la tocó pero el producto acabó en el pedido.</summary>
    public bool EsSenalDebil => Respuesta == null && EntroEnPedido == true;

    /// <summary>Si <see cref="Puntuacion"/> es P1 (la enseñó el modelo 1, o el 2, que la guarda igual).</summary>
    public bool TieneP1 => RespuestasAceptacion.TieneP1(VersionMotor);
}

/// <summary>NestoAPI#671 (corte 2e): las respuestas del cliente (las mismas cadenas que RespuestasSugerencia de la API).</summary>
public static class RespuestasAceptacion
{
    public const string ANADIDO = "Anadido";
    public const string NO_LE_INTERESA = "NoLeInteresa";
    /// <summary>Prefijo de VersionMotor del modelo 1 (P1 en Puntuacion).</summary>
    public const string PREFIJO_MODELO1 = "m1-";
    /// <summary>Prefijo de VersionMotor del modelo 2 cuando ordena (Puntuacion sigue siendo P1).</summary>
    public const string PREFIJO_MODELO2 = "m2-";

    public static bool EsRespondida(string? respuesta) => EsAceptada(respuesta) || string.Equals(respuesta?.Trim(), NO_LE_INTERESA, StringComparison.OrdinalIgnoreCase);

    public static bool EsAceptada(string? respuesta) => string.Equals(respuesta?.Trim(), ANADIDO, StringComparison.OrdinalIgnoreCase);

    public static bool TieneP1(string? versionMotor)
        => versionMotor != null && (versionMotor.StartsWith(PREFIJO_MODELO1, StringComparison.OrdinalIgnoreCase)
                                    || versionMotor.StartsWith(PREFIJO_MODELO2, StringComparison.OrdinalIgnoreCase));
}
