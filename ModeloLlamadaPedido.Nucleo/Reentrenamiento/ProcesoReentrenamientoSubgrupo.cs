using System.Diagnostics;
using System.Text;
using Microsoft.ML;
using ModeloLlamadaPedido.Datos;
using ModeloLlamadaPedido.Entrenamiento;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Features;

namespace ModeloLlamadaPedido.Reentrenamiento;

/// <summary>
/// NestoAPI#618: reentrenamiento del modelo por subgrupo (objetivo: pedido en ≤ 7 días que INCLUYA el subgrupo), con la misma
/// ventana, partición temporal y puerta que el general (<see cref="ProcesoReentrenamiento"/>):
/// <list type="number">
/// <item>Dataset por contacto y subgrupo (<see cref="ConstructorDatasetSubgrupo"/>); validación = los últimos <c>MesesValidacion</c> meses.</item>
/// <item>Con qué se compara («actual»): el modelo por subgrupo de producción si existe; si no, LO QUE SE HACE HOY, el modelo
/// general con el subgrupo pisando GrupoSubgrupoMasVendido. Así el primero solo entra si gana a eso.</item>
/// <item>Puerta justa: solo con los contactos que el actual no vio (desde <c>CorteActual</c>) y un candidato entrenado con
/// ese corte. P@20 por vendedor, mes y subgrupo (la lista que ve el vendedor al elegir un subgrupo).</item>
/// <item>Informativas: el «interino» de la issue, P(pedido) × (pedidos con el subgrupo en 12 m + 0,5) / (pedidos 12 m + 1), y
/// «pedidos con el subgrupo en 12 m».</item>
/// </list>
/// </summary>
public sealed class ProcesoReentrenamientoSubgrupo : IResultadoReentrenamiento
{
    private readonly OpcionesReentrenamiento _op;
    private readonly MLContext _ml;
    private readonly List<FilaEntrenamientoSubgrupo> _filas;
    private readonly List<FilaEntrenamientoSubgrupo> _entrenamiento;
    private readonly List<FilaEntrenamientoSubgrupo> _validacion;
    private readonly Action<string> _log;

    public DateTime Desde { get; }
    public DateTime Hasta { get; }
    public DateTime Corte { get; }
    /// <summary>Filas (contacto × subgrupo).</summary>
    public int Contactos => _filas.Count;
    public int Positivos => _filas.Count(f => f.Label);
    public int ContactosDistintos => _filas.Select(f => (f.ClienteId, f.Fecha)).Distinct().Count();
    public bool DatosSuficientes { get; private set; }
    public string Tabla { get; private set; } = "";
    public string TablaCalibracion { get; private set; } = "";
    public string TablaPuerta { get; private set; } = "";
    /// <summary>Con qué se ha comparado: «modelo por subgrupo», «general con el subgrupo pisado» o "" si con nada.</summary>
    public string Comparado { get; private set; } = "";
    public bool HayModeloActual { get; private set; }
    public DateTime CortePuerta { get; private set; }
    public bool PuertaConContactosNoVistos { get; private set; }
    public MetricasPuerta Nuevo { get; private set; } = new(double.NaN, double.NaN);
    public MetricasPuerta? Actual { get; private set; }
    public DecisionPuerta Decision { get; private set; } = new(false, "");
    public string Comparacion => PuertaCalidad.Comparacion(Actual, Nuevo, _op.K);
    public OpcionesReentrenamiento Opciones => _op;

    private ProcesoReentrenamientoSubgrupo(DatosCrudos datos, OpcionesReentrenamiento op, DateTime hoy, Action<string>? log)
    {
        _op = op;
        _log = log ?? (_ => { });
        _ml = Entrenador.CrearContexto();
        (Desde, Hasta, Corte) = VentanaReentrenamiento.Calcular(hoy, op.Completo, op.Meses, op.MesesValidacion);
        _filas = ConstructorDatasetSubgrupo.Construir(datos, Desde, Hasta, op.Completo ? null : op.Vendedor);
        (List<int> iEnt, List<int> iVal) = ConstructorDataset.Particion(_filas, Corte);
        _entrenamiento = iEnt.Select(i => _filas[i]).ToList();
        _validacion = iVal.Select(i => _filas[i]).ToList();
    }

