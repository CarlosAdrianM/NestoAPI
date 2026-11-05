using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>
    /// NestoAPI#623: acceso a <c>dbo.OrigenesCliente</c> y <c>dbo.ClientesOrigen</c> (fuera del EDMX, como ClientesDiaReparto;
    /// script <c>Scripts/Issue623_OrigenesCliente.sql</c>). Sin tablas = sin orígenes: las lecturas devuelven vacío.
    /// </summary>
    public interface IRepositorioOrigenesCliente
    {
        /// <summary>El catálogo entero (activos e inactivos). Caché de 10 min. Nunca lanza: vacío si no hay tabla o falla.</summary>
        Task<List<OrigenCliente>> LeerCatalogo();
        /// <summary>El origen del cliente, o null si no tiene (o no hay tabla).</summary>
        Task<OrigenAsignadoCliente> LeerDeCliente(string empresa, string cliente);
        /// <summary>
        /// Pone el origen (si ya tenía ese mismo, no cambia nada: conserva la FechaOrigen) o lo quita (<paramref name="origen"/>
        /// null). Al cambiarlo, FechaOrigen = ahora y FechaAvisoRetraso = null. Deja la fila en Modificaciones.
        /// </summary>
        Task Guardar(string empresa, string cliente, string origen, string usuario);
        /// <summary>
        /// Los clientes con origen de un catálogo activo de los últimos <see cref="ReglasLeadsCampana.DIAS_MAXIMOS_LEAD"/> días, con
        /// su contacto principal (estado &gt;= 0) y su último rapport. Con <paramref name="vendedor"/>, solo los de su ficha.
        /// </summary>
        Task<List<LeadCampana>> LeerLeads(string vendedor);
        /// <summary>El job ya avisó del retraso de este lead.</summary>
        Task MarcarAvisoRetraso(string empresa, string cliente, DateTime fecha);
    }

    public class RepositorioOrigenesClienteSql : IRepositorioOrigenesCliente
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        private const string HAY_TABLAS = "IF OBJECT_ID(N'dbo.OrigenesCliente', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ClientesOrigen', N'U') IS NOT NULL ";

        internal const string SQL_CATALOGO =
            "IF OBJECT_ID(N'dbo.OrigenesCliente', N'U') IS NOT NULL " +
            "SELECT RTRIM(Codigo) AS Codigo, Descripcion, Campana, CAST(FechaDesde AS datetime) AS FechaDesde, " +
            "CAST(FechaHasta AS datetime) AS FechaHasta, Activo, HorasParaLlamar FROM dbo.OrigenesCliente WITH (NOLOCK)";

        internal const string SQL_DE_CLIENTE = HAY_TABLAS +
            "SELECT RTRIM(co.Empresa) AS Empresa, RTRIM(co.Cliente) AS Cliente, RTRIM(co.Origen) AS Origen, o.Descripcion, " +
            "co.FechaOrigen, co.UsuarioOrigen, co.FechaAvisoRetraso " +
            "FROM dbo.ClientesOrigen co WITH (NOLOCK) LEFT JOIN dbo.OrigenesCliente o WITH (NOLOCK) ON o.Codigo = co.Origen " +
            "WHERE co.Empresa = @empresa AND co.Cliente = @cliente";

        internal const string SQL_GUARDAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @anterior varchar(30) = (SELECT RTRIM(Origen) FROM dbo.ClientesOrigen WITH (UPDLOCK, HOLDLOCK)
                                 WHERE Empresa = @empresa AND Cliente = @cliente);
IF @origen IS NULL
    DELETE FROM dbo.ClientesOrigen WHERE Empresa = @empresa AND Cliente = @cliente;
ELSE IF @anterior IS NULL
    INSERT INTO dbo.ClientesOrigen (Empresa, Cliente, Origen, FechaOrigen, UsuarioOrigen)
    VALUES (@empresa, @cliente, @origen, GETDATE(), @usuario);
ELSE IF @anterior <> @origen
    UPDATE dbo.ClientesOrigen SET Origen = @origen, FechaOrigen = GETDATE(), UsuarioOrigen = @usuario, FechaAvisoRetraso = NULL
    WHERE Empresa = @empresa AND Cliente = @cliente;
IF ISNULL(@anterior, '') <> ISNULL(@origen, '')
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    VALUES (N'ClientesOrigen', CONCAT(RTRIM(@cliente), N': ', ISNULL(@anterior, N'sin origen')),
            CONCAT(RTRIM(@cliente), N': ', ISNULL(@origen, N'sin origen')), @usuario);
COMMIT TRANSACTION;";

        internal const string SQL_LEADS = HAY_TABLAS + @"
