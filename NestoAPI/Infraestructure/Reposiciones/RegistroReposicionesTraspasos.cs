using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#577 (corte 3a): con qué se hizo una reposición (ReposicionesTraspasos.Herramienta).
    /// </summary>
    public static class HerramientasReposicion
    {
        public const string NESTO = "Nesto";
        public const string ARIADNA = "Ariadna";
        /// <summary>El job que rellena las reposiciones a la hora de corte del calendario (corte 3b).</summary>
        public const string AUTOMATICO = "Automatico";
        /// <summary>
        /// Sugerencia 581 (Paloma, 13/10/26): la reposición puntual que una tienda manda a Algete con solo lo que elige, aparte
        /// de la del calendario (POST api/Reposiciones/Puntual). Nace y se termina de una vez, con número. NO cubre el viaje del
        /// calendario (el job la ignora al buscar si alguien ya ha rellenado la ruta) y no se puede pedir por el POST normal:
        /// solo la pone <see cref="ServicioPreparacionReposicion.CrearPuntual"/>.
        /// </summary>
        public const string PUNTUAL = "Puntual";

        private static readonly string[] VALIDAS = { NESTO, ARIADNA, AUTOMATICO };

        /// <summary>
        /// La que diga la petición (sin distinguir mayúsculas; «Automático» con tilde también vale) o, si no dice ninguna,
        /// la que se deduce del usuario: con dominio («NUEVAVISION\Paloma») es Nesto, que entra por api/auth/windows-token
        /// con el usuario de Windows; sin dominio («Andre») es Ariadna, que entra por /oauth/token con el usuario del
        /// Identity (NestoApp también, pero NestoApp no hace reposiciones). Una que no existe es un 400.
        /// </summary>
        public static string Resolver(string pedida, IPrincipal usuario)
        {
            if (!string.IsNullOrWhiteSpace(pedida))
            {
                string limpia = pedida.Trim().Replace('á', 'a').Replace('Á', 'A');
                string valida = VALIDAS.FirstOrDefault(v => string.Equals(v, limpia, StringComparison.OrdinalIgnoreCase));
                if (valida == null)
                {
                    throw new NestoBusinessException($"La herramienta «{pedida.Trim()}» no existe: tiene que ser {string.Join(", ", VALIDAS)}.");
                }
                return valida;
            }
            string nombre = usuario?.Identity?.Name;
            return !string.IsNullOrWhiteSpace(nombre) && nombre.Contains("\\") ? NESTO : ARIADNA;
        }
    }

    /// <summary>Una fila de ReposicionesTraspasos al crear la reposición.</summary>
    public class CabeceraReposicionTraspaso
    {
        public string Empresa { get; set; }
        /// <summary>
        /// El del traspaso si nace numerada (Algete). Null si se queda en preparación en una tienda: lo pone Terminar
        /// (<see cref="IRegistroReposicionesTraspasos.NumerarYMarcarPreparada"/>).
        /// </summary>
        public int? NumTraspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Herramienta { get; set; }
        public string UsuarioCreacion { get; set; }
        public DateTime FechaCreacion { get; set; }
        /// <summary>El instante de corte con el que la rellenó el job; null si se hizo a mano.</summary>
        public DateTime? FechaCorte { get; set; }
    }

    /// <summary>
    /// NestoAPI#577 (corte 3a): la cabecera de trazabilidad de cada traspaso de reposición (tabla ReposicionesTraspasos):
    /// herramienta, quién y cuándo se crea, se prepara y se recibe. Nesto viejo no escribe aquí: un traspaso SIN cabecera
    /// es de Nesto viejo. Todo va dentro de la transacción del llamante. Si la tabla aún no existe (script sin lanzar),
    /// no se apunta nada y lo demás sigue igual.
    ///
    /// <para>Clave propia (Id). Decisión de Carlos (08/10/26): NO se reservan números del contador. Desde una tienda la
    /// reposición queda en preparación SIN número de traspaso hasta Terminar (como en Nesto viejo) y su cabecera nace con
    /// NumTraspaso NULL; Terminar coge el número del contador, como siempre, y se lo pone a la cabecera ABIERTA (sin
    /// número y sin preparar) de ese origen y destino. Como mucho hay una reposición en preparación por diario de salida
    /// del origen, así que hay como mucho una abierta; si hubiera varias (una que terminó Nesto viejo, que no toca la
    /// cabecera, se queda abierta para siempre), se coge la más reciente, que es la de la preparación en curso.</para>
    /// </summary>
    public interface IRegistroReposicionesTraspasos
    {
        /// <summary>
        /// La cabecera al crear la reposición: con su número desde Algete (nace numerada y cerrada, por recoger en Ariadna) o
        /// sin él desde una tienda (se queda en preparación hasta Terminar).
        /// </summary>
        Task Crear(CabeceraReposicionTraspaso cabecera);

        /// <summary>
        /// NestoAPI#622: la cabecera del job cuando, a la hora de corte, la tienda aún tenía sin enviar la preparación de un
        /// viaje anterior y se le ha sumado lo de hoy (<see cref="ServicioPreparacionReposicion.Completar"/>). Nace sin número,
        /// como la abierta, y enlazada a ella (IdCompletada = la primera cabecera de esa preparación): la abierta más reciente
        /// de la ruta creada desde <paramref name="desde"/> (el día de la línea más antigua de la preparación; así no se
        /// enlaza con una abierta vieja que terminó Nesto viejo). Sin la columna IdCompletada (falta
        /// Scripts/Issue622_ReposicionesTraspasos_IdCompletada.sql) se crea igual, sin enlace.
        /// </summary>
        Task CrearCompletando(CabeceraReposicionTraspaso cabecera, DateTime desde);

        /// <summary>
        /// Al terminar en la tienda: el número recién sacado del contador va a la cabecera abierta de ese origen y destino,
        /// que queda preparada. Sin cabecera abierta (la creó Nesto viejo), no hace nada. NestoAPI#622: también a las que
        /// están enlazadas con ella (IdCompletada): una preparación completada en varios cortes es un solo traspaso.
        /// </summary>
        Task NumerarYMarcarPreparada(string empresa, string origen, string destino, int numTraspaso, string usuario);

        /// <summary>Terminada la salida (recogida en Ariadna, Algete). Sin cabecera, no hace nada.</summary>
        Task MarcarPreparada(string empresa, int numTraspaso, string usuario);

        /// <summary>Anulada (DELETE api/Reposiciones/{n}): fuera la cabecera.</summary>
        Task Borrar(string empresa, int numTraspaso);
    }

    /// <summary>Para quien no lleva cabecera (las pruebas que no la miran): no apunta nada.</summary>
    internal sealed class SinRegistroReposicionesTraspasos : IRegistroReposicionesTraspasos
    {
        internal static readonly SinRegistroReposicionesTraspasos Instancia = new SinRegistroReposicionesTraspasos();

        public Task Crear(CabeceraReposicionTraspaso cabecera) => Task.CompletedTask;
        public Task CrearCompletando(CabeceraReposicionTraspaso cabecera, DateTime desde) => Task.CompletedTask;
        public Task NumerarYMarcarPreparada(string empresa, string origen, string destino, int numTraspaso, string usuario) => Task.CompletedTask;
        public Task MarcarPreparada(string empresa, int numTraspaso, string usuario) => Task.CompletedTask;
        public Task Borrar(string empresa, int numTraspaso) => Task.CompletedTask;
    }

    public class RegistroReposicionesTraspasosSql : IRegistroReposicionesTraspasos
    {
        internal const string SQL_CREAR = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte)
    VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        // La abierta del origen y destino: sin número y sin preparar (y que no sea una marca del job sin reposición, corte 3b:
        // Omitida con el motivo). Si hubiera varias (no debería: una por diario de salida;
        // las que terminó Nesto viejo se quedan abiertas), la más reciente, que es la de la preparación en curso.
        // NestoAPI#622: con la columna IdCompletada, la abierta y las de su misma preparación (la primera cabecera, @raiz, y
        // las que la completan). Va por sp_executesql porque sin la columna el lote entero no compilaría.
        internal const string SQL_NUMERAR_ABIERTA = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL
        UPDATE r SET NumTraspaso = @p3, UsuarioPreparacion = @p4, FechaPreparada = GETDATE()
        FROM dbo.ReposicionesTraspasos r
        WHERE r.Id = (SELECT TOP 1 a.Id FROM dbo.ReposicionesTraspasos a WITH (UPDLOCK, HOLDLOCK)
                      WHERE a.Empresa = @p0 AND a.Origen = @p1 AND a.Destino = @p2
                        AND a.NumTraspaso IS NULL AND a.FechaPreparada IS NULL
                        AND a.Omitida IS NULL
                      ORDER BY a.FechaCreacion DESC, a.Id DESC)
    ELSE
        EXEC sp_executesql N'