    /// <param name="datos">Leído con <c>incluirSubgrupos: true</c> (si no, no hay filas).</param>
    public static ProcesoReentrenamientoSubgrupo Evaluar(DatosCrudos datos, OpcionesReentrenamiento op, DateTime hoy, Action<string>? log = null)
    {
        var r = new ProcesoReentrenamientoSubgrupo(datos, op, hoy, log);
        r.EvaluarInterno();
        return r;
    }

    private static bool Entrenable(List<FilaEntrenamientoSubgrupo> filas) => filas.Any(f => f.Label) && filas.Any(f => !f.Label);

    private FilaInforme Fila(string nombre, List<FilaEntrenamientoSubgrupo> filas, List<float> p, bool calibracion = true)
    {
        List<bool> etiquetas = filas.Select(f => f.Label).ToList();
        return new FilaInforme(nombre, Metricas.Auc(p, etiquetas), Metricas.PrecisionEnK(filas, p, _op.K), calibracion ? Metricas.Calibracion(p, etiquetas) : null);
    }

    private string NombreNuevo => $"Nuevo por subgrupo {_op.Trainer}{(_op.Pesos ? " (pesos)" : "")}";

    private void EvaluarInterno()
    {
        var reloj = Stopwatch.StartNew();
        if (_filas.Count > 0)
            _log($"Dataset por subgrupo: {_filas.Count} filas de {ContactosDistintos} contactos ({Informe.Pct(_filas.Average(f => f.Label ? 1.0 : 0.0))} positivas). " +
                 $"Entrenamiento {_entrenamiento.Count} ({_entrenamiento.Count(f => f.Label)} +), validación {_validacion.Count} ({_validacion.Count(f => f.Label)} +).");
        if (_entrenamiento.Count == 0 || _validacion.Count == 0 || !Entrenable(_entrenamiento))
        {
            DatosSuficientes = false;
            Decision = new DecisionPuerta(false, "no hay datos suficientes para entrenar y validar el modelo por subgrupo");
            _log("No hay datos suficientes para entrenar y validar el modelo por subgrupo.");
            return;
        }
        DatosSuficientes = true;

        EntrenadorSubgrupo.AsignarPesos(_entrenamiento, _op.Pesos);
        ITransformer nuevo = EntrenadorSubgrupo.Entrenar(_ml, _entrenamiento, _op.Trainer, _op.Pesos);
        var resultados = new List<FilaInforme> { Fila(NombreNuevo, _validacion, EntrenadorSubgrupo.Probabilidades(_ml, nuevo, _validacion)) };
        FilaInforme filaNuevo = resultados[0];

        // Con qué se compara: el de subgrupo de producción o, si no hay, lo que se hace hoy.
        ITransformer? general = Existe(_op.ModeloGeneral) ? Entrenador.Cargar(_ml, _op.ModeloGeneral!) : null;
        Func<IEnumerable<ModeloContactoSubgrupoEntrada>, List<float>>? puntuarActual = null;
        if (Existe(_op.ModeloActual))
        {
            ITransformer actual = Entrenador.Cargar(_ml, _op.ModeloActual!);
            puntuarActual = e => EntrenadorSubgrupo.Probabilidades(_ml, actual, e);
            Comparado = $"modelo por subgrupo ({File.GetLastWriteTime(_op.ModeloActual):dd/MM/yyyy})";
        }
        else if (general != null)
        {
            puntuarActual = e => EntrenadorSubgrupo.ProbabilidadesGeneralConSubgrupoPisado(_ml, general, e);
            Comparado = "hoy: modelo general con el subgrupo pisado";
        }
        HayModeloActual = puntuarActual != null;
        FilaInforme? filaActual = null;
        if (puntuarActual != null)
        {
            filaActual = Fila($"Actual ({Comparado})", _validacion, puntuarActual(_validacion));
            resultados.Add(filaActual);
        }
        else
        {
            _log("No hay modelo por subgrupo ni modelo general con el que comparar.");
        }

        if (general != null)
        {
            List<float> pGeneral = EntrenadorSubgrupo.ProbabilidadesGeneral(_ml, general, _validacion);
            resultados.Add(Fila("Interino: P(pedido) × frecuencia del subgrupo", _validacion,
                _validacion.Select((f, i) => pGeneral[i] * f.FrecuenciaSubgrupo).ToList(), calibracion: false));
        }
        resultados.Add(Fila("Base: pedidos con el subgrupo en 12 meses", _validacion,
            _validacion.Select(f => f.PedidosSubgrupo12Meses + f.CuotaImporteSubgrupo / 10f).ToList(), calibracion: false));

        Tabla = Informe.Tabla(resultados, _op.K);
        TablaCalibracion = Informe.TablaCalibracion(resultados);
        _log($"Validación por subgrupo ({Corte:dd/MM/yyyy} – {Hasta.AddDays(-1):dd/MM/yyyy}), precision@{_op.K} por vendedor, mes y subgrupo:");
        _log(Tabla);

        DateTime corteActual = _op.CorteActual ?? DateTime.MinValue;
        CortePuerta = corteActual > Corte ? corteActual.Date : Corte;
        if (puntuarActual == null || CortePuerta == Corte)
        {
            Nuevo = new(filaNuevo.Precision.Media, filaNuevo.Auc);
            if (filaActual != null) Actual = new(filaActual.Precision.Media, filaActual.Auc);
        }
        else
        {
            PuertaConContactosNoVistos = true;
            (List<int> iEnt, List<int> iVal) = ConstructorDataset.Particion(_filas, CortePuerta);
            List<FilaEntrenamientoSubgrupo> entPuerta = iEnt.Select(i => _filas[i]).ToList();
            List<FilaEntrenamientoSubgrupo> valPuerta = iVal.Select(i => _filas[i]).ToList();
            if (valPuerta.Count > 0 && Entrenable(entPuerta))
            {
                EntrenadorSubgrupo.AsignarPesos(entPuerta, _op.Pesos);
                ITransformer candidato = EntrenadorSubgrupo.Entrenar(_ml, entPuerta, _op.Trainer, _op.Pesos);
                var filasPuerta = new List<FilaInforme>
                {
                    Fila(NombreNuevo, valPuerta, EntrenadorSubgrupo.Probabilidades(_ml, candidato, valPuerta)),
                    Fila(filaActual!.Modelo, valPuerta, puntuarActual(valPuerta))
                };
                Nuevo = new(filasPuerta[0].Precision.Media, filasPuerta[0].Auc);
                Actual = new(filasPuerta[1].Precision.Media, filasPuerta[1].Auc);
                TablaPuerta = $"Contactos desde el {CortePuerta:dd/MM/yyyy} (el actual no los vio al entrenar): {valPuerta.Count} filas " +
                              $"({valPuerta.Count(f => f.Label)} +); candidato entrenado con {entPuerta.Count} filas anteriores.{Environment.NewLine}{Environment.NewLine}" +
                              Informe.Tabla(filasPuerta, _op.K);
            }
            else
            {
                Nuevo = new(double.NaN, double.NaN);
                Actual = new(double.NaN, double.NaN);
                TablaPuerta = $"Contactos desde el {CortePuerta:dd/MM/yyyy} (el actual no los vio al entrenar): {valPuerta.Count} filas; no hay datos para comparar.";
            }
            _log("Puerta del modelo por subgrupo, solo con contactos que el actual no vio:");
            _log(TablaPuerta);
        }

        DecisionPuerta decision = PuertaCalidad.Decidir(HayModeloActual ? Actual : null, Nuevo, _op.K, _op.MaxBajadaPrecision, _op.MaxBajadaAuc);
        if (HayModeloActual)
            decision = decision with { Motivo = $"{decision.Motivo} (contra el {Comparado}{(PuertaConContactosNoVistos ? $", con los contactos desde el {CortePuerta:dd/MM/yyyy}, que no vio" : "")})" };
        Decision = decision;
        _log($"Evaluación por subgrupo terminada en {reloj.Elapsed.TotalSeconds:0.0} s.");
    }

