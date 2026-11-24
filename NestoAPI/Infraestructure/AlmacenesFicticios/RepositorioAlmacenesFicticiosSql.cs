using NestoAPI.Infraestructure.Contadores;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlmacenesFicticios
{
    /// <summary>NestoAPI#894: las lecturas (sustituibles en los tests).</summary>
    public interface IRepositorioAlmacenesFicticios
    {
        /// <summary>Almacenes de la empresa con Estado ≥ 0 (ficticios y reales).</summary>
        Task<List<AlmacenLeido>> LeerAlmacenes(string empresa);
        /// <summary>Los usuarios con «AlmacenFicticio» propio y no vacío (sin «(defecto)»), con su «Vendedor».</summary>
        Task<List<DuenoAlmacenFicticio>> LeerDuenos();
        /// <summary>Un parámetro PROPIO del usuario (sin dominio), sin mirar «(defecto)» ni crear la fila. Null si no lo tiene.</summary>
        Task<string> LeerParametroPropio(string usuarioSinDominio, string clave);
        string VendedorDelUsuario(IPrincipal usuario);
        Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor);
        /// <summary>Saldo ≠ 0 por producto en el almacén (empresas 1 y 3), con nombre y familia.</summary>
        Task<List<ExistenciaAlmacenFicticioDTO>> LeerExistencias(string almacen);
        Task<Dictionary<string, ProductoTraspasoFicticio>> LeerProductos(IEnumerable<string> productos);
        /// <summary>SUM(ExtractoProducto.Cantidad) de las empresas 1 y 3 por producto en el almacén.</summary>
        Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos);
        /// <summary>Lo libre por hueco (estado 0, sin pedido, albarán ni reserva) de los productos en el almacén.</summary>
        Task<List<HuecoLibreFicticio>> LeerHuecosLibres(string almacen, IEnumerable<string> productos);
        /// <summary>Todo o nada.</summary>
        Task<T> EnTransaccion<T>(Func<ITransaccionAlmacenFicticio, Task<T>> trabajo);
    }

    /// <summary>NestoAPI#894: lo que se hace dentro de la transacción del traspaso.</summary>
    public interface ITransaccionAlmacenFicticio
    {
        /// <summary>sp_getapplock «TraspasoAlmacenFicticio:{empresa}»: el diario se contabiliza entero, uno detrás de otro.</summary>
        Task Bloquear(string empresa);
        /// <summary>El resultado guardado con esa clave (Modificaciones), o null.</summary>
        Task<ResultadoTraspasoFicticioDTO> LeerPorClave(Guid clave);
        /// <summary>Como <see cref="IRepositorioAlmacenesFicticios.LeerStock"/>, bloqueado hasta el final.</summary>
        Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos);
        Task<int> SiguienteTraspaso();
        /// <summary>Por el núcleo <see cref="MovimientoTraspasoStockSql"/> (prdExtrProducto por su único punto de llamada).</summary>
        Task<List<int?>> Contabilizar(int traspaso, IReadOnlyList<LineaMovimientoTraspaso> lineas, string usuario);
        /// <summary>Por la puerta única de Ubicaciones.</summary>
        Task SacarDeHueco(string almacen, string producto, HuecoUbicacion hueco, int cantidad, int traspaso, string usuario);
        /// <summary>Por la puerta única de Ubicaciones: «pendiente de ubicar».</summary>
        Task DejarPendienteDeUbicar(string almacen, string producto, int cantidad, int traspaso, string usuario);
        /// <summary>Una fila en Modificaciones (Tabla «TraspasoAlmacenFicticio»; Anterior = la clave o «Traspaso N»; Nuevo = el resultado).</summary>
        Task ApuntarRastro(string anterior, string nuevo, string usuario);
    }

    /// <summary>NestoAPI#894: el SQL (Almacenes y ParametrosUsuario por consulta: Almacenes no está en el EDMX).</summary>
    public class RepositorioAlmacenesFicticiosSql : IRepositorioAlmacenesFicticios
    {
        internal const string SQL_ALMACENES = @"
SELECT RTRIM([Número]) AS Codigo, RTRIM([Descripción]) AS Descripcion,
       CAST(ISNULL(AlmacénFicticio, 0) AS bit) AS EsFicticio,
       CAST(ISNULL(PermitirNegativo, 0) AS bit) AS PermiteNegativo,
       CAST(ISNULL(ControlUbicaciones, 0) AS bit) AS ControlUbicaciones
FROM Almacenes
WHERE Empresa = @p0 AND ISNULL(Estado, 0) >= 0
ORDER BY [Número]";

        internal const string SQL_DUENOS = @"
SELECT RTRIM(f.Usuario) AS Usuario, UPPER(LTRIM(RTRIM(f.Valor))) AS Almacen, NULLIF(LTRIM(RTRIM(v.Valor)), '') AS Vendedor
FROM ParametrosUsuario f
LEFT JOIN ParametrosUsuario v ON v.Empresa = f.Empresa AND v.Usuario = f.Usuario AND v.Clave = 'Vendedor'
WHERE f.Empresa = @p0 AND f.Clave = @p1 AND f.Usuario <> '(defecto)' AND LTRIM(RTRIM(ISNULL(f.Valor, ''))) <> ''";

        internal const string SQL_PARAMETRO_PROPIO = @"
SELECT TOP 1 NULLIF(LTRIM(RTRIM(Valor)), '') FROM ParametrosUsuario WHERE Empresa = @p0 AND Usuario = @p1 AND Clave = @p2";

        // Sin costes ni precios: solo unidades
        internal const string SQL_EXISTENCIAS = @"
SELECT RTRIM(e.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia, RTRIM(f.[Descripción]) AS NombreFamilia,
       CAST(SUM(e.Cantidad) AS int) AS Unidades
FROM ExtractoProducto e
LEFT JOIN Productos p ON p.Empresa = @p0 AND p.[Número] = e.[Número]
LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
WHERE e.Empresa IN (@p0, @p1) AND e.[Almacén] = @p2
GROUP BY e.[Número], p.Nombre, p.Familia, f.[Descripción]
HAVING SUM(e.Cantidad) <> 0
ORDER BY p.Nombre, e.[Número]";

        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM([Número]) AS Producto, RTRIM(Nombre) AS Nombre, RTRIM(Familia) AS Familia, CAST(ISNULL(Estado, 0) AS int) AS Estado,
       CAST(Ficticio AS bit) AS Ficticio, CAST(Ubicar AS bit) AS Ubicar
FROM Productos WHERE Empresa = @p0 AND [Número] IN ({LISTA})";

        // {BLOQUEO}: vacío fuera de la transacción; WITH (UPDLOCK, HOLDLOCK) dentro
        internal const string SQL_STOCK = @"
SELECT RTRIM([Número]) AS Producto, CAST(NULL AS nvarchar(50)) AS Nombre, CAST(SUM(Cantidad) AS int) AS Unidades
FROM ExtractoProducto {BLOQUEO}
WHERE Empresa IN (@p0, @p1) AND [Almacén] = @p2 AND [Número] IN ({LISTA})
GROUP BY [Número]";

        internal const string SQL_HUECOS_LIBRES = @"
SELECT RTRIM([Número]) AS Producto, RTRIM(Pasillo) AS Pasillo, RTRIM(Fila) AS Fila, RTRIM(Columna) AS Columna,
       CAST(SUM(Cantidad) AS int) AS Libres, ISNULL(MIN(FechaCreación), '19000101') AS Desde
FROM Ubicaciones
WHERE (Empresa = @p0 OR Empresa = @p1) AND [Almacén] = @p2 AND [Número] IN ({LISTA}) AND Estado = 0 AND Cantidad > 0
  AND PedidoVta IS NULL AND [AlbaránVta] IS NULL AND [NºOrdenVta] IS NULL AND [NºOrdenRepo] IS NULL
  AND Pasillo IS NOT NULL AND Fila IS NOT NULL AND Columna IS NOT NULL
GROUP BY RTRIM([Número]), RTRIM(Pasillo), RTRIM(Fila), RTRIM(Columna)";

        internal const string SQL_BLOQUEO = @"
DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
SELECT @r;";

        internal const string SQL_POR_CLAVE = @"
SELECT TOP 1 Nuevo FROM Modificaciones WHERE Tabla = @p0 AND Anterior = @p1 ORDER BY id DESC";

        internal const string SQL_RASTRO = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@p0, @p1, @p2, @p3)";

        private readonly NVEntities db;
        private readonly LectorVendedoresInscripcion vendedores;
        private readonly IServicioExtractoProducto extractos;
        private readonly INumeradorTraspasos numerador;

        public RepositorioAlmacenesFicticiosSql(NVEntities db, IServicioVendedores servicioVendedores = null,
            IServicioExtractoProducto extractos = null, INumeradorTraspasos numerador = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            vendedores = new LectorVendedoresInscripcion(db, servicioVendedores);
            this.extractos = extractos ?? new ServicioExtractoProducto();
            this.numerador = numerador ?? new NumeradorTraspasosSql();
        }

        private static string Empresa => Constantes.Empresas.EMPRESA_POR_DEFECTO;
        private static string Espejo => Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;

        public Task<List<AlmacenLeido>> LeerAlmacenes(string empresa) =>
            db.Database.SqlQuery<AlmacenLeido>(SQL_ALMACENES, Char("@p0", empresa ?? Empresa, 3)).ToListAsync();

        public Task<List<DuenoAlmacenFicticio>> LeerDuenos() =>
            db.Database.SqlQuery<DuenoAlmacenFicticio>(SQL_DUENOS, Char("@p0", Empresa, 3),
                new SqlParameter("@p1", SqlDbType.VarChar, 50) { Value = ReglasAlmacenesFicticios.CLAVE_PARAMETRO }).ToListAsync();

        public async Task<string> LeerParametroPropio(string usuarioSinDominio, string clave)
        {
            if (string.IsNullOrWhiteSpace(usuarioSinDominio))
            {
                return null;
            }
            return await db.Database.SqlQuery<string>(SQL_PARAMETRO_PROPIO, Char("@p0", Empresa, 3),
                new SqlParameter("@p1", SqlDbType.VarChar, 50) { Value = usuarioSinDominio.Trim() },
                new SqlParameter("@p2", SqlDbType.VarChar, 50) { Value = clave }).FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public string VendedorDelUsuario(IPrincipal usuario) => vendedores.VendedorDelUsuario(usuario);

        public Task<List<string>> VendedoresDelEquipo(string empresa, string vendedor) => vendedores.VendedoresDelEquipo(empresa, vendedor);

        public Task<List<ExistenciaAlmacenFicticioDTO>> LeerExistencias(string almacen) =>
            db.Database.SqlQuery<ExistenciaAlmacenFicticioDTO>(SQL_EXISTENCIAS, Char("@p0", Empresa, 3), Char("@p1", Espejo, 3),
                Char("@p2", almacen, 3)).ToListAsync();

        public async Task<Dictionary<string, ProductoTraspasoFicticio>> LeerProductos(IEnumerable<string> productos)
        {
            List<SqlParameter> lista = Lista(productos, out string nombres);
            var resultado = new Dictionary<string, ProductoTraspasoFicticio>(StringComparer.OrdinalIgnoreCase);
            if (lista.Count == 0)
            {
                return resultado;
            }
            lista.Insert(0, Char("@p0", Empresa, 3));
            foreach (ProductoTraspasoFicticio p in await db.Database.SqlQuery<ProductoTraspasoFicticio>(SQL_PRODUCTOS.Replace("{LISTA}", nombres),
                lista.ToArray()).ToListAsync().ConfigureAwait(false))
            {
                resultado[p.Producto.Trim()] = p;
            }
            return resultado;
        }

        public Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos) => Stock(db, almacen, productos, bloquear: false);

        internal static async Task<Dictionary<string, int>> Stock(NVEntities db, string almacen, IEnumerable<string> productos, bool bloquear)
        {
            List<SqlParameter> lista = Lista(productos, out string nombres);
            var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (lista.Count == 0)
            {
                return resultado;
            }
            lista.InsertRange(0, new[] { Char("@p0", Empresa, 3), Char("@p1", Espejo, 3), Char("@p2", almacen, 3) });
            string sql = SQL_STOCK.Replace("{BLOQUEO}", bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty).Replace("{LISTA}", nombres);
            foreach (SaldoAlmacenFicticioDTO fila in await db.Database.SqlQuery<SaldoAlmacenFicticioDTO>(sql, lista.ToArray()).ToListAsync().ConfigureAwait(false))
            {
                resultado[fila.Producto.Trim()] = fila.Unidades;
            }
            return resultado;
        }

        public async Task<List<HuecoLibreFicticio>> LeerHuecosLibres(string almacen, IEnumerable<string> productos)
        {
            List<SqlParameter> lista = Lista(productos, out string nombres);
            if (lista.Count == 0)
            {
                return new List<HuecoLibreFicticio>();
            }
            lista.InsertRange(0, new[] { Char("@p0", Empresa, 3), Char("@p1", Espejo, 3), Char("@p2", almacen, 3) });
            return await db.Database.SqlQuery<HuecoLibreFicticio>(SQL_HUECOS_LIBRES.Replace("{LISTA}", nombres), lista.ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<T> EnTransaccion<T>(Func<ITransaccionAlmacenFicticio, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(new TransaccionAlmacenFicticioSql(db, extractos, numerador)).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception ex)
                {
                    transaccion.RollbackSeguro(); // prdExtrProducto al fallar ya hace su propio ROLLBACK
                    foreach (System.Data.Entity.Infrastructure.DbEntityEntry entrada in db.ChangeTracker.Entries().ToList())
                    {
                        entrada.State = EntityState.Detached;
                    }
                    SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        throw new NestoBusinessException(sql.Message, ex);
                    }
                    throw;
                }
            }
        }

        internal static List<SqlParameter> Lista(IEnumerable<string> productos, out string nombres)
        {
            List<SqlParameter> lista = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((p, i) => new SqlParameter("@l" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.Char, 15) { Value = p })
                .ToList();
            nombres = string.Join(", ", lista.Select(p => p.ParameterName));
            return lista;
        }

        internal static SqlParameter Char(string nombre, string valor, int longitud) =>
            new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };

        private class TransaccionAlmacenFicticioSql : ITransaccionAlmacenFicticio
        {
            private readonly NVEntities db;
            private readonly IServicioExtractoProducto extractos;
            private readonly INumeradorTraspasos numerador;
            private readonly IPuertaUbicaciones puerta;

            public TransaccionAlmacenFicticioSql(NVEntities db, IServicioExtractoProducto extractos, INumeradorTraspasos numerador)
            {
                this.db = db;
                this.extractos = extractos;
                this.numerador = numerador;
                puerta = new PuertaUbicacionesSql(db);
            }

            public async Task Bloquear(string empresa)
            {
                int r = await db.Database.SqlQuery<int>(SQL_BLOQUEO,
                    new SqlParameter("@p0", SqlDbType.NVarChar, 255) { Value = ReglasAlmacenesFicticios.TABLA_MODIFICACIONES + ":" + empresa?.Trim() })
                    .SingleAsync().ConfigureAwait(false);
                if (r < 0)
                {
                    throw new NestoBusinessException("Otro traspaso con un almacén ficticio se está guardando ahora mismo: vuelva a intentarlo en un momento.")
                    {
                        StatusCode = System.Net.HttpStatusCode.Conflict
                    };
                }
            }

            public async Task<ResultadoTraspasoFicticioDTO> LeerPorClave(Guid clave)
            {
                string nuevo = await db.Database.SqlQuery<string>(SQL_POR_CLAVE,
                    new SqlParameter("@p0", SqlDbType.VarChar, 50) { Value = ReglasAlmacenesFicticios.TABLA_MODIFICACIONES },
                    new SqlParameter("@p1", SqlDbType.NVarChar, -1) { Value = clave.ToString("D", CultureInfo.InvariantCulture) })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                return nuevo == null ? null : JsonConvert.DeserializeObject<ResultadoTraspasoFicticioDTO>(nuevo);
            }

            public Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos) => Stock(db, almacen, productos, bloquear: true);

            public Task<int> SiguienteTraspaso() => numerador.Siguiente(db);

            public Task<List<int?>> Contabilizar(int traspaso, IReadOnlyList<LineaMovimientoTraspaso> lineas, string usuario) =>
                MovimientoTraspasoStockSql.Contabilizar(db, extractos, Empresa, ReglasAlmacenesFicticios.DIARIO, traspaso, lineas, usuario);

            public Task SacarDeHueco(string almacen, string producto, HuecoUbicacion hueco, int cantidad, int traspaso, string usuario) =>
                puerta.SalidaDeHuecoAFicticio(Empresa, almacen, producto, hueco, cantidad, traspaso, usuario);

            public Task DejarPendienteDeUbicar(string almacen, string producto, int cantidad, int traspaso, string usuario) =>
                puerta.EntradaDeFicticio(Empresa, almacen, producto, cantidad, traspaso, usuario);

            public Task ApuntarRastro(string anterior, string nuevo, string usuario) =>
                db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                    new SqlParameter("@p0", SqlDbType.VarChar, 50) { Value = ReglasAlmacenesFicticios.TABLA_MODIFICACIONES },
                    new SqlParameter("@p1", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                    new SqlParameter("@p2", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                    new SqlParameter("@p3", SqlDbType.VarChar, 50) { Value = usuario == null ? (object)DBNull.Value : (usuario.Length > 50 ? usuario.Substring(0, 50) : usuario) });
        }
    }
}
