using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using ModeloLlamadaPedido.Entrenamiento;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada
{
    /// <summary>NestoAPI#881: lo que se guarda junto al zip del modelo de tiempos de parada.</summary>
    public class MetadatosModeloTiemposParada
    {
        public const string VERSION = "tiempos-parada-1";
        public const string ALGORITMO = "LightGBM regresión de los minutos reales de parada";

        public DateTime Fecha { get; set; }
        public string Version { get; set; }
        public string Algoritmo { get; set; }
        public int Ejemplos { get; set; }
        public string Origen { get; set; }
    }

    /// <summary>Resultado de entrenar con corte temporal (sin guardar nada todavía).</summary>
    public class EvaluacionModeloTiemposParada
    {
        public bool DatosSuficientes { get; set; }
        public string Motivo { get; set; }
        public int Ejemplos { get; set; }
        public int EjemplosEntrenamiento { get; set; }
        public int EjemplosValidacion { get; set; }
        public DateTime ValidacionDesde { get; set; }
        public double? ErrorMedioRegla { get; set; }
        public double? ErrorMedioModelo { get; set; }
        /// <summary>Las de validación con la llegada (FechaHoraLlegada): el segundo objetivo (fin − llegada).</summary>
        public int EjemplosConLlegada { get; set; }
        public double? ErrorMedioReglaLlegada { get; set; }
        public double? ErrorMedioModeloLlegada { get; set; }
        public string Veredicto { get; set; }
        /// <summary>Reentrena con TODO y guarda el zip; null si no hay datos suficientes.</summary>
        internal Func<string, DateTime, string, MetadatosModeloTiemposParada> EntrenarFinalYGuardar { get; set; }
    }

    public interface IEntrenadorModeloTiemposParada
    {
        EvaluacionModeloTiemposParada Evaluar(IReadOnlyList<EjemploTiempoParada> ejemplos, DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#881 (corte 3): el modelo EN SOMBRA de los minutos reales de cada parada (patrón de PlazosEntrega/ModeloPlazos). LightGBM de
    /// regresión (el mismo paquete y librería nativa que los demás modelos) sobre los minutos reales (intervalo − conducción estimada). Con
    /// menos de <see cref="MINIMO_EJEMPLOS"/> ejemplos NO entrena: «sin datos suficientes» y sigue la regla. Validación: las últimas
    /// <see cref="SEMANAS_VALIDACION"/> semanas, contra la REGLA de constantes (los MinutosPrevistos que dio en su día) en las mismas paradas.
    /// </summary>
    public class EntrenadorModeloTiemposParada : IEntrenadorModeloTiemposParada
    {
        public const int MINIMO_EJEMPLOS = 200;
        public const int SEMANAS_VALIDACION = 4;
        public const int MINIMO_VALIDACION = 30;

        private readonly int minimoHoja;

        public EntrenadorModeloTiemposParada(int minimoHoja = 10)
        {
            this.minimoHoja = minimoHoja;
        }

        internal static readonly string[] Categoricas =
        {
            nameof(RasgosTiempoParada.TipoParada), nameof(RasgosTiempoParada.ServicioTecnico), nameof(RasgosTiempoParada.FormaCobro),
            nameof(RasgosTiempoParada.Zona), nameof(RasgosTiempoParada.Repartidor)
        };

        internal static readonly string[] Numericas =
        {
            nameof(RasgosTiempoParada.Bultos), nameof(RasgosTiempoParada.LlevaCobro), nameof(RasgosTiempoParada.Hora),
            nameof(RasgosTiempoParada.DiaSemana), nameof(RasgosTiempoParada.MinutosTecleados)
        };

        public EvaluacionModeloTiemposParada Evaluar(IReadOnlyList<EjemploTiempoParada> ejemplos, DateTime hoy)
        {
            List<EjemploTiempoParada> todos = (ejemplos ?? new List<EjemploTiempoParada>()).Where(e => e != null).OrderBy(e => e.Dia).ToList();
            DateTime corte = hoy.Date.AddDays(-7 * SEMANAS_VALIDACION);
            List<EjemploTiempoParada> entrenamiento = todos.Where(e => e.Dia < corte).ToList();
            List<EjemploTiempoParada> validacion = todos.Where(e => e.Dia >= corte).ToList();
            var evaluacion = new EvaluacionModeloTiemposParada
            {
                Ejemplos = todos.Count,
                EjemplosEntrenamiento = entrenamiento.Count,
                EjemplosValidacion = validacion.Count,
                ValidacionDesde = corte,
                ErrorMedioRegla = MetricasTiemposParada.ErrorMedio(validacion.Select(e => (e.Minutos, e.MinutosRegla)))
            };
            if (todos.Count < MINIMO_EJEMPLOS || entrenamiento.Count < MINIMO_VALIDACION || validacion.Count < MINIMO_VALIDACION)
            {
                evaluacion.Motivo = $"sin datos suficientes: {todos.Count} ejemplos (hacen falta {MINIMO_EJEMPLOS}), {entrenamiento.Count} para entrenar y " +
                    $"{validacion.Count} de validación (mínimo {MINIMO_VALIDACION} de cada)";
                evaluacion.Veredicto = MetricasTiemposParada.Veredicto(validacion.Count, null, null);
                return evaluacion;
            }

            MLContext ml = Entrenador.CrearContexto();
            ITransformer modelo = Entrenar(ml, entrenamiento);
            List<double> previstos = Predecir(ml, modelo, validacion.Select(e => e.Rasgos));
            evaluacion.ErrorMedioModelo = MetricasTiemposParada.ErrorMedio(validacion.Select((e, i) => (e.Minutos, previstos[i])));
            List<int> conLlegada = Enumerable.Range(0, validacion.Count).Where(i => validacion[i].MinutosDesdeLlegada.HasValue).ToList();
            evaluacion.EjemplosConLlegada = conLlegada.Count;
            evaluacion.ErrorMedioReglaLlegada = MetricasTiemposParada.ErrorMedio(conLlegada.Select(i => (validacion[i].MinutosDesdeLlegada.Value, validacion[i].MinutosRegla)));
            evaluacion.ErrorMedioModeloLlegada = MetricasTiemposParada.ErrorMedio(conLlegada.Select(i => (validacion[i].MinutosDesdeLlegada.Value, previstos[i])));
            evaluacion.DatosSuficientes = true;
            evaluacion.Veredicto = MetricasTiemposParada.Veredicto(validacion.Count, evaluacion.ErrorMedioRegla, evaluacion.ErrorMedioModelo);
            evaluacion.Motivo = evaluacion.Veredicto;
            evaluacion.EntrenarFinalYGuardar = (ruta, fecha, origen) =>
            {
                MLContext final = Entrenador.CrearContexto();
                ITransformer completo = Entrenar(final, todos);
                IDataView esquema = final.Data.LoadFromEnumerable(new RasgosTiempoParada[0]);
                final.Model.Save(completo, esquema.Schema, ruta);
                return new MetadatosModeloTiemposParada
                {
                    Fecha = fecha,
                    Version = MetadatosModeloTiemposParada.VERSION,
                    Algoritmo = MetadatosModeloTiemposParada.ALGORITMO,
                    Ejemplos = todos.Count,
                    Origen = origen
                };
            };
            return evaluacion;
        }

        internal ITransformer Entrenar(MLContext ml, IEnumerable<EjemploTiempoParada> ejemplos)
        {
            List<FilaTiempoParada> filas = ejemplos.Select(e => new FilaTiempoParada
            {
                TipoParada = e.Rasgos.TipoParada,
                ServicioTecnico = e.Rasgos.ServicioTecnico,
                Bultos = e.Rasgos.Bultos,
                LlevaCobro = e.Rasgos.LlevaCobro,
                FormaCobro = e.Rasgos.FormaCobro,
                Zona = e.Rasgos.Zona,
                Hora = e.Rasgos.Hora,
                DiaSemana = e.Rasgos.DiaSemana,
                Repartidor = e.Rasgos.Repartidor,
                MinutosTecleados = e.Rasgos.MinutosTecleados,
                Label = (float)e.Minutos
            }).ToList();
            IDataView datos = ml.Data.LoadFromEnumerable(filas);
            return Pipeline(ml).Fit(datos);
        }

        internal IEstimator<ITransformer> Pipeline(MLContext ml)
        {
            InputOutputColumnPair[] codificadas = Categoricas.Select(c => new InputOutputColumnPair(c + "Cod", c)).ToArray();
            return ml.Transforms.Categorical.OneHotEncoding(codificadas)
                .Append(ml.Transforms.Concatenate("Features", Categoricas.Select(c => c + "Cod").Concat(Numericas).ToArray()))
                .Append(ml.Regression.Trainers.LightGbm(new LightGbmRegressionTrainer.Options
                {
                    LabelColumnName = nameof(FilaTiempoParada.Label),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 8,
                    NumberOfIterations = 150,
                    MinimumExampleCountPerLeaf = minimoHoja,
                    LearningRate = 0.05,
                    Seed = 881
                }));
        }

        public static List<double> Predecir(MLContext ml, ITransformer modelo, IEnumerable<RasgosTiempoParada> entradas)
        {
            List<RasgosTiempoParada> lista = entradas.ToList();
            if (lista.Count == 0)
            {
                return new List<double>();
            }
            IDataView datos = ml.Data.LoadFromEnumerable(lista);
            return ml.Data.CreateEnumerable<PrediccionTiempoParada>(modelo.Transform(datos), reuseRowObject: false)
                .Select(p => Math.Max(0, (double)p.Score)).ToList();
        }
    }

    /// <summary>
    /// NestoAPI#881: dónde vive el modelo de tiempos de parada: la carpeta de los modelos (appSetting ModeloLlamadas:Carpeta, fuera de lo
    /// publicado) con <see cref="NOMBRE_MODELO"/> y <see cref="NOMBRE_DATOS"/>.
    /// </summary>
    public class AlmacenModeloTiemposParada
    {
        public const string NOMBRE_MODELO = "tiempos_parada.zip";
        public const string NOMBRE_DATOS = "tiempos_parada.json";
        public const string NOMBRE_ANTERIOR = "tiempos_parada_anterior.zip";
        private const int INTENTOS = 5;

        public AlmacenModeloTiemposParada(string carpeta)
        {
            Carpeta = carpeta ?? throw new ArgumentNullException(nameof(carpeta));
        }

        public static AlmacenModeloTiemposParada Produccion()
            => new AlmacenModeloTiemposParada(AlmacenModeloLlamadas.CarpetaConfigurada(clave => ConfigurationManager.AppSettings[clave]));

        public string Carpeta { get; }
        public string RutaModelo => Path.Combine(Carpeta, NOMBRE_MODELO);
        public string RutaDatos => Path.Combine(Carpeta, NOMBRE_DATOS);
        public bool HayModelo => File.Exists(RutaModelo) && File.Exists(RutaDatos);

        public string NuevaRutaTemporal()
        {
            _ = Directory.CreateDirectory(Carpeta);
            return Path.Combine(Carpeta, $"tiempos_parada.{DateTime.Now:yyyyMMddHHmmss}.{Guid.NewGuid():N}.tmp");
        }

        public void Promover(string temporalZip, MetadatosModeloTiemposParada datos)
        {
            _ = Directory.CreateDirectory(Carpeta);
            File.WriteAllText(RutaDatos, JsonConvert.SerializeObject(datos, Formatting.None), new UTF8Encoding(false));
            Reintentar(() =>
            {
                if (File.Exists(RutaModelo))
                {
                    File.Replace(temporalZip, RutaModelo, Path.Combine(Carpeta, NOMBRE_ANTERIOR), ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporalZip, RutaModelo);
                }
            });
            File.SetLastWriteTimeUtc(RutaModelo, DateTime.UtcNow);
        }

        public void Descartar(string temporal)
        {
            try
            {
                if (temporal != null && File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
            catch (Exception)
            {
                // un temporal que no se puede borrar no es un error del entrenamiento
            }
        }

        private static void Reintentar(Action accion)
        {
            for (int intento = 1; ; intento++)
            {
                try
                {
                    accion();
                    return;
                }
                catch (IOException) when (intento < INTENTOS)
                {
                    Thread.Sleep(1000 * intento);
                }
                catch (UnauthorizedAccessException) when (intento < INTENTOS)
                {
                    Thread.Sleep(1000 * intento);
                }
            }
        }
    }

    /// <summary>NestoAPI#881: los minutos de una parada según el modelo (solo con «Reparto.TiempoParadaQuienManda» = «Modelo»).</summary>
    public interface IMinutosParadaModelo
    {
        /// <summary>Null si no hay modelo o falla (entonces, la regla). Nunca lanza.</summary>
        double? Minutos(RasgosTiempoParada rasgos);
    }

    /// <summary>
    /// NestoAPI#881: el modelo guardado, cargado una vez y recargado si cambia la fecha del zip (como el de plazos). Sin modelo, null.
    /// </summary>
    public class PredictorTiemposParada : IMinutosParadaModelo
    {
        /// <summary>Entre estos minutos (lo de fuera es un disparate del modelo con pocos datos: se recorta).</summary>
        public const double MINIMO = 1;
        public const double MAXIMO = 240;

        private static readonly object bloqueo = new object();
        private static MLContext contexto;
        private static ITransformer modelo;
        private static DateTime fecha;
        private static string ruta;

        private readonly AlmacenModeloTiemposParada almacen;

        public PredictorTiemposParada(AlmacenModeloTiemposParada almacen = null)
        {
            this.almacen = almacen;
        }

        public double? Minutos(RasgosTiempoParada rasgos)
        {
            try
            {
                AlmacenModeloTiemposParada a = almacen ?? AlmacenModeloTiemposParada.Produccion();
                if (rasgos == null || !a.HayModelo)
                {
                    return null;
                }
                lock (bloqueo)
                {
                    DateTime cambio = File.GetLastWriteTimeUtc(a.RutaModelo);
                    if (modelo == null || cambio != fecha || !string.Equals(ruta, a.RutaModelo, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = CargadorLightGbmNativo.Asegurar();
                        contexto = Entrenador.CrearContexto();
                        modelo = Entrenador.Cargar(contexto, a.RutaModelo);
                        fecha = cambio;
                        ruta = a.RutaModelo;
                    }
                    double minutos = EntrenadorModeloTiemposParada.Predecir(contexto, modelo, new[] { rasgos })[0];
                    return Math.Max(MINIMO, Math.Min(MAXIMO, minutos));
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] El modelo de tiempos de parada ha fallado; se usa la regla. {ex.Message}", ex));
                return null;
            }
        }
    }
}