    private static bool Existe(string? ruta) => !string.IsNullOrEmpty(ruta) && File.Exists(ruta);

    /// <summary>Reentrena con TODO el dataset, lo guarda con el esquema de <see cref="ModeloContactoSubgrupoEntrada"/> y comprueba la ida y vuelta.</summary>
    public List<float> EntrenarFinalYGuardar(string ruta)
    {
        if (!DatosSuficientes) throw new InvalidOperationException("No hay datos suficientes para entrenar el modelo por subgrupo");
        EntrenadorSubgrupo.AsignarPesos(_filas, _op.Pesos);
        ITransformer final = EntrenadorSubgrupo.Entrenar(_ml, _filas, _op.Trainer, _op.Pesos);
        EntrenadorSubgrupo.Guardar(_ml, final, ruta);
        ITransformer cargado = Entrenador.Cargar(_ml, ruta);
        List<float> prueba = EntrenadorSubgrupo.Probabilidades(_ml, cargado, _validacion.Take(5));
        if (prueba.Count == 0 || prueba.Any(float.IsNaN))
            throw new InvalidOperationException($"El modelo por subgrupo guardado en {ruta} no puntúa bien en la ida y vuelta");
        _log($"Modelo por subgrupo guardado en {ruta} (ida y vuelta OK: {string.Join("; ", prueba.Select(x => x.ToString("0.000")))}).");
        return prueba;
    }

