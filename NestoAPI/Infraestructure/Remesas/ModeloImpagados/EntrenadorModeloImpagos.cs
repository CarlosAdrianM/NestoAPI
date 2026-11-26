using Microsoft.ML;
using Microsoft.ML.Trainers.LightGbm;
using ModeloLlamadaPedido.Entrenamiento;
using NestoAPI.Infraestructure.Rapports;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>NestoAPI#896: lo que se guarda junto al zip del modelo de impagados: la calibración y las categorías con valor propio.</summary>
    public class MetadatosModeloImpagos
    {
        public const string VERSION = "impagos-1";
        public const string ALGORITMO = "LightGBM binario + calibración de Platt";

        public DateTime Fecha { get; set; }
        public string Version { get; set; }
        public string Algoritmo { get; set; }
        public double PlattA { get; set; } = 1;
        public double PlattB { get; set; }
        public int Ejemplos { get; set; }
        public string Origen { get; set; }
        public MapaCategoriasImpagos Categorias { get; set; } = new MapaCategoriasImpagos();

        /// <summary>Lo que se apunta en la sombra: «impagos-1 2026-11-29».</summary>
        [JsonIgnore]
        public string Etiqueta => $"{Version} {Fecha:yyyy-MM-dd}";
    }

    /// <summary>Resultado de entrenar con corte temporal (sin guardar nada todavía).</summary>
    public interface IEvaluacionModeloImpagos
    {
        bool DatosSuficientes { get; }
        string Motivo { get; }
        int Ejemplos { get; }
        int EjemplosEntrenamiento { get; }
        int EjemplosValidacion { get; }
        DateTime ValidacionDesde { get; }
        /// <summary>Métricas de validación del modelo (calibrado con recibos anteriores a la validación).</summary>
        MetricasImpagos Modelo { get; }
        /// <summary>Métricas de LA REGLA ajustada con lo anterior a la validación, en los mismos recibos.</summary>
        MetricasImpagos Regla { get; }
        /// <summary>La calibración que llevará el modelo final.</summary>
        CalibradorPlatt Calibrador { get; }
        /// <summary>LA REGLA ajustada con TODOS los ejemplos (la que se guarda SIEMPRE, se promueva o no el modelo). Null sin devoluciones.</summary>
        PesosReglaImpagos ReglaFinal { get; }
        /// <summary>Reentrena con TODO y guarda el zip en <paramref name="rutaZip"/>; devuelve sus metadatos.</summary>
        MetadatosModeloImpagos EntrenarFinalYGuardar(string rutaZip, DateTime fecha, string origen);
    }

    public interface IEntrenadorModeloImpagos
    {
        /// <param name="ejemplos">Los de <see cref="ConstructorRasgosImpagos.Construir"/> (ya sin los giros de los últimos 45 días).</param>
        IEvaluacionModeloImpagos Evaluar(IReadOnlyList<EjemploImpago> ejemplos, DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#896: modelo ML.NET de probabilidad de devolución de un recibo girado, en sombra frente a LA REGLA simple.
    /// <para><b>Algoritmo</b>: LightGBM binario (el mismo paquete Microsoft.ML.LightGbm 4.0.2 y la misma librería nativa que los demás
    /// modelos) y, encima, calibración de Platt propia (<see cref="CalibradorPlatt"/>) ajustada con recibos que el árbol no ha visto:
    /// con un 3-4 % de devoluciones lo que importa es que «30 %» quiera decir que vuelven 30 de cada 100.</para>
    /// <para><b>Ventanas</b> (hacia atrás desde hoy − 45 días): validación = los últimos <see cref="MESES_VALIDACION"/> meses;
    /// calibración = los <see cref="MESES_CALIBRACION"/> anteriores. Modelo A con lo anterior a la calibración → Platt con sus
    /// puntuaciones en calibración. Modelo B con todo lo anterior a la validación → se mide en validación con ese Platt (nada de la
    /// validación entra en el ajuste ni en la calibración). LA REGLA se ajusta con lo mismo que B. El modelo final se entrena con todo
    /// y lleva el Platt de B en validación.</para>
    /// </summary>
    public class EntrenadorModeloImpagos : IEntrenadorModeloImpagos
    {
        public const int MESES_VALIDACION = 6;
        public const int MESES_CALIBRACION = 6;
        public const int MINIMO_ENTRENAMIENTO = 2000;

        private readonly int minimoHoja;
        private readonly int minimoEntrenamiento;
        private readonly int minimoCategoria;

        /// <param name="minimoHoja">Ejemplos mínimos por hoja de LightGBM (50; los tests con pocos datos pueden bajarlo).</param>
        public EntrenadorModeloImpagos(int minimoHoja = 50, int minimoEntrenamiento = MINIMO_ENTRENAMIENTO, int minimoCategoria = MapaCategoriasImpagos.MINIMO)
        {
            this.minimoHoja = minimoHoja;
            this.minimoEntrenamiento = minimoEntrenamiento;
            this.minimoCategoria = minimoCategoria;
        }

        internal static readonly string[] Categoricas =
        {
            nameof(RasgosImpago.MotivoUltima), nameof(RasgosImpago.FormaVenta), nameof(RasgosImpago.Vendedor), nameof(RasgosImpago.Entidad)
        };

        internal static readonly string[] Numericas =
        {
            nameof(RasgosImpago.DiasDesdeUltimaDevolucion), nameof(RasgosImpago.Devoluciones3Meses), nameof(RasgosImpago.Devoluciones12Meses),
            nameof(RasgosImpago.DevolucionesTotales), nameof(RasgosImpago.RecibosAnteriores), nameof(RasgosImpago.Recibos12Meses),
            nameof(RasgosImpago.TasaDevolucionCliente), nameof(RasgosImpago.AntiguedadDias), nameof(RasgosImpago.RecibosUltimos45),
            nameof(RasgosImpago.Importe), nameof(RasgosImpago.RelacionImporteHabitual), nameof(RasgosImpago.EsRegiro),
            nameof(RasgosImpago.PrimerGiroCuenta), nameof(RasgosImpago.NumeroEfecto), nameof(RasgosImpago.DiasFacturaAGiro),
            nameof(RasgosImpago.DiaMes), nameof(RasgosImpago.Mes), nameof(RasgosImpago.DiaSemana), nameof(RasgosImpago.NivelGeneral14),
            nameof(RasgosImpago.NivelGeneral30)
        };

        public IEvaluacionModeloImpagos Evaluar(IReadOnlyList<EjemploImpago> ejemplos, DateTime hoy)
        {
            List<EjemploImpago> todos = (ejemplos ?? new List<EjemploImpago>()).Where(e => e?.Rasgos != null).OrderBy(e => e.FechaGiro).ToList();
            DateTime corteValidacion = hoy.Date.AddDays(-ConstructorRasgosImpagos.DIAS_ETIQUETA).AddMonths(-MESES_VALIDACION);
            DateTime corteCalibracion = corteValidacion.AddMonths(-MESES_CALIBRACION);
            List<EjemploImpago> entrenamientoA = todos.Where(e => e.FechaGiro < corteCalibracion).ToList();
            List<EjemploImpago> calibracion = todos.Where(e => e.FechaGiro >= corteCalibracion && e.FechaGiro < corteValidacion).ToList();
            List<EjemploImpago> entrenamientoB = todos.Where(e => e.FechaGiro < corteValidacion).ToList();
            List<EjemploImpago> validacion = todos.Where(e => e.FechaGiro >= corteValidacion).ToList();

            var evaluacion = new Evaluacion(this, todos)
            {
                EjemplosEntrenamiento = entrenamientoB.Count,
                EjemplosValidacion = validacion.Count,
                ValidacionDesde = corteValidacion,
                ReglaFinal = ReglaImpagos.Ajustar(todos, hoy)
            };
            int minimo = PuertaCalidadImpagos.MINIMO_DEVUELTOS;
            if (entrenamientoA.Count < minimoEntrenamiento || entrenamientoA.Count(e => e.Devuelto) < minimo
                || calibracion.Count(e => e.Devuelto) < minimo || validacion.Count(e => e.Devuelto) < minimo
                || calibracion.All(e => e.Devuelto) || validacion.All(e => e.Devuelto))
            {
                evaluacion.Motivo = $"datos insuficientes: {entrenamientoA.Count} recibos ({entrenamientoA.Count(e => e.Devuelto)} devueltos) para entrenar antes de la " +
                    $"calibración (mínimo {minimoEntrenamiento} y {minimo}), {calibracion.Count(e => e.Devuelto)} devueltos en calibración y " +
                    $"{validacion.Count(e => e.Devuelto)} en validación (mínimo {minimo})";
                return evaluacion;
            }

            PesosReglaImpagos reglaB = ReglaImpagos.Ajustar(entrenamientoB, hoy);
            evaluacion.Regla = CalculoMetricasImpagos.Calcular(validacion.Select(e => (ReglaImpagos.Probabilidad(reglaB, e.Rasgos), e.Devuelto)));

            MLContext ml = Entrenador.CrearContexto();
            (ITransformer modeloA, MapaCategoriasImpagos mapaA) = Entrenar(ml, entrenamientoA);
            List<double> puntosCalibracion = Puntuar(ml, modeloA, calibracion.Select(e => mapaA.Aplicar(e.Rasgos)));
            CalibradorPlatt plattA = CalibradorPlatt.Ajustar(calibracion.Select((e, i) => (puntosCalibracion[i], e.Devuelto)));

            (ITransformer modeloB, MapaCategoriasImpagos mapaB) = Entrenar(ml, entrenamientoB);
            List<double> puntosValidacion = Puntuar(ml, modeloB, validacion.Select(e => mapaB.Aplicar(e.Rasgos)));
            evaluacion.Modelo = CalculoMetricasImpagos.Calcular(validacion.Select((e, i) => (plattA.Probabilidad(puntosValidacion[i]), e.Devuelto)));
            evaluacion.Calibrador = CalibradorPlatt.Ajustar(validacion.Select((e, i) => (puntosValidacion[i], e.Devuelto)));
            evaluacion.DatosSuficientes = true;
            evaluacion.Motivo = $"modelo: {CalculoMetricasImpagos.Texto(evaluacion.Modelo)}; regla: {CalculoMetricasImpagos.Texto(evaluacion.Regla)}";
            return evaluacion;
        }

        internal (ITransformer Modelo, MapaCategoriasImpagos Mapa) Entrenar(MLContext ml, IReadOnlyCollection<EjemploImpago> ejemplos)
        {
            MapaCategoriasImpagos mapa = MapaCategoriasImpagos.Desde(ejemplos, minimoCategoria);
            IDataView datos = ml.Data.LoadFromEnumerable(ejemplos.Select(mapa.Fila).ToList());
            return (Pipeline(ml).Fit(datos), mapa);
        }

        internal IEstimator<ITransformer> Pipeline(MLContext ml)
        {
            IEstimator<ITransformer> pipeline = null;
            foreach (string categorica in Categoricas)
            {
                IEstimator<ITransformer> paso = ml.Transforms.Categorical.OneHotEncoding(categorica + "Cod", categorica);
                pipeline = pipeline == null ? paso : pipeline.Append(paso);
            }
            return pipeline
                .Append(ml.Transforms.Concatenate("Features", Categoricas.Select(c => c + "Cod").Concat(Numericas).ToArray()))
                .Append(ml.BinaryClassification.Trainers.LightGbm(new LightGbmBinaryTrainer.Options
                {
                    LabelColumnName = nameof(FilaImpago.Label),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 16,
                    NumberOfIterations = 200,
                    MinimumExampleCountPerLeaf = minimoHoja,
                    LearningRate = 0.05,
                    Seed = 896
                }));
        }

        /// <summary>La puntuación sin calibrar de cada entrada, en el mismo orden.</summary>
        public static List<double> Puntuar(MLContext ml, ITransformer modelo, IEnumerable<RasgosImpago> entradas)
        {
            List<RasgosImpago> lista = entradas.ToList();
            if (lista.Count == 0)
            {
                return new List<double>();
            }
            IDataView datos = ml.Data.LoadFromEnumerable(lista);
            return ml.Data.CreateEnumerable<PrediccionImpago>(modelo.Transform(datos), reuseRowObject: false).Select(p => (double)p.Score).ToList();
        }

        public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
        {
            IDataView esquema = ml.Data.LoadFromEnumerable(new RasgosImpago[0]);
            ml.Model.Save(modelo, esquema.Schema, ruta);
        }

        private sealed class Evaluacion : IEvaluacionModeloImpagos
        {
            private readonly EntrenadorModeloImpagos entrenador;
            private readonly List<EjemploImpago> ejemplos;

            public Evaluacion(EntrenadorModeloImpagos entrenador, List<EjemploImpago> ejemplos)
            {
                this.entrenador = entrenador;
                this.ejemplos = ejemplos;
            }

            public bool DatosSuficientes { get; set; }
            public string Motivo { get; set; }
            public int Ejemplos => ejemplos.Count;
            public int EjemplosEntrenamiento { get; set; }
            public int EjemplosValidacion { get; set; }
            public DateTime ValidacionDesde { get; set; }
            public MetricasImpagos Modelo { get; set; }
            public MetricasImpagos Regla { get; set; }
            public CalibradorPlatt Calibrador { get; set; }
            public PesosReglaImpagos ReglaFinal { get; set; }

            public MetadatosModeloImpagos EntrenarFinalYGuardar(string rutaZip, DateTime fecha, string origen)
            {
                if (!DatosSuficientes)
                {
                    throw new InvalidOperationException("NestoAPI#896: no se entrena el modelo final sin datos suficientes");
                }
                MLContext ml = Entrenador.CrearContexto();
                (ITransformer modelo, MapaCategoriasImpagos mapa) = entrenador.Entrenar(ml, ejemplos);
                Guardar(ml, modelo, rutaZip);
                return new MetadatosModeloImpagos
                {
                    Fecha = fecha,
                    Version = MetadatosModeloImpagos.VERSION,
                    Algoritmo = MetadatosModeloImpagos.ALGORITMO,
                    PlattA = Calibrador.A,
                    PlattB = Calibrador.B,
                    Ejemplos = ejemplos.Count,
                    Origen = origen,
                    Categorias = mapa
                };
            }
        }
    }

    /// <summary>
    /// NestoAPI#896: dónde viven el modelo de impagados y LA REGLA ajustada: la MISMA carpeta que los demás modelos (appSetting
    /// ModeloLlamadas:Carpeta, fuera de lo publicado). La regla (<see cref="NOMBRE_REGLA"/>) se guarda en cada entrenamiento; el modelo
    /// (<see cref="NOMBRE_MODELO"/> + <see cref="NOMBRE_DATOS"/>) solo si pasa la puerta de calidad (el anterior queda en «_anterior»).
    /// </summary>
    public class AlmacenModeloImpagos
    {
        public const string NOMBRE_MODELO = "impagos_remesas.zip";
        public const string NOMBRE_DATOS = "impagos_remesas.json";
        public const string NOMBRE_REGLA = "impagos_remesas_regla.json";
        public const string NOMBRE_ANTERIOR = "impagos_remesas_anterior.zip";
        public const string NOMBRE_DATOS_ANTERIOR = "impagos_remesas_anterior.json";
        private const int INTENTOS = 5;

        public AlmacenModeloImpagos(string carpeta)
        {
            Carpeta = carpeta ?? throw new ArgumentNullException(nameof(carpeta));
        }

        public static AlmacenModeloImpagos Produccion()
            => new AlmacenModeloImpagos(AlmacenModeloLlamadas.CarpetaConfigurada(clave => ConfigurationManager.AppSettings[clave]));

        public string Carpeta { get; }
        public string RutaModelo => Path.Combine(Carpeta, NOMBRE_MODELO);
        public string RutaDatos => Path.Combine(Carpeta, NOMBRE_DATOS);
        public string RutaRegla => Path.Combine(Carpeta, NOMBRE_REGLA);

        public bool HayModelo => File.Exists(RutaModelo) && File.Exists(RutaDatos);
        public bool HayRegla => File.Exists(RutaRegla);

        public string NuevaRutaTemporal()
        {
            _ = Directory.CreateDirectory(Carpeta);
            return Path.Combine(Carpeta, $"impagos_remesas.{DateTime.Now:yyyyMMddHHmmss}.{Guid.NewGuid():N}.tmp");
        }

        public MetadatosModeloImpagos LeerDatos() => Leer<MetadatosModeloImpagos>(RutaDatos);

        public PesosReglaImpagos LeerRegla() => Leer<PesosReglaImpagos>(RutaRegla);

        public void GuardarRegla(PesosReglaImpagos regla)
        {
            if (regla == null)
            {
                return;
            }
            _ = Directory.CreateDirectory(Carpeta);
            EscribirAtomico(RutaRegla, JsonConvert.SerializeObject(regla, Formatting.None));
        }

        public void Promover(string temporalZip, MetadatosModeloImpagos datos)
        {
            if (!File.Exists(temporalZip))
            {
                throw new FileNotFoundException("NestoAPI#896: no está el modelo de impagados que se iba a promover", temporalZip);
            }
            _ = Directory.CreateDirectory(Carpeta);
            if (File.Exists(RutaDatos))
            {
                File.Copy(RutaDatos, Path.Combine(Carpeta, NOMBRE_DATOS_ANTERIOR), overwrite: true);
            }
            EscribirAtomico(RutaDatos, JsonConvert.SerializeObject(datos, Formatting.None));
            if (File.Exists(RutaModelo))
            {
                Reintentar(() => File.Replace(temporalZip, RutaModelo, Path.Combine(Carpeta, NOMBRE_ANTERIOR), ignoreMetadataErrors: true));
            }
            else
            {
                Reintentar(() => File.Move(temporalZip, RutaModelo));
            }
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

        private static T Leer<T>(string ruta) where T : class
        {
            try
            {
                return File.Exists(ruta) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(ruta, Encoding.UTF8)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void EscribirAtomico(string ruta, string texto)
        {
            string temporal = ruta + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporal, texto, new UTF8Encoding(false));
            if (File.Exists(ruta))
            {
                Reintentar(() => File.Replace(temporal, ruta, null, ignoreMetadataErrors: true));
            }
            else
            {
                File.Move(temporal, ruta);
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

    /// <summary>La predicción de un recibo: la del modelo (si hay), la de LA REGLA (si hay) y por qué.</summary>
    public class PrediccionRiesgoImpago
    {
        public double? Probabilidad { get; set; }
        public double? ProbabilidadRegla { get; set; }
        /// <summary>Versión del modelo que dio <see cref="Probabilidad"/> (null sin modelo).</summary>
        public string VersionModelo { get; set; }
        /// <summary>Las causas que más suben el riesgo (del modelo; sin él o si no se pueden calcular, de la regla).</summary>
        public List<string> Motivos { get; set; } = new List<string>();
        /// <summary>De dónde salen los <see cref="Motivos"/>: «Modelo» (contribución de cada rasgo) o «Regla» (peso de cada tramo). Null sin motivos.</summary>
        public string OrigenMotivos { get; set; }
    }

    /// <summary>Quien predice (sustituible en los tests).</summary>
    public interface IPredictorImpagos
    {
        /// <summary>Hay regla o modelo guardados (sin ninguno no hay nada que apuntar).</summary>
        bool HayAlgo { get; }
        /// <summary>Una predicción por entrada, en el mismo orden.</summary>
        List<PrediccionRiesgoImpago> Predecir(IList<RasgosImpago> rasgos);
    }

    /// <summary>
    /// NestoAPI#896: predicción con lo guardado en <see cref="AlmacenModeloImpagos"/>. El modelo (zip + datos) y la regla se cargan una
    /// vez y se quedan en memoria hasta que cambie la fecha de sus ficheros. Si el modelo falla, queda la regla.
    /// </summary>
    public class PredictorImpagos : IPredictorImpagos
    {
        private static readonly object bloqueo = new object();
        private static Cargado cargado;

        private readonly AlmacenModeloImpagos almacen;
        private readonly Action<Exception> registrar;

        private sealed class Cargado
        {
            public string Carpeta;
            public DateTime FechaModelo;
            public DateTime FechaRegla;
            public MLContext Contexto;
            public ITransformer Modelo;
            public MetadatosModeloImpagos Datos;
            public PesosReglaImpagos Regla;
        }

        public PredictorImpagos(AlmacenModeloImpagos almacen = null, Action<Exception> registrar = null)
        {
            this.almacen = almacen ?? AlmacenModeloImpagos.Produccion();
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public bool HayAlgo => almacen.HayModelo || almacen.HayRegla;

        public List<PrediccionRiesgoImpago> Predecir(IList<RasgosImpago> rasgos)
        {
            var resultado = (rasgos ?? new List<RasgosImpago>()).Select(_ => new PrediccionRiesgoImpago()).ToList();
            if (resultado.Count == 0)
            {
                return resultado;
            }
            Cargado c = Cargar();
            if (c.Regla != null)
            {
                for (int i = 0; i < resultado.Count; i++)
                {
                    resultado[i].ProbabilidadRegla = ReglaImpagos.Probabilidad(c.Regla, rasgos[i]);
                    resultado[i].Motivos = TextosRiesgoImpago.Motivos(ReglaImpagos.Contribuciones(c.Regla, rasgos[i]), rasgos[i]);
                    resultado[i].OrigenMotivos = resultado[i].Motivos.Count > 0 ? "Regla" : null;
                }
            }
            if (c.Modelo == null || c.Datos == null)
            {
                return resultado;
            }
            try
            {
                List<RasgosImpago> entradas = rasgos.Select(c.Datos.Categorias.Aplicar).ToList();
                List<double> puntos = EntrenadorModeloImpagos.Puntuar(c.Contexto, c.Modelo, entradas);
                var platt = new CalibradorPlatt { A = c.Datos.PlattA, B = c.Datos.PlattB };
                for (int i = 0; i < resultado.Count; i++)
                {
                    resultado[i].Probabilidad = platt.Probabilidad(puntos[i]);
                    resultado[i].VersionModelo = c.Datos.Etiqueta;
                }
                try
                {
                    List<Dictionary<string, float>> contribuciones = ExplicacionModeloContacto.ContribucionesDe(c.Modelo, c.Contexto, entradas);
                    for (int i = 0; i < resultado.Count && i < contribuciones.Count; i++)
                    {
                        List<string> motivos = TextosRiesgoImpago.Motivos(contribuciones[i], rasgos[i]);
                        if (motivos.Count > 0)
                        {
                            resultado[i].Motivos = motivos;
                            resultado[i].OrigenMotivos = "Modelo";
                        }
                    }
                }
                catch (Exception)
                {
                    // sin explicación del modelo se quedan los motivos de la regla
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Modelo de impagados #896] El modelo guardado no ha podido predecir (queda la regla): {ex.Message}", ex));
            }
            return resultado;
        }

        private Cargado Cargar()
        {
            DateTime fechaModelo = almacen.HayModelo ? File.GetLastWriteTimeUtc(almacen.RutaModelo) : DateTime.MinValue;
            DateTime fechaRegla = almacen.HayRegla ? File.GetLastWriteTimeUtc(almacen.RutaRegla) : DateTime.MinValue;
            lock (bloqueo)
            {
                if (cargado != null && cargado.FechaModelo == fechaModelo && cargado.FechaRegla == fechaRegla
                    && string.Equals(cargado.Carpeta, almacen.Carpeta, StringComparison.OrdinalIgnoreCase))
                {
                    return cargado;
                }
                var nuevo = new Cargado { Carpeta = almacen.Carpeta, FechaModelo = fechaModelo, FechaRegla = fechaRegla, Regla = almacen.LeerRegla() };
                if (almacen.HayModelo)
                {
                    try
                    {
                        nuevo.Datos = almacen.LeerDatos();
                        nuevo.Contexto = Entrenador.CrearContexto();
                        nuevo.Modelo = Entrenador.Cargar(nuevo.Contexto, almacen.RutaModelo);
                    }
                    catch (Exception ex)
                    {
                        nuevo.Modelo = null;
                        registrar(new Exception($"[Modelo de impagados #896] No se ha podido cargar el modelo guardado (queda la regla): {ex.Message}", ex));
                    }
                }
                cargado = nuevo;
                return cargado;
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void Olvidar()
        {
            lock (bloqueo)
            {
                cargado = null;
            }
        }
    }
}
