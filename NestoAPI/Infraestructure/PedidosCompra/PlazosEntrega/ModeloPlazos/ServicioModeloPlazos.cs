using Hangfire;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos
{
    public class ResultadoEntrenamientoModeloPlazos
    {
        public bool Hecho { get; set; }
        public bool Promovido { get; set; }
        public string Mensaje { get; set; }
        public EntrenamientoModeloPlazos Fila { get; set; }
    }

    /// <summary>
    /// NestoAPI#853: entrenamiento nocturno del modelo de plazos en sombra (job <see cref="JOB_ID"/>, todos los días a las 4:10 de Madrid,
    /// cola «entrenamiento»: un hilo compartido con los modelos de llamadas y sugerencias y con los indicadores, así que nunca se solapa
    /// con el recálculo de plazos de las 3:40). Lee <see cref="ANIOS_HISTORICO"/> años, entrena con corte temporal
    /// (<see cref="EntrenadorModeloPlazos"/>), apunta SIEMPRE las métricas de validación de modelo y regla en
    /// dbo.PlazosProveedoresModeloEntrenamientos y solo cambia el modelo guardado si pasa <see cref="PuertaCalidadPlazos"/>. Sin la tabla
    /// no hace nada (ELMAH una vez). Nunca lanza.
    /// </summary>
    public class ModeloPlazosProveedoresJobsService
    {
        public const string JOB_ID = "plazos-proveedores-modelo";
        public const string CRON = "10 4 * * *";
        public const string COLA = ReentrenamientoModeloLlamadasJobsService.COLA;
        public const int ANIOS_HISTORICO = 5;
        /// <summary>El programado que arranque a esta hora o más tarde no entrena (sería en horario).</summary>
        internal const int HORA_LIMITE_PROGRAMADO = 7;
        public const string ORIGEN_PROGRAMADO = "job nocturno";
        public const string MENSAJE_SIN_TABLA = "Todavía no existe dbo.PlazosProveedoresModeloEntrenamientos: hay que lanzar Scripts/Issue853_PlazosProveedoresYEnvios.sql.";
        private const string USUARIO_ELMAH = "Sistema (modelo de plazos de proveedores)";

        private static bool sinTablaAvisado;

        private readonly IRepositorioModeloPlazos repositorio;
        private readonly IEntrenadorModeloPlazos entrenador;
        private readonly AlmacenModeloPlazos almacen;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;
        private readonly Action asegurarNativo;
        private readonly int aniosHistorico;

        public ModeloPlazosProveedoresJobsService(IRepositorioModeloPlazos repositorio = null, IEntrenadorModeloPlazos entrenador = null,
            AlmacenModeloPlazos almacen = null, Func<DateTime> ahora = null, Action<Exception> registrar = null, Action asegurarNativo = null,
            int aniosHistorico = ANIOS_HISTORICO)
        {
            this.repositorio = repositorio ?? RepositorioModeloPlazos.DesdeConfiguracion();
            this.entrenador = entrenador ?? new EntrenadorModeloPlazos();
            this.almacen = almacen ?? AlmacenModeloPlazos.Produccion();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
            this.aniosHistorico = aniosHistorico;
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
            _ = new ModeloPlazosProveedoresJobsService().Ejecutar(ORIGEN_PROGRAMADO);
        }

        /// <summary>Lo que encola (o programa para la noche) POST api/Compras/PlazosProveedores/Modelo/Entrenar.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void EntrenarManual(string usuario)
        {
            _ = new ModeloPlazosProveedoresJobsService().Ejecutar(string.IsNullOrWhiteSpace(usuario) ? "a mano" : $"a mano ({usuario})");
        }

        internal static bool TocaEntrenar(DateTime ahora) => ahora.Hour < HORA_LIMITE_PROGRAMADO;

        public ResultadoEntrenamientoModeloPlazos Ejecutar(string origen)
        {
            string temporal = null;
            try
            {
                if (!repositorio.TablaEntrenamientosExiste())
                {
                    if (!sinTablaAvisado)
                    {
                        sinTablaAvisado = true;
                        registrar(new Exception("[Modelo de plazos #853] " + MENSAJE_SIN_TABLA + " No se entrena (se avisa una vez)."));
                    }
                    return new ResultadoEntrenamientoModeloPlazos { Hecho = false, Mensaje = MENSAJE_SIN_TABLA };
                }
                sinTablaAvisado = false;

                DateTime inicio = ahora();
                var cronometro = Stopwatch.StartNew();
                asegurarNativo();
                List<PedidoCompraHistorico> historico = repositorio.LeerHistorico(Constantes.Empresas.EMPRESA_POR_DEFECTO, inicio.Date.AddYears(-aniosHistorico));
                IEvaluacionModeloPlazos evaluacion = entrenador.Evaluar(historico, inicio.Date);

                var fila = new EntrenamientoModeloPlazos
                {
                    Fecha = inicio,
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Ejemplos = evaluacion.Ejemplos,
                    EjemplosEntrenamiento = evaluacion.EjemplosEntrenamiento,
                    EjemplosValidacion = evaluacion.EjemplosValidacion,
                    ValidacionDesde = evaluacion.ValidacionDesde,
                    Version = MetadatosModeloPlazos.VERSION,
                    Algoritmo = MetadatosModeloPlazos.ALGORITMO,
                    Origen = origen
                };
                bool promovido = false;
                if (!evaluacion.DatosSuficientes)
                {
                    fila.Motivo = "no se entrena: " + evaluacion.Motivo;
                }
                else
                {
                    fila.PedidosComparados = evaluacion.Modelo.Pedidos;
                    fila.ErrorMedioModelo = evaluacion.Modelo.ErrorMedio;
                    fila.PorcentajeCortoModelo = evaluacion.Modelo.PorcentajeCorto;
                    fila.DiasDeMasModelo = evaluacion.Modelo.DiasDeMasMedio;
                    fila.ErrorMedioRegla = evaluacion.Regla.ErrorMedio;
                    fila.PorcentajeCortoRegla = evaluacion.Regla.PorcentajeCorto;
                    fila.DiasDeMasRegla = evaluacion.Regla.DiasDeMasMedio;
                    fila.MargenLog = Math.Round(evaluacion.MargenLog, 6);

                    EntrenamientoModeloPlazos activo = repositorio.UltimoActivo();
                    DecisionPuertaPlazos decision = PuertaCalidadPlazos.Decidir(activo?.MetricasModelo(), almacen.HayModelo, evaluacion.Modelo);
                    if (decision.Promover)
                    {
                        temporal = almacen.NuevaRutaTemporal();
                        MetadatosModeloPlazos datos = evaluacion.EntrenarFinalYGuardar(temporal, ahora(), origen);
                        almacen.Promover(temporal, datos);
                        temporal = null;
                        promovido = true;
                    }
                    fila.ModeloActivo = promovido;
                    fila.Motivo = (promovido ? "promovido: " : "no promovido: ") + decision.Motivo;
                }
                fila.Segundos = (int)cronometro.Elapsed.TotalSeconds;
                repositorio.RegistrarEntrenamiento(fila);
                return new ResultadoEntrenamientoModeloPlazos { Hecho = true, Promovido = promovido, Mensaje = fila.Motivo, Fila = fila };
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Modelo de plazos #853] Error al entrenar ({origen}): {ex.Message}. El modelo guardado no se toca.", ex));
                return new ResultadoEntrenamientoModeloPlazos { Hecho = false, Mensaje = ex.Message };
            }
            finally
            {
                almacen.Descartar(temporal);
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos() => sinTablaAvisado = false;
    }

    /// <summary>NestoAPI#853: cómo se lanza a mano el entrenamiento (sustituible en los tests).</summary>
    public interface IProgramadorModeloPlazos
    {
        string Encolar(string usuario);
        string Programar(DateTime cuandoMadrid, string usuario);
    }

    public class ProgramadorModeloPlazosHangfire : IProgramadorModeloPlazos
    {
        public string Encolar(string usuario) => BackgroundJob.Enqueue(() => ModeloPlazosProveedoresJobsService.EntrenarManual(usuario));

        public string Programar(DateTime cuandoMadrid, string usuario)
        {
            DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cuandoMadrid, DateTimeKind.Unspecified), Startup.ZonaHorariaPeninsular());
            return BackgroundJob.Schedule(() => ModeloPlazosProveedoresJobsService.EntrenarManual(usuario), new DateTimeOffset(utc, TimeSpan.Zero));
        }
    }

    /// <summary>Respuesta de GET api/Compras/PlazosProveedores/Comparacion.</summary>
    public class ComparacionModeloPlazosDTO
    {
        public int Meses { get; set; }
        /// <summary>Hay modelo guardado (y se rellena FechaModelo al enviar).</summary>
        public bool ModeloGuardado { get; set; }
        /// <summary>«Regla» o «Modelo» (ParametrosUsuario Compras.FechaRecepcionQuienManda).</summary>
        public string QuienManda { get; set; }
        /// <summary>El último entrenamiento (métricas de VALIDACIÓN de modelo y regla). Null si no hay ninguno.</summary>
        public EntrenamientoModeloPlazos UltimoEntrenamiento { get; set; }
        /// <summary>Lo medido de verdad en los pedidos enviados con las dos fechas y ya recibidos.</summary>
        public ComparacionRealPlazos Real { get; set; }
    }

    public class LanzamientoModeloPlazosDTO
    {
        public string JobId { get; set; }
        public bool Programado { get; set; }
        public string Mensaje { get; set; }
    }
}