    public string Readme(string? rutaModelo, string? resultado, string comoReentrenar)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Modelo de llamadas por subgrupo — {DateTime.Now:dd/MM/yyyy HH:mm} (NestoAPI#618)");
        sb.AppendLine();
        if (rutaModelo != null)
            sb.AppendLine($"- Fichero: `{Path.GetFileName(rutaModelo)}` — trainer **{_op.Trainer}**, reentrenado con todo el dataset.");
        sb.AppendLine($"- Alcance: {_op.Alcance}. Contactos del {Desde:dd/MM/yyyy} al {Hasta.AddDays(-1):dd/MM/yyyy}: {ContactosDistintos} contactos, " +
                      $"{Contactos} filas contacto × subgrupo ({Positivos} positivas).");
        sb.AppendLine($"- Filas por contacto: los subgrupos comprados en los 24 meses anteriores (máximo {CalculadoraFeaturesSubgrupo.MAX_COMPRADOS_POR_CONTACTO}) " +
                      $"y {CalculadoraFeaturesSubgrupo.NO_COMPRADOS_POR_CONTACTO} no comprados al azar del catálogo.");
        sb.AppendLine("- Positiva = pedido del cliente en los 7 días siguientes con alguna línea del subgrupo (TipoLinea 1, base > 0, NotaEntrega 0, Estado ≥ -1, sin MMP).");
        sb.AppendLine($"- Validación temporal desde {Corte:dd/MM/yyyy}: entrenamiento {_entrenamiento.Count}, validación {_validacion.Count} filas.");
        sb.AppendLine($"- Comparado con: {(HayModeloActual ? Comparado : "(nada: no hay modelo por subgrupo ni general)")}.");
        sb.AppendLine();
        if (resultado != null)
        {
            sb.AppendLine("## Puerta de calidad");
            sb.AppendLine();
            sb.AppendLine($"- Resultado: **{resultado}** — {Decision.Motivo}.");
            sb.AppendLine($"- Regla: la P@{_op.K} media (por vendedor, mes y subgrupo) no puede bajar más de {Informe.Num(_op.MaxBajadaPrecision * 100, "0.0")} puntos y el AUC " +
                          (_op.MaxBajadaAuc <= 0 ? "no puede bajar." : $"no puede bajar más de {Informe.Num(_op.MaxBajadaAuc, "0.000")}."));
            sb.AppendLine("- Mientras no haya modelo por subgrupo, se compara con lo que se hace hoy (modelo general con el subgrupo pisado).");
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
        sb.AppendLine($"## Resultados en validación (precision@{_op.K} por vendedor, mes y subgrupo)");
        sb.AppendLine();
        sb.AppendLine(Tabla);
        sb.AppendLine("## Calibración");
        sb.AppendLine();
        sb.AppendLine(TablaCalibracion);
        sb.AppendLine("## Entrada del modelo");
        sb.AppendLine();
        sb.AppendLine("Clase `ModeloContactoSubgrupoEntrada` del núcleo (las features generales de `ModeloContactoEntrada` más las de relación " +
                      "cliente↔subgrupo, calculadas en `CalculadoraFeaturesSubgrupo`). La misma que usa la API.");
        sb.AppendLine();
        sb.AppendLine("## Cómo reentrenar");
        sb.AppendLine();
        sb.AppendLine(comoReentrenar);
        return sb.ToString();
    }
}
