using System.Globalization;

namespace ModeloSugerenciasProductos.Features;

/// <summary>
/// NestoAPI#683: el estado del cliente (Clientes.Estado del contacto principal) agrupado para el modelo 1. La MISMA función
/// en el entrenamiento, en el job nocturno (popularidad por estado) y en Calcular: cambiar un grupo obliga a subir
/// <see cref="ModeloSugerenciaEntrada.VERSION_FEATURES"/> y a reentrenar.
/// <para>Grupos (clientes con compras en 24 meses a 21/10/26 entre paréntesis): «9» = 9 y 99, en vías de recuperación (1.363);
/// «0» = 0, 21, 31 y 88, visita de comercial con cualquier frecuencia (1.235); «-1» (686); «7» comisiona vendedor (567); «8»
/// particular web (361); «93» solo estética sin compra (322); «95» una sola compra por Amazon (287); «2» = 2 y 3, «NO USAR»
/// (133); «11» = 11 y 22, peluquería sin compra (84); «6» = 6 y 67, distribuidores y sus contactos (39); «OTRO» = los raros
/// (centros de estética, escuelas, proveedores, 1ª visitas…); «NADA» = sin ficha de cliente.</para>
/// </summary>
public static class EstadosCliente
{
    public const string NADA = CalculadoraFeatures.NADA;
    public const string OTRO = "OTRO";
    /// <summary>Todos los clientes (la popularidad de referencia).</summary>
    public const string TODOS = "*";
    public const string DISTRIBUIDORES = "6";

    public static string Grupo(short? estado)
    {
        if (estado == null) return NADA;
        switch (estado.Value)
        {
            case 6:
            case 67:
                return DISTRIBUIDORES;
            case 9:
            case 99:
                return "9";
            case 0:
            case 21:
            case 31:
            case 88:
                return "0";
            case 2:
            case 3:
                return "2";
            case 11:
            case 22:
                return "11";
            case -1:
            case 7:
            case 8:
            case 93:
            case 95:
                return estado.Value.ToString(CultureInfo.InvariantCulture);
            default:
                return OTRO;
        }
    }

    public static bool EsDistribuidor(string? grupo) => grupo == DISTRIBUIDORES;
}

/// <summary>NestoAPI#683: una fila de SeguimientoCambiosEstadoCliente (la escribe el trigger trgClientesUpd desde el 16/01/2025).</summary>
public sealed class CambioEstadoCliente
{
    public string Cliente { get; set; } = "";
    public DateTime Fecha { get; set; }
    public short? EstadoAnterior { get; set; }
    public short? EstadoNuevo { get; set; }
}

/// <summary>
/// NestoAPI#683: el estado de cada cliente en una fecha, SIN FUGA: el de hoy (Clientes.Estado del contacto principal)
/// deshaciendo los cambios de SeguimientoCambiosEstadoCliente posteriores. El estado vigente al empezar el día d es el
/// EstadoAnterior del primer cambio con fecha ≥ d o, si no hay ninguno, el de hoy. Antes del primer cambio apuntado (el
/// trigger existe desde el 16/01/2025) no se sabe más: se toma el anterior del primer cambio.
/// <para>En Calcular y en el job nocturno el estado es el de hoy (sin cambios posteriores): lo mismo que da esto para hoy.</para>
/// </summary>
public sealed class HistorialEstadosCliente
{
    private readonly Dictionary<string, short?> actuales;
    private readonly Dictionary<string, List<CambioEstadoCliente>> cambios;

    /// <param name="actuales">Cliente → estado de hoy.</param>
    /// <param name="cambios">Cambios del contacto principal (null o vacío: sin historial, todo es el estado de hoy).</param>
    public HistorialEstadosCliente(IDictionary<string, short?>? actuales, IEnumerable<CambioEstadoCliente>? cambios)
    {
        this.actuales = new Dictionary<string, short?>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, short?> a in actuales ?? new Dictionary<string, short?>())
        {
            if (!string.IsNullOrWhiteSpace(a.Key)) this.actuales[a.Key.Trim()] = a.Value;
        }
        this.cambios = (cambios ?? Enumerable.Empty<CambioEstadoCliente>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Cliente))
            .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Fecha).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public static HistorialEstadosCliente Vacio { get; } = new(null, null);

    /// <summary>Estado vigente al empezar el día <paramref name="dia"/>; null si el cliente no tiene ficha.</summary>
    public short? EstadoEn(string cliente, DateTime dia)
    {
        string c = cliente?.Trim() ?? "";
        DateTime d = dia.Date;
        if (cambios.TryGetValue(c, out List<CambioEstadoCliente>? lista))
        {
            foreach (CambioEstadoCliente cambio in lista)
            {
                if (cambio.Fecha >= d) return cambio.EstadoAnterior;
            }
        }
        return actuales.TryGetValue(c, out short? e) ? e : null;
    }
}
