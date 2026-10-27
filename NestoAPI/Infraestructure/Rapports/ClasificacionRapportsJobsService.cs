using Hangfire;
using NestoAPI.Infraestructure.OpenAI;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>Lo que el job necesita de la BD (sustituible en los tests).</summary>
    public interface IRepositorioClasificacionRapports
    {
        /// <summary>False si Scripts/Issue716 no se ha lanzado (sin tabla no se hace nada).</summary>
        bool TablaDisponible();
        /// <summary>Rapports vigentes con comentario, de <paramref name="desde"/> (incluida) a <paramref name="hasta"/> (excluida), sin clasificar, los más nuevos primero.</summary>
        List<RapportAClasificar> LeerPendientes(DateTime desde, DateTime hasta, int maximo);
        void Guardar(IEnumerable<ClasificacionRapport> clasificaciones);
    }

    /// <summary>Interruptores de Web.config (appSettings), leídos en cada ejecución.</summary>
    public class OpcionesClasificacionRapports
    {
        /// <summary>Rapports:ClasificacionOpenAI (por defecto true): clasificar los rapports del día anterior y de hoy.</summary>
        public bool Activo { get; set; } = true;
        /// <summary>Tope de rapports por noche en el modo normal (un día son ~150).</summary>
        public int MaximoDiario { get; set; } = 1000;
        /// <summary>Rapports:ClasificacionBackfill (por defecto FALSE): además, rellenar hacia atrás por tandas.</summary>
        public bool Backfill { get; set; }
        /// <summary>Rapports:ClasificacionBackfillDesde (por defecto 2024-01-01).</summary>
        public DateTime BackfillDesde { get; set; } = new DateTime(2024, 1, 1);
        /// <summary>Rapports:ClasificacionBackfillPorNoche (por defecto 3000 ≈ 150 llamadas, unos 10-15 minutos).</summary>
        public int BackfillPorNoche { get; set; } = 3000;

        public static OpcionesClasificacionRapports LeerDeConfiguracion()
        {
            var opciones = new OpcionesClasificacionRapports();
            string activo = ConfigurationManager.AppSettings["Rapports:ClasificacionOpenAI"];
            if (!string.IsNullOrWhiteSpace(activo))
            {
                opciones.Activo = !string.Equals(activo.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }
            opciones.Backfill = string.Equals(ConfigurationManager.AppSettings["Rapports:ClasificacionBackfill"]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            if (DateTime.TryParseExact(ConfigurationManager.AppSettings["Rapports:ClasificacionBackfillDesde"], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime desde))
            {
                opciones.BackfillDesde = desde;
            }
            if (int.TryParse(ConfigurationManager.AppSettings["Rapports:ClasificacionBackfillPorNoche"], out int porNoche) && porNoche > 0)
            {
                opciones.BackfillPorNoche = porNoche;
            }
            return opciones;
        }
    }

    public class ResultadoClasificacionRapports
    {
        public int Leidos { get; set; }
        public int Lotes { get; set; }
        public int Guardados { get; set; }
        public int LotesFallidos { get; set; }
        /// <summary>Por qué no se ha hecho nada (null si ha corrido).</summary>
        public string MotivoSalida { get; set; }
    }

    /// <summary>
    /// NestoAPI#716 (2.1, interino): job «clasificar-rapports-nocturno», todos los días a las 23:15, fuera de horario
    /// (regla «Entrenar ML fuera de horario»): clasifica con OpenAI los comentarios de los rapports de ayer y de hoy y
    /// guarda el resultado en SeguimientoClienteClasificacion. Con Rapports:ClasificacionBackfill = true (por defecto
    /// apagado) rellena además hacia atrás, los más nuevos primero, BackfillPorNoche rapports por noche hasta
    /// BackfillDesde. Coste (gpt-4o-mini, lotes de 20): ~0,003 USD por 100 rapports; el histórico 2024-2026 (~93.000
    /// rapports con comentario) sale por unos 3 USD en total.
    /// Sin la tabla (script sin lanzar), sin OpenAIKey o con el interruptor apagado no hace nada. Nunca rompe (ELMAH).
    /// </summary>
    public class ClasificacionRapportsJobsService
    {
        public const string ID_JOB = "clasificar-rapports-nocturno";
        /// <summary>Todos los días a las 23:15: cerrada la jornada de los vendedores, antes de los jobs de la 1:00.</summary>
        public const string CRON = "15 23 * * *";
        /// <summary>Tras tantos lotes seguidos sin respuesta válida, se para: OpenAI no está para hablar.</summary>
        public const int MAXIMO_FALLOS_SEGUIDOS = 3;
        private const string USUARIO_ELMAH = "Sistema (clasificación de rapports)";

        private readonly IRepositorioClasificacionRapports repositorio;
        private readonly Func<IServicioOpenAI> crearOpenAI;

        public ClasificacionRapportsJobsService() : this(new RepositorioClasificacionRapportsSql(), ServicioFrasesRitmo.CrearOpenAISiHayClave)
        {
        }

        internal ClasificacionRapportsJobsService(IRepositorioClasificacionRapports repositorio, Func<IServicioOpenAI> crearOpenAI)
        {
            this.repositorio = repositorio;
            this.crearOpenAI = crearOpenAI ?? (() => null);
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 60)]
        public static void Procesar()
        {
            try
            {
                ResultadoClasificacionRapports resultado = new ClasificacionRapportsJobsService()
                    .Ejecutar(DateTime.Now, OpcionesClasificacionRapports.LeerDeConfiguracion()).GetAwaiter().GetResult();
                Console.WriteLine(resultado.MotivoSalida != null
                    ? $"[{ID_JOB}] Sin hacer nada: {resultado.MotivoSalida}"
                    : $"[{ID_JOB}] {resultado.Leidos} rapports leídos, {resultado.Lotes} lotes, {resultado.Guardados} clasificaciones guardadas, {resultado.LotesFallidos} lotes fallidos");
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Rapports #716] El job {ID_JOB} ha fallado: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }

        /// <summary>
        /// Solo de noche: de 21:00 a 06:59. Hangfire lanza la ejecución perdida al arrancar (p. ej. tras un despliegue a las
        /// 10:00) y entonces hay que esperar a la noche siguiente.
        /// </summary>
        public static bool EsHorarioPermitido(DateTime ahora)
        {
            return ahora.Hour >= 21 || ahora.Hour < 7;
        }

        /// <summary>La ventana del modo normal: desde ayer a las 0:00 hasta mañana a las 0:00 (ayer por si el job de ayer falló).</summary>
        public static (DateTime desde, DateTime hasta) VentanaDiaria(DateTime ahora)
        {
            return (ahora.Date.AddDays(-1), ahora.Date.AddDays(1));
        }

        public async Task<ResultadoClasificacionRapports> Ejecutar(DateTime ahora, OpcionesClasificacionRapports opciones)
        {
            var resultado = new ResultadoClasificacionRapports();
            opciones = opciones ?? new OpcionesClasificacionRapports();
            if (!opciones.Activo)
            {
                resultado.MotivoSalida = "Rapports:ClasificacionOpenAI está apagado";
                return resultado;
            }
            if (!EsHorarioPermitido(ahora))
            {
                resultado.MotivoSalida = $"son las {ahora:HH:mm}: solo se clasifica de noche (21:00-07:00)";
                return resultado;
            }
            if (!repositorio.TablaDisponible())
            {
                resultado.MotivoSalida = "no existe SeguimientoClienteClasificacion (Scripts/Issue716 sin lanzar)";
                return resultado;
            }
            IServicioOpenAI openAI = crearOpenAI();
            if (openAI == null)
            {
                resultado.MotivoSalida = "no hay OpenAIKey";
                return resultado;
            }

            (DateTime desde, DateTime hasta) = VentanaDiaria(ahora);
            var pendientes = new List<RapportAClasificar>(repositorio.LeerPendientes(desde, hasta, opciones.MaximoDiario) ?? new List<RapportAClasificar>());
            if (opciones.Backfill && opciones.BackfillDesde < desde)
            {
                pendientes.AddRange(repositorio.LeerPendientes(opciones.BackfillDesde, desde, opciones.BackfillPorNoche) ?? new List<RapportAClasificar>());
            }
            resultado.Leidos = pendientes.Count;

            int fallosSeguidos = 0;
            foreach (List<RapportAClasificar> lote in ClasificadorRapports.Lotes(pendientes))
            {
                resultado.Lotes++;
                List<ClasificacionRapport> clasificaciones = await ClasificarLote(openAI, lote).ConfigureAwait(false);
                if (clasificaciones.Count == 0)
                {
                    resultado.LotesFallidos++;
                    if (++fallosSeguidos >= MAXIMO_FALLOS_SEGUIDOS)
                    {
                        ElmahHelper.Log(new Exception($"[Rapports #716] {MAXIMO_FALLOS_SEGUIDOS} lotes seguidos sin clasificación válida: se para hasta mañana."), USUARIO_ELMAH);
                        break;
                    }
                    continue;
                }
                fallosSeguidos = 0;
                try
                {
                    repositorio.Guardar(clasificaciones);
                    resultado.Guardados += clasificaciones.Count;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Rapports #716] No se ha podido guardar un lote de {clasificaciones.Count} clasificaciones: {ex.Message}", ex), USUARIO_ELMAH);
                    resultado.LotesFallidos++;
                }
            }
            return resultado;
        }

        private static async Task<List<ClasificacionRapport>> ClasificarLote(IServicioOpenAI openAI, List<RapportAClasificar> lote)
        {
            try
            {
                string respuesta = await openAI.GenerarContenidoAsync(ClasificadorRapports.PROMPT_SISTEMA, ClasificadorRapports.ConstruirMensaje(lote),
                    maxTokens: ClasificadorRapports.MAX_TOKENS_RESPUESTA, temperature: 0.0, modelo: ClasificadorRapports.MODELO).ConfigureAwait(false);
                return ClasificadorRapports.ParsearRespuesta(respuesta, lote);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Rapports #716] OpenAI ha fallado con un lote de {lote.Count} rapports: {ex.Message}", ex), USUARIO_ELMAH);
                return new List<ClasificacionRapport>();
            }
        }
    }

    /// <summary>SeguimientoCliente y SeguimientoClienteClasificacion con SqlClient (como FrasesRitmo).</summary>
    public class RepositorioClasificacionRapportsSql : IRepositorioClasificacionRapports
    {
        internal const string SQL_PENDIENTES = @"
            SELECT TOP (@Maximo) s.NºOrden, s.Fecha, s.Comentarios
            FROM dbo.SeguimientoCliente s WITH (NOLOCK)
            WHERE s.Fecha >= @Desde AND s.Fecha < @Hasta AND s.Estado = 0
              AND s.Comentarios IS NOT NULL AND LEN(s.Comentarios) >= 3
              AND NOT EXISTS (SELECT 1 FROM dbo.SeguimientoClienteClasificacion c WITH (NOLOCK) WHERE c.NºOrden = s.NºOrden)
            ORDER BY s.Fecha DESC, s.NºOrden DESC;";

        internal const string SQL_GUARDAR = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.SeguimientoClienteClasificacion WHERE NºOrden = @NumOrden)
                INSERT INTO dbo.SeguimientoClienteClasificacion (NºOrden, Resultado, ProximoContacto, Confianza, Modelo, Fecha)
                VALUES (@NumOrden, @Resultado, @ProximoContacto, @Confianza, @Modelo, GETDATE());";

        private const int TIMEOUT_SEGUNDOS = 60;

        public bool TablaDisponible()
        {
            try
            {
                using (SqlConnection conexion = ConexionSql.Abrir())
                using (var comando = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.SeguimientoClienteClasificacion') IS NOT NULL THEN 1 ELSE 0 END", conexion))
                {
                    return Convert.ToInt32(comando.ExecuteScalar()) == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        public List<RapportAClasificar> LeerPendientes(DateTime desde, DateTime hasta, int maximo)
        {
            var lista = new List<RapportAClasificar>();
            using (SqlConnection conexion = ConexionSql.Abrir())
            using (var comando = new SqlCommand(SQL_PENDIENTES, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add(new SqlParameter("@Maximo", SqlDbType.Int) { Value = maximo });
                _ = comando.Parameters.Add(new SqlParameter("@Desde", SqlDbType.DateTime) { Value = desde });
                _ = comando.Parameters.Add(new SqlParameter("@Hasta", SqlDbType.DateTime) { Value = hasta });
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        lista.Add(new RapportAClasificar
                        {
                            NumOrden = lector.GetInt32(0),
                            Fecha = lector.GetDateTime(1),
                            Comentarios = lector.IsDBNull(2) ? null : lector.GetString(2)
                        });
                    }
                }
            }
            return lista;
        }

        public void Guardar(IEnumerable<ClasificacionRapport> clasificaciones)
        {
            using (SqlConnection conexion = ConexionSql.Abrir())
            {
                foreach (ClasificacionRapport c in clasificaciones)
                {
                    using (var comando = new SqlCommand(SQL_GUARDAR, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        _ = comando.Parameters.Add(new SqlParameter("@NumOrden", SqlDbType.Int) { Value = c.NumOrden });
                        _ = comando.Parameters.Add(new SqlParameter("@Resultado", SqlDbType.VarChar, ResultadoRapport.LONGITUD_MAXIMA) { Value = c.Resultado });
                        _ = comando.Parameters.Add(new SqlParameter("@ProximoContacto", SqlDbType.Date) { Value = c.ProximoContacto.HasValue ? (object)c.ProximoContacto.Value : DBNull.Value });
                        _ = comando.Parameters.Add(new SqlParameter("@Confianza", SqlDbType.Decimal) { Precision = 4, Scale = 3, Value = c.Confianza });
                        _ = comando.Parameters.Add(new SqlParameter("@Modelo", SqlDbType.VarChar, 40) { Value = c.Modelo ?? ClasificadorRapports.MODELO });
                        _ = comando.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
