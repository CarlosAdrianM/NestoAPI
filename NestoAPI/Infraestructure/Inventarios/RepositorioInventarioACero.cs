using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>
    /// Sugerencia 1185: la BD del «a cero en todas sus ubicaciones», por ADO y en una transacción con sp_getapplock (como el rellenado).
    /// Solo toca Inventarios (filas pendientes): ExtractoProducto y Ubicaciones los toca después prdContabilizarInventario.
    /// </summary>
    public class RepositorioInventarioACeroSql : IRepositorioInventarioACero
    {
        internal const string SQL_PRODUCTO = @"
SELECT RTRIM(p.Número), RTRIM(p.Nombre), RTRIM(p.Grupo), RTRIM(p.SubGrupo), RTRIM(p.Familia), CAST(ISNULL(p.Ficticio, 0) AS bit),
    ISNULL((SELECT SUM(CAST(e.Cantidad AS int)) FROM dbo.ExtractoProducto e WHERE e.Número = p.Número AND e.Empresa IN ('1', '3') AND e.Almacén = @Almacen), 0),
    CAST(ISNULL(p.Ubicar, 0) AS bit)
FROM dbo.Productos p
WHERE p.Empresa = @Empresa AND p.Número = @Producto";

        // Como el teórico de RellenarUbicacion: lo LIBRE (Estado 0), sin filtrar por empresa. Épica #784 (11/11/26, 37215): las
        // reservas de picking/reposición no se dejan a cero (prdContabilizarInventario solo regulariza las filas de Estado 0).
        internal const string SQL_UBICACIONES = @"
SELECT RTRIM(u.Pasillo), RTRIM(u.Fila), RTRIM(u.Columna), SUM(u.Cantidad)
FROM dbo.Ubicaciones u
WHERE u.Almacén = @Almacen AND u.Número = @Producto AND u.Estado = 0 AND u.Pasillo IS NOT NULL
GROUP BY RTRIM(u.Pasillo), RTRIM(u.Fila), RTRIM(u.Columna)";

        internal const string SQL_SIN_UBICAR = @"
SELECT ISNULL(SUM(u.Cantidad), 0) FROM dbo.Ubicaciones u
WHERE u.Almacén = @Almacen AND u.Número = @Producto AND u.Estado >= 0 AND u.Pasillo IS NULL";

        internal const string SQL_PONER_A_CERO = "UPDATE dbo.Inventarios SET StockReal = 0, [Fecha Modificación] = GETDATE() " +
            "WHERE NºOrden IN ({0}) AND Estado = 1 AND NºTraspaso IS NULL";

        internal const string SQL_BORRAR_SIN_UBICACION = "DELETE FROM dbo.Inventarios " +
            "WHERE NºOrden IN ({0}) AND Estado = 1 AND NºTraspaso IS NULL AND Pasillo IS NULL";

        private readonly string cadenaConexion;

        public RepositorioInventarioACeroSql(NVEntities db)
        {
            cadenaConexion = (db ?? throw new ArgumentNullException(nameof(db))).Database.Connection.ConnectionString;
        }

        public async Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(RepositorioInventarioUbicacionesSql.SQL_ALMACEN, conexion))
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
                            Nombre = lector.IsDBNull(0) ? null : lector.GetString(0).Trim(),
                            ControlUbicaciones = !lector.IsDBNull(1) && lector.GetBoolean(1)
                        };
                    }
                }
            }
        }

        public async Task<IUnidadInventarioACero> Empezar(string recurso)
        {
            var conexion = new SqlConnection(cadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted);
                var unidad = new UnidadInventarioACeroSql(conexion, transaccion);
                await unidad.Bloquear(recurso).ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        private class UnidadInventarioACeroSql : IUnidadInventarioACero
        {
            private readonly SqlConnection conexion;
            private readonly SqlTransaction transaccion;
            private bool confirmada;

            public UnidadInventarioACeroSql(SqlConnection conexion, SqlTransaction transaccion)
            {
                this.conexion = conexion;
                this.transaccion = transaccion;
            }

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

            public async Task<ProductoInventarioACero> LeerProducto(string empresa, string almacen, string producto)
            {
                using (var comando = Comando(SQL_PRODUCTO))
                {
                    comando.CommandTimeout = RepositorioInventarioUbicacionesSql.SEGUNDOS_CONSULTA;
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Producto", SqlDbType.Char, 15).Value = producto;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await lector.ReadAsync().ConfigureAwait(false))
                        {
                            return null;
                        }
                        return new ProductoInventarioACero
                        {
                            Numero = Texto(lector, 0),
                            Descripcion = Texto(lector, 1),
                            Grupo = Texto(lector, 2),
                            SubGrupo = Texto(lector, 3),
                            Familia = Texto(lector, 4),
                            Ficticio = !lector.IsDBNull(5) && lector.GetBoolean(5),
                            Stock = lector.IsDBNull(6) ? 0 : lector.GetInt32(6),
                            Ubicar = !lector.IsDBNull(7) && lector.GetBoolean(7)
                        };
                    }
                }
            }

            public async Task<List<LineaInventarioContadaDTO>> Pendientes(string empresa, string almacen, DateTime fecha, string producto)
            {
                using (SqlCommand comando = RepositorioInventarioUbicacionesSql.ComandoContado(conexion, transaccion, empresa, almacen, fecha,
                    new FiltroInventarioContado { Producto = producto }))
                {
                    return await RepositorioInventarioUbicacionesSql.LeerLineas(comando).ConfigureAwait(false);
                }
            }

            public async Task<List<LineaInventarioContadaDTO>> Contabilizadas(string empresa, string almacen, DateTime fecha, string producto)
            {
                using (SqlCommand comando = RepositorioInventarioUbicacionesSql.ComandoContado(conexion, transaccion, empresa, almacen, fecha,
                    new FiltroInventarioContado { Producto = producto }, contabilizadas: true))
                {
                    return await RepositorioInventarioUbicacionesSql.LeerLineas(comando).ConfigureAwait(false);
                }
            }

            public async Task<List<UbicacionProductoInventario>> Ubicaciones(string almacen, string producto)
            {
                var resultado = new List<UbicacionProductoInventario>();
                using (var comando = Comando(SQL_UBICACIONES))
                {
                    comando.CommandTimeout = RepositorioInventarioUbicacionesSql.SEGUNDOS_CONSULTA;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Producto", SqlDbType.Char, 15).Value = producto;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new UbicacionProductoInventario
                            {
                                Pasillo = Texto(lector, 0),
                                Fila = Texto(lector, 1),
                                Columna = Texto(lector, 2),
                                Cantidad = lector.IsDBNull(3) ? 0 : Convert.ToInt32(lector.GetValue(3))
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task<int> UnidadesSinUbicar(string almacen, string producto)
            {
                using (var comando = Comando(SQL_SIN_UBICAR))
                {
                    comando.CommandTimeout = RepositorioInventarioUbicacionesSql.SEGUNDOS_CONSULTA;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Producto", SqlDbType.Char, 15).Value = producto;
                    object valor = await comando.ExecuteScalarAsync().ConfigureAwait(false);
                    return valor == null || valor is DBNull ? 0 : Convert.ToInt32(valor);
                }
            }

            public Task Insertar(IList<Inventario> filas) => RepositorioInventarioUbicacionesSql.InsertarFilas(conexion, transaccion, filas);

            public Task<int> PonerACero(IList<int> numerosOrden) => Ejecutar(SQL_PONER_A_CERO, numerosOrden);

            public Task<int> BorrarSinUbicacion(IList<int> numerosOrden) => Ejecutar(SQL_BORRAR_SIN_UBICACION, numerosOrden);

            private async Task<int> Ejecutar(string plantilla, IList<int> numerosOrden)
            {
                List<int> numeros = (numerosOrden ?? new List<int>()).Distinct().ToList();
                if (numeros.Count == 0)
                {
                    return 0;
                }
                using (var comando = Comando(string.Empty))
                {
                    comando.CommandText = string.Format(plantilla, string.Join(", ", numeros.Select((n, i) => "@o" + i)));
                    for (int i = 0; i < numeros.Count; i++)
                    {
                        comando.Parameters.Add("@o" + i, SqlDbType.Int).Value = numeros[i];
                    }
                    return await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

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

            private SqlCommand Comando(string sql) => new SqlCommand(sql, conexion, transaccion);

            private static string Texto(SqlDataReader lector, int columna) => lector.IsDBNull(columna) ? null : lector.GetString(columna).Trim();
        }
    }
}
