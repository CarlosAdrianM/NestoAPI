using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas
{
    /// <summary>
    /// La BD del alta y la baja en bloque de campañas, por ADO: una consulta de candidatos, una de filas existentes, y las
    /// escrituras por SqlBulkCopy a una tabla temporal + un INSERT/DELETE/UPDATE por tabla (sin 6.900 viajes ni EF fila a fila).
    /// Los INSERT van con NOT EXISTS (idempotentes) y pasan por los disparadores y restricciones de siempre (el SqlBulkCopy
    /// solo llena la temporal).
    /// </summary>
    public class RepositorioCampanaBloqueSql : IRepositorioCampanaBloque
    {
        internal const string SQL_TABLAS =
            "SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NULL THEN 0 ELSE 1 END AS bit), " +
            "CAST(CASE WHEN OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NULL THEN 0 ELSE 1 END AS bit), " +
            "CAST(CASE WHEN OBJECT_ID(N'dbo.CampanasAcumulanDistribuidores', N'U') IS NULL THEN 0 ELSE 1 END AS bit), " +
            "CAST(CASE WHEN OBJECT_ID(N'dbo.CampanasCondicionStock', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        // La marca de distribuidores: una fila por campaña; si ya estaba, se le ponen las fechas de la campaña (no se duplica).
        internal const string SQL_MARCAR_DISTRIBUIDORES = @"IF EXISTS (SELECT 1 FROM dbo.CampanasAcumulanDistribuidores WITH (UPDLOCK, HOLDLOCK) WHERE Campana = @campana)
BEGIN
    UPDATE dbo.CampanasAcumulanDistribuidores SET FechaDesde = @desde, FechaHasta = @hasta, Usuario = @usuario, FechaModificacion = GETDATE()
    WHERE Campana = @campana AND (FechaDesde <> @desde OR FechaHasta <> @hasta);
    SELECT 0;
END
ELSE
BEGIN
    INSERT INTO dbo.CampanasAcumulanDistribuidores (Campana, FechaDesde, FechaHasta, Usuario) VALUES (@campana, @desde, @hasta, @usuario);
    SELECT 1;
END";

        internal const string SQL_BORRAR_DISTRIBUIDORES = @"IF OBJECT_ID(N'dbo.CampanasAcumulanDistribuidores', N'U') IS NULL BEGIN SELECT 0; RETURN; END
DELETE FROM dbo.CampanasAcumulanDistribuidores WHERE Campana = @campana;
SELECT @@ROWCOUNT;";

        internal const string SQL_CERRAR_DISTRIBUIDORES = @"IF OBJECT_ID(N'dbo.CampanasAcumulanDistribuidores', N'U') IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.CampanasAcumulanDistribuidores SET FechaHasta = @fin, FechaDesde = CASE WHEN FechaDesde > @fin THEN @fin ELSE FechaDesde END,
    Usuario = @usuario, FechaModificacion = GETDATE()
WHERE Campana = @campana;
SELECT @@ROWCOUNT;";

        internal const string SQL_CANDIDATOS =
            "SELECT RTRIM(p.Número), RTRIM(p.Nombre), RTRIM(p.Familia), RTRIM(p.Grupo), RTRIM(p.SubGrupo), p.PVP, p.[Aplicar Dto], p.Estado " +
            "FROM dbo.Productos p WHERE p.Empresa = @empresa AND ({0})";

        // Filas de TARIFA de producto (sin cliente ni proveedor, desde 1 unidad) de los candidatos: las de la campaña (cualquier
        // fecha) y las que se solapan con las fechas pedidas.
        internal const string SQL_EXISTENTES =
            "SELECT d.[Nº Orden], RTRIM(d.[Nº Producto]), d.Descuento, d.Precio, CAST(d.FechaDesde AS datetime), CAST(d.FechaHasta AS datetime), " +
            "RTRIM(d.Campana), d.AudienciaOferta " +
            "FROM dbo.DescuentosProducto d INNER JOIN dbo.Productos p ON p.Empresa = d.Empresa AND p.Número = d.[Nº Producto] " +
            "WHERE d.Empresa = @empresa AND d.[Nº Producto] IS NOT NULL " +
            "AND ISNULL(RTRIM(d.[Nº Cliente]), '') = '' AND ISNULL(RTRIM(d.NºProveedor), '') = '' AND d.CantidadMínima <= 1 " +
            "AND ({0}) " +
            "AND (d.Campana = @campana OR ((d.FechaDesde IS NULL OR d.FechaDesde <= @hasta) AND (d.FechaHasta IS NULL OR d.FechaHasta >= @desde)))";

        internal const string SQL_CREAR_TEMPORAL =
            "CREATE TABLE #CampanaBloqueFilas (Producto char(15) NOT NULL PRIMARY KEY, Descuento decimal(5,4) NOT NULL, Audiencia tinyint NOT NULL, " +
            "CrearDescuento bit NOT NULL, Desbloquear bit NOT NULL, Contado bit NOT NULL, Stock bit NOT NULL)";

        internal const string SQL_INSERTAR_DESCUENTOS = @"DECLARE @creados TABLE (Producto char(15) NOT NULL);
INSERT INTO dbo.DescuentosProducto (Empresa, [Nº Producto], CantidadMínima, Descuento, AudienciaOferta, FechaDesde, FechaHasta, Campana, Usuario, [Fecha Modificación])
OUTPUT inserted.[Nº Producto] INTO @creados
SELECT @empresa, f.Producto, 1, f.Descuento, f.Audiencia, @desde, @hasta, @campana, @usuario, GETDATE()
FROM #CampanaBloqueFilas f
WHERE f.CrearDescuento = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.DescuentosProducto d WITH (UPDLOCK, HOLDLOCK)
                  WHERE d.Empresa = @empresa AND d.[Nº Producto] = f.Producto AND d.Campana = @campana
                    AND ISNULL(RTRIM(d.[Nº Cliente]), '') = '' AND ISNULL(RTRIM(d.NºProveedor), '') = '');
SELECT RTRIM(Producto) FROM @creados;";

        internal const string SQL_INSERTAR_APLICAR_DTO = @"INSERT INTO dbo.CampanasAplicarDto (Campana, Empresa, Producto, FechaDesde, FechaHasta, Usuario)
SELECT @campana, @empresa, f.Producto, @desde, @hasta, @usuario
FROM #CampanaBloqueFilas f
WHERE f.Desbloquear = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.CampanasAplicarDto c WITH (UPDLOCK, HOLDLOCK)
                  WHERE c.Campana = @campana AND c.Empresa = @empresa AND c.Producto = f.Producto);
