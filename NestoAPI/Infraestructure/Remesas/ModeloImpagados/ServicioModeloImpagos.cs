using Hangfire;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    public class ResultadoEntrenamientoModeloImpagos
    {
        public bool Hecho { get; set; }
        public bool Promovido { get; set; }
        public string Mensaje { get; set; }
        public EntrenamientoModeloImpagos Fila { get; set; }
    }

    /// <summary>
    /// NestoAPI#896: entrenamiento del modelo de impagados de remesas EN SOMBRA (job <see cref="JOB_ID"/>, los domingos a las 4:40 de
    /// Madrid, cola «entrenamiento»: con unas 250 devoluciones al año, semanal sobra). Lee <see cref="ANIOS_HISTORICO"/> años de recibos
    /// girados e impagados (NOLOCK, solo de noche), entrena con corte temporal (<see cref="EntrenadorModeloImpagos"/>), guarda SIEMPRE
    /// LA REGLA ajustada y apunta SIEMPRE las métricas de validación de modelo y regla en dbo.RemesasModeloEntrenamientos; solo cambia el
    /// modelo guardado si pasa <see cref="PuertaCalidadImpagos"/>. Sin la tabla no hace nada (ELMAH una vez). Nunca lanza.
    /// </summary>
    public class ModeloImpagosRemesasJobsService
    {
        public const string JOB_ID = "remesas-impagos-modelo";
        public const string CRON = "40 4 * * 0";
        public const string COLA = ReentrenamientoModeloLlamadasJobsService.COLA;
        public const int ANIOS_HISTORICO = 8;
        /// <summary>Los giros del primer año leído no entrenan: no tienen historia del cliente detrás.</summary>
        public const int MESES_SIN_HISTORIA = 12;
        /// <summary>El programado que arranque a esta hora o más tarde no entrena (sería en horario).</summary>
        internal const int HORA_LIMITE_PROGRAMADO = 7;
        public const string ORIGEN_PROGRAMADO = "job semanal";
        public const string SCRIPT = "Scripts/Issue896_RemesasModeloImpagos.sql";
        public const string MENSAJE_SIN_TABLA = "Todavía no existen dbo.RemesasModeloEntrenamientos y dbo.RemesasPrediccionesImpago: hay que lanzar " + SCRIPT + ".";
        internal const string USUARIO_ELMAH = "Sistema (modelo de impagados de remesas)";

        private static bool sinTablaAvisado;

        private readonly IRepositorioModeloImpagos repositorio;
        private readonly IEntrenadorModeloImpagos entrenador;
        private readonly AlmacenModeloImpagos almacen;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;
        private readonly Action asegurarNativo;

        public ModeloImpagosRemesasJobsService(IRepositorioModeloImpagos repositorio = null, IEntrenadorModeloImpagos entrenador = null,
            AlmacenModeloImpagos almacen = null, Func<DateTime> ahora = null, Action<Exception> registrar = null, Action asegurarNativo = null)
        {
            this.repositorio = repositorio ?? RepositorioModeloImpagos.DesdeConfiguracion();
            this.entrenador = entrenador ?? new EntrenadorModeloImpagos();
            this.almacen = almacen ?? AlmacenModeloImpagos.Produccion();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
        }

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void EntrenarProgramado()
        {
            if (!TocaEntrenar(DateTime.Now))
            {
                return;
            }
            _ = new ModeloImpagosRemesasJobsService().Ejecutar(ORIGEN_PROGRAMADO);
        }

        /// <summary>Lo que encola (o programa para la noche) POST api/Remesas/ModeloImpagados/Entrenar.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void EntrenarManual(string usuario)
        {
            _ = new ModeloImpagosRemesasJobsService().Ejecutar(string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        internal static bool TocaEntrenar(DateTime ahora) => ahora.Hour < HORA_LIMITE_PROGRAMADO;

        public ResultadoEntrenamientoModeloImpagos Ejecutar(string origen)
        {
            string temporal = null;
            try
            {
                if (!repositorio.TablaEntrenamientosExiste())
                {
                    if (!sinTablaAvisado)
                    {
                        sinTablaAvisado = true;
                        registrar(new Exception("[Modelo de impagados #896] " + MENSAJE_SIN_TABLA + " No se entrena (se avisa una vez)."));
                    }
                    return new ResultadoEntrenamientoModeloImpagos { Hecho = false, Mensaje = MENSAJE_SIN_TABLA };
                }
                sinTablaAvisado = false;

                DateTime inicio = ahora();
                var cronometro = Stopwatch.StartNew();
                asegurarNativo();
                DateTime desde = inicio.Date.AddYears(-ANIOS_HISTORICO);
                HistoricoImpagos historico = repositorio.LeerHistorico(desde);
                _ = EnlazadorImpagados.Enlazar(historico.Recibos, historico.Impagados);
                NivelGeneralImpagos general = NivelGeneralImpagos.Desde(historico.Recibos, historico.Impagados);
                List<EjemploImpago> ejemplos = ConstructorRasgosImpagos.Construir(historico.Recibos, general, inicio.Date, desde.AddMonths(MESES_SIN_HISTORIA));
                IEvaluacionModeloImpagos evaluacion = entrenador.Evaluar(ejemplos, inicio.Date);

                // LA REGLA se guarda siempre: es el respaldo y lo que se compara en la sombra aunque no haya modelo.
                almacen.GuardarRegla(evaluacion.ReglaFinal);

                var fila = new EntrenamientoModeloImpagos
                {
                    Fecha = inicio,
                    Ejemplos = evaluacion.Ejemplos,
                    EjemplosEntrenamiento = evaluacion.EjemplosEntrenamiento,
                    EjemplosValidacion = evaluacion.EjemplosValidacion,
                    ValidacionDesde = evaluacion.ValidacionDesde,
                    Version = MetadatosModeloImpagos.VERSION,
                    Algoritmo = MetadatosModeloImpagos.ALGORITMO,
                    Origen = origen
                };
                bool promovido = false;
                if (!evaluacion.DatosSuficientes)
                {
                    fila.Motivo = "no se entrena: " + evaluacion.Motivo;
                }
                else
                {
                    fila.DevueltosValidacion = evaluacion.Modelo.Devueltos;
                    fila.AucModelo = evaluacion.Modelo.Auc;
                    fila.Captado5Modelo = evaluacion.Modelo.Captado5;
                    fila.Captado10Modelo = evaluacion.Modelo.Captado10;
                    fila.CalibracionAltosModelo = evaluacion.Modelo.CalibracionAltos;
                    fila.TramosModelo = JsonConvert.SerializeObject(evaluacion.Modelo.Tramos);
                    fila.AucRegla = evaluacion.Regla.Auc;
                    fila.Captado5Regla = evaluacion.Regla.Captado5;
                    fila.Captado10Regla = evaluacion.Regla.Captado10;
                    fila.CalibracionAltosRegla = evaluacion.Regla.CalibracionAltos;
                    fila.TramosRegla = JsonConvert.SerializeObject(evaluacion.Regla.Tramos);
                    fila.PlattA = evaluacion.Calibrador.A;
                    fila.PlattB = evaluacion.Calibrador.B;

                    EntrenamientoModeloImpagos activo = repositorio.UltimoActivo();
                    DecisionPuertaImpagos decision = PuertaCalidadImpagos.Decidir(activo?.MetricasModelo(), almacen.HayModelo, evaluacion.Modelo, evaluacion.Regla);
                    if (decision.Promover)
                    {
                        temporal = almacen.NuevaRutaTemporal();
                        MetadatosModeloImpagos datos = evaluacion.EntrenarFinalYGuardar(temporal, ahora(), origen);
                        almacen.Promover(temporal, datos);
                        temporal = null;
                        promovido = true;
                    }
                    fila.ModeloActivo = promovido;
                    fila.Motivo = (promovido ? "promovido: " : "no promovido: ") + decision.Motivo + ". " + evaluacion.Motivo;
                }
                fila.Segundos = (int)cronometro.Elapsed.TotalSeconds;
                repositorio.RegistrarEntrenamiento(fila);
                return new ResultadoEntrenamientoModeloImpagos { Hecho = true, Promovido = promovido, Mensaje = fila.Motivo, Fila = fila };
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Modelo de impagados #896] Error al entrenar ({origen}): {ex.Message}. El modelo guardado no se toca.", ex));
                return new ResultadoEntrenamientoModeloImpagos { Hecho = false, Mensaje = ex.Message };
            }
            finally
            {
                almacen.Descartar(temporal);
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos() => sinTablaAvisado = false;
    }

    /// <summary>
    /// NestoAPI#896, LA SOMBRA: al crearse una remesa se apunta en dbo.RemesasPrediccionesImpago la predicción de cada efecto (modelo,
    /// LA REGLA, rasgos y causas). Punto ÚNICO: <see cref="AlCrearRemesa"/>, que solo ENCOLA un trabajo de Hangfire (cola «indicadores»)
    /// y nunca lanza: ni rompe ni retrasa la remesa. El trabajo lee los efectos YA girados (cartera con ese nº de remesa), la historia de
    /// sus clientes (dos consultas por tanda de 500 clientes) y el nivel general (dos consultas agregadas de las últimas semanas). Sin el script o
    /// sin regla ni modelo guardados, no hace nada. Idempotente (no repite un efecto de la misma remesa).
    /// </summary>
    public class SombraImpagosRemesa
    {
        public const string COLA = IndicadoresStockJobsService.COLA;

        private static bool sinTablaAvisado;
        private static bool falloAlEncolarAvisado;

        /// <summary>Cómo se encola (los tests lo sustituyen). Null = Hangfire.</summary>
        internal static Action<string, int> Encolador { get; set; }

        private readonly IRepositorioModeloImpagos repositorio;
        private readonly IPredictorImpagos predictor;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;

        public SombraImpagosRemesa(IRepositorioModeloImpagos repositorio = null, IPredictorImpagos predictor = null, Func<DateTime> ahora = null,
            Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? RepositorioModeloImpagos.DesdeConfiguracion();
            this.predictor = predictor ?? new PredictorImpagos();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, ModeloImpagosRemesasJobsService.USUARIO_ELMAH));
        }

        /// <summary>Lo llama quien acaba de crear la remesa. Nunca lanza; si no se puede encolar, ELMAH una vez.</summary>
        public static void AlCrearRemesa(string empresa, int remesa)
        {
            try
            {
                if (remesa <= 0 || string.IsNullOrWhiteSpace(empresa))
                {
                    return;
                }
                string empresaLimpia = empresa.Trim();
                if (Encolador != null)
                {
                    Encolador(empresaLimpia, remesa);
                }
                else
                {
                    _ = BackgroundJob.Enqueue(() => Registrar(empresaLimpia, remesa));
                }
            }
            catch (Exception ex)
            {
                if (!falloAlEncolarAvisado)
                {
                    falloAlEncolarAvisado = true;
                    try
                    {
                        ElmahHelper.Log(new Exception($"[Modelo de impagados #896] No se ha podido encolar la predicción en sombra de la remesa {remesa} " +
                            $"(la remesa no se ve afectada; se avisa una vez): {ex.Message}", ex), ModeloImpagosRemesasJobsService.USUARIO_ELMAH);
                    }
                    catch (Exception)
                    {
                        // ni el registro puede romper la remesa
                    }
                }
            }
        }

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static void Registrar(string empresa, int remesa)
        {
            _ = new SombraImpagosRemesa().RegistrarRemesa(empresa, remesa);
        }

        /// <summary>El núcleo: cuántas predicciones ha apuntado. Nunca lanza.</summary>
        public int RegistrarRemesa(string empresa, int remesa)
        {
            try
            {
                if (!repositorio.TablaPrediccionesExiste())
                {
                    if (!sinTablaAvisado)
                    {
                        sinTablaAvisado = true;
                        registrar(new Exception("[Modelo de impagados #896] " + ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA +
                            " No se apunta la predicción de las remesas (se avisa una vez)."));
                    }
                    return 0;
                }
                sinTablaAvisado = false;
                if (!predictor.HayAlgo)
                {
                    return 0;
                }
                List<ReciboGirado> efectos = repositorio.LeerEfectosRemesa(empresa, remesa);
                if (efectos.Count == 0)
                {
                    return 0;
                }
                DateTime hoy = ahora().Date;
                DateTime desde = hoy.AddYears(-ModeloImpagosRemesasJobsService.ANIOS_HISTORICO);
                HistoricoImpagos historia = repositorio.LeerHistorialClientes(empresa, efectos.Select(e => e.Cliente).ToList(), desde);
                _ = EnlazadorImpagados.Enlazar(historia.Recibos, historia.Impagados);
                NivelGeneralImpagos general = repositorio.LeerNivelGeneral(hoy);
                ILookup<string, ReciboGirado> porCliente = historia.Recibos.ToLookup(r => r.ClaveCliente);

                // El día del giro es el de la remesa (hoy, si se apunta al crearla): lo que se sabe ese día.
                List<RasgosImpago> rasgos = efectos.Select(e => ConstructorRasgosImpagos.Rasgos(e, e.FechaGiro, porCliente[e.ClaveCliente], general)).ToList();
                List<PrediccionRiesgoImpago> predicciones = predictor.Predecir(rasgos);
                var filas = new List<FilaPrediccionImpago>();
                for (int i = 0; i < efectos.Count && i < predicciones.Count; i++)
                {
                    ReciboGirado e = efectos[i];
                    PrediccionRiesgoImpago p = predicciones[i];
                    if (!p.Probabilidad.HasValue && !p.ProbabilidadRegla.HasValue)
                    {
                        continue;
                    }
                    filas.Add(new FilaPrediccionImpago
                    {
                        Empresa = e.Empresa,
                        Remesa = remesa,
                        NumOrden = e.NumOrden,
                        Cliente = e.Cliente,
                        Contacto = e.Contacto,
                        Documento = e.Documento,
                        Efecto = e.Efecto,
                        Importe = (decimal)e.Importe,
                        FechaGiro = e.FechaGiro,
                        FechaVto = e.FechaVto,
                        VersionModelo = p.VersionModelo,
                        Probabilidad = Redondear(p.Probabilidad),
                        ProbabilidadRegla = Redondear(p.ProbabilidadRegla),
                        Rasgos = JsonConvert.SerializeObject(rasgos[i]),
                        MotivosRiesgo = p.Motivos == null || p.Motivos.Count == 0 ? null : string.Join("; ", p.Motivos)
                    });
                }
                return repositorio.GuardarPredicciones(filas);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Modelo de impagados #896] Error al apuntar la predicción en sombra de la remesa {remesa}: {ex.Message}", ex));
                return 0;
            }
        }

        private static double? Redondear(double? p) => p.HasValue ? Math.Round(p.Value, 6) : (double?)null;

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos()
        {
            sinTablaAvisado = false;
            falloAlEncolarAvisado = false;
        }
    }

    /// <summary>
    /// NestoAPI#896: job <see cref="JOB_ID"/> (todos los días a las 3:55 de Madrid, cola «indicadores»): pone el resultado a las
    /// predicciones de la sombra. Devuelta en cuanto llega el impagado (dentro de los 45 días); no devuelta al cumplirlos. Por lotes de
    /// <see cref="LOTE"/> Id, idempotente. Sin la tabla, nada.
    /// </summary>
    public static class ResultadoImpagosRemesasJobsService
    {
        public const string JOB_ID = "remesas-impagos-resultado";
        public const string CRON = "55 3 * * *";
        public const int LOTE = 500;

        [Queue(IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Resolver()
        {
            try
            {
                int resueltas = Resolver(RepositorioModeloImpagos.DesdeConfiguracion(), DateTime.Today);
                Console.WriteLine($"[Modelo de impagados #896] Predicciones de la sombra resueltas: {resueltas}");
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Modelo de impagados #896] Error al resolver las predicciones de la sombra: {ex.Message}", ex),
                    ModeloImpagosRemesasJobsService.USUARIO_ELMAH);
            }
        }

        /// <summary>El núcleo (con el repositorio inyectado): cuántas ha resuelto.</summary>
        internal static int Resolver(IRepositorioModeloImpagos repositorio, DateTime hoy)
        {
            if (!repositorio.TablaPrediccionesExiste())
            {
                return 0;
            }
            long ultimo = 0;
            int resueltas = 0;
            while (true)
            {
                List<long> lote = repositorio.LeerPendientes(ultimo, LOTE);
                if (lote.Count == 0)
                {
                    break;
                }
                resueltas += repositorio.Resolver(lote.Min(), lote.Max(), hoy);
                ultimo = lote.Max();
                if (lote.Count < LOTE)
                {
                    break;
                }
            }
            return resueltas;
        }
    }

    /// <summary>
    /// NestoAPI#896: el interruptor de ENSEÑAR la probabilidad en Nesto y NestoApp: ParametrosUsuario empresa 1 «(defecto)»
    /// <see cref="CLAVE"/>. SOLO «1» = encendido; sin fila, apagado (nace apagado y leerlo no crea la fila). Hoy nada lo consulta para
    /// enseñar: se enciende cuando la sombra diga que el modelo es fiable (Carlos 24/11/26).
    /// </summary>
    public static class InterruptorEnsenarImpagos
    {
        public const string CLAVE = "Remesas.ModeloImpagadosEnsenar";

        /// <summary>Para los tests: null = leer ParametrosUsuario.</summary>
        internal static Func<bool> Forzado { get; set; }

        public static bool Encendido
        {
            get
            {
                if (Forzado != null)
                {
                    return Forzado();
                }
                try
                {
                    return Interpretar(ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, CLAVE));
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        internal static bool Interpretar(string valor) => valor != null && valor.Trim() == "1";
    }

    /// <summary>NestoAPI#896: cómo se lanza a mano el entrenamiento (sustituible en los tests).</summary>
    public interface IProgramadorModeloImpagos
    {
        string Encolar(string usuario);
        string Programar(DateTime cuandoMadrid, string usuario);
    }

    public class ProgramadorModeloImpagosHangfire : IProgramadorModeloImpagos
    {
        public string Encolar(string usuario) => BackgroundJob.Enqueue(() => ModeloImpagosRemesasJobsService.EntrenarManual(usuario));

        public string Programar(DateTime cuandoMadrid, string usuario)
        {
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cuandoMadrid, DateTimeKind.Unspecified), Startup.ZonaHorariaPeninsular());
            return BackgroundJob.Schedule(() => ModeloImpagosRemesasJobsService.EntrenarManual(usuario), new DateTimeOffset(utc, TimeSpan.Zero));
        }
    }

    /// <summary>Lo que ha pasado de verdad con lo apuntado en la sombra.</summary>
    public class SombraModeloImpagosDTO
    {
        /// <summary>Predicciones apuntadas (todas, desde el principio).</summary>
        public int Predicciones { get; set; }
        /// <summary>Las que aún no tienen resultado (no han pasado 45 días ni ha llegado el impagado).</summary>
        public int Pendientes { get; set; }
        public DateTime? PrimeraPrediccion { get; set; }
        /// <summary>Resueltas dentro de las semanas pedidas, con probabilidad del modelo.</summary>
        public int ResueltasConModelo { get; set; }
        /// <summary>El modelo: previsto frente a real por tramos, AUC y lo que recoge arriba.</summary>
        public MetricasImpagos Modelo { get; set; }
        /// <summary>LA REGLA en las mismas semanas (todas las resueltas con su probabilidad).</summary>
        public MetricasImpagos Regla { get; set; }
    }

    /// <summary>Respuesta de GET api/Remesas/ModeloImpagados/Comparacion.</summary>
    public class ComparacionModeloImpagosDTO
    {
        public int Semanas { get; set; }
        /// <summary>Hay modelo guardado (pasó la puerta de calidad alguna vez).</summary>
        public bool ModeloGuardado { get; set; }
        /// <summary>Hay regla ajustada guardada (desde el primer entrenamiento).</summary>
        public bool ReglaGuardada { get; set; }
        /// <summary>El interruptor <see cref="InterruptorEnsenarImpagos.CLAVE"/> (hoy no se enseña nada aunque esté encendido: falta ese corte).</summary>
        public bool Ensenar { get; set; }
        /// <summary>El último entrenamiento (métricas de VALIDACIÓN de modelo y regla). Null si no hay ninguno.</summary>
        public EntrenamientoModeloImpagos UltimoEntrenamiento { get; set; }
        public SombraModeloImpagosDTO Sombra { get; set; }
        /// <summary>Cumple la definición de «fiable» (<see cref="ReglaFiabilidadImpagos"/>).</summary>
        public bool Fiable { get; set; }
        public DateTime? FiableDesde { get; set; }
        public int SemanasSeguidasCumpliendo { get; set; }
        public string Veredicto { get; set; }
        /// <summary>Las últimas semanas cerradas, cada una medida con la ventana de 12 semanas que acaba en ella.</summary>
        public List<SemanaFiabilidadImpagos> SemanasEvaluadas { get; set; }
    }

    public class LanzamientoModeloImpagosDTO
    {
        public string JobId { get; set; }
        public bool Programado { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#896: arma la comparación (lo usa el controlador). Solo lee las dos tablas propias.</summary>
    public static class ComparacionModeloImpagos
    {
        public const int SEMANAS_POR_DEFECTO = 12;
        public const int SEMANAS_MAXIMO = 104;

        public static ComparacionModeloImpagosDTO Construir(IRepositorioModeloImpagos repositorio, DateTime hoy, int semanas, bool modeloGuardado,
            bool reglaGuardada, bool ensenar)
        {
            int validas = Math.Max(1, Math.Min(SEMANAS_MAXIMO, semanas));
            DateTime desdeVentana = ReglaFiabilidadImpagos.Lunes(hoy.Date.AddDays(-7 * validas));
            var dto = new ComparacionModeloImpagosDTO
            {
                Semanas = validas,
                ModeloGuardado = modeloGuardado,
                ReglaGuardada = reglaGuardada,
                Ensenar = ensenar,
                UltimoEntrenamiento = repositorio.UltimoEntrenamiento(),
                Sombra = new SombraModeloImpagosDTO(),
                SemanasEvaluadas = new List<SemanaFiabilidadImpagos>()
            };
            if (!repositorio.TablaPrediccionesExiste())
            {
                dto.Veredicto = "No existe la tabla de la sombra.";
                return dto;
            }
            CuentaSombraImpagos cuenta = repositorio.LeerCuentaSombra();
            dto.Sombra.Predicciones = cuenta.Predicciones;
            dto.Sombra.Pendientes = cuenta.Pendientes;
            dto.Sombra.PrimeraPrediccion = cuenta.PrimeraPrediccion;
            // Para la fiabilidad hace falta más atrás: cada semana se mide con la ventana que acaba en ella.
            DateTime desdeLectura = desdeVentana.AddDays(-7 * (ReglaFiabilidadImpagos.SEMANAS_VENTANA + ReglaFiabilidadImpagos.SEMANAS_SEGUIDAS)
                - ConstructorRasgosImpagos.DIAS_ETIQUETA);
            List<PrediccionResueltaImpago> resueltas = repositorio.LeerResueltas(desdeLectura);
            List<PrediccionResueltaImpago> deLaVentana = resueltas.Where(p => p.FechaGiro.Date >= desdeVentana).ToList();
            dto.Sombra.ResueltasConModelo = deLaVentana.Count(p => p.Probabilidad.HasValue);
            dto.Sombra.Modelo = CalculoMetricasImpagos.Calcular(deLaVentana.Where(p => p.Probabilidad.HasValue).Select(p => (p.Probabilidad.Value, p.Devuelto)));
            dto.Sombra.Regla = CalculoMetricasImpagos.Calcular(deLaVentana.Where(p => p.ProbabilidadRegla.HasValue).Select(p => (p.ProbabilidadRegla.Value, p.Devuelto)));

            FiabilidadImpagos fiabilidad = ReglaFiabilidadImpagos.Evaluar(resueltas, hoy, validas);
            dto.Fiable = fiabilidad.Fiable;
            dto.FiableDesde = fiabilidad.FiableDesde;
            dto.SemanasSeguidasCumpliendo = fiabilidad.SemanasSeguidas;
            dto.Veredicto = fiabilidad.Motivo;
            dto.SemanasEvaluadas = fiabilidad.Semanas;
            return dto;
        }
    }
}
