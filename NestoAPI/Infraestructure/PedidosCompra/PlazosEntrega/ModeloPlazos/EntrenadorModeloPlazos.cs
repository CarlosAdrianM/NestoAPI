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

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos
{
    /// <summary>
    /// NestoAPI#853: lo que se guarda junto al zip del modelo de plazos (<see cref="AlmacenModeloPlazos.NOMBRE_DATOS"/>): el margen
    /// prudente, los proveedores con categoría propia y la historia reciente de cada proveedor (para calcular los rasgos al enviar sin
    /// tocar la BD).
    /// </summary>
    public class MetadatosModeloPlazos
    {
        public const string VERSION = "plazos-1";
        public const string ALGORITMO = "LightGBM regresión de log(1+días) + margen P80 de los residuos";

        public DateTime Fecha { get; set; }
        public string Version { get; set; }
        public string Algoritmo { get; set; }
        /// <summary>Lo que se suma al log previsto para la estimación prudente (P80 de los residuos en validación).</summary>
        public double MargenLog { get; set; }
        public int Ejemplos { get; set; }
        public string Origen { get; set; }
        public List<string> Proveedores { get; set; } = new List<string>();
        public List<PedidoCompraHistorico> Historial { get; set; } = new List<PedidoCompraHistorico>();
    }

    /// <summary>Resultado de entrenar con corte temporal (sin guardar nada todavía).</summary>
    public interface IEvaluacionModeloPlazos
    {
        bool DatosSuficientes { get; }
        string Motivo { get; }
        int Ejemplos { get; }
        int EjemplosEntrenamiento { get; }
        int EjemplosValidacion { get; }
        DateTime ValidacionDesde { get; }
        /// <summary>Métricas de validación del modelo (con el margen calibrado ANTES de la validación).</summary>
        MetricasPlazos Modelo { get; }
        /// <summary>Métricas de validación de la regla del P80 «como si» en cada envío, en los mismos pedidos.</summary>
        MetricasPlazos Regla { get; }
        /// <summary>Margen que llevará el modelo final (P80 de los residuos de validación).</summary>
        double MargenLog { get; }
        /// <summary>Reentrena con TODO y guarda el zip en <paramref name="rutaZip"/>; devuelve sus metadatos.</summary>
        MetadatosModeloPlazos EntrenarFinalYGuardar(string rutaZip, DateTime fecha, string origen);
    }

    public interface IEntrenadorModeloPlazos
    {
        IEvaluacionModeloPlazos Evaluar(IReadOnlyList<PedidoCompraHistorico> historico, DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#853: modelo ML.NET de días hasta el primer albarán de un pedido de compra, en sombra frente a la regla del P80.
    /// <para><b>Algoritmo</b>: LightGBM de regresión (el mismo paquete Microsoft.ML.LightGbm 4.0.2 y la misma librería nativa que los
    /// modelos de llamadas y sugerencias) sobre log(1 + días): los plazos son muy asimétricos y en log el error es relativo (un día de más
    /// pesa igual en un proveedor de 2 días que una semana en uno de 30). En 4.0.2 no hay regresión cuantil pública (el FastForest cuantil
    /// es interno), así que la estimación PRUDENTE (comparable con el P80 de la regla) es la media en log + el P80 de los residuos en log
    /// (margen multiplicativo: más días de colchón para quien tarda más) y se redondea hacia arriba.</para>
    /// <para><b>Ventanas</b> (desde <c>hoy</c>): validación = los últimos <see cref="MESES_VALIDACION"/> meses; calibración = los 3 anteriores.
    /// Modelo A con lo anterior a la calibración → margen = P80 de sus residuos en calibración. Modelo B con todo lo anterior a la
    /// validación → se mide en validación con ese margen (nada de la validación entra en el ajuste ni en el margen). El modelo final se
    /// entrena con todo y lleva de margen el P80 de los residuos de B en validación.</para>
    /// </summary>
    public class EntrenadorModeloPlazos : IEntrenadorModeloPlazos
    {
        public const int MESES_VALIDACION = 3;
        public const int MESES_CALIBRACION = 3;
        public const int MINIMO_ENTRENAMIENTO = 100;
        public const double PERCENTIL_MARGEN = 0.8;
        public const int MAX_DIAS_PREVISTOS = 365;

        private readonly int minimoHoja;

        /// <param name="minimoHoja">Ejemplos mínimos por hoja de LightGBM (20; los tests con pocos datos pueden bajarlo).</param>
        public EntrenadorModeloPlazos(int minimoHoja = 20)
        {
            this.minimoHoja = minimoHoja;
        }

        internal static readonly string[] Numericas =
        {
            nameof(RasgosPlazo.DiaSemana), nameof(RasgosPlazo.Mes), nameof(RasgosPlazo.Lineas), nameof(RasgosPlazo.LogImporte),
            nameof(RasgosPlazo.DiasEnServir), nameof(RasgosPlazo.HistCuantos), nameof(RasgosPlazo.HistMediana), nameof(RasgosPlazo.HistP80),
            nameof(RasgosPlazo.HistUltimo), nameof(RasgosPlazo.DiasDesdeAnterior), nameof(RasgosPlazo.EnElAire)
        };

        public IEvaluacionModeloPlazos Evaluar(IReadOnlyList<PedidoCompraHistorico> historico, DateTime hoy)
        {
            hoy = hoy.Date;
            List<EjemploPlazo> ejemplos = ConstructorRasgosPlazos.Construir(historico);
            DateTime corteValidacion = hoy.AddMonths(-MESES_VALIDACION);
            DateTime corteCalibracion = corteValidacion.AddMonths(-MESES_CALIBRACION);
            List<EjemploPlazo> entrenamientoA = ejemplos.Where(e => e.FechaEnvio < corteCalibracion).ToList();
            List<EjemploPlazo> calibracion = ejemplos.Where(e => e.FechaEnvio >= corteCalibracion && e.FechaEnvio < corteValidacion).ToList();
            List<EjemploPlazo> entrenamientoB = ejemplos.Where(e => e.FechaEnvio < corteValidacion).ToList();
            List<EjemploPlazo> validacion = ejemplos.Where(e => e.FechaEnvio >= corteValidacion).ToList();

            var evaluacion = new Evaluacion(this, historico, ejemplos, hoy)
            {
                EjemplosEntrenamiento = entrenamientoB.Count,
                EjemplosValidacion = validacion.Count,
                ValidacionDesde = corteValidacion
            };
            if (entrenamientoA.Count < MINIMO_ENTRENAMIENTO || calibracion.Count < PuertaCalidadPlazos.MINIMO_VALIDACION
                || validacion.Count < PuertaCalidadPlazos.MINIMO_VALIDACION)
            {
                evaluacion.Motivo = $"datos insuficientes: {entrenamientoA.Count} para entrenar antes de la calibración (mínimo {MINIMO_ENTRENAMIENTO}), " +
                    $"{calibracion.Count} de calibración y {validacion.Count} de validación (mínimo {PuertaCalidadPlazos.MINIMO_VALIDACION})";
                return evaluacion;
            }

            var regla = new ReglaPlazosComoSi(historico);
            foreach (EjemploPlazo e in validacion)
            {
                e.DiasRegla = regla.Dias(e.Pedido.Proveedor, e.FechaEnvio, e.Pedido.DiasEnServir);
            }

            MLContext ml = Entrenador.CrearContexto();
            (ITransformer modeloA, MapaProveedoresPlazos mapaA) = Entrenar(ml, entrenamientoA);
            double margenCalibrado = Margen(Residuos(ml, modeloA, mapaA, calibracion));

            (ITransformer modeloB, MapaProveedoresPlazos mapaB) = Entrenar(ml, entrenamientoB);
            List<double> previstosB = PredecirLog(ml, modeloB, validacion.Select(e => mapaB.Aplicar(e.Rasgos)));
            List<int> conRegla = Enumerable.Range(0, validacion.Count).Where(i => validacion[i].DiasRegla.HasValue).ToList();

            evaluacion.Modelo = MetricasPlazos.Calcular(conRegla.Select(i => (Real: validacion[i].Dias, Previsto: DiasPrudentes(previstosB[i], margenCalibrado))));
            evaluacion.Regla = MetricasPlazos.Calcular(conRegla.Select(i => (Real: validacion[i].Dias, Previsto: validacion[i].DiasRegla.Value)));
            evaluacion.MargenLog = Margen(validacion.Select((e, i) => Math.Log(1 + e.Dias) - previstosB[i]));
            evaluacion.DatosSuficientes = true;
            evaluacion.Motivo = $"modelo: {PuertaCalidadPlazos.Texto(evaluacion.Modelo)}; regla: {PuertaCalidadPlazos.Texto(evaluacion.Regla)}";
            return evaluacion;
        }

        internal (ITransformer Modelo, MapaProveedoresPlazos Mapa) Entrenar(MLContext ml, IReadOnlyCollection<EjemploPlazo> ejemplos)
        {
            MapaProveedoresPlazos mapa = MapaProveedoresPlazos.Desde(ejemplos);
            IDataView datos = ml.Data.LoadFromEnumerable(ejemplos.Select(mapa.Fila).ToList());
            return (Pipeline(ml).Fit(datos), mapa);
        }

        internal IEstimator<ITransformer> Pipeline(MLContext ml)
            => ml.Transforms.Categorical.OneHotEncoding("ProveedorCod", nameof(RasgosPlazo.Proveedor))
                .Append(ml.Transforms.Concatenate("Features", new[] { "ProveedorCod" }.Concat(Numericas).ToArray()))
                .Append(ml.Regression.Trainers.LightGbm(new LightGbmRegressionTrainer.Options
                {
                    LabelColumnName = nameof(FilaPlazo.Label),
                    FeatureColumnName = "Features",
                    NumberOfLeaves = 16,
                    NumberOfIterations = 200,
                    MinimumExampleCountPerLeaf = minimoHoja,
                    LearningRate = 0.05,
                    Seed = 853
                }));

        /// <summary>log(1 + días) medio previsto de cada entrada, en el mismo orden.</summary>
        public static List<double> PredecirLog(MLContext ml, ITransformer modelo, IEnumerable<RasgosPlazo> entradas)
        {
            List<RasgosPlazo> lista = entradas.ToList();
            if (lista.Count == 0)
            {
                return new List<double>();
            }
            IDataView datos = ml.Data.LoadFromEnumerable(lista);
            return ml.Data.CreateEnumerable<PrediccionPlazo>(modelo.Transform(datos), reuseRowObject: false).Select(p => (double)p.Score).ToList();
        }

        private static IEnumerable<double> Residuos(MLContext ml, ITransformer modelo, MapaProveedoresPlazos mapa, IReadOnlyList<EjemploPlazo> ejemplos)
        {
            List<double> previstos = PredecirLog(ml, modelo, ejemplos.Select(e => mapa.Aplicar(e.Rasgos)));
            return ejemplos.Select((e, i) => Math.Log(1 + e.Dias) - previstos[i]);
        }

        /// <summary>P80 (rango más cercano, como la regla) de los residuos en log. PURA.</summary>
        public static double Margen(IEnumerable<double> residuos)
        {
            List<double> ordenados = (residuos ?? Enumerable.Empty<double>()).Where(r => !double.IsNaN(r) && !double.IsInfinity(r)).OrderBy(r => r).ToList();
            if (ordenados.Count == 0)
            {
                return 0;
            }
            int rango = (int)Math.Ceiling(PERCENTIL_MARGEN * ordenados.Count);
            return ordenados[Math.Max(1, Math.Min(rango, ordenados.Count)) - 1];
        }

        /// <summary>Días prudentes: exp(log previsto + margen) − 1, redondeado hacia arriba, entre 0 y <see cref="MAX_DIAS_PREVISTOS"/>. PURA.</summary>
        public static int DiasPrudentes(double logPrevisto, double margenLog)
        {
            double dias = Math.Exp(logPrevisto + margenLog) - 1;
            if (double.IsNaN(dias))
            {
                return 0;
            }
            return (int)Math.Max(0, Math.Min(MAX_DIAS_PREVISTOS, Math.Ceiling(dias - 1e-9)));
        }

        public static void Guardar(MLContext ml, ITransformer modelo, string ruta)
        {
            IDataView esquema = ml.Data.LoadFromEnumerable(new RasgosPlazo[0]);
            ml.Model.Save(modelo, esquema.Schema, ruta);
        }

        private sealed class Evaluacion : IEvaluacionModeloPlazos
        {
            private readonly EntrenadorModeloPlazos entrenador;
            private readonly IReadOnlyList<PedidoCompraHistorico> historico;
            private readonly List<EjemploPlazo> ejemplos;
            private readonly DateTime hoy;

            public Evaluacion(EntrenadorModeloPlazos entrenador, IReadOnlyList<PedidoCompraHistorico> historico, List<EjemploPlazo> ejemplos, DateTime hoy)
            {
                this.entrenador = entrenador;
                this.historico = historico;
                this.ejemplos = ejemplos;
                this.hoy = hoy;
            }

            public bool DatosSuficientes { get; set; }
            public string Motivo { get; set; }
            public int Ejemplos => ejemplos.Count;
            public int EjemplosEntrenamiento { get; set; }
            public int EjemplosValidacion { get; set; }
            public DateTime ValidacionDesde { get; set; }
            public MetricasPlazos Modelo { get; set; }
            public MetricasPlazos Regla { get; set; }
            public double MargenLog { get; set; }

            public MetadatosModeloPlazos EntrenarFinalYGuardar(string rutaZip, DateTime fecha, string origen)
            {
                if (!DatosSuficientes)
                {
                    throw new InvalidOperationException("NestoAPI#853: no se entrena el modelo final sin datos suficientes");
                }
                MLContext ml = Entrenador.CrearContexto();
                (ITransformer modelo, MapaProveedoresPlazos mapa) = entrenador.Entrenar(ml, ejemplos);
                Guardar(ml, modelo, rutaZip);
                return new MetadatosModeloPlazos
                {
                    Fecha = fecha,
                    Version = MetadatosModeloPlazos.VERSION,
                    Algoritmo = MetadatosModeloPlazos.ALGORITMO,
                    MargenLog = MargenLog,
                    Ejemplos = ejemplos.Count,
                    Origen = origen,
                    Proveedores = mapa.Conocidos.ToList(),
                    Historial = ConstructorRasgosPlazos.Recortar(historico, hoy)
                };
            }
        }
    }

    /// <summary>
    /// NestoAPI#853: dónde vive el modelo de plazos: la MISMA carpeta que el de llamadas (appSetting ModeloLlamadas:Carpeta, por defecto
    /// C:\NestoAPI\ModelosIA, fuera de lo publicado) con <see cref="NOMBRE_MODELO"/> y <see cref="NOMBRE_DATOS"/>. Promover escribe primero
    /// los datos y luego cambia el zip con File.Replace (el anterior queda en <see cref="NOMBRE_ANTERIOR"/>); el predictor recarga al cambiar
    /// la fecha del zip.
    /// </summary>
    public class AlmacenModeloPlazos
    {
        public const string NOMBRE_MODELO = "plazos_proveedores.zip";
        public const string NOMBRE_DATOS = "plazos_proveedores.json";
        public const string NOMBRE_ANTERIOR = "plazos_proveedores_anterior.zip";
        public const string NOMBRE_DATOS_ANTERIOR = "plazos_proveedores_anterior.json";
        private const int INTENTOS = 5;

        public AlmacenModeloPlazos(string carpeta)
        {
            Carpeta = carpeta ?? throw new ArgumentNullException(nameof(carpeta));
        }

        public static AlmacenModeloPlazos Produccion()
            => new AlmacenModeloPlazos(AlmacenModeloLlamadas.CarpetaConfigurada(clave => ConfigurationManager.AppSettings[clave]));

        public string Carpeta { get; }
        public string RutaModelo => Path.Combine(Carpeta, NOMBRE_MODELO);
        public string RutaDatos => Path.Combine(Carpeta, NOMBRE_DATOS);

        public bool HayModelo => File.Exists(RutaModelo) && File.Exists(RutaDatos);

        public string NuevaRutaTemporal()
        {
            _ = Directory.CreateDirectory(Carpeta);
            return Path.Combine(Carpeta, $"plazos_proveedores.{DateTime.Now:yyyyMMddHHmmss}.{Guid.NewGuid():N}.tmp");
        }

        public MetadatosModeloPlazos LeerDatos()
        {
            try
            {
                return File.Exists(RutaDatos) ? JsonConvert.DeserializeObject<MetadatosModeloPlazos>(File.ReadAllText(RutaDatos, Encoding.UTF8)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Promover(string temporalZip, MetadatosModeloPlazos datos)
        {
            if (!File.Exists(temporalZip))
            {
                throw new FileNotFoundException("NestoAPI#853: no está el modelo de plazos que se iba a promover", temporalZip);
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

    /// <summary>Lo que se sabe de un pedido de compra en el momento de enviarlo.</summary>
    public class ConsultaPlazoModelo
    {
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public string Proveedor { get; set; }
        public DateTime FechaEnvio { get; set; }
        public int DiasEnServir { get; set; }
        public int Lineas { get; set; }
        public double Importe { get; set; }
    }

    /// <summary>
    /// NestoAPI#853: predicción del modelo de plazos al enviar. El modelo (zip + datos) se carga una vez y se queda en memoria hasta que cambie
    /// la fecha del zip (como <see cref="ModeloContacto"/>). Sin modelo guardado, null.
    /// </summary>
    public static class PredictorPlazosProveedores
    {
        private static readonly object bloqueo = new object();
        private static Cargado cargado;

        private sealed class Cargado
        {
            public string Ruta;
            public DateTime Fecha;
            public MLContext Contexto;
            public ITransformer Modelo;
            public MetadatosModeloPlazos Datos;
            public MapaProveedoresPlazos Mapa;
            public Dictionary<string, List<PedidoCompraHistorico>> PorProveedor;
        }

        /// <summary>Días prudentes desde el envío; null sin modelo.</summary>
        public static int? DiasPrudentes(AlmacenModeloPlazos almacen, ConsultaPlazoModelo consulta)
        {
            if (almacen == null || consulta == null || string.IsNullOrWhiteSpace(consulta.Proveedor) || !almacen.HayModelo)
            {
                return null;
            }
            Cargado modelo = Cargar(almacen);
            if (modelo?.Datos == null)
            {
                return null;
            }
            string proveedor = consulta.Proveedor.Trim();
            modelo.PorProveedor.TryGetValue(proveedor, out List<PedidoCompraHistorico> delProveedor);
            RasgosPlazo rasgos = ConstructorRasgosPlazos.Rasgos(delProveedor, proveedor, consulta.FechaEnvio, consulta.DiasEnServir, consulta.Lineas,
                consulta.Importe, consulta.Empresa, consulta.Pedido);
            double log = EntrenadorModeloPlazos.PredecirLog(modelo.Contexto, modelo.Modelo, new[] { modelo.Mapa.Aplicar(rasgos) })[0];
            return EntrenadorModeloPlazos.DiasPrudentes(log, modelo.Datos.MargenLog);
        }

        private static Cargado Cargar(AlmacenModeloPlazos almacen)
        {
            DateTime fecha = File.GetLastWriteTimeUtc(almacen.RutaModelo);
            lock (bloqueo)
            {
                if (cargado != null && cargado.Fecha == fecha && string.Equals(cargado.Ruta, almacen.RutaModelo, StringComparison.OrdinalIgnoreCase))
                {
                    return cargado;
                }
                MetadatosModeloPlazos datos = almacen.LeerDatos();
                MLContext ml = Entrenador.CrearContexto();
                cargado = new Cargado
                {
                    Ruta = almacen.RutaModelo,
                    Fecha = fecha,
                    Contexto = ml,
                    Modelo = Entrenador.Cargar(ml, almacen.RutaModelo),
                    Datos = datos,
                    Mapa = new MapaProveedoresPlazos(datos?.Proveedores),
                    PorProveedor = (datos?.Historial ?? new List<PedidoCompraHistorico>())
                        .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Proveedor))
                        .GroupBy(p => p.Proveedor.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase)
                };
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