SELECT @@ROWCOUNT;";

        internal const string SQL_INSERTAR_CONTADO = @"INSERT INTO dbo.CampanasCondicionContado (Campana, Empresa, Producto, FechaDesde, FechaHasta, TextoAviso, Usuario)
SELECT @campana, @empresa, f.Producto, @desde, @hasta, @texto, @usuario
FROM #CampanaBloqueFilas f
WHERE f.Contado = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.CampanasCondicionContado c WITH (UPDLOCK, HOLDLOCK)
                  WHERE c.Campana = @campana AND c.Empresa = @empresa AND c.Producto = f.Producto);
SELECT @@ROWCOUNT;";

        // NestoAPI#889: «solo en stock», una fila por producto como la de contado.
        internal const string SQL_INSERTAR_STOCK = @"INSERT INTO dbo.CampanasCondicionStock (Campana, Empresa, Producto, FechaDesde, FechaHasta, Usuario)
SELECT @campana, @empresa, f.Producto, @desde, @hasta, @usuario
FROM #CampanaBloqueFilas f
WHERE f.Stock = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.CampanasCondicionStock c WITH (UPDLOCK, HOLDLOCK)
                  WHERE c.Campana = @campana AND c.Empresa = @empresa AND c.Producto = f.Producto);
SELECT @@ROWCOUNT;";

        internal const string SQL_CREAR_TEMPORAL_IDS = "CREATE TABLE #CampanaBloqueIds (Id int NOT NULL PRIMARY KEY)";

        internal const string SQL_BORRAR_DESCUENTOS =
            "DELETE d FROM dbo.DescuentosProducto d INNER JOIN #CampanaBloqueIds i ON i.Id = d.[Nº Orden] WHERE d.Empresa = @empresa; SELECT @@ROWCOUNT;";

        internal const string SQL_CERRAR_DESCUENTOS =
            "UPDATE d SET FechaHasta = @fin, Usuario = @usuario, [Fecha Modificación] = GETDATE() " +
            "FROM dbo.DescuentosProducto d INNER JOIN #CampanaBloqueIds i ON i.Id = d.[Nº Orden] WHERE d.Empresa = @empresa; SELECT @@ROWCOUNT;";

        // Borrar: lo aún no activado se borra; lo activado y sin restaurar se cierra AYER para que el job campanas-aplicar-dto
        // devuelva la ficha a lo que tenía en su siguiente pasada (borrarlo la dejaría desbloqueada para siempre).
        internal const string SQL_BORRAR_APLICAR_DTO = @"IF OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NULL BEGIN SELECT 0; RETURN; END
