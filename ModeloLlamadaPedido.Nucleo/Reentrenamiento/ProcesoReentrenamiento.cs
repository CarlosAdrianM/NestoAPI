using System.Diagnostics;
using System.Text;
using Microsoft.ML;
using ModeloLlamadaPedido.Datos;
using ModeloLlamadaPedido.Entrenamiento;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Reentrenamiento;

/// <summary>
/// NestoAPI#603 c3 / #619: el reentrenamiento entero salvo la lectura de la BD y la promoción, para que la consola y el job
/// de la API hagan exactamente lo mismo:
/// <list type="number">
/// <item>Dataset (una fila por contacto) y partición temporal: validación = los últimos <c>MesesValidacion</c> meses.</item>
/// <item>Modelo nuevo entrenado con la parte de entrenamiento y comparado en la validación con el de producción (puntuado con
/// <see cref="ModeloContactoEntrada"/>, como la API) y con las bases (informativo: ahí el actual juega en casa).</item>
/// <item>Puerta de calidad JUSTA: solo con los contactos que el modelo actual no vio (desde <c>CorteActual</c>) y con un
/// candidato entrenado con ese mismo corte; los dos fuera de muestra y con datos hasta la misma fecha.</item>
/// <item><see cref="EntrenarFinalYGuardar"/>: el modelo que se guarda se reentrena con TODO el dataset.</item>
/// </list>
/// </summary>
public sealed class ProcesoReentrenamiento
{
    private readonly OpcionesReentrenamiento _op;
    private readonly MLContext _ml;
    private readonly List<FilaEntrenamiento> _filas;
    private readonly List<FilaEntrenamiento> _entrenamiento;
    private readonly List<FilaEntrenamiento> _validacion;
    private readonly Action<string> _log;

    public DateTime Desde { get; }
    public DateTime Hasta { get; }
    public DateTime Corte { get; }
    public int Contactos => _filas.Count;
    public int Positivos => _filas.Count(f => f.Label);
    public int ContactosEntrenamiento => _entrenamiento.Count;
    public int ContactosValidacion => _validacion.Count;

    /// <summary>False si no hay con qué entrenar y validar (entonces <see cref="Decision"/> no promueve).</summary>
    public bool DatosSuficientes { get; private set; }
    /// <summary>Tabla de la validación temporal (informativa).</summary>
    public string Tabla { get; private set; } = "";
    public string TablaCalibracion { get; private set; } = "";
    /// <summary>Tabla de la puerta con los contactos no vistos ("" si la puerta usa la validación de arriba).</summary>
    public string TablaPuerta { get; private set; } = "";
    public bool HayModeloActual { get; private set; }
    /// <summary>Día desde el que compara la puerta.</summary>
    public DateTime CortePuerta { get; private set; }
    public bool PuertaConContactosNoVistos { get; private set; }
    public MetricasPuerta Nuevo { get; private set; } = new(double.NaN, double.NaN);
    public MetricasPuerta? Actual { get; private set; }
    public DecisionPuerta Decision { get; private set; } = new(false, "");
    /// <summary>«P@20 62,4 % → 63,0 %, AUC 0,792 → 0,795».</summary>
    public string Comparacion => PuertaCalidad.Comparacion(Actual, Nuevo, _op.K);
    public OpcionesReentrenamiento Opciones => _op;

    private ProcesoReentrenamiento(DatosCrudos datos, OpcionesReentrenamiento op, DateTime hoy, Action<string>? log)
    {
        _op = op;
        _log = log ?? (_ => { });
        _ml = Entrenador.CrearContexto();
        (Desde, Hasta, Corte) = VentanaReentrenamiento.Calcular(hoy, op.Completo, op.Meses, op.MesesValidacion);
        _filas = ConstructorDataset.Construir(datos, Desde, Hasta, op.Completo ? null : op.Vendedor).Filas;
        (List<int> iEntrenamiento, List<int> iValidacion) = ConstructorDataset.Particion(_filas, Corte);
        _entrenamiento = iEntrenamiento.Select(i => _filas[i]).ToList();
        _validacion = iValidacion.Select(i => _filas[i]).ToList();
    }

    /// <summary>Construye el dataset, entrena, compara y decide (sin guardar nada).</summary>
    /// <param name="datos">Lo leído de la BD (<see cref="RepositorioDatosSql"/>) para la misma ventana.</param>
    /// <param name="op">Qué se entrena y contra qué se compara.</param>
    /// <param name="hoy">Día del reentrenamiento (la ventana termina 8 días antes).</param>
    /// <param name="log">Progreso legible (la consola lo escribe en pantalla).</param>
    public static ProcesoReentrenamiento Evaluar(DatosCrudos datos, OpcionesReentrenamiento op, DateTime hoy, Action<string>? log = null)
    {
        var r = new ProcesoReentrenamiento(datos, op, hoy, log);
        r.EvaluarInterno();
        return r;
    }

