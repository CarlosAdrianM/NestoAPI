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
        /// <summary>
        /// NestoAPI#765: una línea de texto de una nota de ENTREGA de solo texto («PRESTAMO HIDRA 7»): Producto vacío, Cantidad 1, el
        /// texto en Descripcion. No se escanea: cuenta como metida.
        /// </summary>
        public bool EsTexto { get; set; }
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
        /// <summary>
        /// NestoAPI#720 (Andre, 28/10/26): pedidos de la entrega que ya tienen envío con etiqueta (EnviosAgencia en curso o más allá),
        /// se haya hecho desde el packing de Ariadna o desde Nesto. Con todos los pedidos etiquetados, la entrega está hecha aunque no
        /// haya bultos con foto (en Algete etiquetan desde Nesto y el picking se quedaba en la lista hasta el albarán).
        /// </summary>
        public int PedidosConEtiqueta { get; set; }
        /// <summary>El picking se terminó en Ariadna (PreparacionSalidasTerminadas).</summary>
        public bool Terminado { get; set; }
    }

    /// <summary>Lo leído de un producto (suma de sus lecturas y toques, sin las faltas).</summary>
    public class LecturaProductoAlmacen
    {
        public string Producto { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>NestoAPI#556: lo metido en la caja y lo dado por falta de un producto de un pedido en el packing.</summary>
    public class LecturaPackingAlmacen
    {
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public int Metidas { get; set; }
        public int Faltas { get; set; }
    }

    /// <summary>Lo leído y lo dado por falta de un producto en el picking por ola.</summary>
    public class LecturaPickingAlmacen
    {
        public string Producto { get; set; }
        public int Unidades { get; set; }
        public int Faltas { get; set; }
        /// <summary>
        /// NestoAPI#640: quién lo ha leído (PreparacionEscaneos.Usuario). Las lecturas vienen por producto y usuario: todo lo
        /// que mira el producto suma las filas, así que cuadra igual que antes. Null en las de antes de #640 o en las pruebas.
        /// </summary>
        public string Usuario { get; set; }
    }

    /// <summary>
    /// NestoAPI#556: el acceso a datos de la preparación con Ariadna. Separado de las reglas
    /// (<see cref="ServicioPreparacionAlmacen"/>) para poder probarlas sin base de datos.
    /// </summary>
    public interface IRepositorioPreparacionAlmacen
    {
        Task<List<LineaPickingAlmacenDTO>> LeerLineasPicking(string empresa, int picking);
        /// <summary>NestoAPI#641: el almacén de las líneas que siguen en el picking (para el orden de sus calles). Null si no queda ninguna.</summary>
        Task<string> LeerAlmacenPicking(string empresa, int picking);
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
        /// <summary>Lo metido y lo dado por falta en el packing del picking, por pedido y producto.</summary>
        /// <param name="pedido">Null = todos los pedidos del picking.</param>
        Task<List<LecturaPackingAlmacen>> LeerLecturasPacking(string empresa, int picking, int? pedido);
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
        /// <summary>
        /// NestoAPI#646: guarda el bulto de una foto sin pisar el de otra PDA. Si ese número ya lo tiene en la entrega (mismo
        /// picking, mismo cliente y contacto) otro usuario u otra PDA, y no es la misma caja repetida por esta, se guarda
        /// con el siguiente libre de la entrega; todo en una transacción con la entrega bloqueada. Devuelve la fila con el
        /// número real y, si se ha cambiado, en <see cref="BultoAlmacenDTO.OcupadoPor"/> quién lo tenía.
        /// </summary>
        Task<BultoAlmacenDTO> GuardarBultoSinPisar(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo);
        /// <summary>La salida se terminó en el servidor (PreparacionSalidasTerminadas).</summary>
        Task<bool> SalidaTerminada(string empresa, string tipo, int numero);
        /// <summary>Ariadna: las entregas de los pickings en curso del almacén, con sus bultos (para «Empaquetar»).</summary>
        Task<List<EntregaPorEmpaquetar>> LeerEntregasPorEmpaquetar(string empresa, string almacen);
        /// <summary>NestoAPI#646: los bultos de un picking (de todos sus pedidos).</summary>
        Task<List<BultoAlmacenDTO>> LeerBultosDelPicking(string empresa, int picking);
        /// <summary>
        /// NestoAPI#646: lo último que ha hecho cada usuario en el packing de cada pedido de esos pickings (lecturas PACK y
        /// fotos de bulto) en los últimos <paramref name="segundos"/>, con la hora de la base de datos.
        /// </summary>
        Task<List<ActividadPackingAlmacen>> LeerActividadPacking(string empresa, IReadOnlyCollection<int> pickings, int segundos);
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

        // La cantidad sale de las ubicaciones reservadas para el picking (estado 3); si la línea no tiene ubicación, de la
        // propia línea menos lo que el cliente recoge. Es la misma cuenta que prdInformePickingAgrupado. (El comentario de
        // antes decía que la reserva va en negativo: en producción, 27/10/26, las 365 filas de los pickings abiertos van en
        // positivo; el ABS se queda para las dos.)
        // Solo las líneas que siguen en el picking (Estado 1), igual que EscriturasSalida.SQL_PIEZAS_PICKING:
        // las ya albaranadas o facturadas conservan el número de picking y no hay que recogerlas (picking 99648, 03/10/26).
        // NestoAPI#713: además devuelve lo reservado (Reservado) y lo que piden las líneas de la parada (PendienteLineas),
        // y RecorteParadasRecogida recorta la cantidad a las líneas si las reservas dicen más (picking 99941, 27/10/26).
        // OJO: las líneas se suman UNA vez por línea (Lineas → Reservas agrupadas por línea y hueco → Paradas), no sobre el
        // JOIN con Ubicaciones, que repite la línea por cada fila de reserva: la 329365300 del pedido 929262 (20 uds, dos
        // reservas de 17 + 3) más la 329365400 (2) daban 42 sumando sobre el JOIN y son 22.
        // Las cuatro columnas fijas del principio son las que rellena después CasadorEscaneos.OrdenarRecorrido:
        // SqlQuery exige una columna por cada propiedad del DTO.
        internal const string SQL_LINEAS_PICKING = @"
WITH Lineas AS (
    SELECT l.[Número] AS Pedido, l.[Nº Orden] AS Linea, l.Producto, ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS Pendiente
    FROM LinPedidoVta l
         LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
    WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1 AND ISNULL(p.Ficticio, 0) = 0
), Reservas AS (
    SELECT x.Pedido, x.Linea, u.Pasillo, u.Fila, u.Columna, SUM(u.Cantidad) AS Cantidad
    FROM Lineas x
         INNER JOIN Ubicaciones u ON u.PedidoVta = x.Pedido AND u.[NºOrdenVta] = x.Linea AND u.Estado = 3
    GROUP BY x.Pedido, x.Linea, u.Pasillo, u.Fila, u.Columna
), Paradas AS (
    SELECT x.Producto, r.Pasillo, r.Fila, r.Columna, SUM(r.Cantidad) AS Reservas, SUM(x.Pendiente) AS Pendiente
    FROM Lineas x
         LEFT JOIN Reservas r ON r.Pedido = x.Pedido AND r.Linea = x.Linea
    GROUP BY x.Producto, r.Pasillo, r.Fila, r.Columna
)
SELECT 0 AS Orden, CAST(0 AS bit) AS SinCodigo, CAST(0 AS bit) AS CodigoDuplicado, CAST(NULL AS varchar(11)) AS Ubicacion,
       RTRIM(t.Producto) AS Producto,
       RTRIM(p.Nombre) AS Descripcion,
       RTRIM(p.CodBarras) AS CodigoBarras,
       p.[Tamaño] AS Tamano,
       RTRIM(p.UnidadMedida) AS UnidadMedida,
       RTRIM(f.[Descripción]) AS Familia,
       CAST(ABS(ISNULL(t.Reservas, t.Pendiente)) AS int) AS Cantidad,
       CAST(ABS(t.Reservas) AS int) AS Reservado,
       CAST(t.Pendiente AS int) AS PendienteLineas,
       RTRIM(t.Pasillo) AS Pasillo, RTRIM(t.Fila) AS Fila, RTRIM(t.Columna) AS Columna
FROM Paradas t
     LEFT JOIN Productos p ON p.Empresa = @p0 AND p.[Número] = t.Producto
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
WHERE ISNULL(t.Reservas, t.Pendiente) <> 0";

        /// <summary>NestoAPI#641: el almacén de un picking (las mismas líneas que SQL_LINEAS_PICKING). Lo usa también el cambio de hueco.</summary>
        internal const string SQL_ALMACEN_PICKING = @"
SELECT TOP 1 RTRIM(l.[Almacén]) FROM LinPedidoVta l
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1
GROUP BY l.[Almacén]
ORDER BY COUNT(*) DESC";

        internal const string SQL_PICKINGS_EN_CURSO = @"
SELECT l.Picking AS Picking, COUNT(*) AS Lineas, COUNT(DISTINCT l.[Número]) AS Pedidos,
       CAST(SUM(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0)) AS int) AS Unidades
FROM LinPedidoVta l
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.Estado = 1 AND l.TipoLinea = 1 AND l.Picking > 0 AND ISNULL(p.Ficticio, 0) = 0
GROUP BY l.Picking
ORDER BY l.Picking DESC";

        // NestoAPI#640: por producto Y por usuario, para saber quién ha leído cada parada (ServicioPreparacionAlmacen.RepartirLectores).
        // Todo lo demás suma las filas del producto, así que el resultado es el mismo que agrupando solo por producto.
        internal const string SQL_LECTURAS_DEL_PICKING = @"
SELECT RTRIM(e.Producto) AS Producto,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Unidades,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas,
       RTRIM(e.Usuario) AS Usuario
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p1 AND e.Fase = 'PICK'
GROUP BY e.Producto, e.Usuario";

        internal const string SQL_LECTURAS_DE_SALIDA = @"
SELECT RTRIM(e.Producto) AS Producto,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Unidades,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas,
       RTRIM(e.Usuario) AS Usuario
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = @p1 AND e.NumeroOrigen = @p2 AND e.Fase = 'PICK'
GROUP BY e.Producto, e.Usuario";

        // NestoAPI#556: la salida de un traspaso de reposición son sus filas de PreExtrProducto en el diario de salida
        // del almacén de origen (Almacenes.DiarioSalidaRep; «General» en Algete), con NºTraspaso y cantidad negativa,
        // hasta que se contabilizan. El destino es el almacén de las filas de entrada (cantidad positiva) del traspaso.
        internal const string SQL_REPOSICIONES_POR_SALIR = @"
SELECT s.[NºTraspaso] AS Traspaso,
       (SELECT TOP 1 RTRIM(e.[Almacén]) FROM PreExtrProducto e
        WHERE e.Empresa = s.Empresa AND e.[NºTraspaso] = s.[NºTraspaso] AND e.Cantidad > 0) AS Destino,
       (SELECT TOP 1 RTRIM(d.[Descripción]) FROM PreExtrProducto e
        INNER JOIN Almacenes d ON d.Empresa = e.Empresa AND d.[Número] = e.[Almacén]
        WHERE e.Empresa = s.Empresa AND e.[NºTraspaso] = s.[NºTraspaso] AND e.Cantidad > 0) AS NombreDestino,
       COUNT(*) AS Lineas, CAST(-SUM(s.Cantidad) AS int) AS Unidades
FROM PreExtrProducto s
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.Empresa = @p0 AND s.[Almacén] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.[NºTraspaso] > 0 AND s.Cantidad < 0
GROUP BY s.Empresa, s.[NºTraspaso]
ORDER BY s.[NºTraspaso] DESC";

        /// <summary>NestoAPI#641: el almacén de origen de una reposición (el de su salida, donde están los huecos).</summary>
        internal const string SQL_ORIGEN_REPOSICION = @"
SELECT TOP 1 RTRIM(s.[Almacén]) FROM PreExtrProducto s
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0";

        internal const string SQL_DESTINO_REPOSICION = @"
SELECT TOP 1 RTRIM(e.[Almacén]) FROM PreExtrProducto e
WHERE e.Empresa = @p0 AND e.[NºTraspaso] = @p1 AND e.Cantidad > 0";

        /// <summary>NestoAPI#600: el nombre de un almacén para enseñarlo («Reina»).</summary>
        internal const string SQL_NOMBRE_ALMACEN = @"
SELECT TOP 1 RTRIM([Descripción]) FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // El hueco sale del registro que queda en Ubicaciones al cerrar el traspaso (Nesto viejo o POST api/Reposiciones):
        // la reserva de prdUbicarReposicion / la puerta de Ubicaciones con el NºTraspasoRepo, en -4 (Nesto viejo) o aún en 4
        // (la API, NestoAPI#651: pasa a -4 al terminar la recogida). OJO: su NºOrdenRepo apunta a la línea de ENTRADA (la que había al reservar), no a la de salida, y la entrada
        // se va al diario del destino (y ExtractoProducto no conserva su Nº Orden): por eso se localiza por NºTraspasoRepo +
        // producto + almacén de origen (traspaso 80885, 05/10/26: 53 de 53 filas así). Lo que no tiene registro (no había
        // nada libre al reservar, o un almacén sin control de ubicaciones) va en una parada sin hueco. Una línea de salida
        // por producto (Nesto viejo y la API consolidan). Mismas columnas que SQL_LINEAS_PICKING.
        // NestoAPI#713: en una parada con hueco, Reservado es lo del registro y PendienteLineas lo que sale del producto
        // (RecorteParadasRecogida la recorta si el registro dice más); la parada sin hueco es el resto y no se recorta.
        internal const string SQL_LINEAS_REPOSICION_SALIDA = @"
WITH Salida AS (
    SELECT s.Empresa, s.[Almacén], s.[NºTraspaso], s.[Número], CAST(-SUM(s.Cantidad) AS int) AS Cantidad
    FROM PreExtrProducto s
         INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
    WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0
    GROUP BY s.Empresa, s.[Almacén], s.[NºTraspaso], s.[Número]
), Huecos AS (
    SELECT x.[Número], CAST(ABS(u.Cantidad) AS int) AS Cantidad, RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna,
           x.Cantidad AS Salida
    FROM Salida x
         INNER JOIN Ubicaciones u ON u.[NºTraspasoRepo] = x.[NºTraspaso] AND u.[Número] = x.[Número] AND u.[Almacén] = x.[Almacén]
                                 AND u.Estado IN (4, -4)
), Paradas AS (
    SELECT [Número], Cantidad, Pasillo, Fila, Columna, Salida FROM Huecos
    UNION ALL
    SELECT x.[Número], x.Cantidad - ISNULL((SELECT SUM(h.Cantidad) FROM Huecos h WHERE h.[Número] = x.[Número]), 0), NULL, NULL, NULL, NULL
    FROM Salida x
)
SELECT 0 AS Orden, CAST(0 AS bit) AS SinCodigo, CAST(0 AS bit) AS CodigoDuplicado, CAST(NULL AS varchar(11)) AS Ubicacion,
       RTRIM(t.[Número]) AS Producto,
       RTRIM(MAX(p.Nombre)) AS Descripcion,
       RTRIM(MAX(p.CodBarras)) AS CodigoBarras,
       MAX(p.[Tamaño]) AS Tamano,
       RTRIM(MAX(p.UnidadMedida)) AS UnidadMedida,
       RTRIM(MAX(f.[Descripción])) AS Familia,
       CAST(SUM(t.Cantidad) AS int) AS Cantidad,
       CAST(CASE WHEN MAX(t.Salida) IS NULL THEN NULL ELSE SUM(CASE WHEN t.Salida IS NULL THEN 0 ELSE t.Cantidad END) END AS int) AS Reservado,
       CAST(ISNULL(MAX(t.Salida), SUM(t.Cantidad)) AS int) AS PendienteLineas,
       t.Pasillo, t.Fila, t.Columna
FROM Paradas t
     LEFT JOIN Productos p ON p.Empresa = @p0 AND p.[Número] = t.[Número]
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
GROUP BY t.[Número], t.Pasillo, t.Fila, t.Columna
HAVING SUM(t.Cantidad) > 0";

        // NestoAPI#765 (decisión 4 de #755): las notas de ENTREGA de solo texto («PRESTAMO HIDRA 7») también se empaquetan y
        // etiquetan. Sus líneas de texto (TipoLinea 0, con texto) salen con Cantidad 1, Producto vacío y EsTexto: no se escanean.
        // Solo si la nota no lleva ninguna línea de producto en ese picking (entonces se empaqueta por sus productos, como siempre)
        // y no es de RECOGIDA (esas no pasan por packing). Va en SQL_LINEAS_PACKING y en SQL_ENTREGAS_POR_EMPAQUETAR.
        internal const string CONDICION_NOTA_SOLO_TEXTO = @"l.TipoLinea = 0 AND c.NotaEntrega = 1 AND LTRIM(RTRIM(ISNULL(l.Texto, ''))) <> ''
      AND NOT EXISTS (SELECT 1 FROM NotasEntregaCabecera ne
                      WHERE ne.Empresa = c.Empresa AND ne.Pedido = c.[Número] AND RTRIM(ne.Tipo) = 'Recogida')
      AND NOT EXISTS (SELECT 1 FROM LinPedidoVta x
                      WHERE x.Empresa = l.Empresa AND x.[Número] = l.[Número] AND x.Picking = l.Picking AND x.Estado = 1 AND x.TipoLinea = 1)";

        internal const string SQL_LINEAS_PACKING = @"
SELECT c.[Número] AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(cl.Nombre) AS Nombre, RTRIM(cl.[Dirección]) AS Direccion, RTRIM(cl.CodPostal) AS CodigoPostal,
       RTRIM(cl.[Población]) AS Poblacion, RTRIM(c.Ruta) AS Ruta,
       RTRIM(CAST(c.ComentarioPicking AS nvarchar(max))) AS ComentarioPicking,
       l.[Nº Orden] AS LineaPedido, RTRIM(l.Producto) AS Producto, RTRIM(l.Texto) AS Descripcion,
       RTRIM(p.CodBarras) AS CodigoBarras,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad,
       CAST(0 AS bit) AS EsTexto
FROM LinPedidoVta l
     JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
     LEFT JOIN Clientes cl ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1 AND ISNULL(p.Ficticio, 0) = 0
      AND (@p2 IS NULL OR l.[Número] = @p2)
      AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) <> 0
UNION ALL
SELECT c.[Número] AS Pedido, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(cl.Nombre) AS Nombre, RTRIM(cl.[Dirección]) AS Direccion, RTRIM(cl.CodPostal) AS CodigoPostal,
       RTRIM(cl.[Población]) AS Poblacion, RTRIM(c.Ruta) AS Ruta,
       RTRIM(CAST(c.ComentarioPicking AS nvarchar(max))) AS ComentarioPicking,
       l.[Nº Orden] AS LineaPedido, '' AS Producto, LTRIM(RTRIM(l.Texto)) AS Descripcion,
       CAST(NULL AS varchar(50)) AS CodigoBarras,
       1 AS Cantidad,
       CAST(1 AS bit) AS EsTexto
FROM LinPedidoVta l
     JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
     LEFT JOIN Clientes cl ON cl.Empresa = c.Empresa AND cl.[Nº Cliente] = c.[Nº Cliente] AND cl.Contacto = c.Contacto
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.Estado = 1
      AND (@p2 IS NULL OR l.[Número] = @p2)
      AND " + CONDICION_NOTA_SOLO_TEXTO + @"
ORDER BY Cliente, Contacto, Pedido, LineaPedido";

        // NestoAPI#556: lo ya metido en las cajas del picking (y lo que no apareció en la mesa), para retomar el packing.
        // Va por IX_PreparacionEscaneos_Origen (Empresa, TipoOrigen, NumeroOrigen, Fase), del script #574.
        internal const string SQL_LECTURAS_PACKING = @"
SELECT e.Pedido AS Pedido, RTRIM(e.Producto) AS Producto,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Metidas,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = @p1 AND e.Fase = 'PACK' AND e.Pedido IS NOT NULL
      AND (@p2 IS NULL OR e.Pedido = @p2)
GROUP BY e.Pedido, e.Producto";

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

        /// <summary>
        /// NestoAPI#713: el mismo INSERT con lo que pedía la parada (Scripts/Issue713_PreparacionEscaneosParada.sql). Mientras
        /// las columnas no existan se usa <see cref="SQL_INSERTAR_ESCANEO"/> y la API sigue guardando igual.
        /// </summary>
        internal const string SQL_INSERTAR_ESCANEO_CON_PARADA = @"
INSERT INTO PreparacionEscaneos
       (IdCliente, Empresa, NumeroOrigen, Pedido, LineaPedido, Producto, Fase, Cantidad, Metodo, Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo, TipoOrigen, PendienteParada, TotalParada)
SELECT @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16
WHERE NOT EXISTS (SELECT 1 FROM PreparacionEscaneos WHERE IdCliente = @p0)";

        internal const string SQL_HAY_COLUMNAS_PARADA = @"
SELECT CAST(CASE WHEN COL_LENGTH('dbo.PreparacionEscaneos', 'PendienteParada') IS NOT NULL
                  AND COL_LENGTH('dbo.PreparacionEscaneos', 'TotalParada') IS NOT NULL THEN 1 ELSE 0 END AS bit)";

        // NestoAPI#713: se mira una vez; si ya están, no se vuelve a preguntar; si no, como mucho cada 5 minutos (para que
        // las use en cuanto se lance el script, sin reiniciar la API). Igual que RepositorioServirPendienteAlHaberStock.
        private static readonly TimeSpan REINTENTO_SIN_COLUMNAS_PARADA = TimeSpan.FromMinutes(5);
        private static readonly object cerrojoColumnasParada = new object();
        private static bool hayColumnasParada;
        private static DateTime ultimaComprobacionColumnasParada = DateTime.MinValue;

        /// <summary>Para los tests: olvida lo que se sabía de las columnas de la parada.</summary>
        internal static void OlvidarColumnasParada()
        {
            lock (cerrojoColumnasParada)
            {
                hayColumnasParada = false;
                ultimaComprobacionColumnasParada = DateTime.MinValue;
            }
        }

        /// <summary>
        /// NestoAPI#713: ¿están ya PendienteParada y TotalParada? <paramref name="preguntar"/> hace la consulta; si falla, se
        /// da por que no (y se vuelve a mirar pasados 5 minutos).
        /// </summary>
        internal static async Task<bool> HayColumnasParada(Func<Task<bool>> preguntar)
        {
            lock (cerrojoColumnasParada)
            {
                if (hayColumnasParada || DateTime.Now - ultimaComprobacionColumnasParada < REINTENTO_SIN_COLUMNAS_PARADA)
                {
                    return hayColumnasParada;
                }
            }
            bool ahora;
            try
            {
                ahora = await preguntar().ConfigureAwait(false);
            }
            catch
            {
                ahora = false;
            }
            lock (cerrojoColumnasParada)
            {
                hayColumnasParada = hayColumnasParada || ahora;
                ultimaComprobacionColumnasParada = DateTime.Now;
                return hayColumnasParada;
            }
        }

        /// <summary>NestoAPI#713: el INSERT y sus parámetros, con o sin las columnas de la parada.</summary>
        internal static (string Sql, SqlParameter[] Parametros) ComandoInsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario,
            bool conParada)
        {
            var parametros = new List<SqlParameter>
            {
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
                new SqlParameter("@p14", CasadorEscaneos.TipoOrigenDe(escaneo))
            };
            if (!conParada)
            {
                return (SQL_INSERTAR_ESCANEO, parametros.ToArray());
            }
            parametros.Add(Entero("@p15", escaneo.PendienteParada));
            parametros.Add(Entero("@p16", escaneo.TotalParada));
            return (SQL_INSERTAR_ESCANEO_CON_PARADA, parametros.ToArray());
        }

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
        internal const string SQL_BULTOS_DEL_PICKING = "SELECT" + COLUMNAS_BULTO +
            " FROM EnviosAgenciaBultos b WHERE b.Empresa = @p0 AND b.Picking = @p1 ORDER BY b.Pedido, b.Bulto";

        // NestoAPI#646: quién está empaquetando. Lo último de cada usuario en cada pedido: sus lecturas PACK (por
        // IX_PreparacionEscaneos_Origen) y sus fotos de bulto, con la hora del servidor al guardarlas (FechaRegistro, no la de
        // la PDA). Sin tabla nueva. {0}: la lista de pickings (@p2, @p3…).
        internal const string SQL_ACTIVIDAD_PACKING = @"