DECLARE @n int;
DELETE FROM dbo.CampanasAplicarDto WHERE Campana = @campana AND Empresa = @empresa AND FechaActivado IS NULL;
SET @n = @@ROWCOUNT;
UPDATE dbo.CampanasAplicarDto SET FechaHasta = @fin, FechaDesde = CASE WHEN FechaDesde > @fin THEN @fin ELSE FechaDesde END
WHERE Campana = @campana AND Empresa = @empresa AND FechaActivado IS NOT NULL AND FechaRestaurado IS NULL;
SELECT @n + @@ROWCOUNT;";

        // Cerrar: lo que no se ha restaurado pasa a acabar en la fecha de fin (lo no activado que empezaba después ya no se activa).
        internal const string SQL_CERRAR_APLICAR_DTO = @"IF OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.CampanasAplicarDto SET FechaHasta = @fin, FechaDesde = CASE WHEN FechaDesde > @fin THEN @fin ELSE FechaDesde END
WHERE Campana = @campana AND Empresa = @empresa AND FechaRestaurado IS NULL;
SELECT @@ROWCOUNT;";

        internal const string SQL_BORRAR_CONTADO = @"IF OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NULL BEGIN SELECT 0; RETURN; END
DELETE FROM dbo.CampanasCondicionContado WHERE Campana = @campana AND Empresa = @empresa;
SELECT @@ROWCOUNT;";

        internal const string SQL_CERRAR_CONTADO = @"IF OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.CampanasCondicionContado SET FechaHasta = @fin, FechaDesde = CASE WHEN FechaDesde > @fin THEN @fin ELSE FechaDesde END
WHERE Campana = @campana AND Empresa = @empresa;
SELECT @@ROWCOUNT;";

        internal const string SQL_BORRAR_STOCK = @"IF OBJECT_ID(N'dbo.CampanasCondicionStock', N'U') IS NULL BEGIN SELECT 0; RETURN; END
DELETE FROM dbo.CampanasCondicionStock WHERE Campana = @campana AND Empresa = @empresa;
SELECT @@ROWCOUNT;";

        internal const string SQL_CERRAR_STOCK = @"IF OBJECT_ID(N'dbo.CampanasCondicionStock', N'U') IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.CampanasCondicionStock SET FechaHasta = @fin, FechaDesde = CASE WHEN FechaDesde > @fin THEN @fin ELSE FechaDesde END
