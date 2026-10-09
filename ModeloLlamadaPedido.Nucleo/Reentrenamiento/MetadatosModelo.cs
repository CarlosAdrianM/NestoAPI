namespace ModeloLlamadaPedido.Reentrenamiento;

/// <summary>
/// NestoAPI#619: lo que el job de la API deja en <c>modelo_llamadas.json</c> junto al zip al promoverlo. Sirve para saber qué
/// contactos vio el modelo (la próxima puerta compara solo con los posteriores a <see cref="ContactosHasta"/>) y de dónde
/// salió. Sin serializador aquí (netstandard2.0 sin dependencias): la API lo escribe con Newtonsoft y la consola lo lee con
/// System.Text.Json; los nombres de las propiedades son el contrato.
/// </summary>
public sealed class MetadatosModelo
{
    public const string NOMBRE_FICHERO = "modelo_llamadas.json";

    /// <summary>Cuándo se promovió.</summary>
    public DateTime Fecha { get; set; }
    public DateTime ContactosDesde { get; set; }
    /// <summary>Fin (excluido) de los contactos con los que se entrenó: el primer día que el modelo NO vio.</summary>
    public DateTime ContactosHasta { get; set; }
    public int Contactos { get; set; }
    public int Positivos { get; set; }
    public string Trainer { get; set; } = "";
    public string Alcance { get; set; } = "";
    public int K { get; set; }
    /// <summary>Métricas de la puerta (fracción; null si no había).</summary>
    public double? PrecisionAnterior { get; set; }
    public double? AucAnterior { get; set; }
    public double? PrecisionNuevo { get; set; }
    public double? AucNuevo { get; set; }
    public string Motivo { get; set; } = "";
    /// <summary>Quién lo lanzó: «job mensual» o el usuario del endpoint.</summary>
    public string Origen { get; set; } = "";

    public static MetadatosModelo Desde(ProcesoReentrenamiento r, DateTime fecha, string origen) => new()
    {
        Fecha = fecha,
        ContactosDesde = r.Desde,
        ContactosHasta = r.Hasta,
        Contactos = r.Contactos,
        Positivos = r.Positivos,
        Trainer = r.Opciones.Trainer,
        Alcance = r.Opciones.Alcance,
        K = r.Opciones.K,
        PrecisionAnterior = Numero(r.Actual?.PrecisionK),
        AucAnterior = Numero(r.Actual?.Auc),
        PrecisionNuevo = Numero(r.Nuevo.PrecisionK),
        AucNuevo = Numero(r.Nuevo.Auc),
        Motivo = r.Decision.Motivo,
        Origen = origen
    };

    public static double? Numero(double? v) => v == null || double.IsNaN(v.Value) ? null : Math.Round(v.Value, 6);
}
