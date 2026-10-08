using NestoAPI.Infraestructure.Contadores;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Data;
using System.Data.Entity;
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
        /// <summary>Al reservar (tiendas) no se usa: lo pone el registro.</summary>
        public int NumTraspaso { get; set; }
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
    /// <para>Desde una tienda, la reposición queda en preparación SIN número de traspaso hasta Terminar (como en Nesto
    /// viejo). Para no perder quién y con qué la creó, al crearla se RESERVA ya el número (<see cref="Reservar"/>) y
    /// Terminar lo usa (<see cref="LeerReserva"/>). Una reserva es una cabecera sin preparar y sin ninguna fila en
    /// PreExtrProducto con su número. Si la reposición la termina Nesto viejo (que coge otro número), la reserva se queda
    /// así y la siguiente que se cree desde ese origen la reutiliza (no se queman números del contador).</para>
    /// </summary>
    public interface IRegistroReposicionesTraspasos
    {
        /// <summary>Desde Algete: la reposición nace ya numerada (y cerrada, por recoger en Ariadna).</summary>
        Task Crear(CabeceraReposicionTraspaso cabecera);

        /// <summary>
        /// Desde una tienda: reserva el número del traspaso para la reposición que se queda en preparación (o reutiliza la
        /// reserva que el origen tenga sin usar). Null si no hay tabla.
        /// </summary>
        Task<int?> Reservar(CabeceraReposicionTraspaso cabecera);

        /// <summary>
        /// Al terminar en la tienda: el número reservado al crear ESTA preparación (mismo origen, destino y usuario que
        /// grabó las líneas). Null si no hay (la creó Nesto viejo o es anterior a la cabecera): se coge uno nuevo.
        /// </summary>
        Task<int?> LeerReserva(string empresa, string origen, string destino, string usuarioCreacion);

        /// <summary>Terminada la salida (Terminar en la tienda o recogida en Ariadna). Sin cabecera, no hace nada.</summary>
        Task MarcarPreparada(string empresa, int numTraspaso, string usuario);

        /// <summary>Anulada (DELETE api/Reposiciones/{n}): fuera la cabecera.</summary>
        Task Borrar(string empresa, int numTraspaso);
    }

    /// <summary>Para quien no lleva cabecera (las pruebas que no la miran): no apunta nada.</summary>
    internal sealed class SinRegistroReposicionesTraspasos : IRegistroReposicionesTraspasos
    {
        internal static readonly SinRegistroReposicionesTraspasos Instancia = new SinRegistroReposicionesTraspasos();

        public Task Crear(CabeceraReposicionTraspaso cabecera) => Task.CompletedTask;
        public Task<int?> Reservar(CabeceraReposicionTraspaso cabecera) => Task.FromResult<int?>(null);
        public Task<int?> LeerReserva(string empresa, string origen, string destino, string usuarioCreacion) => Task.FromResult<int?>(null);
        public Task MarcarPreparada(string empresa, int numTraspaso, string usuario) => Task.CompletedTask;
        public Task Borrar(string empresa, int numTraspaso) => Task.CompletedTask;
    }

    public class RegistroReposicionesTraspasosSql : IRegistroReposicionesTraspasos
    {
        internal const string SQL_EXISTE_TABLA =
            "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ReposicionesTraspasos') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_CREAR = @"
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte)
    VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        // Reserva sin usar: ni preparada ni recibida, y su número no está en ninguna fila de PreExtrProducto (las líneas en
        // preparación no llevan número; en cuanto se numeran, Terminar la marca preparada). PreExtrProducto es pequeña.
        private const string SIN_USAR = @"
  AND r.FechaPreparada IS NULL AND r.FechaRecibida IS NULL
  AND NOT EXISTS (SELECT 1 FROM PreExtrProducto p WHERE p.Empresa = r.Empresa AND p.[NºTraspaso] = r.NumTraspaso)";

        internal const string SQL_RESERVA_DEL_ORIGEN = @"
SELECT TOP 1 r.NumTraspaso FROM dbo.ReposicionesTraspasos r WITH (UPDLOCK, HOLDLOCK)
WHERE r.Empresa = @p0 AND r.Origen = @p1" + SIN_USAR + @"
ORDER BY r.FechaCreacion DESC";

        internal const string SQL_REUTILIZAR = @"
UPDATE dbo.ReposicionesTraspasos
SET Destino = @p2, Herramienta = @p3, UsuarioCreacion = @p4, FechaCreacion = @p5, FechaCorte = @p6
WHERE Empresa = @p0 AND NumTraspaso = @p1";

        internal const string SQL_INSERTAR_RESERVA = @"
INSERT INTO dbo.ReposicionesTraspasos (Empresa, NumTraspaso, Origen, Destino, Herramienta, UsuarioCreacion, FechaCreacion, FechaCorte)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)";

        internal const string SQL_LEER_RESERVA = @"
