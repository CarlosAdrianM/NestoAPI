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
        /// <summary>
        /// Lo que prdExtrProducto deja «pendiente de ubicar» (Ubicaciones estado 2 con NºTraspasoRepo) al contabilizar el
        /// diario de entrada de reposiciones, calculado ANTES de contabilizar (después ya no está en PreExtrProducto).
        /// </summary>
        Task<PendientesDeUbicarEntrada> LeerPendientesDeUbicar(string empresa, string diario);
        /// <summary>Lo deja pendiente de ubicar, salvo lo que ya haya dejado el procedimiento. Devuelve las filas puestas.</summary>
        Task<int> DejarPendientesDeUbicar(string empresa, PendientesDeUbicarEntrada pendientes, string usuario);
        /// <summary>prdExtrProducto del diario entero, como hoy desde Nesto viejo.</summary>
        Task Contabilizar(string empresa, string diario, string usuario);
        Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas);
        /// <summary>Para el ensayo: cómo leer las filas que toca dar entrada al diario (antes y después).</summary>
        Task<Func<Task<List<FilaEnsayoDTO>>>> PrepararFoto(string empresa, string diario, IReadOnlyCollection<int> traspasos);
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

        public Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas)
        {
            return EvidenciasRecepcionSql.Registrar(db, empresa, OrigenRecepcionReposiciones.TIPO, filas);
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