    private void EvaluarInterno()
    {
        var reloj = Stopwatch.StartNew();
        if (_filas.Count > 0)
            _log($"Dataset: {_filas.Count} contactos ({Informe.Pct(_filas.Average(f => f.Label ? 1.0 : 0.0))} positivos, " +
                 $"{_filas.Count(f => f.SinHistorial > 0)} sin historial). Entrenamiento {_entrenamiento.Count} ({_entrenamiento.Count(f => f.Label)} +), " +
                 $"validación {_validacion.Count} ({_validacion.Count(f => f.Label)} +).");
        if (_entrenamiento.Count == 0 || _validacion.Count == 0 || _entrenamiento.All(f => f.Label) || _entrenamiento.All(f => !f.Label))
        {
            DatosSuficientes = false;
            Decision = new DecisionPuerta(false, "no hay datos suficientes para entrenar y validar");
            _log("No hay datos suficientes para entrenar y validar.");
            return;
        }
        DatosSuficientes = true;

        List<bool> etiquetas = _validacion.Select(f => f.Label).ToList();
        var resultados = new List<FilaInforme>();
        FilaInforme? filaNuevo = null;
        IEnumerable<string> trainers = _op.EvaluarAlternativas
            ? new[] { Entrenador.LIGHTGBM, Entrenador.FASTTREE }
            : new[] { _op.Trainer };
        foreach (string trainer in trainers)
        {
            Entrenador.AsignarPesos(_entrenamiento, _op.Pesos);
            ITransformer modelo = Entrenador.Entrenar(_ml, _entrenamiento, trainer, _op.Pesos);
            List<float> p = Entrenador.Probabilidades(_ml, modelo, _validacion);
            var fila = new FilaInforme($"Nuevo {trainer}{(_op.Pesos ? " (pesos)" : "")}", Metricas.Auc(p, etiquetas),
                Metricas.PrecisionEnK(_validacion, p, _op.K), Metricas.Calibracion(p, etiquetas));
            resultados.Add(fila);
            if (trainer == _op.Trainer) filaNuevo = fila;
        }
        _log($"Modelo nuevo entrenado y validado en {reloj.Elapsed.TotalSeconds:0.0} s.");

        // El de producción se puntúa con la MISMA clase de entrada que usa la API (ModeloContactoEntrada) y sobre la MISMA
        // validación temporal.
        FilaInforme? filaActual = null;
        ITransformer? actual = null;
        if (!string.IsNullOrEmpty(_op.ModeloActual) && File.Exists(_op.ModeloActual))
        {
            actual = Entrenador.Cargar(_ml, _op.ModeloActual!);
            HayModeloActual = true;
            List<float> p = Entrenador.Probabilidades(_ml, actual, _validacion);
            filaActual = new FilaInforme($"Actual ({File.GetLastWriteTime(_op.ModeloActual):dd/MM/yyyy})", Metricas.Auc(p, etiquetas),
                Metricas.PrecisionEnK(_validacion, p, _op.K), Metricas.Calibracion(p, etiquetas));
            resultados.Add(filaActual);
        }
        else
        {
            _log($"No se encuentra el modelo actual ({_op.ModeloActual ?? "sin ruta"}): no se compara.");
        }

        if (_op.EvaluarAlternativas)
        {
            List<float> porPedidos = _validacion.Select(f => f.Pedidos12Meses + f.Importe12Meses / 1e7f).ToList(); // desempate por importe
            resultados.Add(new FilaInforme("Base: pedidos en 12 meses", Metricas.Auc(porPedidos, etiquetas), Metricas.PrecisionEnK(_validacion, porPedidos, _op.K), null));
            List<float> porConversion = _validacion.Select(f => f.TasaConversionCliente).ToList();
            resultados.Add(new FilaInforme("Base: tasa de conversión", Metricas.Auc(porConversion, etiquetas), Metricas.PrecisionEnK(_validacion, porConversion, _op.K), null));
        }

        Tabla = Informe.Tabla(resultados, _op.K);
        TablaCalibracion = Informe.TablaCalibracion(resultados);
        _log("");
        _log($"Validación ({Corte:dd/MM/yyyy} – {Hasta.AddDays(-1):dd/MM/yyyy}), precision@{_op.K} por vendedor y mes (un contacto por cliente y mes):");
        _log(Tabla);
        _log("Calibración (el motor de la API usa p ≥ 0,6 para la prioridad Máxima):");
        _log(TablaCalibracion);

        // Puerta: el modelo actual se entrenó con todo lo anterior a su fecha, así que en la validación de arriba juega en
        // casa. La comparación justa es solo con los contactos que NO vio (desde su corte), y el candidato de la puerta se
        // entrena con ese mismo corte. Lo que se mide es si el proceso de hoy (código + datos) es al menos tan bueno como
        // el que está en producción.
        DateTime corteActual = _op.CorteActual ?? DateTime.MinValue;
        CortePuerta = corteActual > Corte ? corteActual.Date : Corte;
        if (actual == null || CortePuerta == Corte)
        {
            Nuevo = new(filaNuevo!.Precision.Media, filaNuevo.Auc);
            if (filaActual != null) Actual = new(filaActual.Precision.Media, filaActual.Auc);
            if (actual != null) _log($"El modelo actual se entrenó con contactos anteriores al {corteActual:dd/MM/yyyy}: la puerta usa la validación de arriba.");
        }
        else
        {
            PuertaConContactosNoVistos = true;
            (List<int> iEntPuerta, List<int> iValPuerta) = ConstructorDataset.Particion(_filas, CortePuerta);
            List<FilaEntrenamiento> entPuerta = iEntPuerta.Select(i => _filas[i]).ToList();
            List<FilaEntrenamiento> valPuerta = iValPuerta.Select(i => _filas[i]).ToList();
            List<bool> etiquetasPuerta = valPuerta.Select(f => f.Label).ToList();
            if (valPuerta.Count > 0 && entPuerta.Any(f => f.Label) && entPuerta.Any(f => !f.Label))
            {
                Entrenador.AsignarPesos(entPuerta, _op.Pesos);
                ITransformer candidato = Entrenador.Entrenar(_ml, entPuerta, _op.Trainer, _op.Pesos);
                List<float> pn = Entrenador.Probabilidades(_ml, candidato, valPuerta);
                List<float> pa = Entrenador.Probabilidades(_ml, actual, valPuerta);
                var filasPuerta = new List<FilaInforme>
                {
                    new($"Nuevo {_op.Trainer}{(_op.Pesos ? " (pesos)" : "")}", Metricas.Auc(pn, etiquetasPuerta), Metricas.PrecisionEnK(valPuerta, pn, _op.K), Metricas.Calibracion(pn, etiquetasPuerta)),
                    new(filaActual!.Modelo, Metricas.Auc(pa, etiquetasPuerta), Metricas.PrecisionEnK(valPuerta, pa, _op.K), Metricas.Calibracion(pa, etiquetasPuerta))
                };
                Nuevo = new(filasPuerta[0].Precision.Media, filasPuerta[0].Auc);
                Actual = new(filasPuerta[1].Precision.Media, filasPuerta[1].Auc);
                TablaPuerta = $"Contactos desde el {CortePuerta:dd/MM/yyyy} (el modelo actual no los vio al entrenar): {valPuerta.Count} " +
                              $"({valPuerta.Count(f => f.Label)} +); candidato entrenado con {entPuerta.Count} contactos anteriores.{Environment.NewLine}{Environment.NewLine}" +
                              Informe.Tabla(filasPuerta, _op.K);
            }
            else
            {
                Nuevo = new(double.NaN, double.NaN);
                Actual = new(double.NaN, double.NaN);
                TablaPuerta = $"Contactos desde el {CortePuerta:dd/MM/yyyy} (el modelo actual no los vio al entrenar): {valPuerta.Count}; no hay datos para comparar.";
            }
            _log("Puerta de calidad, solo con contactos que el modelo actual no vio:");
            _log(TablaPuerta);
        }

        DecisionPuerta decision = PuertaCalidad.Decidir(HayModeloActual ? Actual : null, Nuevo, _op.K, _op.MaxBajadaPrecision, _op.MaxBajadaAuc);
        if (PuertaConContactosNoVistos)
            decision = decision with { Motivo = $"{decision.Motivo} (con los contactos desde el {CortePuerta:dd/MM/yyyy}, que el modelo actual no vio)" };
        Decision = decision;
        _log($"Evaluación terminada en {reloj.Elapsed.TotalSeconds:0.0} s.");
    }

