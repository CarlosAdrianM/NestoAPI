using Hangfire;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#577 (corte 3b): qué ha pasado con una ruta al rellenarla sola.</summary>
    public static class ResultadosReposicionAutomatica
    {
        /// <summary>Creada (desde una tienda, en preparación; desde Algete, cerrada y por recoger en Ariadna).</summary>
        public const string CREADA = "Creada";
        /// <summary>La propuesta no tenía nada que mandar. Se apunta para no reintentar.</summary>
        public const string VACIA = "Vacia";
        /// <summary>El origen ya tenía una en preparación hecha a mano: no se crea otra. Se apunta y se avisa.</summary>
        public const string YA_EN_PREPARACION = "YaEnPreparacion";
        /// <summary>Otro motivo de negocio (inventario en curso, almacén sin diario…). Se apunta y se avisa.</summary>
        public const string NO_SE_PUEDE = "NoSePuede";
        /// <summary>A la hora de llegada habitual aún no se había rellenado: ese viaje ya ha pasado. Se apunta y se avisa.</summary>
        public const string FUERA_DE_PLAZO = "FueraDePlazo";
        /// <summary>Fallo inesperado (BD, bloqueo…): se avisa y NO se apunta, para que la siguiente pasada lo reintente.</summary>
        public const string ERROR = "Error";
    }

    /// <summary>NestoAPI#577 (corte 3b): resultado de rellenar una ruta (job y POST api/Reposiciones/RellenarAutomatica).</summary>
    public class ResultadoReposicionAutomaticaDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>El instante de corte del calendario con el que se ha rellenado (día + HoraCierre).</summary>
        public DateTime Corte { get; set; }
        /// <summary>Ver <see cref="ResultadosReposicionAutomatica"/>.</summary>
        public string Resultado { get; set; }
        /// <summary>Solo desde Algete (nace numerada). Desde una tienda, null hasta que la terminen.</summary>
        public int? NumTraspaso { get; set; }
        public int Lineas { get; set; }
        public string Mensaje { get; set; }
    }

    public interface IServicioReposicionAutomatica
    {
        /// <summary>Ver <see cref="ServicioReposicionAutomatica.RellenarRuta"/>.</summary>
        Task<ResultadoReposicionAutomaticaDTO> RellenarRuta(string empresa, string origen, string destino, IPrincipal usuario);
    }

    public interface IRepositorioReposicionAutomatica
    {
        /// <summary>¿Existe ReposicionesTraspasos con la columna Omitida? Sin ella el job no sabe qué ha hecho ya.</summary>
        Task<bool> TablaPreparada();
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
        /// <summary>
        /// ¿Hay ya una cabecera del job (Herramienta 'Automatico') para esa ruta y ese instante de corte? Con
        /// <paramref name="contarOmitidas"/>, también las marcas sin reposición (vacía, ya había una, fuera de plazo…).
        /// </summary>
        Task<bool> HayCabecera(string empresa, string origen, string destino, DateTime corte, bool contarOmitidas);
        /// <summary>Marca sin reposición (NumTraspaso NULL, Omitida = motivo) para no reintentar ese corte.</summary>
        Task ApuntarOmitida(CabeceraReposicionTraspaso cabecera, string motivo);
        /// <summary>Exclusión entre ejecuciones (job, endpoint, dos servidores): applock por origen y día. Dispose lo suelta.</summary>
        Task<IDisposable> Bloquear(string empresa, string origen, DateTime dia);
    }

    /// <summary>
    /// NestoAPI#577 (corte 3b): rellena cada reposición del calendario (ReposicionesCalendario) a su hora de corte.
    ///
    /// <para><b>El corte es un dato</b>, como el horizonte del picking de cierre (NestoAPI#361): para cada reposición
    /// que cierra (o llega) HOY se calcula el instante día de cierre + HoraCierre y, si ya ha pasado y esa ruta no tiene todavía cabecera del job para
    /// ese instante (ReposicionesTraspasos: Herramienta 'Automatico' + FechaCorte), se llama a
    /// <see cref="ServicioPreparacionReposicion.Crear(CrearReposicionDTO, IPrincipal, DateTime?)"/> con ESE instante (no
    /// con DateTime.Now): solo cuentan las líneas de pedido anteriores. Da igual que Hangfire arranque a las 10:00:03 o a
    /// las 10:05: el resultado es el mismo. Festivos y fines de semana como <see cref="CalculadoraFechaReposicion"/>.</para>
    ///
    /// <para><b>Cierre con antelación</b> (corte 3d, decisión de Carlos 08/10): la fila es la del día de LLEGADA y se cierra
    /// LaborablesAntelacionCierre laborables del origen antes (Algete → tienda: el laborable anterior a las 13:00; la del
    /// lunes, el viernes). Cada pasada mira <see cref="CalculadoraFechaReposicion.CortesDeHoy"/>: las que cierran hoy (la
    /// del lunes se rellena el viernes a las 13:00, con ese instante como corte) y las que llegan hoy (si el viernes no se
    /// rellenó, el lunes aún se rellena, con el corte del viernes, hasta la hora de llegada; después, fuera de plazo). La
    /// cabecera se identifica por ruta + instante de corte (FechaCorte), así que las dos vías no duplican.</para>
    ///
    /// <para><b>Exclusión</b>: applock por ORIGEN y día DE CIERRE (el del instante de corte, no por ruta): Algete → Reina y
    /// Algete → Alcobendas escriben en el mismo diario de salida de Algete y no deben solaparse. El job y el endpoint manual
    /// usan la misma clave (el día del corte), así que una misma reposición siempre se bloquea con el mismo recurso. Dentro
    /// del bloqueo se vuelve a mirar si ya está hecha.</para>
    ///
    /// <para><b>Qué se apunta</b> (para no reintentar cada 5 minutos): la reposición creada lleva su cabecera; si no se crea
    /// por un motivo de negocio, una marca en ReposicionesTraspasos sin número y con Omitida = el motivo:
    /// propuesta vacía (sin aviso: es normal), la tienda ya tenía una en preparación hecha a mano (no se crea otra; aviso
    /// informativo a ELMAH), otro error de negocio (aviso) o fuera de plazo (aviso). Un fallo inesperado (BD, bloqueo,
    /// timeout) se avisa y NO se apunta: la siguiente pasada lo reintenta. Un error en una ruta no para las demás.</para>
    ///
    /// <para><b>Fuera de plazo</b>: si a la hora de llegada habitual de esa fila aún no se ha rellenado (la API estuvo caída),
    /// ya no se rellena sola: ese viaje ha pasado. Se puede relanzar a mano con <see cref="RellenarRuta"/>.</para>
    /// </summary>
    public class ServicioReposicionAutomatica : IServicioReposicionAutomatica
    {
        /// <summary>El usuario con el que el job escribe (PreExtrProducto.Usuario, ReposicionesTraspasos.UsuarioCreacion).</summary>
        public const string USUARIO_AUTOMATICO = "ReposicionAutomatica";
        private const string USUARIO_ELMAH = "Sistema (reposición automática)";

        private readonly IRepositorioReposicionAutomatica repositorio;
        private readonly Func<CrearReposicionDTO, IPrincipal, DateTime?, Task<ReposicionEnPreparacionDTO>> crear;
        private readonly CalculadoraFechaReposicion calculadora;
        private readonly Func<DateTime> reloj;
        private readonly Action<Exception> avisar;

        public ServicioReposicionAutomatica(NVEntities db)
            : this(new RepositorioReposicionAutomaticaSql(db),
                  (peticion, usuario, corte) => new ServicioPreparacionReposicion(db).Crear(peticion, usuario, corte),
                  new CalculadoraFechaReposicion(),
                  () => DateTime.Now,
                  ex => ElmahHelper.Log(ex, USUARIO_ELMAH))
        {
        }

        internal ServicioReposicionAutomatica(IRepositorioReposicionAutomatica repositorio,
            Func<CrearReposicionDTO, IPrincipal, DateTime?, Task<ReposicionEnPreparacionDTO>> crear,
            CalculadoraFechaReposicion calculadora, Func<DateTime> reloj, Action<Exception> avisar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.crear = crear ?? throw new ArgumentNullException(nameof(crear));
            this.calculadora = calculadora ?? throw new ArgumentNullException(nameof(calculadora));
            this.reloj = reloj ?? (() => DateTime.Now);
            this.avisar = avisar ?? (ex => { });
        }

        /// <summary>
        /// Con quién escribe: <paramref name="nombre"/> con el rol Almacén, que puede crear desde cualquier origen (Algete
        /// incluido, ServicioPreparacionReposicion.PuedeEscribir). El job usa <see cref="USUARIO_AUTOMATICO"/>; el endpoint
        /// manual, el nombre de quien lo lanza (para la auditoría).
        /// </summary>
        internal static IPrincipal Principal(string nombre)
        {
            var identidad = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, nombre),
                new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
            }, "NestoAPI#577");
            return new ClaimsPrincipal(identidad);
        }

        /// <summary>El job: todas las rutas de hoy cuyo corte ya ha pasado y aún no están hechas.</summary>
        public async Task<List<ResultadoReposicionAutomaticaDTO>> RellenarPendientes(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            var resultados = new List<ResultadoReposicionAutomaticaDTO>();
            if (!await repositorio.TablaPreparada().ConfigureAwait(false))
            {
                return resultados;
            }
            DateTime ahora = reloj();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresa).ConfigureAwait(false);
            IPrincipal automatico = Principal(USUARIO_AUTOMATICO);

            foreach (CorteReposicion corte in calculadora.CortesDeHoy(calendario, ahora).Where(c => c.Corte <= ahora))
            {
                try
                {
                    using (await repositorio.Bloquear(corte.Empresa, corte.Origen, corte.Corte.Date).ConfigureAwait(false))
                    {
                        if (await repositorio.HayCabecera(corte.Empresa, corte.Origen, corte.Destino, corte.Corte, contarOmitidas: true).ConfigureAwait(false))
                        {
                            continue;
                        }
                        if (ahora >= corte.Llegada)
                        {
                            string motivo = $"Fuera de plazo: a las {corte.Llegada:HH:mm} (llegada habitual) aún no se había rellenado. " +
                                "Se puede relanzar a mano (POST api/Reposiciones/RellenarAutomatica).";
                            await Apuntar(corte, automatico, motivo).ConfigureAwait(false);
                            avisar(new Exception(Prefijo(corte) + motivo));
                            resultados.Add(Resultado(corte, ResultadosReposicionAutomatica.FUERA_DE_PLAZO, motivo));
                            continue;
                        }
                        resultados.Add(await Intentar(corte, automatico, manual: false).ConfigureAwait(false));
                    }
                }
                catch (Exception ex)
                {
                    // Inesperado: sin marca, para que la siguiente pasada lo reintente. Las demás rutas siguen.
                    avisar(new Exception(Prefijo(corte) + "Error al rellenarla: " + ex.Message, ex));
                    resultados.Add(Resultado(corte, ResultadosReposicionAutomatica.ERROR, ex.Message));
                }
            }
            return resultados;
        }

        /// <summary>
        /// POST api/Reposiciones/RellenarAutomatica: relanza a mano la de hoy de una ruta, con el corte del calendario (el
        /// último que ya ha pasado de las que cierran o llegan hoy: con antelación, el lunes relanza la del lunes con el corte
        /// del viernes). 404 si hoy no toca (o es festivo); 409 si aún no ha llegado la hora de cierre,
        /// si ya está rellena (una marca sin reposición no cuenta: se puede relanzar) o si la tienda ya tiene una en
        /// preparación. Escribe con el nombre de quien lo lanza y Herramienta 'Automatico'.
        /// </summary>
        public async Task<ResultadoReposicionAutomaticaDTO> RellenarRuta(string empresa, string origen, string destino, IPrincipal usuario)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string origenLimpio = origen?.Trim().ToUpperInvariant();
            string destinoLimpio = destino?.Trim().ToUpperInvariant();
            if (!await repositorio.TablaPreparada().ConfigureAwait(false))
            {
                throw new NestoBusinessException("Falta lanzar Scripts/Issue577_ReposicionesTraspasos.sql en la base de datos.")
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }
            DateTime ahora = reloj();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresaLimpia).ConfigureAwait(false);
            List<CorteReposicion> deHoy = calculadora.CortesDeHoy(calendario, ahora)
                .Where(c => c.Empresa == empresaLimpia && c.Origen == origenLimpio && c.Destino == destinoLimpio)
                .ToList();
            if (!deHoy.Any())
            {
                throw new NestoBusinessException($"Hoy no hay reposición de {origenLimpio} a {destinoLimpio} en el calendario (o es festivo en alguno de los dos).")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            CorteReposicion corte = deHoy.Where(c => c.Corte <= ahora).OrderByDescending(c => c.Corte).FirstOrDefault();
            if (corte == null)
            {
                throw new NestoBusinessException($"La reposición de hoy de {origenLimpio} a {destinoLimpio} cierra a las {deHoy.Where(c => c.Corte > ahora).Min(c => c.Corte):HH:mm}: " +
                    "se rellenará sola a esa hora.")
                {
                    StatusCode = HttpStatusCode.Conflict
                };
            }
            string nombre = usuario?.Identity?.Name;
            IPrincipal quien = Principal(string.IsNullOrWhiteSpace(nombre) ? USUARIO_AUTOMATICO : nombre);
            using (await repositorio.Bloquear(corte.Empresa, corte.Origen, corte.Corte.Date).ConfigureAwait(false))
            {
                if (await repositorio.HayCabecera(corte.Empresa, corte.Origen, corte.Destino, corte.Corte, contarOmitidas: false).ConfigureAwait(false))
                {
                    throw new NestoBusinessException($"La reposición de {origenLimpio} a {destinoLimpio} de las {corte.Corte:HH:mm} {DiaDelCorte(corte, ahora)} ya está rellena.")
                    {
                        StatusCode = HttpStatusCode.Conflict
                    };
                }
                return await Intentar(corte, quien, manual: true).ConfigureAwait(false);
            }
        }

        private async Task<ResultadoReposicionAutomaticaDTO> Intentar(CorteReposicion corte, IPrincipal quien, bool manual)
        {
            var peticion = new CrearReposicionDTO
            {
                Empresa = corte.Empresa,
                Origen = corte.Origen,
                Destino = corte.Destino,
                Herramienta = HerramientasReposicion.AUTOMATICO
            };
            try
            {
                ReposicionEnPreparacionDTO creada = await crear(peticion, quien, corte.Corte).ConfigureAwait(false);
                ResultadoReposicionAutomaticaDTO resultado = Resultado(corte, ResultadosReposicionAutomatica.CREADA, null);
                resultado.NumTraspaso = creada?.NumTraspaso;
                resultado.Lineas = creada?.Lineas?.Count ?? 0;
                return resultado;
            }
            catch (ReposicionVaciaException ex)
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                return Resultado(corte, ResultadosReposicionAutomatica.VACIA, ex.Message);
            }
            catch (ReposicionYaEnPreparacionException ex) when (!manual)
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                avisar(new Exception(Prefijo(corte) + "No se rellena: " + ex.Message));
                return Resultado(corte, ResultadosReposicionAutomatica.YA_EN_PREPARACION, ex.Message);
            }
            catch (NestoBusinessException ex) when (!manual && !(ex is ReposicionYaEnPreparacionException))
            {
                await Apuntar(corte, quien, ex.Message).ConfigureAwait(false);
                avisar(new Exception(Prefijo(corte) + "No se puede rellenar: " + ex.Message, ex));
                return Resultado(corte, ResultadosReposicionAutomatica.NO_SE_PUEDE, ex.Message);
            }
        }

        private Task Apuntar(CorteReposicion corte, IPrincipal quien, string motivo)
        {
            return repositorio.ApuntarOmitida(new CabeceraReposicionTraspaso
            {
                Empresa = corte.Empresa,
                Origen = corte.Origen,
                Destino = corte.Destino,
                Herramienta = HerramientasReposicion.AUTOMATICO,
                UsuarioCreacion = quien?.Identity?.Name ?? USUARIO_AUTOMATICO,
                FechaCreacion = reloj(),
                FechaCorte = corte.Corte
            }, motivo);
        }

        private static string DiaDelCorte(CorteReposicion corte, DateTime ahora)
        {
            return corte.Corte.Date == ahora.Date ? "de hoy" : string.Format(CultureInfo.InvariantCulture, "del {0:dd/MM}", corte.Corte);
        }

        private static string Prefijo(CorteReposicion corte)
        {
            return string.Format(CultureInfo.InvariantCulture, "[Reposición automática #577] {0} → {1}, corte {2:dd/MM/yyyy HH:mm}: ",
                corte.Origen, corte.Destino, corte.Corte);
        }

        private static ResultadoReposicionAutomaticaDTO Resultado(CorteReposicion corte, string resultado, string mensaje)
        {
            return new ResultadoReposicionAutomaticaDTO
            {
                Origen = corte.Origen,
                Destino = corte.Destino,
                Corte = corte.Corte,
                Resultado = resultado,
                Mensaje = mensaje
            };
        }
    }

    public class RepositorioReposicionAutomaticaSql : IRepositorioReposicionAutomatica
    {
        private const int ESPERA_BLOQUEO_MS = 30000;

        internal const string SQL_TABLA_PREPARADA =
            "SELECT CAST(CASE WHEN COL_LENGTH('dbo.ReposicionesTraspasos', 'Omitida') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_HAY_CABECERA = @"
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ReposicionesTraspasos
    WHERE Empresa = @p0 AND Origen = @p1 AND Destino = @p2 AND Herramienta = 'Automatico' AND FechaCorte = @p3
      AND (@p4 = 1 OR Omitida IS NULL)) THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_APUNTAR_OMITIDA = @"
INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte, Omitida)
VALUES (@p0, NULL, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        // Ámbito Session (como GestorPicking, NestoAPI#405): Crear abre y cierra su propia transacción dentro del bloqueo, así
        // que no puede ir ligado a una transacción. La conexión se abre a mano para que EF no la devuelva al pool a mitad.
        internal const string SQL_BLOQUEAR = @"
DECLARE @resultado int;
EXEC @resultado = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @p1;
IF @resultado < 0 RAISERROR('Otra ejecución está rellenando las reposiciones de este almacén. Se reintentará en la siguiente pasada.', 16, 1);";

        internal const string SQL_SOLTAR = "EXEC sp_releaseapplock @Resource = @p0, @LockOwner = 'Session';";

        private readonly NVEntities db;

        public RepositorioReposicionAutomaticaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal static string Recurso(string empresa, string origen, DateTime dia)
        {
            return string.Format(CultureInfo.InvariantCulture, "ReposicionAutomatica:{0}:{1}:{2:yyyyMMdd}", empresa?.Trim(), origen?.Trim(), dia);
        }

        public Task<bool> TablaPreparada()
        {
            return db.Database.SqlQuery<bool>(SQL_TABLA_PREPARADA).SingleAsync();
        }

        public async Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            return await db.ReposicionesCalendario.AsNoTracking()
                .Where(f => f.Empresa == empresa && f.Activo)
                .ToListAsync().ConfigureAwait(false);
        }

        public Task<bool> HayCabecera(string empresa, string origen, string destino, DateTime corte, bool contarOmitidas)
        {
            return db.Database.SqlQuery<bool>(SQL_HAY_CABECERA,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", destino, 3), RegistroReposicionesTraspasosSql.Fecha("@p3", corte),
                new System.Data.SqlClient.SqlParameter("@p4", SqlDbType.Bit) { Value = contarOmitidas }).SingleAsync();
        }

        public Task ApuntarOmitida(CabeceraReposicionTraspaso cabecera, string motivo)
        {
            string texto = string.IsNullOrWhiteSpace(motivo) ? "Sin motivo" : motivo.Trim();
            return db.Database.ExecuteSqlCommandAsync(SQL_APUNTAR_OMITIDA,
                RegistroReposicionesTraspasosSql.Char("@p0", cabecera.Empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", cabecera.Origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", cabecera.Destino, 3), RegistroReposicionesTraspasosSql.VarChar("@p3", cabecera.Herramienta, 20),
                RegistroReposicionesTraspasosSql.VarChar("@p4", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                RegistroReposicionesTraspasosSql.Fecha("@p5", cabecera.FechaCreacion), RegistroReposicionesTraspasosSql.Fecha("@p6", cabecera.FechaCorte),
                RegistroReposicionesTraspasosSql.VarChar("@p7", texto.Length > 300 ? texto.Substring(0, 300) : texto, 300));
        }

        public async Task<IDisposable> Bloquear(string empresa, string origen, DateTime dia)
        {
            DbConnection conexion = db.Database.Connection;
            bool laAbrimosAqui = conexion.State != ConnectionState.Open;
            if (laAbrimosAqui)
            {
                await conexion.OpenAsync().ConfigureAwait(false);
            }
            string recurso = Recurso(empresa, origen, dia);
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_BLOQUEAR, recurso, ESPERA_BLOQUEO_MS).ConfigureAwait(false);
            }
            catch
            {
                if (laAbrimosAqui)
                {
                    conexion.Close();
                }
                throw;
            }
            return new Bloqueo(db, conexion, recurso, laAbrimosAqui);
        }

        private sealed class Bloqueo : IDisposable
        {
            private readonly NVEntities db;
            private readonly DbConnection conexion;
            private readonly string recurso;
            private readonly bool cerrar;

            public Bloqueo(NVEntities db, DbConnection conexion, string recurso, bool cerrar)
            {
                this.db = db;
                this.conexion = conexion;
                this.recurso = recurso;
                this.cerrar = cerrar;
            }

            public void Dispose()
            {
                try
                {
                    _ = db.Database.ExecuteSqlCommand(SQL_SOLTAR, recurso);
                }
                catch
                {
                    // Si no se puede soltar, se suelta solo al cerrar la conexión (ámbito Session)
                }
                finally
                {
                    if (cerrar)
                    {
                        conexion.Close();
                    }
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#577 (corte 3b): job recurrente <c>reposiciones-automaticas</c> (Startup). Cada 5 minutos en horario laboral
    /// mira qué reposiciones del calendario de hoy han pasado su hora de cierre y las rellena (ver
    /// <see cref="ServicioReposicionAutomatica"/>). Si se pone en el calendario una hora de cierre fuera de
    /// <see cref="CRON"/>, hay que ampliar el cron.
    /// </summary>
    public static class ReposicionesAutomaticasJobsService
    {
        /// <summary>Cada 5 minutos de lunes a viernes de 6:00 a 21:55 (las horas de cierre tienen que caer dentro).</summary>
        public const string CRON = "*/5 6-21 * * 1-5";

        /// <summary>
        /// Sin reintento automático de Hangfire: lo que falla sin apuntarse lo reintenta la siguiente pasada (5 minutos), y
        /// cada ruta lleva su propio applock, así que dos ejecuciones solapadas tampoco duplican.
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        public static async Task RellenarPendientes()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await new ServicioReposicionAutomatica(db).RellenarPendientes().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Reposición automática #577] Error en el job: " + ex.Message, ex), "Sistema (reposición automática)");
                throw;
            }
        }
    }
}
