using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
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
    /// <summary>Lo que se escribe al dar entrada a una reposición, todo dentro de una transacción.</summary>
    public interface ITransaccionCierreReposicion
    {
        Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia);
        /// <summary>Almacenes.DiarioEntradaRep del almacén de destino. Null si no tiene.</summary>
        Task<string> DiarioDeEntrada(string empresa, string almacen);
        /// <summary>Los traspasos que hay en ese diario del almacén, pendientes de contabilizar.</summary>
        Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario);
        /// <summary>Lo enviado en ese traspaso y pendiente de entrar (lo mismo que ve el mozo).</summary>
        Task<List<FilaReposicion>> LeerLineas(string empresa, string almacen, int traspaso);
        /// <summary>
        /// Filas del diario que prdExtrProducto trataría distinto llamado desde la API que desde Nesto viejo (con hueco o en
        /// negativo: van por bloques del procedimiento que dependen del almacén del usuario). 0 en el día a día.
        /// </summary>
        Task<int> FilasQueSoloSabeNestoViejo(string empresa, string diario);
        /// <summary>De dónde viene el traspaso y quién lo creó, según sus filas del diario de entrada.</summary>
        Task<DatosTraspasoReposicion> LeerDatosTraspaso(string empresa, string almacen, string diario, int traspaso);
        /// <summary>
        /// Deja el producto en el diario de entrada de ese traspaso con EXACTAMENTE <paramref name="cantidad"/> (lo leído): si
        /// tiene filas, en la primera (las demás se quitan; con 0, se quitan todas); si no venía, una fila nueva copiada de
        /// otra del mismo traspaso. Lanza si el producto no existe.
        /// </summary>
        Task AjustarALoLeido(string empresa, string almacen, string diario, int traspaso, string producto, int cantidad);
        /// <summary>
        /// Lo que prdExtrProducto deja «pendiente de ubicar» (Ubicaciones estado 2 con NºTraspasoRepo) al contabilizar el
        /// diario de entrada de reposiciones, calculado ANTES de contabilizar (después ya no está en PreExtrProducto).
        /// </summary>
        Task<PendientesDeUbicarEntrada> LeerPendientesDeUbicar(string empresa, string diario);
        /// <summary>Lo deja pendiente de ubicar, salvo lo que ya haya dejado el procedimiento. Devuelve las filas puestas.</summary>
        Task<int> DejarPendientesDeUbicar(string empresa, PendientesDeUbicarEntrada pendientes, string usuario);
        /// <summary>prdExtrProducto del diario entero, como hoy desde Nesto viejo.</summary>
        Task Contabilizar(string empresa, string diario, string usuario);
        /// <summary>NestoAPI#553: aparta del diario lo que no es de este traspaso (ver <see cref="ApartadoTraspasosSql"/>).</summary>
        Task<List<int>> ApartarOtros(string empresa, string diario, int traspaso);
        /// <summary>Devuelve al diario lo apartado con <see cref="ApartarOtros"/>.</summary>
        Task DevolverApartadas(string empresa, string diario, IReadOnlyCollection<int> apartadas);
        Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas);
        /// <summary>
        /// NestoAPI#577: la reposición ya ha entrado en el destino (cabecera ReposicionesTraspasos: quién y cuándo). Sin
        /// cabecera (de Nesto viejo), no hace nada.
        /// </summary>
        Task MarcarReposicionRecibida(string empresa, int traspaso, string usuario);
        /// <summary>Para el ensayo: cómo leer las filas que toca dar entrada al diario (antes y después).</summary>
        Task<Func<Task<List<FilaEnsayoDTO>>>> PrepararFoto(string empresa, string diario, IReadOnlyCollection<int> traspasos);
    }

    /// <summary>
    /// Lo que se sabe de un traspaso por sus filas del diario de entrada (las crea Nesto viejo al hacer la reposición): el
    /// almacén de origen va en Delegación y quien lo creó en Usuario (comprobado con 80867, 80871 y 80872).
    /// </summary>
    public class DatosTraspasoReposicion
    {
        public string Origen { get; set; }
        /// <summary>Con dominio, como se graba («NUEVAVISION\Andre»). Null si no se sabe.</summary>
        public string Creador { get; set; }
    }

    /// <summary>Lo que quedará pendiente de ubicar al dar entrada a un diario de reposiciones.</summary>
    public class PendientesDeUbicarEntrada
    {
        /// <summary>El NºOrden más alto de Ubicaciones antes de contabilizar: lo que el procedimiento ponga irá por encima.</summary>
        public int UltimaUbicacion { get; set; }
        public List<PendienteDeUbicarEntrada> Filas { get; set; } = new List<PendienteDeUbicarEntrada>();
    }

    /// <summary>Una fila de Ubicaciones en estado 2, como la pone prdExtrProducto (agrupada por almacén, producto y traspaso).</summary>
    public class PendienteDeUbicarEntrada
    {
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public int? Traspaso { get; set; }
    }

    public interface IRepositorioCierreReposiciones
    {
        /// <summary>
        /// Todo o nada. Con <paramref name="deshacerSiempre"/> (el ensayo) se deshace SIEMPRE, también si ha ido bien: el
        /// ensayo y lo de verdad son el mismo código y solo cambia quién cierra.
        /// </summary>
        Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> trabajo, bool deshacerSiempre);
    }

    /// <summary>
    /// NestoAPI#553: dar entrada a una reposición es lo que hace hoy Nesto viejo: contabilizar con prdExtrProducto el
    /// diario de entrada de reposiciones (Almacenes.DiarioEntradaRep) del almacén de destino, con el usuario que la da.
    /// prdExtrProducto contabiliza el diario ENTERO (en los últimos 60 días, a veces entraban dos traspasos a la vez).
    /// </summary>
    public class RepositorioCierreReposiciones : IRepositorioCierreReposiciones, IDisposable
    {
        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioCierreReposiciones(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioCierreReposiciones ConContextoPropio()
        {
            return new RepositorioCierreReposiciones(new NVEntities()) { contextoPropio = true };
        }

        public async Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> trabajo,
            bool deshacerSiempre)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    ResultadoTerminarRecepcionDTO resultado = await trabajo(new TransaccionCierreReposicionSql(db)).ConfigureAwait(false);
                    if (deshacerSiempre)
                    {
                        transaccion.Rollback();
                    }
                    else
                    {
                        transaccion.Commit();
                    }
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
                        // El ROLLBACK del procedimiento ya la ha deshecho
                    }
                    SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        throw new NestoBusinessException(EvidenciasRecepcionSql.Traducir(sql), ex);
                    }
                    throw;
                }
            }
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
    /// NestoAPI#553: contabilizar UN traspaso aunque su diario tenga otros. prdExtrProducto contabiliza el diario entero y
    /// ALG saca hacia REI y hacia ALC por el mismo («General»), así que hoy no se puede ir a las dos tiendas el mismo día.
    /// Lo que se hacía a mano: esconder la otra en el diario «RepoEscond» (Reposición escondida, creado para eso en 2023,
    /// nunca contabilizado), contabilizar y devolverla. Aquí, dentro de la misma transacción: se apartan las filas del
    /// diario que no son del traspaso, se contabiliza y se devuelven (por su [Nº Orden], que es identidad: no chocan).
    /// </summary>
    internal static class ApartadoTraspasosSql
    {
        internal const string DIARIO_APARTADO = "RepoEscond";

        internal const string SQL_APARTAR = @"
UPDATE PreExtrProducto SET Diario = @p2
OUTPUT inserted.[Nº Orden]
WHERE Empresa = @p0 AND Diario = @p1 AND ISNULL([NºTraspaso], 0) <> @p3";

        internal const string SQL_DEVOLVER = @"
UPDATE PreExtrProducto SET Diario = @p1
WHERE Empresa = @p0 AND Diario = @p2 AND [Nº Orden] IN ({LISTA})";

        internal static async Task<List<int>> Apartar(NVEntities db, string empresa, string diario, int traspaso)
        {
            return await db.Database.SqlQuery<int>(SQL_APARTAR, empresa, diario, DIARIO_APARTADO, traspaso).ToListAsync().ConfigureAwait(false);
        }

        internal static async Task Devolver(NVEntities db, string empresa, string diario, IReadOnlyCollection<int> apartadas)
        {
            if (apartadas == null || apartadas.Count == 0)
            {
                return;
            }
            string sql = SQL_DEVOLVER.Replace("{LISTA}", string.Join(",", apartadas));
            int devueltas = await db.Database.ExecuteSqlCommandAsync(sql, empresa, diario, DIARIO_APARTADO).ConfigureAwait(false);
            if (devueltas != apartadas.Count)
            {
                throw new NestoBusinessException($"Al devolver al diario {diario} las {apartadas.Count} líneas de otros traspasos " +
                    $"apartadas en {DIARIO_APARTADO}, solo se han encontrado {devueltas}. No se ha hecho nada.");
            }
        }
    }

    public class TransaccionCierreReposicionSql : ITransaccionCierreReposicion
    {
        internal const string SQL_DIARIO_ENTRADA = @"
SELECT RTRIM(DiarioEntradaRep) FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // El mismo origen que la lectura de lo pendiente (RepositorioRecepcionReposiciones)
        internal const string SQL_TRASPASOS_EN_DIARIO = @"
SELECT DISTINCT p.[NºTraspaso] FROM PreExtrProducto p WITH (UPDLOCK)
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.Diario = @p2 AND p.[NºTraspaso] > 0";

        private readonly NVEntities db;
        private readonly IServicioExtractoProducto extractos;

        public TransaccionCierreReposicionSql(NVEntities db, IServicioExtractoProducto extractos = null)
        {
            this.db = db;
            this.extractos = extractos ?? new ServicioExtractoProducto();
        }

        public Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia)
        {
            return EvidenciasRecepcionSql.YaRegistrada(db, idsEvidencia);
        }

        public async Task<string> DiarioDeEntrada(string empresa, string almacen)
        {
            string diario = await db.Database.SqlQuery<string>(SQL_DIARIO_ENTRADA, empresa, almacen).FirstOrDefaultAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(diario) ? null : diario.Trim();
        }

        public Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario)
        {
            return db.Database.SqlQuery<int>(SQL_TRASPASOS_EN_DIARIO, empresa, almacen, diario).ToListAsync();
        }

        public Task<List<FilaReposicion>> LeerLineas(string empresa, string almacen, int traspaso)
        {
            return db.Database.SqlQuery<FilaReposicion>(RepositorioRecepcionReposiciones.SQL_LINEAS, empresa, almacen, traspaso).ToListAsync();
        }

        // Lo que prdExtrProducto hace con estas filas depende del almacén del usuario (bloques de «salidas sin ubicación» y
        // de «más cantidad en la línea que en la ubicación», y el UPDATE/INSERT de huecos con pasillo): desde la API, no
        internal const string SQL_FILAS_QUE_SOLO_SABE_NESTO_VIEJO = @"