    /// <summary>
    /// Reentrena con TODO el dataset (entrenamiento + validación), lo guarda en <paramref name="ruta"/> con el esquema de
    /// <see cref="ModeloContactoEntrada"/> y comprueba la ida y vuelta (cargarlo y puntuar unas filas con la clase de la API).
    /// Devuelve esas probabilidades de prueba.
    /// </summary>
    public List<float> EntrenarFinalYGuardar(string ruta)
    {
        if (!DatosSuficientes) throw new InvalidOperationException("No hay datos suficientes para entrenar el modelo final");
        Entrenador.AsignarPesos(_filas, _op.Pesos);
        ITransformer final = Entrenador.Entrenar(_ml, _filas, _op.Trainer, _op.Pesos);
        Entrenador.Guardar(_ml, final, ruta);
        ITransformer cargado = Entrenador.Cargar(_ml, ruta);
        List<float> prueba = Entrenador.Probabilidades(_ml, cargado, _validacion.Take(5));
        if (prueba.Count == 0 || prueba.Any(float.IsNaN))
            throw new InvalidOperationException($"El modelo guardado en {ruta} no puntúa bien en la ida y vuelta");
        _log($"Modelo guardado en {ruta} (ida y vuelta OK: {string.Join("; ", prueba.Select(x => x.ToString("0.000")))}).");
        return prueba;
    }