DECLARE @abierta int, @raiz int;
SELECT TOP 1 @abierta = a.Id, @raiz = ISNULL(a.IdCompletada, a.Id)
FROM dbo.ReposicionesTraspasos a WITH (UPDLOCK, HOLDLOCK)
WHERE a.Empresa = @empresa AND a.Origen = @origen AND a.Destino = @destino
  AND a.NumTraspaso IS NULL AND a.FechaPreparada IS NULL
  AND a.Omitida IS NULL
ORDER BY a.FechaCreacion DESC, a.Id DESC;
UPDATE r SET NumTraspaso = @numero, UsuarioPreparacion = @usuario, FechaPreparada = GETDATE()
FROM dbo.ReposicionesTraspasos r
WHERE r.Empresa = @empresa AND r.Origen = @origen AND r.Destino = @destino
  AND r.NumTraspaso IS NULL AND r.FechaPreparada IS NULL AND r.Omitida IS NULL
  AND (r.Id = @abierta OR r.Id = @raiz OR r.IdCompletada = @raiz);',
            N'@empresa char(3), @origen char(3), @destino char(3), @numero int, @usuario varchar(30)',
            @empresa = @p0, @origen = @p1, @destino = @p2, @numero = @p3, @usuario = @p4;
END";

        // NestoAPI#622: ver IRegistroReposicionesTraspasos.CrearCompletando. Sin la columna, como SQL_CREAR.
        internal const string SQL_CREAR_COMPLETANDO = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL
        INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte)
        VALUES (@p0, NULL, @p1, @p2, @p3, @p4, @p5, @p6)
    ELSE
        EXEC sp_executesql N'
