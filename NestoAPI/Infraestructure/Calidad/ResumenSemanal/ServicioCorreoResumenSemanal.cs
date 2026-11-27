using Hangfire;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Infraestructure.Calidad.Tablero;
using NestoAPI.Models;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net.Mail;
using System.Text;
using System.Threading;

namespace NestoAPI.Infraestructure.Calidad.ResumenSemanal
{
    public enum EstadoCorreoResumenSemanal
    {
        /// <summary>Ensayo: no se ha mandado nada.</summary>
        Ensayo,
        Enviado,
        /// <summary>Esa semana ya se mandó (idempotente).</summary>
        YaEnviado,
        /// <summary>No hay destinatario en ParametrosUsuario «(defecto)» Calidad.CorreoResumenSemanal (o no es UNA dirección válida).</summary>
        SinDestinatario,
        /// <summary>No se ha podido mirar si ya se mandó: no se manda (mejor uno de menos que dos).</summary>
        SinPoderComprobar,
        /// <summary>El SMTP no lo ha aceptado.</summary>
        FalloEnvio,
        /// <summary>Job: el tablero aún no tiene la semana; se reintenta por la noche.</summary>
        Aplazado
    }

    /// <summary>Respuesta de POST api/Calidad/ResumenSemanal/Enviar.</summary>
    public class ResultadoCorreoResumenSemanalDTO
    {
        public DateTime Semana { get; set; }
        public bool Ensayo { get; set; }
        public bool Enviado { get; set; }
        public string Estado { get; set; }
        /// <summary>El destinatario configurado (null si no hay).</summary>
        public string Destinatario { get; set; }
        /// <summary>Cuándo se mandó esa semana (si ya se mandó).</summary>
        public DateTime? YaEnviadoEl { get; set; }
        public string Asunto { get; set; }
        public string Html { get; set; }
        public string Mensaje { get; set; }

        internal EstadoCorreoResumenSemanal EstadoEnum { get; set; }
    }

    /// <summary>Dónde se apunta que una semana ya se mandó: dbo.Modificaciones (la tabla de rastro de siempre; nada nuevo que crear).</summary>
    public interface IRegistroCorreoResumenSemanal
    {
        /// <summary>Cuándo se mandó el correo de esa semana (null si no se ha mandado).</summary>
        DateTime? EnviadoEl(DateTime lunes);
        void Apuntar(DateTime lunes, string destinatario, string asunto, string usuario);
    }

    public class RegistroCorreoResumenSemanalSql : IRegistroCorreoResumenSemanal
    {
        public const string TABLA = "CalidadCorreoResumenSemanal";
        internal const int TIMEOUT_SEGUNDOS = 30;

        // Fecha >= el lunes siguiente: no se puede haber mandado antes de que acabe la semana (acota la lectura de Modificaciones).
        internal const string SQL_ENVIADO = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT TOP 1 m.Fecha FROM Modificaciones m WITH (NOLOCK)
WHERE m.Tabla = @Tabla AND m.Fecha >= @Desde AND m.Anterior = @Clave
ORDER BY m.Fecha;";

        internal const string SQL_APUNTAR = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Clave, @Nuevo, @Usuario);";

        private readonly string cadenaConexion;

        public RegistroCorreoResumenSemanalSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        public static RegistroCorreoResumenSemanalSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"]
                ?? throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            return new RegistroCorreoResumenSemanalSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI (correo semanal de calidad #914)"
            }.ConnectionString);
        }

        /// <summary>«Semana 2026-11-23»: lo que va en Modificaciones.Anterior.</summary>
        public static string Clave(DateTime lunes) => "Semana " + SemanaKpi.LunesDe(lunes).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        public DateTime? EnviadoEl(DateTime lunes)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_ENVIADO, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                _ = comando.Parameters.Add("@Tabla", SqlDbType.NVarChar, 50).Value = TABLA;
                _ = comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = SemanaKpi.LunesDe(lunes).AddDays(7);
                _ = comando.Parameters.Add("@Clave", SqlDbType.NVarChar, 100).Value = Clave(lunes);
                conexion.Open();
                object fecha = comando.ExecuteScalar();
                return fecha == null || fecha == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(fecha);
            }
        }

        public void Apuntar(DateTime lunes, string destinatario, string asunto, string usuario)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_APUNTAR, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                string u = string.IsNullOrWhiteSpace(usuario) ? CorreoResumenSemanalJobsService.USUARIO : usuario.Trim();
                _ = comando.Parameters.Add("@Tabla", SqlDbType.NVarChar, 50).Value = TABLA;
                _ = comando.Parameters.Add("@Clave", SqlDbType.NVarChar, 100).Value = Clave(lunes);
                _ = comando.Parameters.Add("@Nuevo", SqlDbType.NVarChar, -1).Value = Newtonsoft.Json.JsonConvert.SerializeObject(new { Destinatario = destinatario, Asunto = asunto });
                _ = comando.Parameters.Add("@Usuario", SqlDbType.NVarChar, 50).Value = u.Length > 50 ? u.Substring(0, 50) : u;
                conexion.Open();
                _ = comando.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// NestoAPI#914: el correo semanal de calidad (solo a Carlos). NO calcula nada pesado: lee lo guardado del tablero y de las encuestas en
    /// sombra y, de Almacén, el servicio con su caché (como el endpoint) para la semana cerrada. Una parte que falla sale «no disponible: …» y
    /// el resto sale igual. Idempotente por semana (dbo.Modificaciones).
    /// </summary>
    public class ServicioCorreoResumenSemanal
    {
        public const string REMITENTE = "nesto@nuevavision.es";
        public const string NOMBRE_REMITENTE = "Nesto - Nueva Visión";
        private const string PREFIJO = "[Correo de calidad #914]";

        private readonly Func<IRepositorioTablero> tablero;
        private readonly Func<IServicioCalidadAlmacen> almacen;
        private readonly Func<IServicioEncuestas> encuestas;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IServicioCorreoElectronico correo;
        private readonly Func<IRegistroCorreoResumenSemanal> registro;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;

        private static int avisadoSinDestinatario;

        public ServicioCorreoResumenSemanal(Func<IRepositorioTablero> tablero = null, Func<IServicioCalidadAlmacen> almacen = null,
            Func<IServicioEncuestas> encuestas = null, ILectorParametrosUsuario parametros = null, IServicioCorreoElectronico correo = null,
            Func<IRegistroCorreoResumenSemanal> registro = null, Func<DateTime> ahora = null, Action<Exception> registrar = null)
        {
            this.ahora = ahora ?? FuentesKpiSql.AhoraMadrid;
            this.tablero = tablero ?? (() => RepositorioTableroSql.DesdeConfiguracion());
            this.almacen = almacen ?? (() => new ServicioCalidadAlmacen(ahora: this.ahora));
            this.encuestas = encuestas ?? (() => new ServicioEncuestas(ahora: this.ahora));
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.registro = registro ?? (() => RegistroCorreoResumenSemanalSql.DesdeConfiguracion());
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, CorreoResumenSemanalJobsService.USUARIO));
        }

        // ---------------------------------------------------------------- Destinatario

        /// <summary>
        /// La dirección de ParametrosUsuario «(defecto)» Calidad.CorreoResumenSemanal (leerla no crea la fila). Null si no hay fila, está vacía o no
        /// es UNA dirección válida (con coma o punto y coma, no: es un correo para una sola persona).
        /// </summary>
        public string Destinatario()
        {
            string valor;
            try
            {
                valor = parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} No se ha podido leer el parámetro {ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO}: {ex.Message}", ex));
                return null;
            }
            return DireccionValida(valor);
        }

        /// <summary>La dirección si es UNA válida; null si no.</summary>
        internal static string DireccionValida(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor) || valor.IndexOfAny(new[] { ',', ';' }) >= 0)
            {
                return null;
            }
            try
            {
                var direccion = new MailAddress(valor.Trim());
                return direccion.Address;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- Lectura (cada parte por su lado)

        public DatosResumenSemanal Reunir(DateTime semana)
        {
            DateTime lunes = SemanaKpi.LunesDe(semana);
            DateTime momento = ahora();
            var datos = new DatosResumenSemanal { Semana = lunes, Hoy = momento.Date, GeneradoEl = momento };

            try
            {
                IRepositorioTablero repo = tablero();
                if (!repo.TablasExisten())
                {
                    datos.TableroSinTablas = true;
                    datos.TableroNoDisponible = TableroKpiJobsService.MENSAJE_SIN_TABLAS;
                }
                else
                {
                    var t = new DatosTableroResumen();
                    t.Semanas = repo.LeerSemanas(null, null, null, ReglaCorreoResumenSemanal.DesdeLectura(t.Catalogo, lunes, datos.Hoy), lunes.AddDays(7));
                    t.Umbrales = repo.LeerUmbrales();
                    datos.Tablero = t;
                }
            }
            catch (Exception ex)
            {
                datos.Tablero = null;
                datos.TableroNoDisponible = "no se ha podido leer el tablero (" + ex.Message + ").";
                registrar(new Exception($"{PREFIJO} Tablero: {ex.Message}", ex));
            }

            try
            {
                datos.Almacen = almacen().Resumen(Constantes.Empresas.EMPRESA_POR_DEFECTO, lunes, lunes.AddDays(7));
            }
            catch (Exception ex)
            {
                datos.AlmacenNoDisponible = "no se han podido calcular los indicadores de Almacén (" + ex.Message + ").";
                registrar(new Exception($"{PREFIJO} Almacén: {ex.Message}", ex));
            }

            IServicioEncuestas servicioEncuestas = null;
            try
            {
                servicioEncuestas = encuestas();
                datos.EncuestasSombraEncendida = servicioEncuestas.SombraEncendida();
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} Interruptor de las encuestas: {ex.Message}", ex));
            }
            try
            {
                datos.Encuestas = (servicioEncuestas ?? encuestas()).SombraEntre(Constantes.Empresas.EMPRESA_POR_DEFECTO, lunes, lunes.AddDays(7));
            }
            catch (EncuestasSinTablasException ex)
            {
                datos.EncuestasSinTablas = true;
                datos.EncuestasNoDisponible = ex.Message;
            }
            catch (Exception ex)
            {
                datos.EncuestasNoDisponible = "no se han podido leer las encuestas en sombra (" + ex.Message + ").";
                registrar(new Exception($"{PREFIJO} Encuestas: {ex.Message}", ex));
            }
            return datos;
        }

        // ---------------------------------------------------------------- Componer, ensayar y mandar

        /// <summary>
        /// Con <paramref name="ensayo"/> no manda nada: devuelve el asunto y el HTML. Sin ensayo, lo manda SOLO al destinatario configurado si esa
        /// semana no se ha mandado ya, y lo apunta. Con <paramref name="aplazarSiFaltaTablero"/> (el job), si el tablero aún no tiene la semana,
        /// no manda y devuelve <see cref="EstadoCorreoResumenSemanal.Aplazado"/>. Nunca lanza.
        /// </summary>
        public ResultadoCorreoResumenSemanalDTO Ejecutar(DateTime semana, bool ensayo, string usuario, bool aplazarSiFaltaTablero = false)
        {
            DateTime lunes = SemanaKpi.LunesDe(semana);
            var resultado = new ResultadoCorreoResumenSemanalDTO { Semana = lunes, Ensayo = ensayo };
            try
            {
                resultado.Destinatario = Destinatario();
                if (!ensayo && resultado.Destinatario == null)
                {
                    AvisarSinDestinatario();
                    return Con(resultado, EstadoCorreoResumenSemanal.SinDestinatario,
                        $"No se ha mandado: falta el destinatario. Ponga UNA dirección en ParametrosUsuario (empresa 1, «(defecto)») {ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO} " +
                        "(la fila la crea Scripts/Issue914r_CalidadCorreoResumen.sql, vacía).");
                }

                try
                {
                    resultado.YaEnviadoEl = registro().EnviadoEl(lunes);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"{PREFIJO} No se ha podido mirar si la semana del {lunes:dd/MM/yyyy} ya se mandó: {ex.Message}", ex));
                    if (!ensayo)
                    {
                        return Con(resultado, EstadoCorreoResumenSemanal.SinPoderComprobar,
                            "No se ha mandado: no se ha podido comprobar si esta semana ya se había mandado (para no mandarlo dos veces). Inténtelo más tarde.");
                    }
                }
                if (!ensayo && resultado.YaEnviadoEl.HasValue)
                {
                    return Con(resultado, EstadoCorreoResumenSemanal.YaEnviado,
                        $"El correo de esta semana ya se mandó el {resultado.YaEnviadoEl.Value:dd/MM/yyyy HH:mm}: no se manda otra vez. Para verlo, use ensayo=true.");
                }

                DatosResumenSemanal datos = Reunir(lunes);
                if (!ensayo && aplazarSiFaltaTablero && ReglaCorreoResumenSemanal.TableroSinLaSemana(datos))
                {
                    return Con(resultado, EstadoCorreoResumenSemanal.Aplazado, "El tablero aún no tiene la semana: se reintenta por la noche.");
                }
                CorreoResumenSemanal modelo = ReglaCorreoResumenSemanal.Componer(datos);
                resultado.Asunto = modelo.Asunto;
                resultado.Html = PintorCorreoResumenSemanal.Pintar(modelo);
                if (ensayo)
                {
                    string ya = resultado.YaEnviadoEl.HasValue ? $" Esta semana ya se mandó el {resultado.YaEnviadoEl.Value:dd/MM/yyyy HH:mm}." : string.Empty;
                    string a = resultado.Destinatario == null
                        ? $" Ahora no saldría: falta el destinatario ({ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO})."
                        : $" Saldría para {resultado.Destinatario}.";
                    return Con(resultado, EstadoCorreoResumenSemanal.Ensayo, "Ensayo: no se ha mandado nada." + a + ya);
                }

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(REMITENTE, NOMBRE_REMITENTE);
                    mail.To.Add(new MailAddress(resultado.Destinatario));
                    mail.Subject = modelo.Asunto;
                    mail.SubjectEncoding = Encoding.UTF8;
                    mail.Body = resultado.Html;
                    mail.BodyEncoding = Encoding.UTF8;
                    mail.IsBodyHtml = true;
                    if (!correo.EnviarCorreoSMTP(mail))
                    {
                        registrar(new Exception($"{PREFIJO} El servidor de correo no ha aceptado el correo de la semana del {lunes:dd/MM/yyyy}."));
                        return Con(resultado, EstadoCorreoResumenSemanal.FalloEnvio, "No se ha podido mandar: el servidor de correo no lo ha aceptado. Inténtelo más tarde.");
                    }
                }
                try
                {
                    registro().Apuntar(lunes, resultado.Destinatario, modelo.Asunto, usuario);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"{PREFIJO} Mandado, pero no se ha podido apuntar el envío de la semana del {lunes:dd/MM/yyyy} " +
                        $"(si se relanza, saldría otra vez): {ex.Message}", ex));
                }
                resultado.Enviado = true;
                return Con(resultado, EstadoCorreoResumenSemanal.Enviado, $"Mandado a {resultado.Destinatario}.");
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} El correo de la semana del {lunes:dd/MM/yyyy} ha fallado: {ex.Message}", ex));
                return Con(resultado, EstadoCorreoResumenSemanal.FalloEnvio, "No se ha podido preparar o mandar el correo: " + ex.Message);
            }
        }

        private static ResultadoCorreoResumenSemanalDTO Con(ResultadoCorreoResumenSemanalDTO r, EstadoCorreoResumenSemanal estado, string mensaje)
        {
            r.EstadoEnum = estado;
            r.Estado = estado.ToString();
            r.Mensaje = mensaje;
            return r;
        }

        private void AvisarSinDestinatario()
        {
            if (Interlocked.Exchange(ref avisadoSinDestinatario, 1) == 0)
            {
                registrar(new Exception($"{PREFIJO} No se manda el correo semanal de calidad: ParametrosUsuario «(defecto)» " +
                    $"{ReglaCorreoResumenSemanal.PARAMETRO_DESTINATARIO} no existe, está vacío o no es UNA dirección válida. Este aviso sale una sola vez."));
            }
        }

        /// <summary>Para los tests.</summary>
        internal static void ReiniciarAvisos() => Interlocked.Exchange(ref avisadoSinDestinatario, 0);
    }

    /// <summary>
    /// NestoAPI#914: el job del correo semanal de calidad, los LUNES a las 7:30 de Madrid (cola «indicadores», DESPUÉS del tablero de las 4:20).
    /// Si el tablero aún no tiene la semana (su job se pasó a la noche por ir la cola con retraso), se reprograma UNA vez para las 23:00.
    /// </summary>
    public static class CorreoResumenSemanalJobsService
    {
        public const string JOB_ID = "calidad-correo-resumen-semanal";
        public const string CRON = "30 7 * * 1";
        public const int HORA_REINTENTO = 23;
        internal const string USUARIO = "Sistema (correo de calidad)";

        [Queue(IndicadoresStock.IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void EnviarSemanal() => Ejecutar(false);

        [Queue(IndicadoresStock.IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Reintentar() => Ejecutar(true);

        private static void Ejecutar(bool esReintento)
        {
            DateTime madrid = FuentesKpiSql.AhoraMadrid();
            DateTime lunes = ReglaCorreoResumenSemanal.SemanaCerrada(madrid);
            ResultadoCorreoResumenSemanalDTO r = new ServicioCorreoResumenSemanal().Ejecutar(lunes, ensayo: false, usuario: USUARIO, aplazarSiFaltaTablero: !esReintento);
            if (r.EstadoEnum == EstadoCorreoResumenSemanal.Aplazado)
            {
                DateTime cuando = madrid.Date.AddHours(HORA_REINTENTO);
                DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cuando, DateTimeKind.Unspecified), Startup.ZonaHorariaPeninsular());
                _ = BackgroundJob.Schedule(() => Reintentar(), new DateTimeOffset(utc, TimeSpan.Zero));
            }
        }
    }
}