SELECT RTRIM(co.Empresa) AS Empresa, RTRIM(co.Cliente) AS Cliente, RTRIM(c.Contacto) AS Contacto, c.Nombre,
    c.Dirección AS Direccion, c.CodPostal AS CodigoPostal, c.Teléfono AS Telefono, c.Población AS Poblacion, c.Provincia,
    RTRIM(c.Vendedor) AS Vendedor, RTRIM(co.Origen) AS Origen, o.Descripcion, co.FechaOrigen, o.HorasParaLlamar,
    co.FechaAvisoRetraso,
    (SELECT MAX(s.Fecha) FROM SeguimientoCliente s WITH (NOLOCK)
     WHERE s.Empresa = co.Empresa AND s.Número = co.Cliente AND s.Estado IN (0, 1)) AS UltimoRapport
FROM dbo.ClientesOrigen co WITH (NOLOCK)
INNER JOIN dbo.OrigenesCliente o WITH (NOLOCK) ON o.Codigo = co.Origen
CROSS APPLY (SELECT TOP 1 x.Contacto, x.Nombre, x.Dirección, x.CodPostal, x.Teléfono, x.Población, x.Provincia, x.Vendedor
             FROM Clientes x WITH (NOLOCK)
             WHERE x.Empresa = co.Empresa AND x.[Nº Cliente] = co.Cliente AND x.Estado >= 0
             ORDER BY x.ClientePrincipal DESC, x.Contacto) c
WHERE o.Activo = 1 AND (o.FechaHasta IS NULL OR o.FechaHasta >= CAST(GETDATE() AS date))
    AND co.FechaOrigen >= DATEADD(day, -@dias, GETDATE())
    AND (@vendedor IS NULL OR c.Vendedor = @vendedor);";

        internal const string SQL_MARCAR_AVISO = HAY_TABLAS +
            "UPDATE dbo.ClientesOrigen SET FechaAvisoRetraso = @fecha WHERE Empresa = @empresa AND Cliente = @cliente AND FechaAvisoRetraso IS NULL";

        private static readonly object candado = new object();
        private static List<OrigenCliente> catalogoEnCache = new List<OrigenCliente>();
        private static DateTime catalogoCaducaEn = DateTime.MinValue;

        private readonly Func<NVEntities> crearContexto;

        public RepositorioOrigenesClienteSql(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        /// <summary>La siguiente lectura del catálogo vuelve a la BD.</summary>
        public static void InvalidarCatalogo()
        {
            lock (candado)
            {
                catalogoCaducaEn = DateTime.MinValue;
            }
        }

        public async Task<List<OrigenCliente>> LeerCatalogo()
        {
            DateTime ahora = DateTime.UtcNow;
            lock (candado)
            {
                if (ahora < catalogoCaducaEn)
                {
                    return catalogoEnCache.ToList();
                }
            }
            try
            {
                using (NVEntities db = crearContexto())
                {
                    List<OrigenCliente> filas = await db.Database.SqlQuery<OrigenCliente>(SQL_CATALOGO).ToListAsync().ConfigureAwait(false);
                    lock (candado)
                    {
                        catalogoEnCache = filas;
                        catalogoCaducaEn = ahora + DURACION_CACHE;
                        return catalogoEnCache.ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#623: no se ha podido leer el catálogo de orígenes de cliente: {ex.Message}", ex));
                lock (candado)
                {
                    // Se queda con lo último que leyó y lo vuelve a intentar dentro de un minuto.
                    catalogoCaducaEn = ahora + TimeSpan.FromMinutes(1);
                    return catalogoEnCache.ToList();
                }
            }
        }

        public async Task<OrigenAsignadoCliente> LeerDeCliente(string empresa, string cliente)
        {
            using (NVEntities db = crearContexto())
            {
                List<OrigenAsignadoCliente> filas = await db.Database.SqlQuery<OrigenAsignadoCliente>(SQL_DE_CLIENTE,
                    new SqlParameter("@empresa", empresa ?? string.Empty),
                    new SqlParameter("@cliente", cliente ?? string.Empty)).ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task Guardar(string empresa, string cliente, string origen, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_GUARDAR,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente },
                    new SqlParameter("@origen", SqlDbType.VarChar, 30) { Value = (object)origen ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }).ConfigureAwait(false);
            }
        }

        public async Task<List<LeadCampana>> LeerLeads(string vendedor)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<LeadCampana>(SQL_LEADS,
                    new SqlParameter("@dias", ReglasLeadsCampana.DIAS_MAXIMOS_LEAD),
                    new SqlParameter("@vendedor", SqlDbType.VarChar, 10) { Value = string.IsNullOrWhiteSpace(vendedor) ? (object)DBNull.Value : vendedor.Trim() })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task MarcarAvisoRetraso(string empresa, string cliente, DateTime fecha)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_MARCAR_AVISO,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha }).ConfigureAwait(false);
            }
        }
    }
}