SELECT COUNT(*) FROM PreExtrProducto p
WHERE p.Empresa = @p0 AND p.Diario = @p1 AND p.Estado >= 0 AND (p.Pasillo IS NOT NULL OR p.Cantidad < 0)";

        public Task<int> FilasQueSoloSabeNestoViejo(string empresa, string diario)
        {
            return db.Database.SqlQuery<int>(SQL_FILAS_QUE_SOLO_SABE_NESTO_VIEJO, empresa, diario).SingleAsync();
        }

        internal const string SQL_DATOS_TRASPASO = @"
SELECT RTRIM(MAX(p.[Delegación])) AS Origen, RTRIM(MAX(p.Usuario)) AS Creador
FROM PreExtrProducto p
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.Diario = @p2 AND p.[NºTraspaso] = @p3";

        public async Task<DatosTraspasoReposicion> LeerDatosTraspaso(string empresa, string almacen, string diario, int traspaso)
        {
            DatosTraspasoReposicion datos = await db.Database.SqlQuery<DatosTraspasoReposicion>(SQL_DATOS_TRASPASO,
                ParametroChar("@p0", empresa, 3), ParametroChar("@p1", almacen, 3), ParametroChar("@p2", diario, 10),
                new SqlParameter("@p3", System.Data.SqlDbType.Int) { Value = traspaso })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            return datos ?? new DatosTraspasoReposicion();
        }

        // #553 (Carlos, 04/10/26): lo que entra es lo leído. Lo que haría a mano quien da la entrada en Nesto viejo: cambiar la
        // cantidad de la fila del diario antes de contabilizar. Si el producto tiene varias filas en el traspaso (raro: 2 en
        // 120 días), queda una con todo. Lo que no venía, una fila nueva igual que las demás del traspaso (Fecha, Texto,
        // Almacén, Delegación = origen, Forma Venta, Asiento Automático, Vendedor, Estado, NºTraspaso, Usuario = quien lo
        // creó…) con el Grupo del producto (en los datos siempre coincide con Productos.Grupo). Coste e Importe van a NULL
        // como en todas las filas de entrada de reposiciones (120 días: ninguna con valor). Devuelve las filas que quedan
        // del producto (0 si no existe el producto).
        internal const string SQL_AJUSTAR_A_LO_LEIDO = @"
