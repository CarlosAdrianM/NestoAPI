using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.Contadores;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ProductosIncompletos
{
    /// <summary>NestoAPI#902: las lecturas (sustituibles en los tests).</summary>
    public interface IRepositorioProductosIncompletos
    {
        /// <summary>Si existe dbo.ProductosIncompletos (sin ella, 503).</summary>
        Task<bool> TablaCreada();
        /// <summary>ParametrosUsuario empresa 1, «(defecto)», AlmacenIncompletos (null si no está o está vacío).</summary>
        Task<string> LeerAlmacenIncompletos();
        /// <summary>Almacenes de la empresa con Estado ≥ 0.</summary>
        Task<List<AlmacenLeido>> LeerAlmacenes();
        /// <summary>Un parámetro PROPIO del usuario (sin dominio), sin mirar «(defecto)» ni crear la fila.</summary>
        Task<string> LeerParametroPropio(string usuarioSinDominio, string clave);
        Task<Dictionary<string, ProductoIncompletoLeido>> LeerProductos(IEnumerable<string> productos);
        /// <summary>SUM(ExtractoProducto.Cantidad) de las empresas 1 y 3 por producto en el almacén.</summary>
        Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos);
        /// <summary>Lo libre por hueco de los productos en el almacén.</summary>
        Task<List<HuecoLibreFicticio>> LeerHuecosLibres(string almacen, IEnumerable<string> productos);
        /// <summary>Las fichas (con nombres y PVP); filtros opcionales.</summary>
        Task<List<ProductoIncompletoDTO>> LeerFichas(bool soloVivas, string almacen, string producto);
        Task<ProductoIncompletoDTO> LeerFicha(int id);
        /// <summary>Las piezas que alguna vez se han sacado de ese producto, la más usada primero.</summary>
        Task<List<PiezaSacadaDTO>> LeerPiezasSacadas(string producto);
        /// <summary>Todo o nada.</summary>
        Task<T> EnTransaccion<T>(Func<ITransaccionProductosIncompletos, Task<T>> trabajo);
    }

    /// <summary>NestoAPI#902: lo que se hace dentro de la transacción.</summary>
    public interface ITransaccionProductosIncompletos
    {
        /// <summary>sp_getapplock «ProductoIncompleto:{empresa}»: el diario se contabiliza entero, uno detrás de otro.</summary>
        Task Bloquear(string empresa);
        /// <summary>La ficha creada con esa clave, o null.</summary>
        Task<ProductoIncompletoDTO> LeerPorClave(Guid clave);
        /// <summary>Las fichas vivas del donante, bloqueadas hasta el final.</summary>
        Task<List<ProductoIncompletoDTO>> LeerVivasDelDonante(string producto);
        /// <summary>La ficha, bloqueada hasta el final.</summary>
        Task<ProductoIncompletoDTO> LeerFicha(int id);
        /// <summary>Como <see cref="IRepositorioProductosIncompletos.LeerStock"/>, bloqueado hasta el final.</summary>
        Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos);
        Task<int> SiguienteTraspaso();
        /// <summary>Por el núcleo <see cref="MovimientoTraspasoStockSql"/> (PreExtrProducto + prdExtrProducto por su único punto de llamada).</summary>
        Task Contabilizar(string diario, int traspaso, IReadOnlyList<LineaMovimientoTraspaso> lineas, string usuario);
        /// <summary>Por la puerta única de Ubicaciones (<see cref="IPuertaUbicaciones.SalidaPorProductoIncompleto"/>).</summary>
        Task SacarDeUbicaciones(string almacen, string producto, HuecoUbicacion hueco, int cantidad, int traspaso, string usuario);
        /// <summary>Por la puerta única de Ubicaciones: «pendiente de ubicar».</summary>
        Task DejarPendienteDeUbicar(string almacen, string producto, int cantidad, int traspaso, string usuario);
        /// <summary>Inserta la ficha y devuelve su Id.</summary>
        Task<int> Insertar(ProductoIncompletoDTO ficha, Guid? clave);
        /// <summary>Cierra la ficha (solo si sigue Incompleto; si no, 409).</summary>
        Task Cerrar(int id, string estado, string motivo, int? traspaso, string usuario);
        /// <summary>Una fila en Modificaciones (Tabla «ProductoIncompleto»).</summary>
        Task ApuntarRastro(string anterior, string nuevo, string usuario);
    }

    /// <summary>NestoAPI#902: el SQL. dbo.ProductosIncompletos está fuera del EDMX.</summary>
    public class RepositorioProductosIncompletosSql : IRepositorioProductosIncompletos
    {
        internal const string SQL_TABLA = "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ProductosIncompletos', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_PARAMETRO_DEFECTO = @"
SELECT TOP 1 NULLIF(UPPER(LTRIM(RTRIM(Valor))), '') FROM ParametrosUsuario WHERE Empresa = @p0 AND Usuario = '(defecto)' AND Clave = @p1";

        internal const string SQL_PRODUCTOS = @"
SELECT RTRIM([Número]) AS Producto, RTRIM(Nombre) AS Nombre, CAST(ISNULL(Estado, 0) AS int) AS Estado,
       CAST(Ficticio AS bit) AS Ficticio, CAST(Ubicar AS bit) AS Ubicar, CAST(NecesitaNumSerie AS bit) AS NecesitaNumSerie,
       CAST(PVP AS decimal(19, 4)) AS Pvp
FROM Productos WHERE Empresa = @p0 AND [Número] IN ({LISTA})";

        // {FILTRO}: el WHERE de cada lectura; {BLOQUEO}: vacío o WITH (UPDLOCK, HOLDLOCK)
        internal const string SQL_FICHAS = @"
SELECT i.Id, RTRIM(i.Empresa) AS Empresa, RTRIM(i.Almacen) AS Almacen, RTRIM(i.AlmacenIncompletos) AS AlmacenIncompletos,
       RTRIM(i.ProductoDonante) AS ProductoDonante, RTRIM(d.Nombre) AS NombreDonante, i.NumeroSerie,
       RTRIM(i.Pieza) AS Pieza, RTRIM(p.Nombre) AS NombrePieza, i.Cantidad, i.Pedido, i.Comentario,
       i.HuecoOrigen, i.HuecoDestino, i.Estado, i.Usuario, i.Fecha, i.Traspaso, i.FechaCierre, i.UsuarioCierre, i.MotivoCierre, i.TraspasoCierre,
       CAST(d.PVP AS decimal(19, 4)) AS PvpDonante, CAST(0 AS int) AS Dias, CAST(NULL AS nvarchar(200)) AS Donde
FROM dbo.ProductosIncompletos i {BLOQUEO}
LEFT JOIN Productos d ON d.Empresa = i.Empresa AND d.[Número] = i.ProductoDonante
LEFT JOIN Productos p ON p.Empresa = i.Empresa AND p.[Número] = i.Pieza
WHERE i.Empresa = @p0 {FILTRO}
ORDER BY i.Fecha, i.Id";

        internal const string SQL_PIEZAS = @"
SELECT RTRIM(i.Pieza) AS Pieza, RTRIM(p.Nombre) AS Nombre, COUNT(*) AS Veces, CAST(SUM(i.Cantidad) AS int) AS Unidades, MAX(i.Fecha) AS UltimaVez
FROM dbo.ProductosIncompletos i
LEFT JOIN Productos p ON p.Empresa = i.Empresa AND p.[Número] = i.Pieza
WHERE i.Empresa = @p0 AND i.ProductoDonante = @p1
GROUP BY i.Pieza, p.Nombre
ORDER BY COUNT(*) DESC, MAX(i.Fecha) DESC";

        internal const string SQL_BLOQUEO = @"
DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
SELECT @r;";

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.ProductosIncompletos (Empresa, Almacen, AlmacenIncompletos, ProductoDonante, NumeroSerie, Pieza, Cantidad, Pedido, Comentario,
    HuecoOrigen, HuecoDestino, Estado, Usuario, Fecha, Traspaso, Clave)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, 'Incompleto', @p11, GETDATE(), @p12, @p13);
