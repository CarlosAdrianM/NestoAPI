using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#634: el SQL de DiferenciasReposiciones (Scripts/Issue634_DiferenciasReposiciones.sql; no está en el EDMX).
    /// Sin la tabla (script sin lanzar), insertar no hace nada y las consultas devuelven vacío: la recepción sigue como antes.
    /// </summary>
    public static class DiferenciasReposicionSql
    {
        internal const string SQL_EXISTE_TABLA = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.DiferenciasReposiciones', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        // Una por traspaso y producto (UNIQUE): un reenvío devuelve la que ya había
        internal const string SQL_INSERTAR = @"
IF OBJECT_ID(N'dbo.DiferenciasReposiciones', N'U') IS NULL
    SELECT CAST(NULL AS int)
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.DiferenciasReposiciones WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @p0 AND NumTraspaso = @p1 AND Producto = @p4)
        INSERT INTO dbo.DiferenciasReposiciones (Empresa, NumTraspaso, Origen, Destino, Producto, Enviado, Leido, Diferencia,
            UsuarioRecepcion, Preparador, IdRecepcion)
        VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p6 - @p5, @p7, @p8, @p9);
    SELECT Id FROM dbo.DiferenciasReposiciones WHERE Empresa = @p0 AND NumTraspaso = @p1 AND Producto = @p4;
END";

        // {BLOQUEO} y {FILTRO} los pone quien lo usa
        internal const string SQL_SELECT = @"
SELECT {TOP} d.Id, RTRIM(d.Empresa) AS Empresa, d.NumTraspaso AS Traspaso, RTRIM(d.Origen) AS Origen, RTRIM(d.Destino) AS Destino,
       RTRIM(d.Producto) AS Producto, RTRIM(p.Nombre) AS Descripcion, d.Enviado, d.Leido, d.Diferencia,
       d.FechaRecepcion, d.UsuarioRecepcion, d.Preparador, d.Ajuste, d.AjusteMensaje, d.NumOrdenAjuste,
       d.Estado, d.Resolucion, d.UsuarioResolucion, d.FechaResolucion, d.Comentario, d.NumOrdenResolucion, d.NumOrdenResolucionDestino,
       CAST(ISNULL(a.ControlUbicaciones, 0) AS bit) AS OrigenConControlUbicaciones,
       CAST(NULL AS nvarchar(200)) AS QuePaso, CAST(NULL AS nvarchar(300)) AS QueComprobar, -- los pone el servicio (SqlQuery exige la columna)
       (SELECT TOP 1 RTRIM(s.Pasillo) + '/' + RTRIM(s.Fila) + '/' + RTRIM(s.Columna) FROM Ubicaciones s
         WHERE s.[NºTraspasoRepo] = d.NumTraspaso AND s.Estado = -4 AND s.[Almacén] = d.Origen AND s.[Número] = d.Producto
           AND s.Pasillo IS NOT NULL
         ORDER BY s.[NºOrden] DESC) AS HuecoSalida,
       STUFF((SELECT DISTINCT ', ' + RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna) FROM Ubicaciones u
               WHERE u.[Almacén] = d.Origen AND u.[Número] = d.Producto AND u.Estado = 0 AND u.Cantidad > 0 AND u.Pasillo IS NOT NULL
               FOR XML PATH('')), 1, 2, '') AS Huecos
FROM dbo.DiferenciasReposiciones d {BLOQUEO}
LEFT JOIN Productos p ON p.Empresa = d.Empresa AND p.[Número] = d.Producto
LEFT JOIN Almacenes a ON a.Empresa = d.Empresa AND a.[Número] = d.Origen
WHERE {FILTRO}";

        internal static readonly string SQL_LISTAR = SQL_SELECT
            .Replace("{TOP}", "TOP 500").Replace("{BLOQUEO}", "")
            .Replace("{FILTRO}", "d.Empresa = @p0 AND (@p1 IS NULL OR d.Estado = @p1) AND (@p2 IS NULL OR d.Origen = @p2)") + @"