DECLARE @primera int, @quedan int = 0;
SELECT @primera = MIN([Nº Orden]) FROM PreExtrProducto WITH (UPDLOCK, HOLDLOCK)
WHERE Empresa = @p0 AND [Almacén] = @p1 AND Diario = @p2 AND [NºTraspaso] = @p3 AND [Número] = @p4;
IF @primera IS NULL
BEGIN
    IF @p5 > 0
    BEGIN
        INSERT INTO PreExtrProducto (Empresa, Diario, [Número], Fecha, [Nº Cliente], ContactoCliente, [NºProveedor], ContactoProveedor,
            [Albarán], Factura, Texto, [Almacén], Grupo, Cantidad, Coste, Importe, [Delegación], [Forma Venta], [Asiento Automático],
            LinPedido, Vendedor, Estado, [NºTraspaso], [NºPedido], Pasillo, Fila, Columna, CentroCoste, Departamento, Usuario)
        SELECT TOP 1 m.Empresa, m.Diario, pr.[Número], m.Fecha, m.[Nº Cliente], m.ContactoCliente, m.[NºProveedor], m.ContactoProveedor,
            m.[Albarán], m.Factura, m.Texto, m.[Almacén], pr.Grupo, @p5, NULL, NULL, m.[Delegación], m.[Forma Venta], m.[Asiento Automático],
            m.LinPedido, m.Vendedor, m.Estado, m.[NºTraspaso], m.[NºPedido], m.Pasillo, m.Fila, m.Columna, m.CentroCoste, m.Departamento, m.Usuario
        FROM PreExtrProducto m INNER JOIN Productos pr ON pr.Empresa = m.Empresa AND pr.[Número] = @p4
        WHERE m.Empresa = @p0 AND m.[Almacén] = @p1 AND m.Diario = @p2 AND m.[NºTraspaso] = @p3
        ORDER BY m.[Nº Orden];
        SET @quedan = @@ROWCOUNT;
    END