SELECT CAST(SCOPE_IDENTITY() AS int);";

        internal const string SQL_CERRAR = @"
UPDATE dbo.ProductosIncompletos
SET Estado = @p1, FechaCierre = GETDATE(), UsuarioCierre = @p2, MotivoCierre = @p3, TraspasoCierre = @p4
WHERE Id = @p0 AND Estado = 'Incompleto'";

        internal const string SQL_RASTRO = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@p0, @p1, @p2, @p3)";

        private readonly NVEntities db;
        private readonly RepositorioAlmacenesFicticiosSql ficticios;
        private readonly IServicioExtractoProducto extractos;
        private readonly INumeradorTraspasos numerador;

        public RepositorioProductosIncompletosSql(NVEntities db, IServicioExtractoProducto extractos = null, INumeradorTraspasos numerador = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.extractos = extractos ?? new ServicioExtractoProducto();
            this.numerador = numerador ?? new NumeradorTraspasosSql();
            ficticios = new RepositorioAlmacenesFicticiosSql(db, null, this.extractos, this.numerador);
        }

        private static string Empresa => Constantes.Empresas.EMPRESA_POR_DEFECTO;

        public Task<bool> TablaCreada() => db.Database.SqlQuery<bool>(SQL_TABLA).SingleAsync();

        public Task<string> LeerAlmacenIncompletos() =>
            db.Database.SqlQuery<string>(SQL_PARAMETRO_DEFECTO, Char("@p0", Empresa, 3),
                new SqlParameter("@p1", SqlDbType.VarChar, 50) { Value = ReglasProductosIncompletos.CLAVE_PARAMETRO }).FirstOrDefaultAsync();

        // Las del núcleo de #894: los mismos almacenes, el mismo stock (empresas 1 y 3) y los mismos huecos libres
        public Task<List<AlmacenLeido>> LeerAlmacenes() => ficticios.LeerAlmacenes(Empresa);

        public Task<string> LeerParametroPropio(string usuarioSinDominio, string clave) => ficticios.LeerParametroPropio(usuarioSinDominio, clave);

        public Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos) => ficticios.LeerStock(almacen, productos);

        public Task<List<HuecoLibreFicticio>> LeerHuecosLibres(string almacen, IEnumerable<string> productos) => ficticios.LeerHuecosLibres(almacen, productos);

        public async Task<Dictionary<string, ProductoIncompletoLeido>> LeerProductos(IEnumerable<string> productos)
        {
            List<SqlParameter> lista = RepositorioAlmacenesFicticiosSql.Lista(productos, out string nombres);
            var resultado = new Dictionary<string, ProductoIncompletoLeido>(StringComparer.OrdinalIgnoreCase);
            if (lista.Count == 0)
            {
                return resultado;
            }
            lista.Insert(0, Char("@p0", Empresa, 3));
            foreach (ProductoIncompletoLeido p in await db.Database.SqlQuery<ProductoIncompletoLeido>(SQL_PRODUCTOS.Replace("{LISTA}", nombres),
                lista.ToArray()).ToListAsync().ConfigureAwait(false))
            {
                resultado[p.Producto.Trim()] = p;
            }
            return resultado;
        }

        public Task<List<ProductoIncompletoDTO>> LeerFichas(bool soloVivas, string almacen, string producto) =>
            Fichas(db, soloVivas, almacen, producto, null, bloquear: false);

        public async Task<ProductoIncompletoDTO> LeerFicha(int id) =>
            (await Fichas(db, false, null, null, id, bloquear: false).ConfigureAwait(false)).FirstOrDefault();

        internal static Task<List<ProductoIncompletoDTO>> Fichas(NVEntities db, bool soloVivas, string almacen, string producto, int? id, bool bloquear)
        {
            var parametros = new List<SqlParameter> { Char("@p0", Empresa, 3) };
            var filtro = new List<string>();
            if (soloVivas)
            {
                filtro.Add("i.Estado = 'Incompleto'");
            }
            if (!string.IsNullOrWhiteSpace(almacen))
            {
                filtro.Add("i.Almacen = @p1");
                parametros.Add(Char("@p1", almacen, 3));
            }
            if (!string.IsNullOrWhiteSpace(producto))
            {
                filtro.Add("i.ProductoDonante = @p2");
                parametros.Add(Char("@p2", producto, 15));
            }
            if (id.HasValue)
            {
                filtro.Add("i.Id = @p3");
                parametros.Add(new SqlParameter("@p3", SqlDbType.Int) { Value = id.Value });
            }
            string sql = SQL_FICHAS
                .Replace("{BLOQUEO}", bloquear ? "WITH (UPDLOCK, HOLDLOCK)" : string.Empty)
                .Replace("{FILTRO}", string.Concat(filtro.Select(f => " AND " + f)));
            return db.Database.SqlQuery<ProductoIncompletoDTO>(sql, parametros.ToArray()).ToListAsync();
        }

        public Task<List<PiezaSacadaDTO>> LeerPiezasSacadas(string producto) =>
            db.Database.SqlQuery<PiezaSacadaDTO>(SQL_PIEZAS, Char("@p0", Empresa, 3), Char("@p1", producto, 15)).ToListAsync();

        public async Task<T> EnTransaccion<T>(Func<ITransaccionProductosIncompletos, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(new TransaccionProductosIncompletosSql(db, extractos, numerador)).ConfigureAwait(false);
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

        internal static SqlParameter Char(string nombre, string valor, int longitud) =>
            new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };

        internal static SqlParameter NVarChar(string nombre, string valor, int longitud)
        {
            string limpio = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            if (limpio != null && longitud > 0 && limpio.Length > longitud)
            {
                limpio = limpio.Substring(0, longitud);
            }
            return new SqlParameter(nombre, SqlDbType.NVarChar, longitud) { Value = (object)limpio ?? DBNull.Value };
        }

        private static SqlParameter Entero(string nombre, int? valor) =>
            new SqlParameter(nombre, SqlDbType.Int) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };

        private class TransaccionProductosIncompletosSql : ITransaccionProductosIncompletos
        {
            private readonly NVEntities db;
            private readonly IServicioExtractoProducto extractos;
            private readonly INumeradorTraspasos numerador;
            private readonly IPuertaUbicaciones puerta;

            public TransaccionProductosIncompletosSql(NVEntities db, IServicioExtractoProducto extractos, INumeradorTraspasos numerador)
            {
                this.db = db;
                this.extractos = extractos;
                this.numerador = numerador;
                puerta = new PuertaUbicacionesSql(db);
            }

            public async Task Bloquear(string empresa)
            {
                int r = await db.Database.SqlQuery<int>(SQL_BLOQUEO,
                    new SqlParameter("@p0", SqlDbType.NVarChar, 255) { Value = ReglasProductosIncompletos.TABLA_MODIFICACIONES + ":" + empresa?.Trim() })
                    .SingleAsync().ConfigureAwait(false);
                if (r < 0)
                {
                    throw new NestoBusinessException("Otra pieza se está sacando o poniendo ahora mismo: vuelva a intentarlo en un momento.")
                    {
                        StatusCode = System.Net.HttpStatusCode.Conflict
                    };
                }
            }

            public async Task<ProductoIncompletoDTO> LeerPorClave(Guid clave)
            {
                string sql = SQL_FICHAS.Replace("{BLOQUEO}", string.Empty).Replace("{FILTRO}", " AND i.Clave = @p1");
                return await db.Database.SqlQuery<ProductoIncompletoDTO>(sql, Char("@p0", Empresa, 3),
                    new SqlParameter("@p1", SqlDbType.UniqueIdentifier) { Value = clave }).FirstOrDefaultAsync().ConfigureAwait(false);
            }

            public Task<List<ProductoIncompletoDTO>> LeerVivasDelDonante(string producto) => Fichas(db, true, null, producto, null, bloquear: true);

            public async Task<ProductoIncompletoDTO> LeerFicha(int id) =>
                (await Fichas(db, false, null, null, id, bloquear: true).ConfigureAwait(false)).FirstOrDefault();

            public Task<Dictionary<string, int>> LeerStock(string almacen, IEnumerable<string> productos) =>
                RepositorioAlmacenesFicticiosSql.Stock(db, almacen, productos, bloquear: true);

            public Task<int> SiguienteTraspaso() => numerador.Siguiente(db);

            public Task Contabilizar(string diario, int traspaso, IReadOnlyList<LineaMovimientoTraspaso> lineas, string usuario) =>
                MovimientoTraspasoStockSql.Contabilizar(db, extractos, Empresa, diario, traspaso, lineas, usuario);

            public Task SacarDeUbicaciones(string almacen, string producto, HuecoUbicacion hueco, int cantidad, int traspaso, string usuario) =>
                puerta.SalidaPorProductoIncompleto(Empresa, almacen, producto, hueco, cantidad, traspaso, usuario);

            public Task DejarPendienteDeUbicar(string almacen, string producto, int cantidad, int traspaso, string usuario) =>
                puerta.EntradaPorProductoIncompleto(Empresa, almacen, producto, cantidad, traspaso, usuario);

            public Task<int> Insertar(ProductoIncompletoDTO ficha, Guid? clave) =>
                db.Database.SqlQuery<int>(SQL_INSERTAR,
                    Char("@p0", Empresa, 3), Char("@p1", ficha.Almacen, 3), Char("@p2", ficha.AlmacenIncompletos, 3),
                    Char("@p3", ficha.ProductoDonante, 15), NVarChar("@p4", ficha.NumeroSerie, ReglasProductosIncompletos.LONGITUD_SERIE),
                    Char("@p5", ficha.Pieza, 15), new SqlParameter("@p6", SqlDbType.Int) { Value = ficha.Cantidad }, Entero("@p7", ficha.Pedido),
                    NVarChar("@p8", ficha.Comentario, ReglasProductosIncompletos.LONGITUD_COMENTARIO),
                    new SqlParameter("@p9", SqlDbType.VarChar, 11) { Value = (object)ficha.HuecoOrigen ?? DBNull.Value },
                    new SqlParameter("@p10", SqlDbType.VarChar, 11) { Value = (object)ficha.HuecoDestino ?? DBNull.Value },
                    new SqlParameter("@p11", SqlDbType.VarChar, 50) { Value = Corto(ficha.Usuario, 50) },
                    Entero("@p12", ficha.Traspaso),
                    new SqlParameter("@p13", SqlDbType.UniqueIdentifier) { Value = clave.HasValue ? (object)clave.Value : DBNull.Value })
                    .SingleAsync();

            public async Task Cerrar(int id, string estado, string motivo, int? traspaso, string usuario)
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                    new SqlParameter("@p0", SqlDbType.Int) { Value = id },
                    new SqlParameter("@p1", SqlDbType.VarChar, 12) { Value = estado },
                    new SqlParameter("@p2", SqlDbType.VarChar, 50) { Value = Corto(usuario, 50) },
                    NVarChar("@p3", motivo, ReglasProductosIncompletos.LONGITUD_COMENTARIO),
                    Entero("@p4", traspaso)).ConfigureAwait(false);
                if (filas != 1)
                {
                    throw new NestoBusinessException($"La ficha {id} ha cambiado mientras se guardaba: vuelva a cargarla. No se ha hecho nada.")
                    {
                        StatusCode = System.Net.HttpStatusCode.Conflict
                    };
                }
            }

            public Task ApuntarRastro(string anterior, string nuevo, string usuario) =>
                db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                    new SqlParameter("@p0", SqlDbType.VarChar, 50) { Value = ReglasProductosIncompletos.TABLA_MODIFICACIONES },
                    new SqlParameter("@p1", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                    new SqlParameter("@p2", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                    new SqlParameter("@p3", SqlDbType.VarChar, 50) { Value = (object)Corto(usuario, 50) ?? DBNull.Value });

            private static string Corto(string texto, int largo) => texto == null ? null : (texto.Length > largo ? texto.Substring(0, largo) : texto);
        }
    }
}