SELECT x.Picking, x.Pedido, x.Usuario, CAST(MIN(DATEDIFF(second, x.Fecha, GETDATE())) AS int) AS Segundos
FROM (
    SELECT e.NumeroOrigen AS Picking, e.Pedido AS Pedido, RTRIM(e.Usuario) AS Usuario, e.FechaRegistro AS Fecha
    FROM PreparacionEscaneos e
    WHERE e.Empresa = @p0 AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen IN ({0}) AND e.Fase = 'PACK' AND e.Pedido IS NOT NULL
          AND e.FechaRegistro >= DATEADD(second, -@p1, GETDATE())
    UNION ALL
    SELECT b.Picking, b.Pedido, RTRIM(b.Usuario), b.FechaRegistro
    FROM EnviosAgenciaBultos b
    WHERE b.Empresa = @p0 AND b.Picking IN ({0}) AND b.FechaRegistro >= DATEADD(second, -@p1, GETDATE())
) x
GROUP BY x.Picking, x.Pedido, x.Usuario";

        // Repetir la foto de un bulto sustituye a la anterior: manda la última
        internal const string SQL_ACTUALIZAR_BULTO = @"
UPDATE EnviosAgenciaBultos
SET IdCliente = @p0, RutaBlob = @p5, HashSha256 = @p6, TamanoBytes = @p7, Usuario = @p8, Dispositivo = @p9,
    FechaFoto = @p10, FechaRegistro = GETDATE()