END
ELSE
BEGIN
    DELETE FROM PreExtrProducto
    WHERE Empresa = @p0 AND [Almacén] = @p1 AND Diario = @p2 AND [NºTraspaso] = @p3 AND [Número] = @p4
      AND ([Nº Orden] <> @primera OR @p5 = 0);
    IF @p5 > 0
    BEGIN
        UPDATE PreExtrProducto SET Cantidad = @p5 WHERE Empresa = @p0 AND Diario = @p2 AND [Nº Orden] = @primera;
        SET @quedan = 1;
    END
END
SELECT @quedan;";

        public async Task AjustarALoLeido(string empresa, string almacen, string diario, int traspaso, string producto, int cantidad)
        {
            if (cantidad < 0 || cantidad > short.MaxValue)
            {
                throw new NestoBusinessException($"No se pueden recibir {cantidad} unidades de {producto} en una línea. No se ha recibido nada.");
            }
            int quedan = await db.Database.SqlQuery<int>(SQL_AJUSTAR_A_LO_LEIDO,
                ParametroChar("@p0", empresa, 3), ParametroChar("@p1", almacen, 3), ParametroChar("@p2", diario, 10),
                new SqlParameter("@p3", System.Data.SqlDbType.Int) { Value = traspaso },
                ParametroChar("@p4", producto, 15),
                new SqlParameter("@p5", System.Data.SqlDbType.SmallInt) { Value = (short)cantidad })
                .SingleAsync().ConfigureAwait(false);
            if (cantidad > 0 && quedan == 0)
            {
                throw new NestoBusinessException($"El producto {producto} no existe: no se puede dar entrada a lo leído. No se ha recibido nada.");
            }
        }

        private static SqlParameter ParametroChar(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };
        }

        // COPIA EXACTA del SELECT del INSERT de prdExtrProducto para el diario de entrada de reposiciones del almacén del
        // usuario (bloque «David Sanchez 20/06/05», líneas ~431-439): mismos filtros, misma agrupación. Desde la API
        // prdExtrProducto busca el AlmacénPedidoVta de SYSTEM_USER (RDS2016$, que no tiene), @DiarioEntradaRepo queda NULL,
        // «@diario <> @diariorepo» es UNKNOWN y se salta el bloque entero: lo recibido no quedaba pendiente de ubicar.
        internal const string SQL_PENDIENTES_DE_UBICAR = @"
