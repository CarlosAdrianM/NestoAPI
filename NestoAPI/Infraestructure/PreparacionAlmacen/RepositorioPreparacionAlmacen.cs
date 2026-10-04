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
    /// <summary>Una línea de pedido dentro de un picking, con los datos de su entrega.</summary>
    public class FilaPackingAlmacen
    {
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Ruta { get; set; }
        public string ComentarioPicking { get; set; }
        public int LineaPedido { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Ariadna: una entrega (cliente + dirección) de un picking en curso con cuántos bultos tiene ya.</summary>
    public class EntregaPorEmpaquetar
    {
        public int Picking { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public int PrimerPedido { get; set; }
        public int Pedidos { get; set; }
        public int Unidades { get; set; }
        /// <summary>Números de bulto distintos con foto (un bulto compartido por dos pedidos cuenta una vez).</summary>
        public int BultosConFoto { get; set; }
        public int BultosSinFoto { get; set; }
        /// <summary>El picking se terminó en Ariadna (PreparacionSalidasTerminadas).</summary>
        public bool Terminado { get; set; }
    }

    /// <summary>Lo leído de un producto (suma de sus lecturas y toques, sin las faltas).</summary>
    public class LecturaProductoAlmacen
    {
        public string Producto { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>Lo leído y lo dado por falta de un producto en el picking por ola.</summary>
    public class LecturaPickingAlmacen
    {
        public string Producto { get; set; }
        public int Unidades { get; set; }
        public int Faltas { get; set; }
    }

    /// <summary>
    /// NestoAPI#556: el acceso a datos de la preparación con Ariadna. Separado de las reglas
    /// (<see cref="ServicioPreparacionAlmacen"/>) para poder probarlas sin base de datos.
    /// </summary>
    public interface IRepositorioPreparacionAlmacen
    {
        Task<List<LineaPickingAlmacenDTO>> LeerLineasPicking(string empresa, int picking);
        /// <summary>Los pickings del almacén con líneas todavía sin servir, el más reciente primero.</summary>
        Task<List<PickingEnCursoDTO>> LeerPickingsEnCurso(string empresa, string almacen);
        Task<List<LecturaPickingAlmacen>> LeerLecturasDelPicking(string empresa, int picking);
        /// <summary>Lo leído y dado por falta al sacar un documento de cualquier tipo (PICK, REPO).</summary>
        Task<List<LecturaPickingAlmacen>> LeerLecturasDeSalida(string empresa, string tipoOrigen, int numero);
        /// <summary>Los traspasos de reposición con la salida del almacén aún sin contabilizar.</summary>
        Task<List<ReposicionPorSalir>> LeerReposicionesPorSalir(string empresa, string almacen);
        /// <summary>Lo que sale del almacén de origen en un traspaso, con sus huecos. Null si no queda nada por sacar.</summary>
        Task<ReposicionSalida> LeerReposicionSalida(string empresa, int traspaso);
        /// <param name="pedido">Null = todos los pedidos del picking.</param>
        Task<List<FilaPackingAlmacen>> LeerLineasPacking(string empresa, int picking, int? pedido);
        /// <summary>El picking que tiene ahora mismo el pedido sin servir, o null si no tiene ninguno.</summary>
        Task<int?> PickingEnCursoDelPedido(string empresa, int pedido);
        Task<bool> ExistePedidoEnPicking(string empresa, int pedido, int picking);
        /// <summary>True si lo guarda; false si ya estaba (mismo IdCliente).</summary>
        Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario);
        Task<List<LecturaProductoAlmacen>> LeerLecturas(string empresa, int pedido, int picking, string fase);
        Task<BultoAlmacenDTO> LeerBultoPorIdCliente(Guid idCliente);
        Task<BultoAlmacenDTO> LeerBulto(int id);
        Task<List<BultoAlmacenDTO>> LeerBultos(string empresa, int pedido);
        Task<BultoAlmacenDTO> GuardarBulto(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo);
        /// <summary>La salida se terminó en el servidor (PreparacionSalidasTerminadas).</summary>
        Task<bool> SalidaTerminada(string empresa, string tipo, int numero);
        /// <summary>Ariadna: las entregas de los pickings en curso del almacén, con sus bultos (para «Empaquetar»).</summary>
        Task<List<EntregaPorEmpaquetar>> LeerEntregasPorEmpaquetar(string empresa, string almacen);
    }

    /// <summary>
    /// Todo por SQL directo y con parámetros, sin tocar el EDMX: las dos tablas nuevas
    /// (PreparacionEscaneos y EnviosAgenciaBultos) no están mapeadas, igual que RectificativaPendiente.
    /// Un EDMX mal editado tira la API entera, y para dos tablas de evidencia no compensa.
    /// </summary>
    public class RepositorioPreparacionAlmacen : IRepositorioPreparacionAlmacen
    {
        private readonly Database baseDeDatos;

        public RepositorioPreparacionAlmacen(NVEntities db) : this(db.Database)
        {
        }

        // Solo necesita ejecutar SQL: así la prueba de integración puede darle cualquier conexión
        internal RepositorioPreparacionAlmacen(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        // La cantidad sale de las ubicaciones reservadas para el picking (estado 3), que guardan la
        // reserva en negativo; si la línea no tiene ubicación, de la propia línea menos lo que el
        // cliente recoge. Es la misma cuenta que prdInformePickingAgrupado.
        // Solo las líneas que siguen en el picking (Estado 1), igual que EscriturasSalida.SQL_PIEZAS_PICKING:
        // las ya albaranadas o facturadas conservan el número de picking y no hay que recogerlas (picking 99648, 03/10/26).
        // Las cuatro columnas fijas del principio son las que rellena después CasadorEscaneos.OrdenarRecorrido:
        // SqlQuery exige una columna por cada propiedad del DTO.
        internal const string SQL_LINEAS_PICKING = @"
SELECT 0 AS Orden, CAST(0 AS bit) AS SinCodigo, CAST(0 AS bit) AS CodigoDuplicado, CAST(NULL AS varchar(11)) AS Ubicacion,
       RTRIM(l.Producto) AS Producto,
       RTRIM(MAX(p.Nombre)) AS Descripcion,
       RTRIM(MAX(p.CodBarras)) AS CodigoBarras,
       MAX(p.[Tamaño]) AS Tamano,
       RTRIM(MAX(p.UnidadMedida)) AS UnidadMedida,
       CAST(ABS(ISNULL(SUM(u.Cantidad), ISNULL(SUM(l.Cantidad - l.Recoger), 0))) AS int) AS Cantidad,
       RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna
FROM LinPedidoVta l
     LEFT JOIN Ubicaciones u ON l.[Número] = u.PedidoVta AND l.[Nº Orden] = u.[NºOrdenVta] AND u.Estado = 3
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1
GROUP BY l.Producto, u.Pasillo, u.Fila, u.Columna
HAVING ISNULL(SUM(u.Cantidad), ISNULL(SUM(l.Cantidad - l.Recoger), 0)) <> 0";

        internal const string SQL_PICKINGS_EN_CURSO = @"
SELECT l.Picking AS Picking, COUNT(*) AS Lineas, COUNT(DISTINCT l.[Número]) AS Pedidos,
       CAST(SUM(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0)) AS int) AS Unidades
FROM LinPedidoVta l
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.Estado = 1 AND l.TipoLinea = 1 AND l.Picking > 0
GROUP BY l.Picking
ORDER BY l.Picking DESC";

        internal const string SQL_LECTURAS_DEL_PICKING = @"
SELECT RTRIM(e.Producto) AS Producto,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Unidades,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p1 AND e.Fase = 'PICK'
GROUP BY e.Producto";

        internal const string SQL_LECTURAS_DE_SALIDA = @"
SELECT RTRIM(e.Producto) AS Producto,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Unidades,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = @p1 AND e.NumeroOrigen = @p2 AND e.Fase = 'PICK'
GROUP BY e.Producto";

        // NestoAPI#556: la salida de un traspaso de reposición son sus filas de PreExtrProducto en el diario de salida
        // del almacén de origen (Almacenes.DiarioSalidaRep; «General» en Algete), con NºTraspaso y cantidad negativa,
        // hasta que se contabilizan. El destino es el almacén de las filas de entrada (cantidad positiva) del traspaso.
        internal const string SQL_REPOSICIONES_POR_SALIR = @"
SELECT s.[NºTraspaso] AS Traspaso,
       (SELECT TOP 1 RTRIM(e.[Almacén]) FROM PreExtrProducto e
        WHERE e.Empresa = s.Empresa AND e.[NºTraspaso] = s.[NºTraspaso] AND e.Cantidad > 0) AS Destino,
       COUNT(*) AS Lineas, CAST(-SUM(s.Cantidad) AS int) AS Unidades
FROM PreExtrProducto s
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.Empresa = @p0 AND s.[Almacén] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.[NºTraspaso] > 0 AND s.Cantidad < 0
GROUP BY s.Empresa, s.[NºTraspaso]
ORDER BY s.[NºTraspaso] DESC";

        internal const string SQL_DESTINO_REPOSICION = @"
SELECT TOP 1 RTRIM(e.[Almacén]) FROM PreExtrProducto e
WHERE e.Empresa = @p0 AND e.[NºTraspaso] = @p1 AND e.Cantidad > 0";

        // El hueco sale del registro que deja Nesto viejo al crear el traspaso (Ubicaciones en estado -4, ya quitado del hueco)
        // o de la reserva de prdUbicarReposicion (estado 4), enlazados por NºOrdenRepo con la fila de PreExtrProducto; si no
        // hay, la parada va sin hueco. Mismas columnas que SQL_LINEAS_PICKING.
        internal const string SQL_LINEAS_REPOSICION_SALIDA = @"
SELECT 0 AS Orden, CAST(0 AS bit) AS SinCodigo, CAST(0 AS bit) AS CodigoDuplicado, CAST(NULL AS varchar(11)) AS Ubicacion,
       RTRIM(s.[Número]) AS Producto,
       RTRIM(MAX(p.Nombre)) AS Descripcion,
       RTRIM(MAX(p.CodBarras)) AS CodigoBarras,
       MAX(p.[Tamaño]) AS Tamano,
       RTRIM(MAX(p.UnidadMedida)) AS UnidadMedida,
       CAST(ABS(ISNULL(SUM(u.Cantidad), SUM(s.Cantidad))) AS int) AS Cantidad,
       RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna
FROM PreExtrProducto s
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
     LEFT JOIN Ubicaciones u ON u.[NºOrdenRepo] = s.[Nº Orden] AND u.Estado IN (4, -4)
     LEFT JOIN Productos p ON p.Empresa = s.Empresa AND p.[Número] = s.[Número]
WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0
GROUP BY s.[Número], u.Pasillo, u.Fila, u.Columna
HAVING ISNULL(SUM(u.Cantidad), SUM(s.Cantidad)) <> 0";

        internal const string SQL_LINEAS_PACKING = @"
SELECT c.[Número] AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(cl.Nombre) AS Nombre, RTRIM(cl.[Dirección]) AS Direccion, RTRIM(cl.CodPostal) AS CodigoPostal,
       RTRIM(cl.[Población]) AS Poblacion, RTRIM(c.Ruta) AS Ruta,
       RTRIM(CAST(c.ComentarioPicking AS nvarchar(max))) AS ComentarioPicking,
       l.[Nº Orden] AS LineaPedido, RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Descripcion,
       RTRIM(p.CodBarras) AS CodigoBarras,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad
FROM LinPedidoVta l
     JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
     LEFT JOIN Clientes cl ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1
      AND (@p2 IS NULL OR l.[Número] = @p2)
      AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) <> 0
ORDER BY c.[Nº Cliente], c.Contacto, c.[Número], l.[Nº Orden]";

        internal const string SQL_PICKING_EN_CURSO = @"
SELECT MAX(l.Picking) FROM LinPedidoVta l
WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND l.Estado = 1 AND l.Picking > 0";

        internal const string SQL_EXISTE_PEDIDO_EN_PICKING = @"
SELECT COUNT(*) FROM LinPedidoVta l WHERE l.Empresa = @p0 AND l.[Número] = @p1 AND l.Picking = @p2";

        internal const string SQL_INSERTAR_ESCANEO = @"
INSERT INTO PreparacionEscaneos
       (IdCliente, Empresa, NumeroOrigen, Pedido, LineaPedido, Producto, Fase, Cantidad, Metodo, Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo, TipoOrigen)
SELECT @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14
WHERE NOT EXISTS (SELECT 1 FROM PreparacionEscaneos WHERE IdCliente = @p0)";

        internal const string SQL_LECTURAS = @"
SELECT RTRIM(e.Producto) AS Producto, CAST(SUM(e.Cantidad) AS int) AS Unidades
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.Pedido = @p1 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p2 AND e.Fase = @p3 AND e.Metodo <> 'FALTA'
GROUP BY e.Producto";

        private const string COLUMNAS_BULTO = @"
b.Id, b.IdCliente, RTRIM(b.Empresa) AS Empresa, b.Pedido, b.Picking, CAST(b.Bulto AS int) AS Bulto, b.Peso,
CAST(CASE WHEN b.RutaBlob IS NULL THEN 0 ELSE 1 END AS bit) AS TieneFoto, b.NumeroEnvio,
RTRIM(b.Usuario) AS Usuario, b.FechaFoto, b.RutaBlob";

        internal const string SQL_BULTO_POR_ID_CLIENTE = "SELECT" + COLUMNAS_BULTO + " FROM EnviosAgenciaBultos b WHERE b.IdCliente = @p0";
        internal const string SQL_BULTO_POR_ID = "SELECT" + COLUMNAS_BULTO + " FROM EnviosAgenciaBultos b WHERE b.Id = @p0";
        internal const string SQL_BULTOS_DEL_PEDIDO = "SELECT" + COLUMNAS_BULTO +
            " FROM EnviosAgenciaBultos b WHERE b.Empresa = @p0 AND b.Pedido = @p1 ORDER BY b.Picking, b.Bulto";

        // Repetir la foto de un bulto sustituye a la anterior: manda la última
        internal const string SQL_ACTUALIZAR_BULTO = @"
UPDATE EnviosAgenciaBultos
SET IdCliente = @p0, RutaBlob = @p5, HashSha256 = @p6, TamanoBytes = @p7, Usuario = @p8, Dispositivo = @p9,
    FechaFoto = @p10, FechaRegistro = GETDATE()
WHERE Empresa = @p1 AND Pedido = @p2 AND Picking = @p3 AND Bulto = @p4";

        internal const string SQL_INSERTAR_BULTO = @"
INSERT INTO EnviosAgenciaBultos (IdCliente, Empresa, Pedido, Picking, Bulto, RutaBlob, HashSha256, TamanoBytes, Usuario, Dispositivo, FechaFoto)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)";

        // Si la tabla aún no existe (script de Ariadna#6 sin lanzar), ninguna salida está terminada
        internal const string SQL_SALIDA_TERMINADA = @"
IF OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NULL
    SELECT CAST(0 AS bit)
ELSE
    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas
                                  WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2) THEN 1 ELSE 0 END AS bit)";

        // Ariadna («Empaquetar»): las entregas de los pickings en curso del almacén. Mismas líneas que SQL_LINEAS_PACKING
        // (siguen en el picking y llevan algo a la caja) y mismas entregas que MontarPacking (cliente + contacto); los
        // bultos, los de EnviosAgenciaBultos de ese picking y de los pedidos de la entrega, contando cada número una vez
        // (un bulto compartido tiene una fila por pedido). Comprobado en producción el 04/10/26: 10 entregas, al momento.
        internal const string SQL_ENTREGAS_POR_EMPAQUETAR = @"
WITH lineas AS (
    SELECT l.Picking, l.[Número] AS Pedido, c.[Nº Cliente] AS Cliente, c.Contacto,
           ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS Unidades
    FROM LinPedidoVta l
         JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
    WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.Estado = 1 AND l.TipoLinea = 1 AND l.Picking > 0
          AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) <> 0
), entregas AS (
    SELECT Picking, Cliente, Contacto, MIN(Pedido) AS PrimerPedido, COUNT(DISTINCT Pedido) AS Pedidos, SUM(Unidades) AS Unidades
    FROM lineas GROUP BY Picking, Cliente, Contacto
)
SELECT e.Picking, RTRIM(e.Cliente) AS Cliente, RTRIM(e.Contacto) AS Contacto, RTRIM(cl.Nombre) AS Nombre, e.PrimerPedido, e.Pedidos,
       CAST(e.Unidades AS int) AS Unidades,
       (SELECT COUNT(DISTINCT b.Bulto) FROM EnviosAgenciaBultos b
        WHERE b.Empresa = @p0 AND b.Picking = e.Picking AND b.RutaBlob IS NOT NULL
              AND b.Pedido IN (SELECT li.Pedido FROM lineas li WHERE li.Picking = e.Picking AND li.Cliente = e.Cliente AND li.Contacto = e.Contacto)) AS BultosConFoto,
       (SELECT COUNT(*) FROM EnviosAgenciaBultos b
        WHERE b.Empresa = @p0 AND b.Picking = e.Picking AND b.RutaBlob IS NULL
              AND b.Pedido IN (SELECT li.Pedido FROM lineas li WHERE li.Picking = e.Picking AND li.Cliente = e.Cliente AND li.Contacto = e.Contacto)) AS BultosSinFoto,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas t
                              WHERE t.Empresa = @p0 AND t.TipoOrigen = 'PICK' AND t.NumeroOrigen = e.Picking) THEN 1 ELSE 0 END AS bit) AS Terminado