WHERE Empresa = @p1 AND Pedido = @p2 AND Picking = @p3 AND Bulto = @p4";

        internal const string SQL_INSERTAR_BULTO = @"
INSERT INTO EnviosAgenciaBultos (IdCliente, Empresa, Pedido, Picking, Bulto, RutaBlob, HashSha256, TamanoBytes, Usuario, Dispositivo, FechaFoto)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)";

        // NestoAPI#646: dos PDA que cierran «el bulto 2» de la misma entrega ya no se pisan la foto. Las subidas de una misma
        // entrega (los bultos del picking de los pedidos del mismo cliente y contacto, que es como los cuenta Agencias) se
        // ponen en fila con UN cerrojo de aplicación por entrega (sp_getapplock, dueño la transacción), que es lo primero que
        // se pide: así nunca hay dos bloqueos tomados en distinto orden (con UPDLOCK + HOLDLOCK sobre rangos, dos subidas de
        // entregas que comparten rango podían interbloquearse, SQL 1205). Si en 10 s no se consigue, error claro y no se toca
        // nada. «Mío» = mismo usuario y, si las dos lo dicen, misma PDA. Si esta PDA ya tiene ese bulto en ese pedido, es repetir la foto: se sustituye, como siempre.
        // Si no, y el número lo tiene otro, va al siguiente libre de la entrega. Mismos parámetros que SQL_ACTUALIZAR_BULTO.
        internal const string SQL_GUARDAR_BULTO_SIN_PISAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @cliente varchar(20), @contacto varchar(10);