ORDER BY CASE WHEN d.Estado = 'Pendiente' THEN 0 ELSE 1 END, CASE WHEN d.Estado = 'Pendiente' THEN d.FechaRecepcion END,
         d.FechaResolucion DESC, d.Id DESC";

        internal static readonly string SQL_LEER = SQL_SELECT
            .Replace("{TOP}", "").Replace("{BLOQUEO}", "").Replace("{FILTRO}", "d.Empresa = @p0 AND d.Id = @p1");

        internal static readonly string SQL_LEER_BLOQUEANDO = SQL_SELECT
            .Replace("{TOP}", "").Replace("{BLOQUEO}", "WITH (UPDLOCK, HOLDLOCK)").Replace("{FILTRO}", "d.Empresa = @p0 AND d.Id = @p1");

        internal const string SQL_CONTAR_PENDIENTES = @"
SELECT COUNT(*) FROM dbo.DiferenciasReposiciones WHERE Empresa = @p0 AND Estado = 'Pendiente' AND (@p1 IS NULL OR Origen = @p1)";

        internal const string SQL_GUARDAR_AJUSTE = @"
UPDATE dbo.DiferenciasReposiciones SET Ajuste = @p1, NumOrdenAjuste = @p2, AjusteMensaje = @p3 WHERE Id = @p0";

        internal const string SQL_GUARDAR_RESOLUCION = @"
UPDATE dbo.DiferenciasReposiciones
SET Estado = 'Resuelta', Resolucion = @p1, UsuarioResolucion = @p2, FechaResolucion = GETDATE(), Comentario = @p3,
    NumOrdenResolucion = @p4, NumOrdenResolucionDestino = @p5
