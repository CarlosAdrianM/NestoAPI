using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// C1: la transacción del «No está». Un contexto propio por llamada y UNA transacción de EF para todo: lo de Ubicaciones, la fila
    /// de Inventarios (por el insert de siempre, en la misma conexión y transacción) y prdContabilizarInventario (por su único call
    /// site, que se apunta a la transacción abierta). Si el SP falla hace su propio ROLLBACK, que se lleva también lo de antes: no
    /// queda nada a medias. Solo se confirma si se ha regularizado de verdad y no es un ensayo.
    /// </summary>
    public class RepositorioNoEstaSql : IRepositorioNoEsta
    {
        public async Task<ResultadoNoEsta> EnTransaccion(Func<ITransaccionNoEsta, Task<ResultadoNoEsta>> trabajo, bool deshacerSiempre)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                using (DbContextTransaction transaccion = db.Database.BeginTransaction())
                {
                    try
                    {
                        ResultadoNoEsta resultado = await trabajo(new TransaccionNoEstaSql(db, transaccion)).ConfigureAwait(false);
                        bool guardar = !deshacerSiempre && resultado?.Estado == EstadoNoEsta.Hecho && resultado.Resultado?.Regularizado == true;
                        if (guardar)
                        {
                            transaccion.Commit();
                        }
                        else
                        {
                            transaccion.Rollback();
                        }
                        return resultado;
                    }
                    catch
                    {
                        try
                        {
                            transaccion.Rollback();
                        }
                        catch (Exception)
                        {
                            // El ROLLBACK del SP ya la ha deshecho
                        }
                        throw;
                    }
                }
            }
        }
    }

    public class TransaccionNoEstaSql : TransaccionSalidaSql, ITransaccionNoEsta
    {
        internal const string SQL_BLOQUEAR = @"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 50001, 'Otro usuario está inventariando este hueco ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";

        internal const string SQL_PRODUCTO = @"
SELECT RTRIM(p.Número) AS Numero, RTRIM(p.Nombre) AS Descripcion, RTRIM(p.Grupo) AS Grupo, RTRIM(p.SubGrupo) AS SubGrupo,
       RTRIM(p.Familia) AS Familia, CAST(ISNULL(p.Ficticio, 0) AS bit) AS Ficticio, CAST(ISNULL(p.Ubicar, 0) AS bit) AS Ubicar
FROM dbo.Productos p WHERE p.Empresa = @p0 AND p.Número = @p1";

        internal const string SQL_INVENTARIO_PENDIENTE = @"
SELECT (SELECT COUNT(*) FROM dbo.Inventarios i WHERE i.Empresa = @p0 AND i.Almacén = @p1 AND i.Número = @p2 AND i.Estado = 1 AND i.NºTraspaso IS NULL) AS DelProducto,
       (SELECT COUNT(*) FROM dbo.Inventarios i WHERE i.Empresa = @p0 AND i.Almacén = @p1 AND i.Usuario = @p3 AND i.Estado = 1 AND i.NºTraspaso IS NULL) AS DelUsuario";

        // Las mismas filas que pone a cero prdContabilizarInventario (estado 0 del hueco, sin mirar la empresa)
        internal const string SQL_LIBRE_EN_HUECO = @"
SELECT CAST(ISNULL(SUM(u.Cantidad), 0) AS int) FROM dbo.Ubicaciones u WITH (UPDLOCK)
WHERE u.Almacén = @p0 AND u.Número = @p1 AND u.Estado = 0 AND u.Pasillo = @p2 AND u.Fila = @p3 AND u.Columna = @p4";

        // Solo se LEE: ExtractoProducto solo lo toca prdExtrProducto (aquí, desde prdContabilizarInventario)
        internal const string SQL_STOCK_ALMACEN = @"
SELECT CAST(ISNULL(SUM(CAST(e.Cantidad AS int)), 0) AS int) FROM dbo.ExtractoProducto e
WHERE e.Número = @p0 AND e.Empresa IN ('1', '3') AND e.Almacén = @p1";

        // Como SQL_ALTERNATIVAS de CambiarHueco, por almacén
        internal const string SQL_LIBRES_EN_OTROS_HUECOS = @"
SELECT RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna) AS Ubicacion,
       RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) AS Codigo,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM dbo.Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.[Número] = @p2 AND u.Estado = 0 AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL
      AND u.Pasillo IS NOT NULL AND u.Fila IS NOT NULL AND u.Columna IS NOT NULL
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) <> @p3
GROUP BY u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) > 0";

        // Las reservas del picking en el hueco (como SQL_RESERVAS_EN_EL_HUECO de CambiarHueco), sin las líneas «en carpeta» ni las
        // negativas (el Terminar no les aplica faltas), con lo de muestra y nota de entrega para la falta definitiva
        internal const string SQL_RESERVAS_PICKING = @"