SELECT @cliente = [Nº Cliente], @contacto = Contacto FROM CabPedidoVta WHERE Empresa = @p1 AND [Número] = @p2;
DECLARE @recurso nvarchar(255) = CONCAT(N'EnviosAgenciaBultos:', RTRIM(@p1), N':', @p3, N':', RTRIM(ISNULL(@cliente, '')), N':', RTRIM(ISNULL(@contacto, '')));
DECLARE @cerrojo int;
EXEC @cerrojo = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
IF @cerrojo < 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Ahora mismo otra PDA está guardando un bulto de esta entrega: no se ha guardado nada. Vuelva a intentarlo.', 16, 1);
    RETURN;
END
DECLARE @entrega TABLE (Pedido int NOT NULL, Bulto int NOT NULL, Usuario varchar(50) NULL, Dispositivo varchar(50) NULL, Mio bit NOT NULL);
INSERT INTO @entrega (Pedido, Bulto, Usuario, Dispositivo, Mio)
SELECT b.Pedido, b.Bulto, RTRIM(b.Usuario), RTRIM(b.Dispositivo),
       CASE WHEN ISNULL(RTRIM(b.Usuario), '') = ISNULL(@p8, '')
                 AND (b.Dispositivo IS NULL OR @p9 IS NULL OR RTRIM(b.Dispositivo) = @p9) THEN 1 ELSE 0 END
