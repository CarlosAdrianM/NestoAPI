using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using Hangfire;
using NestoAPI.Infraestructure.Rapports;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada
{
    /// <summary>NestoAPI#881: una fila de dbo.RepartoTiemposModeloEntrenamientos (SIEMPRE se apunta, también «sin datos suficientes»).</summary>
    public class EntrenamientoModeloTiemposParada
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int Ejemplos { get; set; }
        public int EjemplosEntrenamiento { get; set; }
        public int EjemplosValidacion { get; set; }
        public DateTime? ValidacionDesde { get; set; }
        /// <summary>Error medio (minutos) de la regla de constantes en las paradas de validación.</summary>
        public double? ErrorMedioRegla { get; set; }
        public double? ErrorMedioModelo { get; set; }
        public int EjemplosConLlegada { get; set; }
        public double? ErrorMedioReglaLlegada { get; set; }
        public double? ErrorMedioModeloLlegada { get; set; }
        public string Veredicto { get; set; }
        public bool ModeloActivo { get; set; }
        public string Motivo { get; set; }
        public string Origen { get; set; }
        public string Version { get; set; }
        public int? Segundos { get; set; }
    }

    public interface IRepositorioModeloTiemposParada
    {
        bool TablaEntrenamientosExiste();
        bool TablaTiemposExiste();
        List<TiempoParadaReparto> LeerTiempos(DateTime desde);
        void RegistrarEntrenamiento(EntrenamientoModeloTiemposParada fila);
        EntrenamientoModeloTiemposParada UltimoActivo();
        /// <summary>Los últimos <paramref name="cuantos"/>, del más reciente al más antiguo.</summary>
        List<EntrenamientoModeloTiemposParada> Ultimos(int cuantos);
    }

    /// <summary>NestoAPI#881: <see cref="IRepositorioModeloTiemposParada"/> con ADO (fuera del EDMX).</summary>
    public class RepositorioModeloTiemposParada : IRepositorioModeloTiemposParada
    {
        public const string TABLA = "RepartoTiemposModeloEntrenamientos";
        private const int TIMEOUT = 30;

        private readonly string cadenaConexion;
        private readonly RepositorioTiemposReparto tiempos;

        public RepositorioModeloTiemposParada(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion ?? throw new ArgumentNullException(nameof(cadenaConexion));
            tiempos = new RepositorioTiemposReparto(cadenaConexion);
        }

        public static RepositorioModeloTiemposParada DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioModeloTiemposParada(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = "NestoAPI-ModeloTiemposParada" }.ConnectionString);
        }

        public bool TablaEntrenamientosExiste()
        {
            try
            {
                using (SqlConnection conexion = Abrir())
                using (var comando = new SqlCommand($"SELECT CASE WHEN OBJECT_ID('dbo.{TABLA}', 'U') IS NULL THEN 0 ELSE 1 END", conexion) { CommandTimeout = TIMEOUT })
                {
                    return Convert.ToInt32(comando.ExecuteScalar()) == 1;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TablaTiemposExiste() => tiempos.TablaTiemposExiste();

        public List<TiempoParadaReparto> LeerTiempos(DateTime desde) => tiempos.LeerTiempos(desde);

        internal const string SQL_INSERTAR = @"
IF @activo = 1
    UPDATE dbo." + TABLA + @" SET ModeloActivo = 0 WHERE ModeloActivo = 1;
INSERT INTO dbo." + TABLA + @" (Fecha, Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, ValidacionDesde, ErrorMedioRegla, ErrorMedioModelo,
    EjemplosConLlegada, ErrorMedioReglaLlegada, ErrorMedioModeloLlegada, Veredicto, ModeloActivo, Motivo, Origen, Version, Segundos)
VALUES (@fecha, @ejemplos, @entrenamiento, @validacion, @validacionDesde, @errorRegla, @errorModelo, @conLlegada, @errorReglaLlegada,
    @errorModeloLlegada, @veredicto, @activo, @motivo, @origen, @version, @segundos);";

        private const string COLUMNAS = "Id, Fecha, Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, CAST(ValidacionDesde AS datetime), ErrorMedioRegla, " +
            "ErrorMedioModelo, EjemplosConLlegada, ErrorMedioReglaLlegada, ErrorMedioModeloLlegada, Veredicto, ModeloActivo, Motivo, Origen, Version, Segundos";

        public void RegistrarEntrenamiento(EntrenamientoModeloTiemposParada f)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            using (var c = new SqlCommand(SQL_INSERTAR, conexion, transaccion) { CommandTimeout = TIMEOUT })
            {
                c.Parameters.Add("@fecha", SqlDbType.DateTime).Value = f.Fecha;
                c.Parameters.Add("@ejemplos", SqlDbType.Int).Value = f.Ejemplos;
                c.Parameters.Add("@entrenamiento", SqlDbType.Int).Value = f.EjemplosEntrenamiento;
                c.Parameters.Add("@validacion", SqlDbType.Int).Value = f.EjemplosValidacion;
                c.Parameters.Add("@validacionDesde", SqlDbType.Date).Value = (object)f.ValidacionDesde?.Date ?? DBNull.Value;
                Decimal(c, "@errorRegla", f.ErrorMedioRegla);
                Decimal(c, "@errorModelo", f.ErrorMedioModelo);
                c.Parameters.Add("@conLlegada", SqlDbType.Int).Value = f.EjemplosConLlegada;
                Decimal(c, "@errorReglaLlegada", f.ErrorMedioReglaLlegada);
                Decimal(c, "@errorModeloLlegada", f.ErrorMedioModeloLlegada);
                c.Parameters.Add("@veredicto", SqlDbType.NVarChar, 400).Value = (object)Recortar(f.Veredicto, 400) ?? DBNull.Value;
                c.Parameters.Add("@activo", SqlDbType.Bit).Value = f.ModeloActivo;
                c.Parameters.Add("@motivo", SqlDbType.NVarChar, 500).Value = (object)Recortar(f.Motivo, 500) ?? DBNull.Value;
                c.Parameters.Add("@origen", SqlDbType.NVarChar, 100).Value = (object)Recortar(f.Origen, 100) ?? DBNull.Value;
                c.Parameters.Add("@version", SqlDbType.VarChar, 20).Value = (object)Recortar(f.Version, 20) ?? DBNull.Value;
                c.Parameters.Add("@segundos", SqlDbType.Int).Value = (object)f.Segundos ?? DBNull.Value;
                _ = c.ExecuteNonQuery();
                transaccion.Commit();
            }
        }

        public EntrenamientoModeloTiemposParada UltimoActivo()
        {
            List<EntrenamientoModeloTiemposParada> filas = Leer($"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) WHERE ModeloActivo = 1 ORDER BY Id DESC");
            return filas.Count == 0 ? null : filas[0];
        }

        public List<EntrenamientoModeloTiemposParada> Ultimos(int cuantos)
            => Leer($"SELECT TOP ({Math.Max(1, Math.Min(cuantos, 365))}) {COLUMNAS} FROM dbo.{TABLA} WITH (NOLOCK) ORDER BY Id DESC");

        private List<EntrenamientoModeloTiemposParada> Leer(string sql)
        {
            var filas = new List<EntrenamientoModeloTiemposParada>();
            if (!TablaEntrenamientosExiste())
            {
                return filas;
            }
            using (SqlConnection conexion = Abrir())
            using (var c = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT })
            using (SqlDataReader l = c.ExecuteReader())
            {
                while (l.Read())
                {
                    filas.Add(new EntrenamientoModeloTiemposParada
                    {
                        Id = l.GetInt32(0),
                        Fecha = l.GetDateTime(1),
                        Ejemplos = l.GetInt32(2),
                        EjemplosEntrenamiento = l.GetInt32(3),
                        EjemplosValidacion = l.GetInt32(4),
                        ValidacionDesde = l.IsDBNull(5) ? (DateTime?)null : l.GetDateTime(5),
                        ErrorMedioRegla = Doble(l, 6),
                        ErrorMedioModelo = Doble(l, 7),
                        EjemplosConLlegada = l.GetInt32(8),
                        ErrorMedioReglaLlegada = Doble(l, 9),
                        ErrorMedioModeloLlegada = Doble(l, 10),
                        Veredicto = l.IsDBNull(11) ? null : l.GetString(11),
                        ModeloActivo = l.GetBoolean(12),
                        Motivo = l.IsDBNull(13) ? null : l.GetString(13),
                        Origen = l.IsDBNull(14) ? null : l.GetString(14),
                        Version = l.IsDBNull(15) ? null : l.GetString(15),
                        Segundos = l.IsDBNull(16) ? (int?)null : l.GetInt32(16)
                    });
                }
            }
            return filas;
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static void Decimal(SqlCommand c, string nombre, double? valor)
        {
            SqlParameter p = c.Parameters.Add(nombre, SqlDbType.Decimal);
            p.Precision = 9;
            p.Scale = 2;
            p.Value = valor.HasValue ? (object)Math.Round((decimal)valor.Value, 2) : DBNull.Value;
        }

        private static double? Doble(SqlDataReader l, int i) => l.IsDBNull(i) ? (double?)null : Convert.ToDouble(l.GetValue(i));

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }

    /// <summary>
    /// NestoAPI#881: la puerta de calidad del modelo de tiempos. PURA. Sin modelo guardado, pasa el primero con datos suficientes; con modelo,
    /// el nuevo solo lo sustituye si su error medio no es más de <see cref="TOLERANCIA_MINUTOS"/> minutos peor que el del guardado. Se guarda
    /// aunque la regla acierte más: así está listo para cuando empiece a acertar (el veredicto es el que dice quién gana).
    /// </summary>
    public static class PuertaCalidadTiemposParada
    {
        public const double TOLERANCIA_MINUTOS = 0.5;

        public static (bool Promover, string Motivo) Decidir(double? errorActivo, bool hayModeloGuardado, double? errorNuevo)
        {
            if (!errorNuevo.HasValue)
            {
                return (false, "sin error medido del modelo nuevo");
            }
            if (!hayModeloGuardado || !errorActivo.HasValue)
            {
                return (true, "no había modelo guardado");
            }
            return errorNuevo.Value <= errorActivo.Value + TOLERANCIA_MINUTOS + 1e-9
                ? (true, $"no empeora ({ConstructorRasgosTiemposParada.Num(errorActivo.Value)} → {ConstructorRasgosTiemposParada.Num(errorNuevo.Value)} min)")
                : (false, $"empeora ({ConstructorRasgosTiemposParada.Num(errorActivo.Value)} → {ConstructorRasgosTiemposParada.Num(errorNuevo.Value)} min)");
        }
    }

    /// <summary>
    /// NestoAPI#881 (corte 3): el entrenamiento nocturno del modelo de tiempos de parada EN SOMBRA (job <see cref="JOB_ID"/>, todos los días a
    /// las 4:25 de Madrid, cola «entrenamiento», un hilo compartido con los demás modelos). Lee <see cref="DIAS_HISTORICO"/> días de
    /// RepartoParadasTiempos, entrena con corte temporal, apunta SIEMPRE una fila en dbo.RepartoTiemposModeloEntrenamientos (también «sin
    /// datos suficientes») y solo guarda el modelo si pasa la puerta. El fin de ruta NO lo usa salvo con ParametrosUsuario «(defecto)»
    /// Reparto.TiempoParadaQuienManda = «Modelo». Sin las tablas no hace nada (ELMAH una vez). Nunca lanza.
    /// </summary>
    public class ModeloTiemposParadaJobsService
    {
        public const string JOB_ID = "reparto-tiempos-parada-modelo";
        public const string CRON = "25 4 * * *";
        public const string COLA = ReentrenamientoModeloLlamadasJobsService.COLA;
        public const int DIAS_HISTORICO = 365;
        internal const int HORA_LIMITE_PROGRAMADO = 7;
        public const string ORIGEN_PROGRAMADO = "job nocturno";
        public const string MENSAJE_SIN_TABLA = "Todavía no existen dbo.RepartoParadasTiempos y dbo.RepartoTiemposModeloEntrenamientos: hay que lanzar " +
            "Scripts/Issue881_RepartoFinRutaYTiempos.sql.";
        private const string USUARIO_ELMAH = "Sistema (modelo de tiempos de parada)";

        private static bool sinTablaAvisado;

        private readonly IRepositorioModeloTiemposParada repositorio;
        private readonly IEntrenadorModeloTiemposParada entrenador;
        private readonly AlmacenModeloTiemposParada almacen;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;
        private readonly Action asegurarNativo;
        private readonly Func<ConstantesTiemposReparto> constantes;

        public ModeloTiemposParadaJobsService(IRepositorioModeloTiemposParada repositorio = null, IEntrenadorModeloTiemposParada entrenador = null,
            AlmacenModeloTiemposParada almacen = null, Func<DateTime> ahora = null, Action<Exception> registrar = null, Action asegurarNativo = null,
            Func<ConstantesTiemposReparto> constantes = null)
        {
            this.repositorio = repositorio ?? RepositorioModeloTiemposParada.DesdeConfiguracion();
            this.entrenador = entrenador ?? new EntrenadorModeloTiemposParada();
            this.almacen = almacen ?? AlmacenModeloTiemposParada.Produccion();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
            this.asegurarNativo = asegurarNativo ?? (() => _ = CargadorLightGbmNativo.Asegurar());
            this.constantes = constantes ?? (() => new ConfiguracionTiemposRepartoParametros().Leer());
        }

        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void EntrenarProgramado()
        {
            if (!TocaEntrenar(DateTime.Now))
            {
                return; // nunca en caliente: si el job se retrasa hasta horario de trabajo, se salta ese día
            }
            _ = new ModeloTiemposParadaJobsService().Ejecutar(ORIGEN_PROGRAMADO);
        }

        internal static bool TocaEntrenar(DateTime ahora) => ahora.Hour < HORA_LIMITE_PROGRAMADO;

        public EntrenamientoModeloTiemposParada Ejecutar(string origen)
        {
            string temporal = null;
            try
            {
                if (!repositorio.TablaEntrenamientosExiste() || !repositorio.TablaTiemposExiste())
                {
                    if (!sinTablaAvisado)
                    {
                        sinTablaAvisado = true;
                        registrar(new Exception("[Modelo de tiempos de parada #881] " + MENSAJE_SIN_TABLA + " No se entrena (se avisa una vez)."));
                    }
                    return null;
                }
                sinTablaAvisado = false;

                DateTime inicio = ahora();
                var cronometro = Stopwatch.StartNew();
                List<EjemploTiempoParada> ejemplos = ConstructorRasgosTiemposParada.Construir(
                    repositorio.LeerTiempos(inicio.Date.AddDays(-DIAS_HISTORICO)), constantes());
                bool datosSuficientes = ejemplos.Count >= EntrenadorModeloTiemposParada.MINIMO_EJEMPLOS;
                if (datosSuficientes)
                {
                    asegurarNativo();
                }
                EvaluacionModeloTiemposParada evaluacion = entrenador.Evaluar(ejemplos, inicio.Date);
                var fila = new EntrenamientoModeloTiemposParada
                {
                    Fecha = inicio,
                    Ejemplos = evaluacion.Ejemplos,
                    EjemplosEntrenamiento = evaluacion.EjemplosEntrenamiento,
                    EjemplosValidacion = evaluacion.EjemplosValidacion,
                    ValidacionDesde = evaluacion.ValidacionDesde,
                    ErrorMedioRegla = evaluacion.ErrorMedioRegla,
                    ErrorMedioModelo = evaluacion.ErrorMedioModelo,
                    EjemplosConLlegada = evaluacion.EjemplosConLlegada,
                    ErrorMedioReglaLlegada = evaluacion.ErrorMedioReglaLlegada,
                    ErrorMedioModeloLlegada = evaluacion.ErrorMedioModeloLlegada,
                    Veredicto = evaluacion.Veredicto,
                    Origen = origen,
                    Version = MetadatosModeloTiemposParada.VERSION
                };
                if (!evaluacion.DatosSuficientes)
                {
                    fila.Motivo = "no se entrena: " + evaluacion.Motivo;
                }
                else
                {
                    EntrenamientoModeloTiemposParada activo = repositorio.UltimoActivo();
                    (bool promover, string motivo) = PuertaCalidadTiemposParada.Decidir(activo?.ErrorMedioModelo, almacen.HayModelo, evaluacion.ErrorMedioModelo);
                    if (promover && evaluacion.EntrenarFinalYGuardar != null)
                    {
                        temporal = almacen.NuevaRutaTemporal();
                        MetadatosModeloTiemposParada datos = evaluacion.EntrenarFinalYGuardar(temporal, ahora(), origen);
                        almacen.Promover(temporal, datos);
                        temporal = null;
                        fila.ModeloActivo = true;
                    }
                    fila.Motivo = (fila.ModeloActivo ? "guardado: " : "no guardado: ") + motivo;
                }
                fila.Segundos = (int)cronometro.Elapsed.TotalSeconds;
                repositorio.RegistrarEntrenamiento(fila);
                return fila;
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Modelo de tiempos de parada #881] Error al entrenar ({origen}): {ex.Message}. El modelo guardado no se toca.", ex));
                return null;
            }
            finally
            {
                almacen.Descartar(temporal);
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos() => sinTablaAvisado = false;
    }

    /// <summary>NestoAPI#881: GET api/RepartoPropio/TiemposParada/Comparacion.</summary>
    public class ComparacionTiemposParadaDTO
    {
        /// <summary>«Regla» o «Modelo» (ParametrosUsuario «(defecto)» Reparto.TiempoParadaQuienManda).</summary>
        public string QuienManda { get; set; }
        public bool ModeloGuardado { get; set; }
        /// <summary>False sin el script (entonces todo lo demás va vacío).</summary>
        public bool TablasCreadas { get; set; }
        /// <summary>El último entrenamiento (null si no hay ninguno).</summary>
        public EntrenamientoModeloTiemposParada UltimoEntrenamiento { get; set; }
        /// <summary>El del modelo guardado (null si no hay).</summary>
        public EntrenamientoModeloTiemposParada ModeloEnUso { get; set; }
        /// <summary>El veredicto del último entrenamiento (quién acierta más), listo para enseñar.</summary>
        public string Veredicto { get; set; }
        /// <summary>Los últimos entrenamientos (hasta 60), del más reciente al más antiguo: para ver cuándo empieza a ganar el modelo.</summary>
        public List<EntrenamientoModeloTiemposParada> Historia { get; set; } = new List<EntrenamientoModeloTiemposParada>();
    }
}