SELECT u.[NºOrden] AS Ubicacion, l.[Nº Orden] AS Linea, l.[Número] AS Pedido, RTRIM(l.Empresa) AS EmpresaLinea,
       RTRIM(l.[Almacén]) AS Almacen, RTRIM(l.Producto) AS Producto, CAST(u.Cantidad AS int) AS Cantidad,
       CAST(CASE WHEN l.SubGrupo = '" + Constantes.Productos.SUBGRUPO_MUESTRAS + @"' THEN 1 ELSE 0 END AS bit) AS EsMuestra,
       CAST(ISNULL(c.NotaEntrega, 0) AS bit) AS EsNotaEntrega
FROM LinPedidoVta l WITH (UPDLOCK)
     INNER JOIN Ubicaciones u WITH (UPDLOCK) ON u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3
     LEFT JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1 AND l.Producto = @p2
      AND ISNULL(l.Recoger, 0) = 0 AND l.Cantidad > 0
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) = @p3 AND u.Cantidad > 0
ORDER BY l.[Número] DESC, l.[Nº Orden] DESC";

        // La reserva (estado 3 del picking o 4 de la reposición, NestoAPI#858) vuelve a libre EN SU HUECO (no a «pendiente de
        // ubicar»): así el inventario del hueco la quita del stock. La fila libre nueva no lleva pedido, línea ni traspaso.
        internal const string SQL_RESERVA_A_LIBRE = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Pasillo, Fila, Columna, Estado, Usuario)
SELECT Empresa, [Almacén], [Número], @p1, Pasillo, Fila, Columna, 0, @p2 FROM Ubicaciones WHERE [NºOrden] = @p0 AND Estado IN (3, 4) AND Cantidad >= @p1;
IF @@ROWCOUNT = 0 THROW 50002, 'La reserva ya no está en ese hueco (otra PDA la ha cambiado): vuelva a abrir la recogida.', 1;
UPDATE Ubicaciones SET Cantidad = Cantidad - @p1 WHERE [NºOrden] = @p0;
DELETE Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0;";

        // NestoAPI#858: lo reservado para el traspaso en el hueco (como SQL_RESERVAS_DEL_TRASPASO_EN_EL_HUECO de la puerta), solo en
        // estado 4 (las -4 de Nesto viejo ya figuran como salida) y en el almacén de su salida pendiente. Pedido = el traspaso.
        internal const string SQL_RESERVAS_REPOSICION = @"
SELECT u.[NºOrden] AS Ubicacion, 0 AS Linea, CAST(u.[NºTraspasoRepo] AS int) AS Pedido, u.[NºOrdenRepo] AS LineaEntrada,
       RTRIM(@p0) AS EmpresaLinea, RTRIM(u.[Almacén]) AS Almacen, RTRIM(u.[Número]) AS Producto, CAST(u.Cantidad AS int) AS Cantidad,
       CAST(0 AS bit) AS EsMuestra, CAST(0 AS bit) AS EsNotaEntrega
FROM Ubicaciones u WITH (UPDLOCK)
WHERE u.[NºTraspasoRepo] = @p1 AND u.Estado = 4 AND u.[Número] = @p2 AND u.Cantidad > 0
      AND RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) = @p3
      AND u.[Almacén] IN (SELECT s.[Almacén] FROM PreExtrProducto s
                               INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
                          WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0
                                AND s.[Número] = @p2)