FROM EnviosAgenciaBultos b
WHERE b.Empresa = @p1 AND b.Picking = @p3
  AND (b.Pedido = @p2 OR b.Pedido IN (SELECT c.[Número] FROM CabPedidoVta c
                                      WHERE c.Empresa = @p1 AND c.[Nº Cliente] = @cliente AND c.Contacto = @contacto));
DECLARE @bulto int = @p4, @ocupadoPor varchar(50) = NULL;
IF NOT EXISTS (SELECT 1 FROM @entrega WHERE Pedido = @p2 AND Bulto = @p4 AND Mio = 1)
    SELECT TOP 1 @ocupadoPor = ISNULL(Usuario, '') FROM @entrega WHERE Bulto = @p4 AND Mio = 0 ORDER BY Pedido;
IF @ocupadoPor IS NOT NULL
    SELECT @bulto = ISNULL(MAX(Bulto), 0) + 1 FROM @entrega;
UPDATE EnviosAgenciaBultos
SET IdCliente = @p0, RutaBlob = @p5, HashSha256 = @p6, TamanoBytes = @p7, Usuario = @p8, Dispositivo = @p9,
    FechaFoto = @p10, FechaRegistro = GETDATE()
WHERE Empresa = @p1 AND Pedido = @p2 AND Picking = @p3 AND Bulto = @bulto;
IF @@ROWCOUNT = 0
    INSERT INTO EnviosAgenciaBultos (IdCliente, Empresa, Pedido, Picking, Bulto, RutaBlob, HashSha256, TamanoBytes, Usuario, Dispositivo, FechaFoto)
    VALUES (@p0, @p1, @p2, @p3, @bulto, @p5, @p6, @p7, @p8, @p9, @p10);
