using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>
    /// NestoAPI#795: la BD de la pantalla de inventario de Ariadna. prdUbicacionesParaInventario por SqlQuery (único call site); lo
    /// demás por ADO, y el rellenado en una transacción con sp_getapplock (como #789).
    /// </summary>
    public class RepositorioInventarioUbicacionesSql : IRepositorioInventarioUbicaciones
    {
        public const int SEGUNDOS_CONSULTA = 120;

        internal const string SQL_UBICACIONES_PARA_INVENTARIO = "EXEC dbo.prdUbicacionesParaInventario @p0";

        internal const string SQL_ALMACEN = "SELECT Descripción, CAST(ISNULL(ControlUbicaciones, 0) AS bit) FROM dbo.Almacenes WHERE Empresa = @Empresa AND Número = @Almacen";

        // Como el SP de contabilizar: Ubicaciones sin filtrar por empresa (en ALG hay alguna de la empresa 3).
        internal const string SQL_PRODUCTOS_POR_UBICACION = @"
SELECT RTRIM(u.Pasillo), RTRIM(u.Fila), RTRIM(u.Columna), COUNT(DISTINCT u.Número)
FROM dbo.Ubicaciones u
WHERE u.Almacén = @Almacen AND u.Estado = 0 AND u.Pasillo IS NOT NULL
GROUP BY RTRIM(u.Pasillo), RTRIM(u.Fila), RTRIM(u.Columna)";

        internal const string SQL_UBICACIONES_RELLENADAS = @"
SELECT RTRIM(i.Pasillo), RTRIM(i.Fila), RTRIM(i.Columna), COUNT(*)
FROM dbo.Inventarios i
WHERE i.Empresa = @Empresa AND i.Almacén = @Almacen AND i.Fecha >= @Desde AND i.Fecha < @Hasta AND i.Estado = 1 AND i.NºTraspaso IS NULL
    AND i.Pasillo IS NOT NULL
GROUP BY RTRIM(i.Pasillo), RTRIM(i.Fila), RTRIM(i.Columna)";

        internal const string SQL_CONTADO_DEL_DIA = @"
SELECT i.NºOrden, RTRIM(i.Número), RTRIM(ISNULL(p.Nombre, i.Descripción)), RTRIM(ISNULL(i.Familia, p.Familia)),
    RTRIM(i.Pasillo), RTRIM(i.Fila), RTRIM(i.Columna), ISNULL(i.StockCalculado, 0), ISNULL(i.StockReal, 0), RTRIM(i.Usuario),
    i.Fecha, i.[Fecha Modificación], i.PorCodigoBarras, i.NºTraspaso
FROM dbo.Inventarios i
LEFT JOIN dbo.Productos p ON p.Empresa = i.Empresa AND p.Número = i.Número
WHERE i.Empresa = @Empresa AND i.Almacén = @Almacen AND i.Fecha >= @Desde AND i.Fecha < @Hasta AND ";

        internal const string SQL_CONTADO = SQL_CONTADO_DEL_DIA + "i.Estado = 1 AND i.NºTraspaso IS NULL";

        /// <summary>NestoAPI#784: las filas ya contabilizadas ese día (lo que «Contar otra vez» volvería a abrir).</summary>
        internal const string SQL_CONTABILIZADO = SQL_CONTADO_DEL_DIA + "i.Estado = 2 AND i.NºTraspaso IS NOT NULL";

        // El teórico de la ubicación: lo LIBRE del hueco (Ubicaciones Estado 0), sumado por producto. Épica #784 (11/11/26, 37215):
        // las reservas (Estado 3 de picking, 4 de reposición) NO cuentan: pueden estar ya recogidas y prdContabilizarInventario solo
        // regulariza las filas de Estado 0, así que contarlas en el teórico bajaba el stock de una unidad que sigue existiendo.
        internal const string SQL_TEORICO_UBICACION = @"
SELECT RTRIM(p.Número), RTRIM(p.Nombre), RTRIM(p.Grupo), RTRIM(p.SubGrupo), RTRIM(p.Familia), CAST(ISNULL(p.Ficticio, 0) AS bit), SUM(u.Cantidad)
FROM dbo.Ubicaciones u
INNER JOIN dbo.Productos p ON p.Empresa = @Empresa AND p.Número = u.Número
WHERE u.Almacén = @Almacen AND u.Estado = 0 AND u.Pasillo = @Pasillo AND u.Fila = @Fila AND u.Columna = @Columna
GROUP BY p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, p.Ficticio";

        // Épica #784 (11/11/26): lo reservado en el hueco (Estado > 0 con pasillo: 3 picking, 4 reposición), por producto y pedido.
        internal const string SQL_RESERVAS_UBICACION = @"
SELECT RTRIM(u.Número), u.PedidoVta, SUM(u.Cantidad)
FROM dbo.Ubicaciones u
WHERE u.Almacén = @Almacen AND u.Estado > 0 AND u.Pasillo = @Pasillo AND u.Fila = @Fila AND u.Columna = @Columna
GROUP BY RTRIM(u.Número), u.PedidoVta";

        // Como #789 (la suma del extracto de las empresas 1 y 3), pero de los productos de la familia o de la referencia, aunque no
        // tengan movimientos (la referencia se rellena con stock 0; la familia se queda con los de stock distinto de cero en el servicio).
        internal const string SQL_STOCKS = @"
SELECT RTRIM(p.Número), RTRIM(p.Nombre), RTRIM(p.Grupo), RTRIM(p.SubGrupo), RTRIM(p.Familia), CAST(ISNULL(p.Ficticio, 0) AS bit),
    ISNULL(SUM(CAST(e.Cantidad AS int)), 0)
FROM dbo.Productos p
LEFT JOIN dbo.ExtractoProducto e ON e.Número = p.Número AND e.Empresa IN ('1', '3') AND e.Almacén = @Almacen
WHERE p.Empresa = @Empresa AND ";

        private readonly NVEntities db;
        private readonly string cadenaConexion;

        public RepositorioInventarioUbicacionesSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            cadenaConexion = db.Database.Connection.ConnectionString;
        }

        public async Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL_ALMACEN, conexion))
                {
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await lector.ReadAsync().ConfigureAwait(false))
                        {
                            return null;
                        }
                        return new AlmacenInventario
                        {
                            Nombre = Texto(lector, 0),
                            ControlUbicaciones = !lector.IsDBNull(1) && lector.GetBoolean(1)
                        };
                    }
                }
            }
        }

        public async Task<List<UbicacionPrioridadInventario>> UbicacionesParaInventario(int numero)
        {
            db.Database.CommandTimeout = SEGUNDOS_CONSULTA;
            // El SP devuelve pasillo, fila, columna (char(3)) y DiasUltInventario; SqlQuery empareja sin distinguir mayúsculas.
            List<UbicacionPrioridadInventario> ubicaciones = await db.Database
                .SqlQuery<UbicacionPrioridadInventario>(SQL_UBICACIONES_PARA_INVENTARIO, numero)
                .ToListAsync().ConfigureAwait(false);
            return ubicaciones;
        }

        public async Task<List<UbicacionConProductos>> ProductosPorUbicacion(string almacen)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL_PRODUCTOS_POR_UBICACION, conexion) { CommandTimeout = SEGUNDOS_CONSULTA })
                {
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    return await LeerUbicaciones(comando).ConfigureAwait(false);
                }
            }
        }

        public async Task<List<UbicacionConProductos>> UbicacionesRellenadas(string empresa, string almacen, DateTime fecha)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL_UBICACIONES_RELLENADAS, conexion) { CommandTimeout = SEGUNDOS_CONSULTA })
                {
                    ParametrosDia(comando, empresa, almacen, fecha);
                    return await LeerUbicaciones(comando).ConfigureAwait(false);
                }
            }
        }

        public async Task<List<LineaInventarioContadaDTO>> LeerContado(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlCommand comando = ComandoContado(conexion, null, empresa, almacen, fecha, filtro))
                {
                    return await LeerLineas(comando).ConfigureAwait(false);
                }
            }
        }

        public async Task<IUnidadInventarioUbicaciones> Empezar(string recurso)
        {
            var conexion = new SqlConnection(cadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted);
                var unidad = new UnidadInventarioUbicacionesSql(conexion, transaccion);
                await unidad.Bloquear(recurso).ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        /// <param name="contabilizadas">NestoAPI#784: las contabilizadas (Estado 2, con traspaso) en vez de las pendientes.</param>
        internal static SqlCommand ComandoContado(SqlConnection conexion, SqlTransaction transaccion, string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro,
            bool contabilizadas = false)
        {
            var sql = new StringBuilder(contabilizadas ? SQL_CONTABILIZADO : SQL_CONTADO);
            var comando = new SqlCommand { Connection = conexion, Transaction = transaccion, CommandTimeout = SEGUNDOS_CONSULTA };
            ParametrosDia(comando, empresa, almacen, fecha);
            filtro = filtro ?? new FiltroInventarioContado();
            Filtrar(comando, sql, "i.Pasillo", "@Pasillo", SqlDbType.Char, 3, filtro.Pasillo);
            Filtrar(comando, sql, "i.Fila", "@Fila", SqlDbType.Char, 3, filtro.Fila);
            Filtrar(comando, sql, "i.Columna", "@Columna", SqlDbType.Char, 3, filtro.Columna);
            Filtrar(comando, sql, "i.Número", "@Producto", SqlDbType.Char, 15, filtro.Producto);
            Filtrar(comando, sql, "i.Familia", "@Familia", SqlDbType.Char, 10, filtro.Familia);
            sql.Append(" ORDER BY i.Pasillo, i.Fila, i.Columna, i.Número, i.NºOrden");
            comando.CommandText = sql.ToString();
            return comando;
        }

        private static void Filtrar(SqlCommand comando, StringBuilder sql, string columna, string parametro, SqlDbType tipo, int longitud, string valor)
        {
            if (valor == null)
            {
                return;
            }
            sql.Append($" AND {columna} = {parametro}");
            comando.Parameters.Add(parametro, tipo, longitud).Value = valor;
        }

        private static void ParametrosDia(SqlCommand comando, string empresa, string almacen, DateTime fecha)
        {
            comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
            comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
            comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = fecha.Date;
            comando.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = fecha.Date.AddDays(1);
        }

        private static async Task<List<UbicacionConProductos>> LeerUbicaciones(SqlCommand comando)
        {
            var resultado = new List<UbicacionConProductos>();
            using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await lector.ReadAsync().ConfigureAwait(false))
                {
                    resultado.Add(new UbicacionConProductos
                    {
                        Pasillo = Texto(lector, 0),
                        Fila = Texto(lector, 1),
                        Columna = Texto(lector, 2),
                        Productos = lector.GetInt32(3)
                    });
                }
            }
            return resultado;
        }

        internal static async Task<List<LineaInventarioContadaDTO>> LeerLineas(SqlCommand comando)
        {
            var resultado = new List<LineaInventarioContadaDTO>();
            using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await lector.ReadAsync().ConfigureAwait(false))
                {
                    resultado.Add(new LineaInventarioContadaDTO
                    {
                        NumeroOrden = lector.GetInt32(0),
                        Producto = Texto(lector, 1),
                        Descripcion = Texto(lector, 2),
                        Familia = Texto(lector, 3),
                        Pasillo = Texto(lector, 4),
                        Fila = Texto(lector, 5),
                        Columna = Texto(lector, 6),
                        StockCalculado = lector.GetInt32(7),
                        StockReal = lector.GetInt32(8),
                        Usuario = Texto(lector, 9),
                        Fecha = lector.GetDateTime(10),
                        FechaModificacion = lector.GetDateTime(11),
                        PorCodigoBarras = lector.IsDBNull(12) ? (bool?)null : lector.GetBoolean(12),
                        Traspaso = lector.IsDBNull(13) ? (int?)null : lector.GetInt32(13)
                    });
                }
            }
            return resultado;
        }

        internal static async Task<List<StockProductoInventario>> LeerStocks(SqlCommand comando)
        {
            var resultado = new List<StockProductoInventario>();
            using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await lector.ReadAsync().ConfigureAwait(false))
                {
                    resultado.Add(new StockProductoInventario
                    {
                        Numero = Texto(lector, 0),
                        Descripcion = Texto(lector, 1),
                        Grupo = Texto(lector, 2),
                        SubGrupo = Texto(lector, 3),
                        Familia = Texto(lector, 4),
                        Ficticio = !lector.IsDBNull(5) && lector.GetBoolean(5),
                        Stock = lector.IsDBNull(6) ? 0 : lector.GetInt32(6)
                    });
                }
            }
            return resultado;
        }

        private const int FILAS_POR_LOTE_INSERTAR = 200;

        /// <summary>El INSERT de las filas (con su ubicación), por lotes, dentro de la transacción. Lo usan el rellenado y el «a cero» (Sugerencia 1185).</summary>
        internal static async Task InsertarFilas(SqlConnection conexion, SqlTransaction transaccion, IList<Inventario> filas)
        {
            for (int inicio = 0; inicio < filas.Count; inicio += FILAS_POR_LOTE_INSERTAR)
            {
                List<Inventario> lote = filas.Skip(inicio).Take(FILAS_POR_LOTE_INSERTAR).ToList();
                var sql = new StringBuilder("INSERT INTO Inventarios (Empresa, Almacén, Fecha, Número, Descripción, Grupo, Subgrupo, Familia, StockCalculado, StockReal, Estado, Aplicacion, Usuario, Pasillo, Fila, Columna) VALUES ");
                using (var comando = new SqlCommand(string.Empty, conexion, transaccion))
                {
                    Inventario primera = lote[0];
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = primera.Empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = primera.Almacén;
                    comando.Parameters.Add("@Fecha", SqlDbType.DateTime).Value = primera.Fecha;
                    comando.Parameters.Add("@Estado", SqlDbType.TinyInt).Value = primera.Estado;
                    comando.Parameters.Add("@Aplicacion", SqlDbType.Char, 10).Value = primera.Aplicacion;
                    comando.Parameters.Add("@Usuario", SqlDbType.VarChar, 30).Value = primera.Usuario;
                    for (int i = 0; i < lote.Count; i++)
                    {
                        Inventario fila = lote[i];
                        if (i > 0)
                        {
                            sql.Append(", ");
                        }
                        sql.Append($"(@Empresa, @Almacen, @Fecha, @n{i}, @d{i}, @g{i}, @s{i}, @f{i}, @c{i}, 0, @Estado, @Aplicacion, @Usuario, @pa{i}, @fi{i}, @co{i})");
                        comando.Parameters.Add($"@n{i}", SqlDbType.Char, 15).Value = fila.Número;
                        comando.Parameters.Add($"@d{i}", SqlDbType.Char, 50).Value = (object)fila.Descripción ?? DBNull.Value;
                        comando.Parameters.Add($"@g{i}", SqlDbType.Char, 3).Value = (object)fila.Grupo ?? DBNull.Value;
                        comando.Parameters.Add($"@s{i}", SqlDbType.Char, 3).Value = (object)fila.Subgrupo ?? DBNull.Value;
                        comando.Parameters.Add($"@f{i}", SqlDbType.Char, 10).Value = (object)fila.Familia ?? DBNull.Value;
                        comando.Parameters.Add($"@c{i}", SqlDbType.Int).Value = (object)fila.StockCalculado ?? DBNull.Value;
                        comando.Parameters.Add($"@pa{i}", SqlDbType.Char, 3).Value = (object)fila.Pasillo ?? DBNull.Value;
                        comando.Parameters.Add($"@fi{i}", SqlDbType.Char, 3).Value = (object)fila.Fila ?? DBNull.Value;
                        comando.Parameters.Add($"@co{i}", SqlDbType.Char, 3).Value = (object)fila.Columna ?? DBNull.Value;
                    }
                    comando.CommandText = sql.ToString();
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private static string Texto(SqlDataReader lector, int columna)
        {
            return lector.IsDBNull(columna) ? null : lector.GetString(columna).Trim();
        }

        private class UnidadInventarioUbicacionesSql : IUnidadInventarioUbicaciones
        {
            private readonly SqlConnection conexion;
            private readonly SqlTransaction transaccion;
            private bool confirmada;

            public UnidadInventarioUbicacionesSql(SqlConnection conexion, SqlTransaction transaccion)
            {
                this.conexion = conexion;
                this.transaccion = transaccion;
            }

            /// <summary>Dos rellenados a la vez de lo mismo: el segundo espera y, al entrar, ve las filas del primero (409).</summary>
            public async Task Bloquear(string recurso)
            {
                const string sql = @"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 50001, 'Otro usuario está rellenando este inventario ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";
                using (var comando = Comando(sql))
                {
                    comando.Parameters.Add("@Recurso", SqlDbType.NVarChar, 255).Value = recurso;
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public async Task<List<LineaInventarioContadaDTO>> Pendientes(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro)
            {
                using (SqlCommand comando = ComandoContado(conexion, transaccion, empresa, almacen, fecha, filtro))
                {
                    return await LeerLineas(comando).ConfigureAwait(false);
                }
            }

            public async Task<List<LineaInventarioContadaDTO>> Contabilizadas(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro)
            {
                using (SqlCommand comando = ComandoContado(conexion, transaccion, empresa, almacen, fecha, filtro, contabilizadas: true))
                {
                    return await LeerLineas(comando).ConfigureAwait(false);
                }
            }

            public async Task<List<StockProductoInventario>> TeoricoUbicacion(string empresa, string almacen, string pasillo, string fila, string columna)
            {
                using (var comando = Comando(SQL_TEORICO_UBICACION))
                {
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Pasillo", SqlDbType.Char, 3).Value = pasillo;
                    comando.Parameters.Add("@Fila", SqlDbType.Char, 3).Value = fila;
                    comando.Parameters.Add("@Columna", SqlDbType.Char, 3).Value = columna;
                    return await LeerStocks(comando).ConfigureAwait(false);
                }
            }

            public async Task<List<ReservaUbicacionInventario>> ReservasUbicacion(string almacen, string pasillo, string fila, string columna)
            {
                var resultado = new List<ReservaUbicacionInventario>();
                using (var comando = Comando(SQL_RESERVAS_UBICACION))
                {
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Pasillo", SqlDbType.Char, 3).Value = pasillo;
                    comando.Parameters.Add("@Fila", SqlDbType.Char, 3).Value = fila;
                    comando.Parameters.Add("@Columna", SqlDbType.Char, 3).Value = columna;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new ReservaUbicacionInventario
                            {
                                Producto = lector.IsDBNull(0) ? null : lector.GetString(0).Trim(),
                                Pedido = lector.IsDBNull(1) ? (int?)null : Convert.ToInt32(lector.GetValue(1)),
                                Cantidad = lector.IsDBNull(2) ? 0 : Convert.ToInt32(lector.GetValue(2))
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task<List<StockProductoInventario>> Stocks(string empresa, string almacen, string familia, string producto)
            {
                string filtro = producto != null ? "p.Número = @Producto" : "p.Familia = @Familia";
                using (var comando = Comando(SQL_STOCKS + filtro + " GROUP BY p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, p.Ficticio"))
                {
                    comando.CommandTimeout = SEGUNDOS_CONSULTA;
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    if (producto != null)
                    {
                        comando.Parameters.Add("@Producto", SqlDbType.Char, 15).Value = producto;
                    }
                    else
                    {
                        comando.Parameters.Add("@Familia", SqlDbType.Char, 10).Value = familia;
                    }
                    return await LeerStocks(comando).ConfigureAwait(false);
                }
            }

            public Task Insertar(IList<Inventario> filas) => InsertarFilas(conexion, transaccion, filas);

            public void Confirmar()
            {
                transaccion.Commit();
                confirmada = true;
            }

            public void Dispose()
            {
                if (!confirmada)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ya no estaba viva (la cerró un error de SQL): no hay nada que deshacer.
                    }
                }
                transaccion.Dispose();
                conexion.Dispose();
            }

            private SqlCommand Comando(string sql)
            {
                return new SqlCommand(sql, conexion, transaccion);
            }
        }
    }
}