    /// <param name="rutaModelo">Zip guardado (null si no se guardó).</param>
    /// <param name="resultado">Lo que se hizo con la decisión («pasa», «no pasa», «pasa (simulación…)»); null si no se aplicó la puerta.</param>
    /// <param name="comoReentrenar">Sección final con cómo lanzarlo (cada llamante pone la suya).</param>
    public string Readme(string? rutaModelo, string? resultado, string comoReentrenar)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Modelo de llamadas — {DateTime.Now:dd/MM/yyyy HH:mm} (NestoAPI#603 c3, #619)");
        sb.AppendLine();
        if (rutaModelo != null)
            sb.AppendLine($"- Fichero: `{Path.GetFileName(rutaModelo)}` — trainer **{_op.Trainer}**{(_op.Pesos ? " con pesos de clase" : "")}, reentrenado con todo el dataset.");
        sb.AppendLine($"- Alcance: {_op.Alcance}. Contactos del {Desde:dd/MM/yyyy} al {Hasta.AddDays(-1):dd/MM/yyyy}: {Contactos} ({Positivos} positivos).");
        sb.AppendLine($"- Validación temporal desde {Corte:dd/MM/yyyy}: entrenamiento {ContactosEntrenamiento}, validación {ContactosValidacion} (hueco de 7 días).");
        sb.AppendLine($"- Comparado con el modelo actual: {(HayModeloActual ? _op.ModeloActual : "(sin modelo actual)")} (puntuado con ModeloContactoEntrada sobre la misma validación).");
        sb.AppendLine("- Positivo = rapport T/V/W Estado 0 con Pedido = 1, o pedido del cliente en los 7 días siguientes (CabPedidoVta/LinPedidoVta TipoLinea 1, base > 0, NotaEntrega 0, Estado ≥ -1).");
        sb.AppendLine();
        if (resultado != null)
        {
            sb.AppendLine("## Puerta de calidad");
            sb.AppendLine();
            sb.AppendLine($"- Resultado: **{resultado}** — {Decision.Motivo}.");
            sb.AppendLine($"- Regla: la P@{_op.K} media no puede bajar más de {Informe.Num(_op.MaxBajadaPrecision * 100, "0.0")} puntos y el AUC " +
                          (_op.MaxBajadaAuc <= 0 ? "no puede bajar." : $"no puede bajar más de {Informe.Num(_op.MaxBajadaAuc, "0.000")}."));
            sb.AppendLine("- Se compara solo con los contactos que el modelo actual no vio al entrenar (desde la fecha del modelo " +
                          "menos 8 días), con un candidato entrenado con ese mismo corte. Si el actual es anterior a la validación, se usa la validación.");
            sb.AppendLine();
            if (TablaPuerta.Length > 0)
            {
                sb.AppendLine(TablaPuerta);
                sb.AppendLine();
            }
        }
        if (!DatosSuficientes)
        {
            sb.AppendLine("No hay datos suficientes para entrenar y validar.");
            return sb.ToString();
        }
        sb.AppendLine($"## Resultados en validación (precision@{_op.K} por vendedor y mes)");
        sb.AppendLine();
        sb.AppendLine("El modelo actual ya vio al entrenar la parte de esta validación anterior a su fecha: aquí juega en casa (solo informativo).");
        sb.AppendLine();
        sb.AppendLine(Tabla);
        sb.AppendLine("## Calibración");
        sb.AppendLine();
        sb.AppendLine(TablaCalibracion);
        sb.AppendLine("## Entrada del modelo");
        sb.AppendLine();
        sb.AppendLine("Clase `ModeloContactoEntrada` del núcleo (NestoAPI/ModeloLlamadaPedido.Nucleo/Features), la MISMA que usa la API para " +
                      "puntuar: nombres y tipos exactos (ML.NET enlaza por nombre). Cálculo en `CalculadoraFeatures` (d = día del contacto).");
        sb.AppendLine();
        sb.AppendLine("Salida: `PredictedLabel` (bool), `Score` (float), `Probability` (float).");
        sb.AppendLine();
        sb.AppendLine("## Cómo reentrenar");
        sb.AppendLine();
        sb.AppendLine(comoReentrenar);
        return sb.ToString();
    }
}