COMMIT TRANSACTION;
SELECT @bulto AS Bulto, @ocupadoPor AS OcupadoPor;";

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
         LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
    WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.Estado = 1 AND l.TipoLinea = 1 AND l.Picking > 0
          AND ISNULL(p.Ficticio, 0) = 0
          AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) <> 0
    UNION ALL
    SELECT l.Picking, l.[Número] AS Pedido, c.[Nº Cliente] AS Cliente, c.Contacto, 1 AS Unidades
    FROM LinPedidoVta l
         JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
    WHERE l.Empresa = @p0 AND l.[Almacén] = @p1 AND l.Estado = 1 AND l.Picking > 0
          AND " + CONDICION_NOTA_SOLO_TEXTO + @"
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
       (SELECT COUNT(DISTINCT ea.Pedido) FROM EnviosAgencia ea
        WHERE ea.Empresa = @p0 AND ea.Estado >= 0
              AND ea.Pedido IN (SELECT li.Pedido FROM lineas li WHERE li.Picking = e.Picking AND li.Cliente = e.Cliente AND li.Contacto = e.Contacto)) AS PedidosConEtiqueta,
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

        public async Task<List<LineaPickingAlmacenDTO>> LeerLineasPicking(string empresa, int picking)
        {
            List<FilaParadaRecogida> filas = await baseDeDatos.SqlQuery<FilaParadaRecogida>(SQL_LINEAS_PICKING, empresa, picking)
                .ToListAsync().ConfigureAwait(false);
            return RecorteParadasRecogida.Aplicar(empresa, CasadorEscaneos.ORIGEN_PICKING, picking, filas);
        }

        public Task<List<PickingEnCursoDTO>> LeerPickingsEnCurso(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<PickingEnCursoDTO>(SQL_PICKINGS_EN_CURSO, empresa, almacen).ToListAsync();
        }

        public async Task<string> LeerAlmacenPicking(string empresa, int picking)
        {
            return (await baseDeDatos.SqlQuery<string>(SQL_ALMACEN_PICKING, empresa, picking).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
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
            List<FilaParadaRecogida> filas = await baseDeDatos.SqlQuery<FilaParadaRecogida>(SQL_LINEAS_REPOSICION_SALIDA, empresa, traspaso)
                .ToListAsync().ConfigureAwait(false);
            List<LineaPickingAlmacenDTO> lineas = RecorteParadasRecogida.Aplicar(empresa, CasadorEscaneos.ORIGEN_REPOSICION, traspaso, filas);
            if (lineas.Count == 0)
            {
                return null;
            }
            string destino = (await baseDeDatos.SqlQuery<string>(SQL_DESTINO_REPOSICION, empresa, traspaso).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
            string nombreDestino = await LeerNombreAlmacen(baseDeDatos, empresa, destino).ConfigureAwait(false);
            string origen = (await baseDeDatos.SqlQuery<string>(SQL_ORIGEN_REPOSICION, empresa, traspaso).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
            return new ReposicionSalida { Destino = destino, NombreDestino = nombreDestino, Almacen = origen, Lineas = lineas };
        }

        /// <summary>
        /// NestoAPI#600: el nombre de un almacén para enseñarlo («Reina», Almacenes.Descripción); null si no hay código o no
        /// existe. El único sitio que lee SQL_NOMBRE_ALMACEN (también lo usan las reposiciones).
        /// </summary>
        internal static async Task<string> LeerNombreAlmacen(Database baseDeDatos, string empresa, string almacen)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return null;
            }
            string nombre = (await baseDeDatos.SqlQuery<string>(SQL_NOMBRE_ALMACEN, empresa?.Trim(), almacen.Trim()).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        }

        public Task<List<LecturaPackingAlmacen>> LeerLecturasPacking(string empresa, int picking, int? pedido)
        {
            return baseDeDatos.SqlQuery<LecturaPackingAlmacen>(SQL_LECTURAS_PACKING,
                new SqlParameter("@p0", empresa), new SqlParameter("@p1", picking),
                new SqlParameter("@p2", System.Data.SqlDbType.Int) { Value = (object)pedido ?? DBNull.Value }).ToListAsync();
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
                bool conParada = (escaneo.PendienteParada.HasValue || escaneo.TotalParada.HasValue)
                    && await HayColumnasParada(() => baseDeDatos.SqlQuery<bool>(SQL_HAY_COLUMNAS_PARADA).FirstAsync()).ConfigureAwait(false);
                (string sql, SqlParameter[] parametros) = ComandoInsertarEscaneo(empresa, escaneo, usuario, conParada);
                int filas = await baseDeDatos.ExecuteSqlCommandAsync(sql, parametros).ConfigureAwait(false);
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

        public Task<List<BultoAlmacenDTO>> LeerBultosDelPicking(string empresa, int picking)
        {
            return baseDeDatos.SqlQuery<BultoAlmacenDTO>(SQL_BULTOS_DEL_PICKING, empresa, picking).ToListAsync();
        }

        public Task<List<ActividadPackingAlmacen>> LeerActividadPacking(string empresa, IReadOnlyCollection<int> pickings, int segundos)
        {
            List<int> lista = (pickings ?? new int[0]).Distinct().ToList();
            if (lista.Count == 0)
            {
                return Task.FromResult(new List<ActividadPackingAlmacen>());
            }
            var parametros = new List<object> { empresa, segundos };
            var nombres = new List<string>();
            foreach (int picking in lista)
            {
                nombres.Add("@p" + parametros.Count);
                parametros.Add(picking);
            }
            return baseDeDatos.SqlQuery<ActividadPackingAlmacen>(string.Format(SQL_ACTIVIDAD_PACKING, string.Join(", ", nombres)), parametros.ToArray())
                .ToListAsync();
        }

        /// <summary>Lo que devuelve SQL_GUARDAR_BULTO_SIN_PISAR: el número con el que se ha guardado y quién tenía el pedido.</summary>
        public class NumeroBultoGuardado
        {
            public int Bulto { get; set; }
            public string OcupadoPor { get; set; }
        }

        public async Task<BultoAlmacenDTO> GuardarBultoSinPisar(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo)
        {
            NumeroBultoGuardado numero = await baseDeDatos.SqlQuery<NumeroBultoGuardado>(SQL_GUARDAR_BULTO_SIN_PISAR,
                ParametrosBulto(bulto, hashSha256, tamanoBytes, dispositivo)).SingleAsync().ConfigureAwait(false);
            BultoAlmacenDTO guardado = await LeerBultoPorIdCliente(bulto.IdCliente).ConfigureAwait(false);
            if (guardado != null && numero.Bulto != bulto.Bulto)
            {
                guardado.OcupadoPor = string.IsNullOrWhiteSpace(numero.OcupadoPor) ? null : numero.OcupadoPor.Trim();
            }
            return guardado;
        }

        private static object[] ParametrosBulto(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo) => new object[]
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

        public async Task<BultoAlmacenDTO> GuardarBulto(BultoAlmacenDTO bulto, string hashSha256, int tamanoBytes, string dispositivo)
        {
            object[] Parametros() => ParametrosBulto(bulto, hashSha256, tamanoBytes, dispositivo);

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
