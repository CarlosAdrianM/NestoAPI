namespace ModeloSugerenciasProductos.Entrenamiento;

/// <summary>Qué se entrena y cómo. Los valores por defecto son los del plan de NestoAPI#671 (20/10/26).</summary>
public sealed class OpcionesEntrenamiento
{
    /// <summary>Meses de compras que se leen hacia atrás desde hoy.</summary>
    public int Meses { get; set; } = 36;
    /// <summary>Instantáneas mensuales de entrenamiento.</summary>
    public int Instantaneas { get; set; } = 18;
    /// <summary>Instantáneas (meses) de validación, después de un mes de hueco.</summary>
    public int MesesValidacion { get; set; } = 3;
    /// <summary>Meses de historial que tiene que haber, como poco, antes de la primera instantánea.</summary>
    public int MesesHistoriaMinima { get; set; } = 6;
    /// <summary>0 = todos los clientes; N = una muestra fija de N clientes (prueba ligera).</summary>
    public int MuestraClientes { get; set; }
    public int Semilla { get; set; } = 671;
    /// <summary>Filas (sesiones) con el carrito vacío.</summary>
    public double ProporcionCarritoVacio { get; set; } = 0.30;
    /// <summary>Días después del pedido que cuentan para la etiqueta.</summary>
    public int DiasEtiqueta { get; set; } = 30;
    public int MaximoCandidatosPorFuente { get; set; } = 15;

    // LightGBM
    public int Hojas { get; set; } = 31;
    public int Iteraciones { get; set; } = 300;
    public int MinimoEjemplosPorHoja { get; set; } = 50;
    public double TasaAprendizaje { get; set; } = 0.05;
    /// <summary>
    /// Hilos de LightGBM (con los mismos hilos y la misma semilla, el mismo modelo). 1 por defecto: medido el 20/10/26 en
    /// RDS2016 (2 núcleos), con 2 hilos tarda el doble y LambdaRank, diez veces más (OpenMP se pelea con lo demás).
    /// </summary>
    public int Hilos { get; set; } = 1;
    /// <summary>Entrena y mide también LambdaRank (solo para el informe).</summary>
    public bool EvaluarLambdaRank { get; set; } = true;

    /// <summary>Puerta: el modelo tiene que ganar a reglas-1 en P@3 por al menos esto (fracción: 0,02 = 2 puntos).</summary>
    public double MinimaMejoraP3 { get; set; } = 0.02;

    public string Alcance => MuestraClientes > 0 ? $"muestra de {MuestraClientes} clientes, {Meses} meses" : $"todos los clientes, {Meses} meses";
}