WHERE Campana = @campana AND Empresa = @empresa;
SELECT @@ROWCOUNT;";

        internal const string SQL_ANOTAR = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @Usuario)";

        private const int TIEMPO_COMANDO = 300;

        private readonly NVEntities db;

        /// <summary>La cadena de conexión se toma al usarlo (así un contexto falso de las pruebas no lo rompe al construir).</summary>
        public RepositorioCampanaBloqueSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private string CadenaConexion => db.Database.Connection.ConnectionString;

        public async Task<DatosCampanaBloque> Leer(ConsultaCampanaBloque consulta)
        {
            using (var conexion = new SqlConnection(CadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                return await LeerDatos(conexion, null, consulta).ConfigureAwait(false);
            }
        }

        public async Task<IUnidadCampanaBloque> Empezar(string empresa, string campana)
        {
            var conexion = new SqlConnection(CadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                var unidad = new Unidad(conexion, conexion.BeginTransaction(IsolationLevel.ReadCommitted));
                await Bloquear(conexion, unidad.Transaccion, empresa, campana).ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        public async Task<ResultadoRetiradaCampanaBloque> Retirar(RetiradaCampanaBloque r)
        {
            using (IUnidadCampanaBloque unidad = await Empezar(r.Empresa, r.Campana).ConfigureAwait(false))
            {
                var u = (Unidad)unidad;
                await CrearTemporalIds(u.Conexion, u.Transaccion, r.Ids).ConfigureAwait(false);
                bool cerrar = r.FechaFin.HasValue;
                DateTime fin = cerrar ? r.FechaFin.Value.Date : r.Hoy.Date.AddDays(-1);
                var hecho = new ResultadoRetiradaCampanaBloque
                {
                    FilasDescuento = await Escalar(u, cerrar ? SQL_CERRAR_DESCUENTOS : SQL_BORRAR_DESCUENTOS, r, fin).ConfigureAwait(false),
                    FilasAplicarDto = await Escalar(u, cerrar ? SQL_CERRAR_APLICAR_DTO : SQL_BORRAR_APLICAR_DTO, r, fin).ConfigureAwait(false),
                    FilasCondicionContado = await Escalar(u, cerrar ? SQL_CERRAR_CONTADO : SQL_BORRAR_CONTADO, r, fin).ConfigureAwait(false),
                    FilasCondicionStock = await Escalar(u, cerrar ? SQL_CERRAR_STOCK : SQL_BORRAR_STOCK, r, fin).ConfigureAwait(false),
                    FilasAcumulaDistribuidores = await Escalar(u, cerrar ? SQL_CERRAR_DISTRIBUIDORES : SQL_BORRAR_DISTRIBUIDORES, r, fin).ConfigureAwait(false)
                };
                string anterior = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    r.Campana, r.Empresa, Accion = cerrar ? "Cerrar" : "Borrar", FechaFin = cerrar ? fin.ToString("yyyy-MM-dd") : null,
                    hecho.FilasDescuento, hecho.FilasAplicarDto, hecho.FilasCondicionContado, hecho.FilasCondicionStock, hecho.FilasAcumulaDistribuidores, Ids = r.Ids
                });
                await unidad.Anotar(cerrar ? ServicioCampanaBloque.TABLA_CERRADA : ServicioCampanaBloque.TABLA_BORRADA, anterior,
                    ServicioCampanaBloque.DescripcionRetirada(r, hecho), r.Usuario).ConfigureAwait(false);
                unidad.Confirmar();
                return hecho;
            }
        }

        public Task<int> Encolar(IEnumerable<string> productos, string usuario) => db.EncolarProductosSync(productos, usuario);

        private static async Task<int> Escalar(Unidad u, string sql, RetiradaCampanaBloque r, DateTime fin)
        {
            using (var comando = new SqlCommand(sql, u.Conexion, u.Transaccion) { CommandTimeout = TIEMPO_COMANDO })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = r.Empresa;
                comando.Parameters.Add("@campana", SqlDbType.NVarChar, 50).Value = r.Campana;
                comando.Parameters.Add("@fin", SqlDbType.Date).Value = fin;
                comando.Parameters.Add("@usuario", SqlDbType.VarChar, 30).Value = Recortar(r.Usuario, 30);
                object valor = await comando.ExecuteScalarAsync().ConfigureAwait(false);
                return valor == null || valor == DBNull.Value ? 0 : Convert.ToInt32(valor);
            }
        }

        private static async Task Bloquear(SqlConnection conexion, SqlTransaction transaccion, string empresa, string campana)
        {
            const string sql = @"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 50001, 'Otra persona está dando de alta o borrando esta campaña ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";
            using (var comando = new SqlCommand(sql, conexion, transaccion))
            {
                string recurso = $"CampanaBloque:{empresa?.Trim()}:{campana?.Trim().ToUpperInvariant()}";
                comando.Parameters.Add("@Recurso", SqlDbType.NVarChar, 255).Value = recurso.Length > 255 ? recurso.Substring(0, 255) : recurso;
                _ = await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static async Task CrearTemporalIds(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> ids)
        {
            using (var crear = new SqlCommand(SQL_CREAR_TEMPORAL_IDS, conexion, transaccion))
            {
                _ = await crear.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            var tabla = new DataTable();
            _ = tabla.Columns.Add("Id", typeof(int));
            foreach (int id in (ids ?? Enumerable.Empty<int>()).Distinct())
            {
                _ = tabla.Rows.Add(id);
            }
            await Copiar(conexion, transaccion, "#CampanaBloqueIds", tabla).ConfigureAwait(false);
        }

        private static async Task Copiar(SqlConnection conexion, SqlTransaction transaccion, string destino, DataTable tabla)
        {
            if (tabla.Rows.Count == 0)
            {
                return;
            }
            using (var copia = new SqlBulkCopy(conexion, SqlBulkCopyOptions.Default, transaccion) { DestinationTableName = destino, BulkCopyTimeout = TIEMPO_COMANDO })
            {
                foreach (DataColumn columna in tabla.Columns)
                {
                    _ = copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                }
                await copia.WriteToServerAsync(tabla).ConfigureAwait(false);
            }
        }

        /// <summary>«p.Familia IN (@f0, …) OR p.Número IN (@p0, …)» con sus parámetros.</summary>
        internal static string Filtro(ConsultaCampanaBloque consulta, SqlParameterCollection parametros)
        {
            var partes = new List<string>();
            List<string> familias = consulta.Familias ?? new List<string>();
            List<string> productos = consulta.Productos ?? new List<string>();
            if (familias.Any())
            {
                var nombres = new StringBuilder();
                for (int i = 0; i < familias.Count; i++)
                {
                    _ = nombres.Append(i == 0 ? string.Empty : ", ").Append("@f").Append(i);
                    parametros.Add("@f" + i, SqlDbType.Char, 10).Value = familias[i];
                }
                partes.Add($"p.Familia IN ({nombres})");
            }
            if (productos.Any())
            {
                var nombres = new StringBuilder();
                for (int i = 0; i < productos.Count; i++)
                {
                    _ = nombres.Append(i == 0 ? string.Empty : ", ").Append("@p").Append(i);
                    parametros.Add("@p" + i, SqlDbType.Char, 15).Value = productos[i];
                }
                partes.Add($"p.Número IN ({nombres})");
            }
            return partes.Any() ? string.Join(" OR ", partes) : "1 = 0";
        }

        internal static async Task<DatosCampanaBloque> LeerDatos(SqlConnection conexion, SqlTransaction transaccion, ConsultaCampanaBloque consulta)
        {
            var datos = new DatosCampanaBloque();
            using (var comando = new SqlCommand(SQL_TABLAS, conexion, transaccion))
            using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await lector.ReadAsync().ConfigureAwait(false))
                {
                    datos.TablaAplicarDto = lector.GetBoolean(0);
                    datos.TablaCondicionContado = lector.GetBoolean(1);
                    datos.TablaAcumulaDistribuidores = lector.GetBoolean(2);
                    datos.TablaCondicionStock = lector.GetBoolean(3);
                }
            }

            using (var comando = new SqlCommand { Connection = conexion, Transaction = transaccion, CommandTimeout = TIEMPO_COMANDO })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = consulta.Empresa;
                comando.CommandText = string.Format(SQL_CANDIDATOS, Filtro(consulta, comando.Parameters));
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        datos.Candidatos.Add(new ProductoCandidatoCampana
                        {
                            Numero = Texto(lector, 0),
                            Nombre = Texto(lector, 1),
                            Familia = Texto(lector, 2),
                            Grupo = Texto(lector, 3),
                            SubGrupo = Texto(lector, 4),
                            PVP = lector.IsDBNull(5) ? (decimal?)null : Convert.ToDecimal(lector.GetValue(5)),
                            AplicarDto = !lector.IsDBNull(6) && lector.GetBoolean(6),
                            Estado = lector.IsDBNull(7) ? (short?)null : Convert.ToInt16(lector.GetValue(7))
                        });
                    }
                }
            }

            using (var comando = new SqlCommand { Connection = conexion, Transaction = transaccion, CommandTimeout = TIEMPO_COMANDO })
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = consulta.Empresa;
                comando.Parameters.Add("@campana", SqlDbType.NVarChar, 50).Value = consulta.Campana;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = consulta.Desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.Date).Value = consulta.Hasta.Date;
                comando.CommandText = string.Format(SQL_EXISTENTES, Filtro(consulta, comando.Parameters));
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        datos.Existentes.Add(new FilaExistenteCampana
                        {
                            Id = lector.GetInt32(0),
                            Producto = Texto(lector, 1),
                            Descuento = Convert.ToDecimal(lector.GetValue(2)),
                            Precio = lector.IsDBNull(3) ? (decimal?)null : Convert.ToDecimal(lector.GetValue(3)),
                            FechaDesde = lector.IsDBNull(4) ? (DateTime?)null : lector.GetDateTime(4),
                            FechaHasta = lector.IsDBNull(5) ? (DateTime?)null : lector.GetDateTime(5),
                            Campana = Texto(lector, 6),
                            Audiencia = lector.IsDBNull(7) ? (byte)0 : Convert.ToByte(lector.GetValue(7))
                        });
                    }
                }
            }
            return datos;
        }

        private static string Texto(SqlDataReader lector, int i) => lector.IsDBNull(i) ? null : Convert.ToString(lector.GetValue(i));

        private static string Recortar(string texto, int longitud)
        {
            string limpio = string.IsNullOrWhiteSpace(texto) ? "NestoAPI" : texto.Trim();
            return limpio.Length > longitud ? limpio.Substring(0, longitud) : limpio;
        }

        private class Unidad : IUnidadCampanaBloque
        {
            public SqlConnection Conexion { get; }
            public SqlTransaction Transaccion { get; }
            private bool confirmada;

            public Unidad(SqlConnection conexion, SqlTransaction transaccion)
            {
                Conexion = conexion;
                Transaccion = transaccion;
            }

            public Task<DatosCampanaBloque> Leer(ConsultaCampanaBloque consulta) => LeerDatos(Conexion, Transaccion, consulta);

            public async Task<ResultadoInsercionCampanaBloque> Insertar(AltaCampanaBloque alta, DatosCampanaBloque tablas)
            {
                var resultado = new ResultadoInsercionCampanaBloque();
                using (var crear = new SqlCommand(SQL_CREAR_TEMPORAL, Conexion, Transaccion))
                {
                    _ = await crear.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                var tabla = new DataTable();
                _ = tabla.Columns.Add("Producto", typeof(string));
                _ = tabla.Columns.Add("Descuento", typeof(decimal));
                _ = tabla.Columns.Add("Audiencia", typeof(byte));
                _ = tabla.Columns.Add("CrearDescuento", typeof(bool));
                _ = tabla.Columns.Add("Desbloquear", typeof(bool));
                _ = tabla.Columns.Add("Contado", typeof(bool));
                _ = tabla.Columns.Add("Stock", typeof(bool));
                foreach (FilaAltaCampanaBloque f in alta.Filas)
                {
                    _ = tabla.Rows.Add(f.Producto.Trim(), f.Descuento, f.Audiencia, f.CrearDescuento, f.Desbloquear, f.Contado, f.Stock);
                }
                await Copiar(Conexion, Transaccion, "#CampanaBloqueFilas", tabla).ConfigureAwait(false);

                using (SqlCommand comando = Comando(SQL_INSERTAR_DESCUENTOS, alta, 30))
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        resultado.ProductosCreados.Add(Texto(lector, 0));
                    }
                }
                if (tablas.TablaAplicarDto && alta.Filas.Any(f => f.Desbloquear))
                {
                    using (SqlCommand comando = Comando(SQL_INSERTAR_APLICAR_DTO, alta, 50))
                    {
                        resultado.AplicarDtoCreadas = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false));
                    }
                }
                if (tablas.TablaCondicionContado && alta.Filas.Any(f => f.Contado))
                {
                    using (SqlCommand comando = Comando(SQL_INSERTAR_CONTADO, alta, 50))
                    {
                        comando.Parameters.Add("@texto", SqlDbType.NVarChar, 500).Value = (object)alta.TextoAvisoContado ?? DBNull.Value;
                        resultado.CondicionContadoCreadas = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false));
                    }
                }
                if (tablas.TablaCondicionStock && alta.Filas.Any(f => f.Stock))
                {
                    using (SqlCommand comando = Comando(SQL_INSERTAR_STOCK, alta, 50))
                    {
                        resultado.CondicionStockCreadas = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false));
                    }
                }
                if (tablas.TablaAcumulaDistribuidores && alta.AcumulaParaDistribuidores)
                {
                    using (SqlCommand comando = Comando(SQL_MARCAR_DISTRIBUIDORES, alta, 50))
                    {
                        resultado.MarcaDistribuidoresCreada = Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false)) > 0;
                    }
                }
                return resultado;
            }

            private SqlCommand Comando(string sql, AltaCampanaBloque alta, int longitudUsuario)
            {
                var comando = new SqlCommand(sql, Conexion, Transaccion) { CommandTimeout = TIEMPO_COMANDO };
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = alta.Empresa;
                comando.Parameters.Add("@campana", SqlDbType.NVarChar, 50).Value = alta.Campana;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = alta.Desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.Date).Value = alta.Hasta.Date;
                comando.Parameters.Add("@usuario", SqlDbType.VarChar, longitudUsuario).Value = Recortar(alta.Usuario, longitudUsuario);
                return comando;
            }

            public async Task Anotar(string tabla, string anterior, string nuevo, string usuario)
            {
                using (var comando = new SqlCommand(SQL_ANOTAR, Conexion, Transaccion))
                {
                    comando.Parameters.Add("@Tabla", SqlDbType.NVarChar, 50).Value = tabla;
                    comando.Parameters.Add("@Anterior", SqlDbType.NVarChar, -1).Value = (object)anterior ?? DBNull.Value;
                    comando.Parameters.Add("@Nuevo", SqlDbType.NVarChar, -1).Value = (object)nuevo ?? DBNull.Value;
                    comando.Parameters.Add("@Usuario", SqlDbType.NVarChar, 50).Value = Recortar(usuario, 50);
                    _ = await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public void Confirmar()
            {
                Transaccion.Commit();
                confirmada = true;
            }

            public void Dispose()
            {
                if (!confirmada)
                {
                    try
                    {
                        Transaccion.Rollback();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ya no estaba viva: no hay nada que deshacer.
                    }
                }
                Transaccion.Dispose();
                Conexion.Dispose();
            }
        }
    }
}