DECLARE @raiz int = (SELECT TOP 1 ISNULL(a.IdCompletada, a.Id)
                     FROM dbo.ReposicionesTraspasos a WITH (UPDLOCK, HOLDLOCK)
                     WHERE a.Empresa = @empresa AND a.Origen = @origen AND a.Destino = @destino
                       AND a.NumTraspaso IS NULL AND a.FechaPreparada IS NULL AND a.Omitida IS NULL
                       AND a.FechaCreacion >= @desde
                     ORDER BY a.FechaCreacion DESC, a.Id DESC);
INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte, IdCompletada)
VALUES (@empresa, NULL, @origen, @destino, @herramienta, @usuario, @creacion, @corte, @raiz);',
            N'@empresa char(3), @origen char(3), @destino char(3), @herramienta varchar(20), @usuario varchar(30), @creacion datetime, @corte datetime, @desde datetime',
            @empresa = @p0, @origen = @p1, @destino = @p2, @herramienta = @p3, @usuario = @p4, @creacion = @p5, @corte = @p6, @desde = @p7;
END";

        internal const string SQL_MARCAR_PREPARADA = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    UPDATE dbo.ReposicionesTraspasos SET UsuarioPreparacion = @p2, FechaPreparada = GETDATE()
    WHERE Empresa = @p0 AND NumTraspaso = @p1 AND FechaPreparada IS NULL";

        internal const string SQL_MARCAR_RECIBIDA = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    UPDATE dbo.ReposicionesTraspasos SET UsuarioRecepcion = @p2, FechaRecibida = GETDATE()
    WHERE Empresa = @p0 AND NumTraspaso = @p1 AND FechaRecibida IS NULL";

        internal const string SQL_BORRAR = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    DELETE FROM dbo.ReposicionesTraspasos WHERE Empresa = @p0 AND NumTraspaso = @p1";

        private readonly NVEntities db;

        public RegistroReposicionesTraspasosSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task Crear(CabeceraReposicionTraspaso cabecera)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_CREAR,
                Char("@p0", cabecera.Empresa, 3), EnteroONulo("@p1", cabecera.NumTraspaso), Char("@p2", cabecera.Origen, 3), Char("@p3", cabecera.Destino, 3),
                VarChar("@p4", cabecera.Herramienta, 20), VarChar("@p5", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                Fecha("@p6", cabecera.FechaCreacion), Fecha("@p7", cabecera.FechaCorte));
        }

        public Task CrearCompletando(CabeceraReposicionTraspaso cabecera, DateTime desde)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_CREAR_COMPLETANDO,
                Char("@p0", cabecera.Empresa, 3), Char("@p1", cabecera.Origen, 3), Char("@p2", cabecera.Destino, 3),
                VarChar("@p3", cabecera.Herramienta, 20), VarChar("@p4", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                Fecha("@p5", cabecera.FechaCreacion), Fecha("@p6", cabecera.FechaCorte), Fecha("@p7", desde));
        }

        public Task NumerarYMarcarPreparada(string empresa, string origen, string destino, int numTraspaso, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_NUMERAR_ABIERTA,
                Char("@p0", empresa, 3), Char("@p1", origen, 3), Char("@p2", destino, 3), EnteroONulo("@p3", numTraspaso),
                VarChar("@p4", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }

        public Task MarcarPreparada(string empresa, int numTraspaso, string usuario)
        {
            return MarcarPreparada(db, empresa, numTraspaso, usuario);
        }

        public Task Borrar(string empresa, int numTraspaso)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_BORRAR, Char("@p0", empresa, 3), EnteroONulo("@p1", numTraspaso));
        }

        /// <summary>Para las transacciones de Ariadna (Salidas), que llevan su propio contexto.</summary>
        internal static Task MarcarPreparada(NVEntities db, string empresa, int numTraspaso, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_PREPARADA, Char("@p0", empresa, 3), EnteroONulo("@p1", numTraspaso),
                VarChar("@p2", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }

        /// <summary>Para la transacción de Ariadna que recibe la reposición (Entradas). Sin cabecera, no hace nada.</summary>
        internal static Task MarcarRecibida(NVEntities db, string empresa, int numTraspaso, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_RECIBIDA, Char("@p0", empresa, 3), EnteroONulo("@p1", numTraspaso),
                VarChar("@p2", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }

        internal static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };
        }

        internal static SqlParameter VarChar(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.VarChar, longitud) { Value = (object)valor ?? DBNull.Value };
        }

        internal static SqlParameter EnteroONulo(string nombre, int? valor)
        {
            return new SqlParameter(nombre, SqlDbType.Int) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };
        }

        internal static SqlParameter Fecha(string nombre, DateTime? valor)
        {
            return new SqlParameter(nombre, SqlDbType.DateTime) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };
        }
    }
}