SELECT RTRIM(p.[Almacén]) AS Almacen, RTRIM(p.[Número]) AS Producto, CAST(SUM(p.Cantidad) AS int) AS Cantidad, p.[NºTraspaso] AS Traspaso
FROM PreExtrProducto AS p INNER JOIN Almacenes AS a
     ON p.Empresa = a.Empresa AND p.[Almacén] = a.[Número]
     INNER JOIN Productos AS pr ON p.Empresa = pr.Empresa AND p.[Número] = pr.[Número]
WHERE p.Estado >= 0 AND p.Empresa = @p0 AND a.ControlUbicaciones = 1 AND p.Cantidad > 0 AND p.Diario = @p1
  AND (p.LinPedido IS NULL OR (p.LinPedido IS NOT NULL AND p.Diario = '_EntregFac' AND p.[Albarán] IS NULL)
       OR (p.LinPedido IS NOT NULL AND p.Diario = '_EntFacCmp' AND p.[Albarán] IS NULL)
       OR (p.LinPedido IS NOT NULL AND p.Diario = '_RecogFac' AND p.[Albarán] IS NULL))
  AND p.Pasillo IS NULL AND pr.Ubicar = 1
GROUP BY p.[Almacén], p.[Número], p.[NºTraspaso]";

        internal const string SQL_ULTIMA_UBICACION = "SELECT ISNULL(MAX([NºOrden]), 0) FROM Ubicaciones";

        // La misma fila que pone el procedimiento (empresa, almacén, número, cantidad, estado 2, NºTraspasoRepo, usuario),
        // salvo que ya la haya puesto él (si algún día encuentra el almacén del usuario): así nunca hay dos
        internal const string SQL_DEJAR_PENDIENTE_DE_UBICAR = @"
DECLARE @puestas int = 0;
IF NOT EXISTS (SELECT 1 FROM Ubicaciones
               WHERE [NºOrden] > @p0 AND Empresa = @p1 AND [Almacén] = @p2 AND [Número] = @p3 AND Estado = 2 AND Pasillo IS NULL
                 AND ([NºTraspasoRepo] = @p5 OR ([NºTraspasoRepo] IS NULL AND @p5 IS NULL)))
BEGIN
    INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, [NºTraspasoRepo], Usuario)
    VALUES (@p1, @p2, @p3, @p4, 2, @p5, @p6);
    SET @puestas = @@ROWCOUNT;