WHERE Id = @p0 AND Estado = 'Pendiente'";

        /// <summary>Inserta las diferencias dentro de la transacción del llamante. Devuelve sus Id (vacía si no hay tabla).</summary>
        public static async Task<List<int>> Insertar(NVEntities db, IEnumerable<DiferenciaReposicionNueva> filas)
        {
            var ids = new List<int>();
            foreach (DiferenciaReposicionNueva fila in filas ?? Enumerable.Empty<DiferenciaReposicionNueva>())
            {
                if (fila == null || fila.Diferencia == 0)
                {
                    continue;
                }
                int? id = await db.Database.SqlQuery<int?>(SQL_INSERTAR,
                    Char("@p0", fila.Empresa, 3),
                    new SqlParameter("@p1", SqlDbType.Int) { Value = fila.Traspaso },
                    Char("@p2", string.IsNullOrWhiteSpace(fila.Origen) ? null : fila.Origen.Trim(), 3),
                    Char("@p3", fila.Destino, 3),
                    new SqlParameter("@p4", SqlDbType.VarChar, 15) { Value = (object)fila.Producto?.Trim() ?? DBNull.Value },
                    new SqlParameter("@p5", SqlDbType.Int) { Value = fila.Enviado },
                    new SqlParameter("@p6", SqlDbType.Int) { Value = fila.Leido },
                    VarChar("@p7", UsuarioAuditoriaHelper.Resolver(null, fila.UsuarioRecepcion) ?? UsuarioAuditoriaHelper.DESCONOCIDO, 50),
                    VarChar("@p8", fila.Preparador, 50),
                    new SqlParameter("@p9", SqlDbType.UniqueIdentifier) { Value = fila.IdRecepcion })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (id.HasValue)
                {
                    ids.Add(id.Value);
                }
            }
            return ids;
        }

        internal static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };
        }

        internal static SqlParameter VarChar(string nombre, string valor, int longitud)
        {
            string recortado = valor == null ? null : (valor.Length > longitud ? valor.Substring(0, longitud) : valor);
            return new SqlParameter(nombre, SqlDbType.VarChar, longitud) { Value = (object)recortado ?? DBNull.Value };
        }

        internal static SqlParameter NVarChar(string nombre, string valor, int longitud)
        {
            string recortado = valor == null ? null : (valor.Length > longitud ? valor.Substring(0, longitud) : valor);
            return new SqlParameter(nombre, SqlDbType.NVarChar, longitud) { Value = (object)recortado ?? DBNull.Value };
        }

        internal static SqlParameter EnteroONulo(string nombre, int? valor)
        {
            return new SqlParameter(nombre, SqlDbType.Int) { Value = (object)valor ?? DBNull.Value };
        }
    }

    public class RepositorioDiferenciasReposicionSql : IRepositorioDiferenciasReposicion, IDisposable
    {
        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioDiferenciasReposicionSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioDiferenciasReposicionSql ConContextoPropio()
        {
            return new RepositorioDiferenciasReposicionSql(new NVEntities()) { contextoPropio = true };
        }

        public async Task<T> EnTransaccion<T>(Func<ITransaccionDiferenciasReposicion, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(new TransaccionDiferenciasReposicionSql(db)).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (Exception)
                    {
                        // El ROLLBACK de prdExtrProducto ya la ha deshecho
                    }
                    finally
                    {
                        DescartarCambiosPendientes();
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

        /// <summary>Lo que EF tenga sin guardar (las líneas de PreExtrProducto de un movimiento que ha fallado) no vale ya.</summary>
        private void DescartarCambiosPendientes()
        {
            foreach (System.Data.Entity.Infrastructure.DbEntityEntry entrada in db.ChangeTracker.Entries().ToList())
            {
                entrada.State = EntityState.Detached;
            }
        }

        public async Task<List<DiferenciaReposicionDTO>> Listar(string empresa, string estado, string origen)
        {
            if (!await ExisteTabla().ConfigureAwait(false))
            {
                return new List<DiferenciaReposicionDTO>();
            }
            return await db.Database.SqlQuery<DiferenciaReposicionDTO>(DiferenciasReposicionSql.SQL_LISTAR,
                DiferenciasReposicionSql.Char("@p0", empresa, 3),
                DiferenciasReposicionSql.VarChar("@p1", estado, 10),
                DiferenciasReposicionSql.Char("@p2", origen, 3)).ToListAsync().ConfigureAwait(false);
        }

        public async Task<DiferenciaReposicionDTO> Leer(string empresa, int id)
        {
            if (!await ExisteTabla().ConfigureAwait(false))
            {
                return null;
            }
            return await db.Database.SqlQuery<DiferenciaReposicionDTO>(DiferenciasReposicionSql.SQL_LEER,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), new SqlParameter("@p1", SqlDbType.Int) { Value = id })
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<int> ContarPendientes(string empresa, string origen)
        {
            if (!await ExisteTabla().ConfigureAwait(false))
            {
                return 0;
            }
            return await db.Database.SqlQuery<int>(DiferenciasReposicionSql.SQL_CONTAR_PENDIENTES,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", origen, 3))
                .SingleAsync().ConfigureAwait(false);
        }

        private Task<bool> ExisteTabla()
        {
            return db.Database.SqlQuery<bool>(DiferenciasReposicionSql.SQL_EXISTE_TABLA).SingleAsync();
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db?.Dispose();
            }
        }
    }

    /// <summary>
    /// NestoAPI#634: dentro de la transacción. El stock, SOLO por <see cref="IServicioExtractoProducto"/> (la única llamada a
    /// prdExtrProducto) en el diario «_ErrRepo» y, en un almacén con control de ubicaciones, los huecos por la puerta única
    /// (<see cref="IPuertaUbicaciones"/>).
    ///
    /// <para>Desde la API, prdExtrProducto no encuentra el almacén del usuario (RDS2016$, ver
    /// <see cref="TransaccionCierreReposicionSql.LeerPendientesDeUbicar"/>): se salta todos sus bloques de Ubicaciones, también
    /// el de «salidas sin ubicación» (con el usuario de Nesto viejo, con varios huecos daría error). Por eso los huecos los
    /// toca la puerta, en la misma transacción.</para>
    /// </summary>
    public class TransaccionDiferenciasReposicionSql : ITransaccionDiferenciasReposicion
    {
        /// <summary>«Cuadre de errores de reposicion» (Sistema = 1), el de David para esto, sin uso desde 2012.</summary>
        public const string DIARIO = "_ErrRepo";

        internal const string SQL_GRUPO = @"
SELECT RTRIM(Grupo) FROM Productos WHERE Empresa = @p0 AND [Número] = @p1";

        internal const string SQL_CON_UBICACIONES = @"
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM Almacenes a INNER JOIN Productos pr ON pr.Empresa = a.Empresa AND pr.[Número] = @p2
                              WHERE a.Empresa = @p0 AND a.[Número] = @p1 AND a.ControlUbicaciones = 1 AND pr.Ubicar = 1)
            THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_APUNTE = @"
SELECT MAX([Nº Orden]) FROM ExtractoProducto
WHERE Empresa = @p0 AND Diario = @p1 AND [NºTraspaso] = @p2 AND [Número] = @p3 AND [Almacén] = @p4 AND Cantidad = @p5";

        private readonly NVEntities db;
        private readonly IServicioExtractoProducto extractos;
        private readonly IPuertaUbicaciones puerta;

        public TransaccionDiferenciasReposicionSql(NVEntities db, IServicioExtractoProducto extractos = null, IPuertaUbicaciones puerta = null)
        {
            this.db = db;
            this.extractos = extractos ?? new ServicioExtractoProducto();
            this.puerta = puerta ?? new PuertaUbicacionesSql(db);
        }

        public Task<DiferenciaReposicionDTO> Leer(string empresa, int id)
        {
            return db.Database.SqlQuery<DiferenciaReposicionDTO>(DiferenciasReposicionSql.SQL_LEER_BLOQUEANDO,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), new SqlParameter("@p1", SqlDbType.Int) { Value = id })
                .FirstOrDefaultAsync();
        }

        public async Task<ResultadoMovimientoStock> Mover(MovimientoStockDiferencia movimiento)
        {
            if (movimiento == null || movimiento.Cantidad == 0)
            {
                return new ResultadoMovimientoStock();
            }
            if (Math.Abs(movimiento.Cantidad) > short.MaxValue)
            {
                throw new NestoBusinessException($"No se pueden mover {movimiento.Cantidad} unidades de {movimiento.Producto} en una línea.");
            }
            string empresa = movimiento.Empresa;
            string usuario = UsuarioAuditoriaHelper.ParaAuditoria(movimiento.Usuario);
            string grupo = await db.Database.SqlQuery<string>(SQL_GRUPO,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", movimiento.Producto, 15))
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (grupo == null)
            {
                throw new NestoBusinessException($"El producto {movimiento.Producto} no existe: no se puede ajustar su stock.");
            }

            // prdExtrProducto contabiliza el diario ENTERO: lo que haya de otros traspasos se aparta y se devuelve (NestoAPI#553)
            List<int> apartadas = await ApartadoTraspasosSql.Apartar(db, empresa, DIARIO, movimiento.Traspaso).ConfigureAwait(false);
            var linea = new PreExtrProducto
            {
                Empresa = empresa,
                Diario = DIARIO,
                Número = movimiento.Producto?.Trim(),
                Fecha = DateTime.Now,
                Texto = movimiento.Texto,
                Almacén = movimiento.Almacen?.Trim(),
                Grupo = grupo,
                Cantidad = (short)movimiento.Cantidad,
                // Como los apuntes de _ErrRepo de siempre: delegación ALG y forma de venta VAR
                Delegación = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                Forma_Venta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                Asiento_Automático = true,
                Estado = 0,
                NºTraspaso = movimiento.Traspaso,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            };
            _ = await extractos.CrearLineas(db, new List<PreExtrProducto> { linea }).ConfigureAwait(false);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            int resultado = await extractos.ContabilizarDiario(db, empresa, DIARIO, usuario).ConfigureAwait(false);
            if (resultado <= 0)
            {
                throw new NestoBusinessException($"prdExtrProducto no ha contabilizado el diario {DIARIO} (devuelve {resultado}).");
            }
            db.Entry(linea).State = EntityState.Detached; // el procedimiento ya la ha borrado de PreExtrProducto
            await ApartadoTraspasosSql.Devolver(db, empresa, DIARIO, apartadas).ConfigureAwait(false);

            int? apunte = await db.Database.SqlQuery<int?>(SQL_APUNTE,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", DIARIO, 10),
                new SqlParameter("@p2", SqlDbType.Int) { Value = movimiento.Traspaso },
                DiferenciasReposicionSql.Char("@p3", movimiento.Producto, 15), DiferenciasReposicionSql.Char("@p4", movimiento.Almacen, 3),
                new SqlParameter("@p5", SqlDbType.SmallInt) { Value = (short)movimiento.Cantidad })
                .FirstOrDefaultAsync().ConfigureAwait(false);

            var respuesta = new ResultadoMovimientoStock { NumeroOrdenExtracto = apunte };
            bool conUbicaciones = await db.Database.SqlQuery<bool>(SQL_CON_UBICACIONES,
                DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", movimiento.Almacen, 3),
                DiferenciasReposicionSql.Char("@p2", movimiento.Producto, 15)).SingleAsync().ConfigureAwait(false);
            if (conUbicaciones)
            {
                if (movimiento.Cantidad > 0)
                {
                    _ = await puerta.EntradaPorDiferenciaReposicion(empresa, movimiento.Almacen, movimiento.Producto, movimiento.Cantidad,
                        movimiento.Traspaso, usuario).ConfigureAwait(false);
                }
                else
                {
                    int quitar = -movimiento.Cantidad;
                    ResumenUbicaciones resumen = await puerta.SalidaPorDiferenciaReposicion(empresa, movimiento.Almacen, movimiento.Producto, quitar,
                        movimiento.Traspaso, movimiento.PrimeroPendienteDeUbicar, usuario).ConfigureAwait(false);
                    respuesta.UnidadesSinHueco = Math.Max(0, quitar - (resumen?.Movimientos.Sum(m => m.Cantidad) ?? 0));
                }
            }
            return respuesta;
        }

        public Task GuardarAjuste(int id, string ajuste, int? numeroOrden, string mensaje)
        {
            return db.Database.ExecuteSqlCommandAsync(DiferenciasReposicionSql.SQL_GUARDAR_AJUSTE,
                new SqlParameter("@p0", SqlDbType.Int) { Value = id },
                DiferenciasReposicionSql.VarChar("@p1", ajuste, 10),
                DiferenciasReposicionSql.EnteroONulo("@p2", numeroOrden),
                DiferenciasReposicionSql.NVarChar("@p3", mensaje, 500));
        }

        public async Task GuardarResolucion(int id, string resolucion, string usuario, string comentario, int? numeroOrdenOrigen, int? numeroOrdenDestino)
        {
            int cambiadas = await db.Database.ExecuteSqlCommandAsync(DiferenciasReposicionSql.SQL_GUARDAR_RESOLUCION,
                new SqlParameter("@p0", SqlDbType.Int) { Value = id },
                DiferenciasReposicionSql.VarChar("@p1", resolucion, 20),
                DiferenciasReposicionSql.VarChar("@p2", usuario, 50),
                DiferenciasReposicionSql.NVarChar("@p3", comentario, 500),
                DiferenciasReposicionSql.EnteroONulo("@p4", numeroOrdenOrigen),
                DiferenciasReposicionSql.EnteroONulo("@p5", numeroOrdenDestino)).ConfigureAwait(false);
            if (cambiadas != 1)
            {
                throw new NestoBusinessException("La diferencia ya no está pendiente: no se ha hecho nada.") { StatusCode = System.Net.HttpStatusCode.Conflict };
            }
        }
    }
}