SELECT TOP 1 r.NumTraspaso FROM dbo.ReposicionesTraspasos r WITH (UPDLOCK, HOLDLOCK)
WHERE r.Empresa = @p0 AND r.Origen = @p1 AND r.Destino = @p2 AND r.UsuarioCreacion = @p3" + SIN_USAR + @"
ORDER BY r.FechaCreacion DESC";

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
        private readonly INumeradorTraspasos numerador;

        public RegistroReposicionesTraspasosSql(NVEntities db, INumeradorTraspasos numerador = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.numerador = numerador ?? new NumeradorTraspasosSql();
        }

        public Task Crear(CabeceraReposicionTraspaso cabecera)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_CREAR, ParametrosCabecera(cabecera, cabecera.NumTraspaso));
        }

        public async Task<int?> Reservar(CabeceraReposicionTraspaso cabecera)
        {
            if (!await ExisteTabla().ConfigureAwait(false))
            {
                return null;
            }
            int? sinUsar = (await db.Database.SqlQuery<int>(SQL_RESERVA_DEL_ORIGEN, Char("@p0", cabecera.Empresa, 3), Char("@p1", cabecera.Origen, 3))
                .ToListAsync().ConfigureAwait(false)).Cast<int?>().FirstOrDefault();
            if (sinUsar.HasValue)
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_REUTILIZAR,
                    Char("@p0", cabecera.Empresa, 3), Entero("@p1", sinUsar.Value), Char("@p2", cabecera.Destino, 3),
                    VarChar("@p3", cabecera.Herramienta, 20), VarChar("@p4", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                    Fecha("@p5", cabecera.FechaCreacion), Fecha("@p6", cabecera.FechaCorte)).ConfigureAwait(false);
                return sinUsar;
            }
            int numero = await numerador.Siguiente(db).ConfigureAwait(false);
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_RESERVA, ParametrosCabecera(cabecera, numero)).ConfigureAwait(false);
            return numero;
        }

        public async Task<int?> LeerReserva(string empresa, string origen, string destino, string usuarioCreacion)
        {
            if (!await ExisteTabla().ConfigureAwait(false))
            {
                return null;
            }
            return (await db.Database.SqlQuery<int>(SQL_LEER_RESERVA, Char("@p0", empresa, 3), Char("@p1", origen, 3), Char("@p2", destino, 3),
                    VarChar("@p3", UsuarioAuditoriaHelper.ParaAuditoria(usuarioCreacion), 30))
                .ToListAsync().ConfigureAwait(false)).Cast<int?>().FirstOrDefault();
        }

        public Task MarcarPreparada(string empresa, int numTraspaso, string usuario)
        {
            return MarcarPreparada(db, empresa, numTraspaso, usuario);
        }

        public Task Borrar(string empresa, int numTraspaso)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_BORRAR, Char("@p0", empresa, 3), Entero("@p1", numTraspaso));
        }

        /// <summary>Para las transacciones de Ariadna (Salidas), que llevan su propio contexto.</summary>
        internal static Task MarcarPreparada(NVEntities db, string empresa, int numTraspaso, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_PREPARADA, Char("@p0", empresa, 3), Entero("@p1", numTraspaso),
                VarChar("@p2", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }

        /// <summary>Para la transacción de Ariadna que recibe la reposición (Entradas). Sin cabecera, no hace nada.</summary>
        internal static Task MarcarRecibida(NVEntities db, string empresa, int numTraspaso, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_RECIBIDA, Char("@p0", empresa, 3), Entero("@p1", numTraspaso),
                VarChar("@p2", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }

        private Task<bool> ExisteTabla()
        {
            return db.Database.SqlQuery<bool>(SQL_EXISTE_TABLA).SingleAsync();
        }

        private static object[] ParametrosCabecera(CabeceraReposicionTraspaso cabecera, int numero)
        {
            return new object[]
            {
                Char("@p0", cabecera.Empresa, 3), Entero("@p1", numero), Char("@p2", cabecera.Origen, 3), Char("@p3", cabecera.Destino, 3),
                VarChar("@p4", cabecera.Herramienta, 20), VarChar("@p5", UsuarioAuditoriaHelper.ParaAuditoria(cabecera.UsuarioCreacion), 30),
                Fecha("@p6", cabecera.FechaCreacion), Fecha("@p7", cabecera.FechaCorte)
            };
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };
        }

        private static SqlParameter VarChar(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.VarChar, longitud) { Value = (object)valor ?? DBNull.Value };
        }

        private static SqlParameter Entero(string nombre, int valor)
        {
            return new SqlParameter(nombre, SqlDbType.Int) { Value = valor };
        }

        private static SqlParameter Fecha(string nombre, DateTime? valor)
        {
            return new SqlParameter(nombre, SqlDbType.DateTime) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };
        }
    }
}