END
SELECT @puestas;";

        public async Task<PendientesDeUbicarEntrada> LeerPendientesDeUbicar(string empresa, string diario)
        {
            return new PendientesDeUbicarEntrada
            {
                UltimaUbicacion = await db.Database.SqlQuery<int>(SQL_ULTIMA_UBICACION).SingleAsync().ConfigureAwait(false),
                Filas = await db.Database.SqlQuery<PendienteDeUbicarEntrada>(SQL_PENDIENTES_DE_UBICAR, empresa, diario).ToListAsync().ConfigureAwait(false)
            };
        }

        public async Task<int> DejarPendientesDeUbicar(string empresa, PendientesDeUbicarEntrada pendientes, string usuario)
        {
            int puestas = 0;
            foreach (PendienteDeUbicarEntrada fila in pendientes?.Filas ?? new List<PendienteDeUbicarEntrada>())
            {
                puestas += await db.Database.SqlQuery<int>(SQL_DEJAR_PENDIENTE_DE_UBICAR,
                    new SqlParameter("@p0", System.Data.SqlDbType.Int) { Value = pendientes.UltimaUbicacion },
                    new SqlParameter("@p1", System.Data.SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@p2", System.Data.SqlDbType.Char, 3) { Value = fila.Almacen },
                    new SqlParameter("@p3", System.Data.SqlDbType.Char, 15) { Value = fila.Producto },
                    new SqlParameter("@p4", System.Data.SqlDbType.Int) { Value = fila.Cantidad },
                    new SqlParameter("@p5", System.Data.SqlDbType.Int) { Value = (object)fila.Traspaso ?? DBNull.Value },
                    new SqlParameter("@p6", System.Data.SqlDbType.VarChar, 30) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) })
                    .SingleAsync().ConfigureAwait(false);
            }
            return puestas;
        }

        public Task Contabilizar(string empresa, string diario, string usuario)
        {
            return extractos.ContabilizarDiario(db, empresa, diario, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public Task<List<int>> ApartarOtros(string empresa, string diario, int traspaso)
        {
            return ApartadoTraspasosSql.Apartar(db, empresa, diario, traspaso);
        }

        public Task DevolverApartadas(string empresa, string diario, IReadOnlyCollection<int> apartadas)
        {
            return ApartadoTraspasosSql.Devolver(db, empresa, diario, apartadas);
        }

        public Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas)
        {
            return EvidenciasRecepcionSql.Registrar(db, empresa, OrigenRecepcionReposiciones.TIPO, filas);
        }

        public Task MarcarReposicionRecibida(string empresa, int traspaso, string usuario)
        {
            return Reposiciones.RegistroReposicionesTraspasosSql.MarcarRecibida(db, empresa, traspaso, usuario);
        }

        internal const string UBICACIONES_DE_REPOSICIONES = "u.Empresa = @p0 AND u.Estado = 2 AND u.[NºTraspasoRepo] IN ({LISTA})";

        public Task<Func<Task<List<FilaEnsayoDTO>>>> PrepararFoto(string empresa, string diario, IReadOnlyCollection<int> traspasos)
        {
            return FotosEnsayoRecepcionSql.Preparar(db.Database, empresa, diario, null, UBICACIONES_DE_REPOSICIONES, traspasos);
        }
    }

    /// <summary>
    /// Las filas que enseña el ensayo de una recepción, comunes a todos los tipos: el diario de producto que se contabiliza,
    /// los apuntes nuevos del extracto y las ubicaciones nuevas (por encima de los últimos números de antes de empezar) más
    /// las «pendientes de ubicar» de lo que se recibe. Cada tipo añade las suyas.
    /// </summary>
    public static class FotosEnsayoRecepcionSql
    {
        public class Marcas
        {
            public int UltimaUbicacion { get; set; }
            public int UltimoExtracto { get; set; }
            public int UltimoAlbaranCmp { get; set; }
        }

        internal const string SQL_MARCAS = @"
SELECT CAST(ISNULL((SELECT MAX([NºOrden]) FROM Ubicaciones), 0) AS int) AS UltimaUbicacion,
       CAST(ISNULL((SELECT MAX([Nº Orden]) FROM ExtractoProducto WHERE Empresa = @p0), 0) AS int) AS UltimoExtracto,
       CAST(ISNULL((SELECT MAX([Número]) FROM [CabAlbaránCmp] WHERE Empresa = @p0), 0) AS int) AS UltimoAlbaranCmp";

        // @p0 empresa, @p1 diario, @p2 último extracto, @p3 última ubicación, @p4 último albarán de compra, @p5… la lista
        internal const string SQL_FILAS_COMUNES = @"
SELECT 'PreExtrProducto' AS Tabla, CAST(p.[Nº Orden] AS varchar(20)) AS Clave,
       CONCAT('Diario=', RTRIM(p.Diario), '; Almacén=', RTRIM(p.[Almacén]), '; Producto=', RTRIM(p.[Número]), '; Cantidad=', p.Cantidad,
              '; Estado=', p.Estado, '; Traspaso=', p.[NºTraspaso], '; LinPedido=', p.LinPedido) AS Datos
FROM PreExtrProducto p WHERE p.Empresa = @p0 AND p.Diario = @p1
UNION ALL
SELECT 'ExtractoProducto', CAST(e.[Nº Orden] AS varchar(20)),
       CONCAT('Diario=', RTRIM(e.Diario), '; Almacén=', RTRIM(e.[Almacén]), '; Producto=', RTRIM(e.[Número]), '; Cantidad=', e.Cantidad,
              '; Traspaso=', e.[NºTraspaso], '; Pedido=', e.[NºPedido], '; LinPedido=', e.LinPedido, '; Usuario=', RTRIM(e.Usuario))
FROM ExtractoProducto e WHERE e.Empresa = @p0 AND e.[Nº Orden] > @p2
UNION ALL
SELECT 'Ubicaciones', CAST(u.[NºOrden] AS varchar(20)),
       CONCAT('Almacén=', RTRIM(u.[Almacén]), '; Producto=', RTRIM(u.[Número]), '; Estado=', u.Estado, '; Cantidad=', u.Cantidad,
              '; Hueco=', ISNULL(RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna), '-'), '; NºTraspasoRepo=', u.[NºTraspasoRepo],
              '; PedidoCmp=', u.PedidoCmp, '; AlbaránCmp=', u.[AlbaránCmp], '; NºOrdenCmp=', u.[NºOrdenCmp], '; Usuario=', RTRIM(u.Usuario))