FROM entregas e
     LEFT JOIN Clientes cl ON cl.Empresa = @p0 AND cl.[Nº Cliente] = e.Cliente AND cl.Contacto = e.Contacto
ORDER BY e.Picking DESC, e.Cliente, e.Contacto";

        public async Task<bool> SalidaTerminada(string empresa, string tipo, int numero)
        {
            return await baseDeDatos.SqlQuery<bool>(SQL_SALIDA_TERMINADA, empresa, tipo, numero).FirstAsync().ConfigureAwait(false);
        }

        public Task<List<EntregaPorEmpaquetar>> LeerEntregasPorEmpaquetar(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<EntregaPorEmpaquetar>(SQL_ENTREGAS_POR_EMPAQUETAR, empresa, almacen).ToListAsync();
        }

        public Task<List<LineaPickingAlmacenDTO>> LeerLineasPicking(string empresa, int picking)
        {
            return baseDeDatos.SqlQuery<LineaPickingAlmacenDTO>(SQL_LINEAS_PICKING, empresa, picking).ToListAsync();
        }

        public Task<List<PickingEnCursoDTO>> LeerPickingsEnCurso(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<PickingEnCursoDTO>(SQL_PICKINGS_EN_CURSO, empresa, almacen).ToListAsync();
        }

        public Task<List<LecturaPickingAlmacen>> LeerLecturasDelPicking(string empresa, int picking)
        {
            return baseDeDatos.SqlQuery<LecturaPickingAlmacen>(SQL_LECTURAS_DEL_PICKING, empresa, picking).ToListAsync();
        }

        public Task<List<LecturaPickingAlmacen>> LeerLecturasDeSalida(string empresa, string tipoOrigen, int numero)
        {
            return baseDeDatos.SqlQuery<LecturaPickingAlmacen>(SQL_LECTURAS_DE_SALIDA, empresa, tipoOrigen, numero).ToListAsync();
        }

        public Task<List<ReposicionPorSalir>> LeerReposicionesPorSalir(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<ReposicionPorSalir>(SQL_REPOSICIONES_POR_SALIR, empresa, almacen).ToListAsync();
        }

        public async Task<ReposicionSalida> LeerReposicionSalida(string empresa, int traspaso)
        {
            List<LineaPickingAlmacenDTO> lineas = await baseDeDatos.SqlQuery<LineaPickingAlmacenDTO>(SQL_LINEAS_REPOSICION_SALIDA, empresa, traspaso)
                .ToListAsync().ConfigureAwait(false);
            if (lineas.Count == 0)
            {
                return null;
            }
            string destino = (await baseDeDatos.SqlQuery<string>(SQL_DESTINO_REPOSICION, empresa, traspaso).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
            return new ReposicionSalida { Destino = destino, Lineas = lineas };
        }

        public Task<List<FilaPackingAlmacen>> LeerLineasPacking(string empresa, int picking, int? pedido)
        {
            return baseDeDatos.SqlQuery<FilaPackingAlmacen>(SQL_LINEAS_PACKING,
                new SqlParameter("@p0", empresa), new SqlParameter("@p1", picking),
                new SqlParameter("@p2", System.Data.SqlDbType.Int) { Value = (object)pedido ?? DBNull.Value }).ToListAsync();
        }

        public async Task<int?> PickingEnCursoDelPedido(string empresa, int pedido)
        {
            return (await baseDeDatos.SqlQuery<int?>(SQL_PICKING_EN_CURSO, empresa, pedido).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
        }

        public async Task<bool> ExistePedidoEnPicking(string empresa, int pedido, int picking)
        {
            return (await baseDeDatos.SqlQuery<int>(SQL_EXISTE_PEDIDO_EN_PICKING, empresa, pedido, picking)
                .ToListAsync().ConfigureAwait(false)).Single() > 0;
        }

        public async Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario)
        {
            try
            {
                int filas = await baseDeDatos.ExecuteSqlCommandAsync(SQL_INSERTAR_ESCANEO,
                    new SqlParameter("@p0", escaneo.IdCliente),
                    new SqlParameter("@p1", empresa),
                    new SqlParameter("@p2", CasadorEscaneos.NumeroOrigenDe(escaneo)),
                    Entero("@p3", escaneo.Pedido),
                    Entero("@p4", escaneo.LineaPedido),
                    new SqlParameter("@p5", escaneo.Producto.Trim()),
                    new SqlParameter("@p6", escaneo.Fase.Trim().ToUpperInvariant()),
                    new SqlParameter("@p7", escaneo.Cantidad),
                    new SqlParameter("@p8", escaneo.Metodo.Trim().ToUpperInvariant()),
                    Entero("@p9", escaneo.Bulto),
                    Texto("@p10", escaneo.Motivo, 100),
                    Texto("@p11", usuario, 50),
                    Texto("@p12", escaneo.Dispositivo, 50),
                    new SqlParameter("@p13", escaneo.FechaEscaneo),
                    new SqlParameter("@p14", CasadorEscaneos.TipoOrigenDe(escaneo))).ConfigureAwait(false);
                return filas > 0;
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Dos reenvíos del mismo escaneo a la vez: el segundo choca con el índice único
                return false;
            }
        }

        public Task<List<LecturaProductoAlmacen>> LeerLecturas(string empresa, int pedido, int picking, string fase)
        {
            return baseDeDatos.SqlQuery<LecturaProductoAlmacen>(SQL_LECTURAS, empresa, pedido, picking, fase).ToListAsync();
        }

        public async Task<BultoAlmacenDTO> LeerBultoPorIdCliente(Guid idCliente)
        {
            return (await baseDeDatos.SqlQuery<BultoAlmacenDTO>(SQL_BULTO_POR_ID_CLIENTE, idCliente)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<BultoAlmacenDTO> LeerBulto(int id)
        {
            return (await baseDeDatos.SqlQuery<BultoAlmacenDTO>(SQL_BULTO_POR_ID, id)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
        }

        public Task<List<BultoAlmacenDTO>> LeerBultos(string empresa, int pedido)
        {
            return baseDeDatos.SqlQuery<BultoAlmacenDTO>(SQL_BULTOS_DEL_PEDIDO, empresa, pedido).ToListAsync();
        }

        public async Task<BultoAlmacenDTO> GuardarBulto(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo)
        {
            object[] Parametros() => new object[]
            {
                new SqlParameter("@p0", bulto.IdCliente),
                new SqlParameter("@p1", bulto.Empresa),
                new SqlParameter("@p2", bulto.Pedido),
                new SqlParameter("@p3", bulto.Picking),
                new SqlParameter("@p4", bulto.Bulto),
                Texto("@p5", bulto.RutaBlob, 200),
                Texto("@p6", hashSha256, 64),
                new SqlParameter("@p7", tamanoBytes),
                Texto("@p8", bulto.Usuario, 50),
                Texto("@p9", dispositivo, 50),
                new SqlParameter("@p10", System.Data.SqlDbType.DateTime) { Value = (object)bulto.FechaFoto ?? DBNull.Value }
            };

            int actualizadas = await baseDeDatos.ExecuteSqlCommandAsync(SQL_ACTUALIZAR_BULTO, Parametros()).ConfigureAwait(false);
            if (actualizadas == 0)
            {
                _ = await baseDeDatos.ExecuteSqlCommandAsync(SQL_INSERTAR_BULTO, Parametros()).ConfigureAwait(false);
            }
            return await LeerBultoPorIdCliente(bulto.IdCliente).ConfigureAwait(false);
        }

        private static SqlParameter Entero(string nombre, int? valor)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.Int) { Value = (object)valor ?? DBNull.Value };
        }

        private static SqlParameter Texto(string nombre, string valor, int longitudMaxima)
        {
            string limpio = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            if (limpio != null && limpio.Length > longitudMaxima)
            {
                limpio = limpio.Substring(0, longitudMaxima);
            }
            return new SqlParameter(nombre, System.Data.SqlDbType.VarChar, longitudMaxima) { Value = (object)limpio ?? DBNull.Value };
        }
    }
}