ORDER BY u.[NºOrden] DESC";

        // NestoAPI#858: lo que queda por salir del traspaso (todas sus líneas de salida)
        internal const string SQL_UNIDADES_POR_SALIR = @"
SELECT CAST(ISNULL(-SUM(s.Cantidad), 0) AS int) FROM PreExtrProducto s WITH (UPDLOCK)
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0";

        private readonly NVEntities db;
        private readonly DbContextTransaction transaccion;

        public TransaccionNoEstaSql(NVEntities db, DbContextTransaction transaccion) : base(db)
        {
            this.db = db;
            this.transaccion = transaccion;
        }

        private static SqlParameter Char(string nombre, int largo, string valor) =>
            new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter Texto(string nombre, string valor) =>
            new SqlParameter(nombre, System.Data.SqlDbType.VarChar, 30) { Value = (object)valor ?? DBNull.Value };

        private static string Quien(string usuario)
        {
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            quien = string.IsNullOrWhiteSpace(quien) ? "Ariadna" : quien;
            return quien.Length > 30 ? quien.Substring(0, 30) : quien;
        }

        public async Task Bloquear(string recurso)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_BLOQUEAR, new SqlParameter("@p0", System.Data.SqlDbType.NVarChar, 255) { Value = recurso }).ConfigureAwait(false);
        }

        public async Task<bool> ControlUbicaciones(string empresa, string almacen)
        {
            List<bool> control = await db.Database.SqlQuery<bool>(RepositorioContabilizarInventarioSql.SQL_CONTROL_UBICACIONES, empresa, almacen).ToListAsync().ConfigureAwait(false);
            return control.Count > 0 && control[0];
        }

        public async Task<ProductoNoEsta> LeerProducto(string empresa, string producto) =>
            (await db.Database.SqlQuery<ProductoNoEsta>(SQL_PRODUCTO, Char("@p0", 3, empresa), Char("@p1", 15, producto)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<InventarioPendienteNoEsta> InventarioPendiente(string empresa, string almacen, string producto, string usuarioTecnico) =>
            db.Database.SqlQuery<InventarioPendienteNoEsta>(SQL_INVENTARIO_PENDIENTE, Char("@p0", 3, empresa), Char("@p1", 3, almacen),
                Char("@p2", 15, producto), Texto("@p3", usuarioTecnico)).SingleAsync();

        public Task<int> LibreEnHueco(string almacen, string producto, string hueco) =>
            db.Database.SqlQuery<int>(SQL_LIBRE_EN_HUECO, Char("@p0", 3, almacen), Char("@p1", 15, producto),
                Char("@p2", 3, hueco.Substring(0, 3)), Char("@p3", 3, hueco.Substring(3, 3)), Char("@p4", 3, hueco.Substring(6, 3))).SingleAsync();

        public Task<int> StockAlmacen(string almacen, string producto) =>
            db.Database.SqlQuery<int>(SQL_STOCK_ALMACEN, Char("@p0", 15, producto), Char("@p1", 3, almacen)).SingleAsync();

        public Task InsertarFilaInventario(Inventario fila) =>
            RepositorioInventarioUbicacionesSql.InsertarFilas((SqlConnection)db.Database.Connection, (SqlTransaction)transaccion.UnderlyingTransaction,
                new List<Inventario> { fila });

        public Task<int> Contabilizar(string empresa, string usuario, string almacen) =>
            new RepositorioContabilizarInventarioSql(db).Contabilizar(empresa, usuario, almacen);

        public Task<int?> LeerTraspasoInventario(string empresa, string almacen, string usuario, DateTime fecha) =>
            new RepositorioContabilizarInventarioSql(db).LeerTraspaso(empresa, almacen, usuario, fecha);

        public Task<List<HuecoAlternativoDTO>> LibresEnOtrosHuecos(string empresa, string almacen, string producto, string huecoOrigen) =>
            db.Database.SqlQuery<HuecoAlternativoDTO>(SQL_LIBRES_EN_OTROS_HUECOS, Char("@p0", 3, empresa), Char("@p1", 3, almacen),
                Char("@p2", 15, producto), Texto("@p3", huecoOrigen ?? string.Empty)).ToListAsync();

        public Task<List<LibreEnHueco>> LibresEnHueco(string empresa, string almacen, string producto, string hueco) =>
            db.Database.SqlQuery<LibreEnHueco>(RepositorioCambioHuecoSql.SQL_LIBRES_EN_EL_HUECO, Char("@p0", 3, empresa), Char("@p1", 3, almacen),
                Char("@p2", 15, producto), Texto("@p3", hueco)).ToListAsync();

        public async Task<string> AlmacenPicking(string empresa, int picking) =>
            (await db.Database.SqlQuery<string>(RepositorioPreparacionAlmacen.SQL_ALMACEN_PICKING, Char("@p0", 3, empresa), new SqlParameter("@p1", picking))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<ReservaPropiaNoEsta>> ReservasPicking(string empresa, int picking, string producto, string hueco) =>
            db.Database.SqlQuery<ReservaPropiaNoEsta>(SQL_RESERVAS_PICKING, Char("@p0", 3, empresa), new SqlParameter("@p1", picking),
                Char("@p2", 15, producto), Texto("@p3", hueco)).ToListAsync();

        public async Task SoltarReservaAlHueco(int ubicacion, int cantidad, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_RESERVA_A_LIBRE, new SqlParameter("@p0", ubicacion), new SqlParameter("@p1", cantidad),
                Texto("@p2", Quien(usuario))).ConfigureAwait(false);
        }

        public async Task ReservarDeLaLibre(int libre, int cantidad, int linea, int pedido, string empresaLinea, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(RepositorioCambioHuecoSql.SQL_RESERVAR_DE_LA_LIBRE,
                new SqlParameter("@p0", libre), new SqlParameter("@p1", cantidad), new SqlParameter("@p2", linea), new SqlParameter("@p3", pedido),
                Char("@p4", 3, empresaLinea), Texto("@p5", Quien(usuario))).ConfigureAwait(false);
        }

        public async Task<string> AlmacenReposicion(string empresa, int traspaso) =>
            (await db.Database.SqlQuery<string>(RepositorioPreparacionAlmacen.SQL_ORIGEN_REPOSICION, Char("@p0", 3, empresa), new SqlParameter("@p1", traspaso))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<ReservaPropiaNoEsta>> ReservasReposicion(string empresa, int traspaso, string producto, string hueco) =>
            db.Database.SqlQuery<ReservaPropiaNoEsta>(SQL_RESERVAS_REPOSICION, Char("@p0", 3, empresa), new SqlParameter("@p1", traspaso),
                Char("@p2", 15, producto), Texto("@p3", hueco)).ToListAsync();

        public async Task ReservarDeLaLibreParaElTraspaso(int libre, int cantidad, int? lineaEntrada, int traspaso, string usuario)
        {
            // La misma escritura que CambiarHuecoReservaReposicion (puerta de Ubicaciones): NULL si la libre ya no tenía esas unidades
            int? nueva = (await db.Database.SqlQuery<int?>(PuertaUbicacionesSql.SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO,
                new SqlParameter("@p0", libre), new SqlParameter("@p1", cantidad),
                new SqlParameter("@p2", System.Data.SqlDbType.Int) { Value = (object)lineaEntrada ?? DBNull.Value },
                new SqlParameter("@p3", traspaso), Texto("@p4", Quien(usuario))).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            if (nueva == null)
            {
                throw new InvalidOperationException("el hueco al que se iba a pasar la reserva ha cambiado mientras tanto (otra PDA): vuelva a intentarlo");
            }
        }

        public Task<int> UnidadesPorSalir(string empresa, int traspaso) =>
            db.Database.SqlQuery<int>(SQL_UNIDADES_POR_SALIR, Char("@p0", 3, empresa), new SqlParameter("@p1", traspaso)).SingleAsync();

        public async Task ApuntarRastro(Modificacion rastro)
        {
            if (rastro == null)
            {
                return;
            }
            _ = db.Modificaciones.Add(rastro);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