FROM Ubicaciones u WHERE u.[NºOrden] > @p3 OR ({UBICACIONES})";

        /// <summary>
        /// Fija las marcas y la lista ANTES de tocar nada y devuelve cómo leer las filas (se llama antes y después).
        /// </summary>
        /// <param name="sqlPropio">Las filas propias del tipo (o null), con {LISTA} donde va la lista de números.</param>
        /// <param name="condicionUbicaciones">Qué ubicaciones de antes enseñar además de las nuevas, con {LISTA}.</param>
        public static async Task<Func<Task<List<FilaEnsayoDTO>>>> Preparar(Database baseDeDatos, string empresa, string diario,
            string sqlPropio, string condicionUbicaciones, IEnumerable<int> numeros)
        {
            Marcas marcas = await baseDeDatos.SqlQuery<Marcas>(SQL_MARCAS, empresa).SingleAsync().ConfigureAwait(false);
            string sql = Montar(sqlPropio, condicionUbicaciones, numeros, out List<object> lista);
            var parametros = new List<object> { empresa, diario, marcas.UltimoExtracto, marcas.UltimaUbicacion, marcas.UltimoAlbaranCmp };
            parametros.AddRange(lista);
            object[] valores = parametros.ToArray();
            return () => baseDeDatos.SqlQuery<FilaEnsayoDTO>(sql, valores).ToListAsync();
        }

        internal static string Montar(string sqlPropio, string condicionUbicaciones, IEnumerable<int> numeros, out List<object> lista)
        {
            lista = (numeros ?? Enumerable.Empty<int>()).Distinct().Cast<object>().ToList();
            if (lista.Count == 0)
            {
                lista.Add(-1);
            }
            string enLista = string.Join(", ", lista.Select((n, i) => "@p" + (5 + i)));
            string comunes = SQL_FILAS_COMUNES.Replace("{UBICACIONES}", (condicionUbicaciones ?? "1 = 0").Replace("{LISTA}", enLista));
            return string.IsNullOrWhiteSpace(sqlPropio) ? comunes : sqlPropio.Replace("{LISTA}", enLista) + Environment.NewLine + "UNION ALL" + comunes;
        }
    }

    /// <summary>La evidencia de las recepciones en PreparacionEscaneos (Fase RECE), común a todos los tipos.</summary>
    public static class EvidenciasRecepcionSql
    {
        public const string FASE = "RECE";

        internal const string SQL_YA_REGISTRADA = "SELECT COUNT(*) FROM PreparacionEscaneos WHERE IdCliente IN ({0})";

        internal const string SQL_EVIDENCIA = @"
INSERT INTO PreparacionEscaneos
       (IdCliente, Empresa, NumeroOrigen, Pedido, LineaPedido, Producto, Fase, Cantidad, Metodo, Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo, TipoOrigen)
SELECT @p0, @p1, @p2, NULL, NULL, @p3, '" + FASE + @"', @p4, 'SCAN', NULL, @p5, @p6, @p7, GETDATE(), @p8
WHERE NOT EXISTS (SELECT 1 FROM PreparacionEscaneos WHERE IdCliente = @p0)";

        public static async Task<bool> YaRegistrada(NVEntities db, IEnumerable<Guid> idsEvidencia)
        {
            List<Guid> ids = (idsEvidencia ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (!ids.Any())
            {
                return false;
            }
            string parametros = string.Join(", ", ids.Select((id, i) => "@p" + i));
            int encontrados = await db.Database
                .SqlQuery<int>(string.Format(SQL_YA_REGISTRADA, parametros), ids.Cast<object>().ToArray())
                .SingleAsync().ConfigureAwait(false);
            return encontrados > 0;
        }

        public static async Task Registrar(NVEntities db, string empresa, string tipoOrigen, IEnumerable<EvidenciaRecepcion> filas)
        {
            foreach (EvidenciaRecepcion fila in filas ?? Enumerable.Empty<EvidenciaRecepcion>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_EVIDENCIA, fila.IdCliente, empresa, fila.NumeroOrigen, fila.Producto,
                    (short)Math.Min(fila.Cantidad, short.MaxValue), $"Recepción {fila.IdRecepcion}",
                    Recortar(UsuarioAuditoriaHelper.ParaAuditoria(fila.Usuario), 50), (object)Recortar(fila.Dispositivo, 50) ?? DBNull.Value, tipoOrigen)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>El IdCliente de la evidencia de un producto en una recepción: siempre el mismo (un reenvío se reconoce).</summary>
        public static Guid IdEvidencia(Guid idRecepcion, string producto)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] datos = idRecepcion.ToByteArray()
                    .Concat(System.Text.Encoding.UTF8.GetBytes(PlanificadorRecepcionCompra.Normalizar(producto) ?? string.Empty))
                    .ToArray();
                return new Guid(md5.ComputeHash(datos));
            }
        }

        /// <summary>Un error de SQL de la recepción, en palabras del almacén.</summary>
        internal static string Traducir(SqlException ex)
        {
            string mensaje = ex?.Message ?? string.Empty;
            if (mensaje.Contains("CK_PreparacionEscaneos"))
            {
                return "La base de datos todavía no admite la evidencia de las recepciones: falta lanzar el script " +
                    "Scripts/Issue559_Ariadna_RecepcionCompras.sql. No se ha recibido nada.";
            }
            return mensaje;
        }

        private static string Recortar(string texto, int maximo)
        {
            return texto == null ? null : (texto.Length > maximo ? texto.Substring(0, maximo) : texto);
        }
    }
}
