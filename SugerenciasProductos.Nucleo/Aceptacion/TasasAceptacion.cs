namespace ModeloSugerenciasProductos.Aceptacion;

/// <summary>
/// NestoAPI#671 (corte 2e): tasas históricas de aceptación (respuestas «Anadido» ÷ respuestas «Anadido» + «NoLeInteresa»),
/// suavizadas hacia la de arriba para que 1 de 1 no valga 100 %: la global hacia <see cref="PRIOR_GLOBAL"/>, la del motivo, el
/// vendedor y el cliente hacia la global y la del producto hacia la de su motivo. Con <see cref="FUERZA"/> respuestas «de
/// mentira» en cada nivel. En el entrenamiento se van sumando por orden de fecha (cada fila solo ve las respuestas anteriores:
/// sin fuga); en la API, con todas las respuestas (caché de unos minutos).
/// </summary>
public sealed class TasasAceptacion
{
    /// <summary>Lo que se supone antes de ver ninguna respuesta (≈ la tasa de la primera semana: 8 de 21).</summary>
    public const double PRIOR_GLOBAL = 0.35;
    public const double FUERZA = 10;

    private sealed class Cuenta
    {
        public int Aceptadas;
        public int Respondidas;
    }

    private readonly Cuenta global = new();
    private readonly Dictionary<string, Cuenta> porProducto = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Cuenta> porMotivo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Cuenta> porVendedor = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Cuenta> porCliente = new(StringComparer.OrdinalIgnoreCase);

    public static TasasAceptacion Desde(IEnumerable<MostradaAceptacion> mostradas)
    {
        var tasas = new TasasAceptacion();
        foreach (MostradaAceptacion m in mostradas ?? Enumerable.Empty<MostradaAceptacion>())
        {
            tasas.Anadir(m);
        }
        return tasas;
    }

    public int Respondidas => global.Respondidas;
    public int Aceptadas => global.Aceptadas;

    /// <summary>Suma la respuesta (si la tiene: lo demás se ignora).</summary>
    public void Anadir(MostradaAceptacion? m)
    {
        if (m == null || !m.EsRespondida) return;
        bool aceptada = m.Aceptada;
        Sumar(global, aceptada);
        Sumar(Clave(porProducto, m.Producto), aceptada);
        Sumar(Clave(porMotivo, m.Motivo), aceptada);
        Sumar(Clave(porVendedor, m.Vendedor), aceptada);
        Sumar(Clave(porCliente, m.Cliente), aceptada);
    }

    public double Global => Suavizada(global, PRIOR_GLOBAL);
    public double Motivo(string? motivo) => Suavizada(Buscar(porMotivo, motivo), Global);
    public double Vendedor(string? vendedor) => Suavizada(Buscar(porVendedor, vendedor), Global);
    public double Cliente(string? cliente) => Suavizada(Buscar(porCliente, cliente), Global);
    /// <summary>La del producto, suavizada hacia la del motivo con el que se enseña.</summary>
    public double Producto(string? producto, string? motivo) => Suavizada(Buscar(porProducto, producto), Motivo(motivo));

    public int RespondidasProducto(string? producto) => Buscar(porProducto, producto)?.Respondidas ?? 0;
    public int RespondidasCliente(string? cliente) => Buscar(porCliente, cliente)?.Respondidas ?? 0;

    private static double Suavizada(Cuenta? cuenta, double prior)
        => ((cuenta?.Aceptadas ?? 0) + FUERZA * prior) / ((cuenta?.Respondidas ?? 0) + FUERZA);

    private static void Sumar(Cuenta? cuenta, bool aceptada)
    {
        if (cuenta == null) return;
        cuenta.Respondidas++;
        if (aceptada) cuenta.Aceptadas++;
    }

    private static Cuenta? Clave(Dictionary<string, Cuenta> diccionario, string? clave)
    {
        string? limpia = clave?.Trim();
        if (string.IsNullOrEmpty(limpia)) return null;
        if (!diccionario.TryGetValue(limpia!, out Cuenta? cuenta))
        {
            cuenta = new Cuenta();
            diccionario[limpia!] = cuenta;
        }
        return cuenta;
    }

    private static Cuenta? Buscar(Dictionary<string, Cuenta> diccionario, string? clave)
    {
        string? limpia = clave?.Trim();
        return string.IsNullOrEmpty(limpia) ? null : diccionario.TryGetValue(limpia!, out Cuenta? c) ? c : null;
    }
}

/// <summary>
/// NestoAPI#671 (corte 2e): lo que hay HASTA que se pueda entrenar el modelo 2 (plan del 20/10/26): P1 × la tasa de aceptación
/// suavizada del producto (con su motivo) ÷ la global. Sin respuestas da P1 (el mismo orden que el modelo 1). Solo en sombra:
/// no tiene puerta (con tan pocas respuestas no se puede medir), así que nunca cambia lo que se enseña.
/// </summary>
public static class ReordenadorBayesiano
{
    public const string VERSION = "bayes-1";

    public static float Puntuar(float p1, TasasAceptacion tasas, string? producto, string? motivo)
    {
        if (tasas == null) return p1;
        double global = tasas.Global;
        return global <= 0 ? p1 : (float)(p1 * tasas.Producto(producto, motivo) / global);
    }
}
