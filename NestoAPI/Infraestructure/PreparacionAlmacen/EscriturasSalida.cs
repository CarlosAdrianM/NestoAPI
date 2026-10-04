using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.PedidosVenta;
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
    /// <summary>Unidades dadas por «No está» de un producto, en un hueco si se sabe (PPPFFFCCC).</summary>
    public class FaltaSalida
    {
        public string Producto { get; set; }
        public string Hueco { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// Un trozo de lo que se esperaba sacar. En un picking, la reserva de una línea de pedido en un hueco (o la línea
    /// entera si no tiene reserva); en una reposición, una fila de la salida del traspaso con su registro en el hueco.
    /// </summary>
    public class PiezaSalida
    {
        /// <summary>LinPedidoVta.[Nº Orden] en un picking; PreExtrProducto.[Nº Orden] de la salida en una reposición.</summary>
        public int Linea { get; set; }
        /// <summary>El pedido de venta (0 en una reposición): el más nuevo es el primero que se queda sin lo que falta.</summary>
        public int Pedido { get; set; }
        /// <summary>Ubicaciones.NºOrden de la reserva o del registro. Null si no hay.</summary>
        public int? Ubicacion { get; set; }
        public string Producto { get; set; }
        public string Hueco { get; set; }
        public int Cantidad { get; set; }
        /// <summary>La línea lleva producto «en carpeta» (Recoger): partirla sola no es seguro, se dice para la oficina.</summary>
        public bool QuitarAMano { get; set; }
    }

    /// <summary>
    /// Una línea de un pedido que sigue en el picking (estado 1) después de quitar las faltas: de producto (TipoLinea 1)
    /// o no (portes, cuentas…). Para saber si a un pedido le queda algo que salga.
    /// </summary>
    public class LineaEnPickingSalida
    {
        public int Linea { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public short TipoLinea { get; set; }
        /// <summary>Lo que sale de la línea (cantidad menos lo que el cliente se lleva «en carpeta»).</summary>
        public int Cantidad { get; set; }
    }

    public class RecorteSalida
    {
        public PiezaSalida Pieza { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>El diario de salida de reposiciones del almacén de origen de un traspaso (Almacenes.DiarioSalidaRep).</summary>
    public class DiarioSalidaReposicion
    {
        public string Almacen { get; set; }
        public string Diario { get; set; }
        /// <summary>El almacén de las filas de entrada del traspaso.</summary>
        public string Destino { get; set; }
    }

    /// <summary>
    /// NestoAPI#556: a quién se le quita lo que falta. Primero, de la pieza del hueco donde se dio por falta (la parada);
    /// después, del pedido más nuevo (el stock se reparte por antigüedad, así que el último en llegar es el que se queda
    /// sin él). Las líneas que hay que quitar a mano se tocan las últimas. Puro: sin base de datos.
    /// </summary>
    public static class PlanificadorFaltasSalida
    {
        /// <summary>«004/002/012» (como lo enseña la app) o «004002012» (la etiqueta) → «004002012». Null si no es un hueco.</summary>
        public static string NormalizarHueco(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string sinBarras = texto.Trim().Replace("/", string.Empty);
            return sinBarras.Length == 9 && sinBarras.All(char.IsDigit) ? sinBarras : null;
        }

        /// <summary>
        /// Las filas de faltas (con sus «deshacer» en negativo) netas por producto y hueco. Manda el total del producto:
        /// lo que no se pueda atribuir a un hueco queda sin hueco.
        /// </summary>
        public static List<FaltaSalida> AgruparFaltas(IEnumerable<FaltaSalida> filas)
        {
            var resultado = new List<FaltaSalida>();
            foreach (IGrouping<string, FaltaSalida> producto in (filas ?? Enumerable.Empty<FaltaSalida>())
                .Where(f => !string.IsNullOrWhiteSpace(f.Producto))
                .GroupBy(f => f.Producto.Trim()))
            {
                int total = producto.Sum(f => f.Cantidad);
                if (total <= 0)
                {
                    continue;
                }
                int atribuido = 0;
                foreach (IGrouping<string, FaltaSalida> hueco in producto
                    .Where(f => NormalizarHueco(f.Hueco) != null)
                    .GroupBy(f => NormalizarHueco(f.Hueco)))
                {
                    int cantidad = Math.Min(hueco.Sum(f => f.Cantidad), total - atribuido);
                    if (cantidad > 0)
                    {
                        resultado.Add(new FaltaSalida { Producto = producto.Key, Hueco = hueco.Key, Cantidad = cantidad });
                        atribuido += cantidad;
                    }
                }
                if (total > atribuido)
                {
                    resultado.Add(new FaltaSalida { Producto = producto.Key, Hueco = null, Cantidad = total - atribuido });
                }
            }
            return resultado;
        }

        public static List<RecorteSalida> Repartir(IEnumerable<FaltaSalida> faltas, IEnumerable<PiezaSalida> piezas)
        {
            List<PiezaSalida> todas = (piezas ?? Enumerable.Empty<PiezaSalida>()).ToList();
            var quitado = new Dictionary<PiezaSalida, int>();
            // Primero las faltas con hueco: son las que obligan a una pieza concreta
            foreach (FaltaSalida falta in (faltas ?? Enumerable.Empty<FaltaSalida>())
                .Where(f => f.Cantidad > 0)
                .OrderBy(f => f.Hueco == null ? 1 : 0))
            {
                string hueco = NormalizarHueco(falta.Hueco);
                int queda = falta.Cantidad;
                IEnumerable<PiezaSalida> candidatas = todas
                    .Where(p => string.Equals(p.Producto?.Trim(), falta.Producto?.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => hueco != null && p.Hueco == hueco ? 0 : 1)
                    .ThenBy(p => p.QuitarAMano ? 1 : 0)
                    .ThenByDescending(p => p.Pedido)
                    .ThenByDescending(p => p.Linea)
                    .ThenByDescending(p => p.Ubicacion ?? 0);
                foreach (PiezaSalida pieza in candidatas)
                {
                    int libre = pieza.Cantidad - (quitado.TryGetValue(pieza, out int ya) ? ya : 0);
                    if (libre <= 0)
                    {
                        continue;
                    }
                    int menos = Math.Min(libre, queda);
                    quitado[pieza] = (quitado.TryGetValue(pieza, out int antes) ? antes : 0) + menos;
                    queda -= menos;
                    if (queda == 0)
                    {
                        break;
                    }
                }
                if (queda > 0)
                {
                    throw new InvalidOperationException(
                        $"Se han dado por falta {falta.Cantidad} de {falta.Producto?.Trim()} y no hay tanto que sacar: faltan {queda} por quitar.");
                }
            }
            return quitado.Select(q => new RecorteSalida { Pieza = q.Key, Cantidad = q.Value }).ToList();
        }
    }

    /// <summary>Lo que lee y escribe la salida de mercancía al terminar, todo dentro de UNA transacción.</summary>
    public interface ITransaccionSalida
    {
        /// <summary>Las faltas de la salida (PreparacionEscaneos, método FALTA), con el hueco que la app manda en Motivo.</summary>
        Task<List<FaltaSalida>> LeerFaltas(string empresa, string tipoOrigen, int numero);

        Task<List<PiezaSalida>> LeerPiezasPicking(string empresa, int picking);
        Task<List<int>> PedidosDelPicking(string empresa, int picking);
        /// <summary>Lo que no estaba en el hueco sale de la reserva y pasa a «pendiente de ubicar» (estado 2).</summary>
        Task QuitarDeLaReserva(PiezaSalida pieza, int cantidad, string usuario);
        /// <summary>
        /// Lo que falta de una línea sale del picking: la línea se queda con lo servido y lo que falta pasa a una línea
        /// nueva pendiente (-1, sin picking), como GeneradorPendientes al sacar el picking. Si falta todo, la línea entera
        /// pasa a pendiente.
        /// </summary>
        Task SacarDelPedido(int lineaPedido, int cantidadQueFalta);
        /// <summary>Las líneas de esos pedidos que siguen en el picking (estado 1), de cualquier tipo.</summary>
        Task<List<LineaEnPickingSalida>> LineasQueSiguenEnElPicking(string empresa, int picking, IReadOnlyCollection<int> pedidos);

        Task<List<PiezaSalida>> LeerPiezasReposicion(string empresa, int traspaso);
        /// <summary>Lo que no sale: menos en la salida del origen, menos en la entrada del destino y a «pendiente de ubicar».</summary>
        Task QuitarDeLaReposicion(string empresa, int traspaso, PiezaSalida pieza, int cantidad, string usuario);
        /// <summary>Null si el traspaso ya no tiene salida pendiente de contabilizar.</summary>
        Task<DiarioSalidaReposicion> DiarioDeSalida(string empresa, int traspaso);
        Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario);
        /// <summary>prdExtrProducto del diario, por el único punto de llamada (ServicioExtractoProducto).</summary>
        Task Contabilizar(string empresa, string diario, string usuario);
        /// <summary>NestoAPI#553: aparta del diario lo que no es de este traspaso (ver ApartadoTraspasosSql).</summary>
        Task<List<int>> ApartarOtros(string empresa, string diario, int traspaso);
        Task DevolverApartadas(string empresa, string diario, IReadOnlyCollection<int> apartadas);

        Task<DateTime> AhoraEnBaseDeDatos();
        /// <summary>
        /// Ariadna#6: la salida queda apuntada como terminada (PreparacionSalidasTerminadas). Después ya no se puede anular
        /// lo leído en ella: lo que faltaba ya se ha quitado del pedido.
        /// </summary>
        Task ApuntarTerminada(string empresa, string tipo, int numero, string usuario);
        /// <summary>
        /// Ya está apuntada como terminada: no se vuelve a hacer nada (las faltas se quitarían dos veces). Bloquea la marca
        /// hasta el final de la transacción para que dos «Terminar» a la vez no pasen los dos.
        /// </summary>
        Task<bool> EstaTerminada(string empresa, string tipo, int numero);
        Task<List<FilaEnsayoDTO>> FotoPicking(string empresa, IReadOnlyCollection<int> pedidos, IReadOnlyCollection<string> productos, DateTime desde);
        Task<List<FilaEnsayoDTO>> FotoReposicion(string empresa, int traspaso, IReadOnlyCollection<string> productos, DateTime desde);
    }

    /// <summary>
    /// Ariadna#6: anula TODO lo que un mozo ha leído en una salida (una prueba olvidada en la cola de la PDA), nunca
    /// lecturas sueltas. Anular es lo mismo que si el mozo hubiera pulsado «Deshacer» en todo: por cada grupo de lecturas
    /// (producto, método, hueco…) que no esté a cero, una lectura en negativo a su nombre. Así lo leído de ese mozo queda
    /// en cero sin borrar la evidencia, y se apunta quién lo anuló y cuándo (PreparacionAnulacionesLecturas).
    /// </summary>
    public interface IRepositorioAnulacionLecturas
    {
        /// <returns>Las lecturas en negativo que se han añadido, o null si la salida ya está terminada (no se toca nada).</returns>
        Task<int?> Anular(string empresa, string tipo, int numero, string usuarioLecturas, string anuladoPor);
    }

    public class RepositorioAnulacionLecturasSql : IRepositorioAnulacionLecturas
    {
        // Todo en una transacción: comprobar que no está terminada, compensar y apuntar quién. UPDLOCK+HOLDLOCK para que
        // un «Terminar» o una subida de la cola a la vez no se cuele entre medias. -1 = ya terminada.
        internal const string SQL_ANULAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @filas int = -1;
IF NOT EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas WITH (UPDLOCK, HOLDLOCK)
               WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2)
BEGIN
    INSERT INTO dbo.PreparacionEscaneos (IdCliente, Empresa, TipoOrigen, NumeroOrigen, Pedido, LineaPedido, Producto, Fase,
                                         Cantidad, Metodo, Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo)
    SELECT NEWID(), e.Empresa, e.TipoOrigen, e.NumeroOrigen, e.Pedido, e.LineaPedido, e.Producto, e.Fase,
           CAST(-SUM(e.Cantidad) AS smallint), e.Metodo, e.Bulto, e.Motivo, e.Usuario, @p4, GETDATE()
    FROM dbo.PreparacionEscaneos e WITH (UPDLOCK, HOLDLOCK)
    WHERE e.Empresa = @p0 AND e.TipoOrigen = @p1 AND e.NumeroOrigen = @p2 AND e.Fase = 'PICK' AND RTRIM(e.Usuario) = @p3
    GROUP BY e.Empresa, e.TipoOrigen, e.NumeroOrigen, e.Pedido, e.LineaPedido, e.Producto, e.Fase, e.Metodo, e.Bulto, e.Motivo, e.Usuario
    HAVING SUM(e.Cantidad) <> 0;
    SET @filas = @@ROWCOUNT;
    INSERT INTO dbo.PreparacionAnulacionesLecturas (Empresa, TipoOrigen, NumeroOrigen, UsuarioLecturas, AnuladoPor, Filas)
    VALUES (@p0, @p1, @p2, @p3, @p5, @filas);
END
COMMIT TRANSACTION;
SELECT @filas;";

        private readonly NVEntities db;

        public RepositorioAnulacionLecturasSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<int?> Anular(string empresa, string tipo, int numero, string usuarioLecturas, string anuladoPor)
        {
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(anuladoPor);
            string dispositivo = ("Anulado por " + quien).Length > 50 ? ("Anulado por " + quien).Substring(0, 50) : "Anulado por " + quien;
            int filas = await db.Database.SqlQuery<int>(SQL_ANULAR, empresa, tipo, numero, usuarioLecturas, dispositivo, quien)
                .SingleAsync().ConfigureAwait(false);
            return filas < 0 ? (int?)null : filas;
        }
    }

    public interface IRepositorioSalidas
    {
        /// <summary>
        /// Hace el trabajo en una transacción. Con <paramref name="deshacerSiempre"/> (el ensayo) la deshace SIEMPRE, también
        /// si ha ido bien: así el ensayo y lo de verdad son exactamente el mismo código y solo cambia quién cierra.
        /// </summary>
        Task<ResultadoTerminarSalidaDTO> EnTransaccion(Func<ITransaccionSalida, Task<ResultadoTerminarSalidaDTO>> trabajo, bool deshacerSiempre);
    }

    public class RepositorioSalidasSql : IRepositorioSalidas
    {
        private readonly NVEntities db;

        public RepositorioSalidasSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ResultadoTerminarSalidaDTO> EnTransaccion(Func<ITransaccionSalida, Task<ResultadoTerminarSalidaDTO>> trabajo, bool deshacerSiempre)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    ResultadoTerminarSalidaDTO resultado = await trabajo(new TransaccionSalidaSql(db)).ConfigureAwait(false);
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
                        // Un procedimiento con su propio ROLLBACK (prdExtrProducto al fallar) ya la ha deshecho
                    }
                    SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        throw new NestoBusinessException(sql.Message, ex);
                    }
                    throw;
                }
                finally
                {
                    // Lo que EF tenga en memoria de esta transacción no vale si se ha deshecho
                    foreach (System.Data.Entity.Infrastructure.DbEntityEntry entrada in db.ChangeTracker.Entries().ToList())
                    {
                        entrada.State = EntityState.Detached;
                    }
                }
            }
        }
    }

    public class TransaccionSalidaSql : ITransaccionSalida
    {
        // Las faltas netas (los «deshacer» van en negativo) por producto y por lo que la app manda en Motivo (el hueco)
        internal const string SQL_FALTAS = @"
SELECT RTRIM(e.Producto) AS Producto, RTRIM(e.Motivo) AS Hueco, CAST(SUM(e.Cantidad) AS int) AS Cantidad
FROM PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = @p1 AND e.NumeroOrigen = @p2 AND e.Fase = 'PICK' AND e.Metodo = 'FALTA'
GROUP BY e.Producto, e.Motivo";

        // Lo que se reservó para el picking: una pieza por reserva (Ubicaciones en estado 3) o la línea entera si no
        // tiene (almacén sin control de ubicaciones, notas de entrega). Igual que SQL_LINEAS_PICKING: lo que se recoge
        // es la cantidad menos lo que el cliente se lleva «en carpeta».
        internal const string SQL_PIEZAS_PICKING = @"
SELECT l.[Nº Orden] AS Linea, l.[Número] AS Pedido, u.[NºOrden] AS Ubicacion, RTRIM(l.Producto) AS Producto,
       CASE WHEN u.Pasillo IS NULL OR u.Fila IS NULL OR u.Columna IS NULL THEN NULL
            ELSE RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) END AS Hueco,
       CAST(ABS(ISNULL(u.Cantidad, l.Cantidad - l.Recoger)) AS int) AS Cantidad,
       CAST(CASE WHEN l.Recoger <> 0 THEN 1 ELSE 0 END AS bit) AS QuitarAMano
FROM LinPedidoVta l WITH (UPDLOCK)
     LEFT JOIN Ubicaciones u WITH (UPDLOCK) ON u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.TipoLinea = 1 AND l.Estado = 1";

        internal const string SQL_PEDIDOS_DEL_PICKING = @"
SELECT DISTINCT l.[Número] FROM LinPedidoVta l WHERE l.Empresa = @p0 AND l.Picking = @p1";

        internal const string SQL_LINEAS_QUE_SIGUEN_EN_EL_PICKING = @"
SELECT l.[Nº Orden] AS Linea, l.[Número] AS Pedido, RTRIM(l.Producto) AS Producto, CAST(ISNULL(l.TipoLinea, 0) AS smallint) AS TipoLinea,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad
FROM LinPedidoVta l WITH (UPDLOCK)
WHERE l.Empresa = @p0 AND l.Picking = @p1 AND l.Estado = 1 AND l.[Número] IN ({0})";

        // Lo que no estaba en el hueco no sale: pasa a «pendiente de ubicar» (sin hueco), como deja las reservas
        // prdDeshacerUbicacionPicking con @Reubicar = 0. Primero la fila nueva (copiando de la reserva), luego la resta.
        internal const string SQL_RESERVA_A_PENDIENTE_DE_UBICAR = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
SELECT Empresa, [Almacén], [Número], @p1, 2, @p2 FROM Ubicaciones WHERE [NºOrden] = @p0;
UPDATE Ubicaciones SET Cantidad = Cantidad - @p1 WHERE [NºOrden] = @p0;
DELETE Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0;";

        internal const string SQL_RESERVAS_DE_LA_LINEA = @"
SELECT [NºOrden] FROM Ubicaciones WITH (UPDLOCK) WHERE [NºOrdenVta] = @p0 AND Estado = 3";

        // trgLinPedidoVtaUpd no deja cambiar la cantidad de una línea con picking, ni quitarle el picking con la
        // ubicación reservada. Así que, dentro de la transacción, las reservas que quedan se sueltan de la línea un
        // momento, se parte la línea sin picking y se vuelven a enganchar con su picking: al acabar está igual que si
        // el picking se hubiera sacado ya con la cantidad buena.
        internal const string SQL_SOLTAR_RESERVAS = @"
UPDATE Ubicaciones SET [NºOrdenVta] = NULL WHERE [NºOrdenVta] = @p0 AND Estado = 3";

        internal const string SQL_ENGANCHAR_RESERVA = @"
UPDATE Ubicaciones SET [NºOrdenVta] = @p0 WHERE [NºOrden] = @p1";

        // La salida de un traspaso de reposición: sus filas de PreExtrProducto en el diario de salida del origen, con el
        // registro de lo que salió de cada hueco. Nesto viejo, al crear el traspaso, ya quita la mercancía del hueco y
        // deja ese registro en estado -4 (NºOrdenRepo = la fila de la salida); el estado 4 es la reserva de
        // prdUbicarReposicion, que hoy casi no se usa.
        internal const string SQL_PIEZAS_REPOSICION = @"
SELECT s.[Nº Orden] AS Linea, 0 AS Pedido, u.[NºOrden] AS Ubicacion, RTRIM(s.[Número]) AS Producto,
       CASE WHEN u.Pasillo IS NULL OR u.Fila IS NULL OR u.Columna IS NULL THEN NULL
            ELSE RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) END AS Hueco,
       CAST(ABS(ISNULL(u.Cantidad, s.Cantidad)) AS int) AS Cantidad,
       CAST(0 AS bit) AS QuitarAMano
FROM PreExtrProducto s WITH (UPDLOCK)
     INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
     LEFT JOIN Ubicaciones u WITH (UPDLOCK) ON u.[NºOrdenRepo] = s.[Nº Orden] AND u.Estado IN (4, -4)
WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0";

        internal const string SQL_DIARIO_SALIDA = @"
SELECT TOP 1 RTRIM(s.[Almacén]) AS Almacen, RTRIM(s.Diario) AS Diario,
       (SELECT TOP 1 RTRIM(e.[Almacén]) FROM PreExtrProducto e
        WHERE e.Empresa = s.Empresa AND e.[NºTraspaso] = s.[NºTraspaso] AND e.Cantidad > 0) AS Destino
FROM PreExtrProducto s INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0";

        // El registro del hueco baja (va en negativo en -4, en positivo en la reserva 4) y lo que no salió pasa a
        // «pendiente de ubicar» en el origen
        internal const string SQL_REGISTRO_REPOSICION_A_PENDIENTE_DE_UBICAR = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
SELECT Empresa, [Almacén], [Número], @p1, 2, @p2 FROM Ubicaciones WHERE [NºOrden] = @p0;
UPDATE Ubicaciones SET Cantidad = Cantidad - SIGN(Cantidad) * @p1 WHERE [NºOrden] = @p0;
DELETE Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0;";

        // Sin registro en un hueco (almacén sin control de ubicaciones o salida de «pendiente de ubicar»): no hay hueco
        // que corregir, pero lo que no salió sigue en el origen, sin ubicar
        internal const string SQL_PENDIENTE_DE_UBICAR_DE_LA_SALIDA = @"
INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
SELECT s.Empresa, s.[Almacén], s.[Número], @p1, 2, @p2
FROM PreExtrProducto s INNER JOIN Almacenes a ON a.Empresa = s.Empresa AND a.[Número] = s.[Almacén]
WHERE s.[Nº Orden] = @p0 AND a.ControlUbicaciones = 1";

        // La salida va en negativo: se acerca a cero; si queda en cero, la fila sobra
        internal const string SQL_MENOS_SALIDA = @"
UPDATE PreExtrProducto SET Cantidad = Cantidad + @p1 WHERE [Nº Orden] = @p0;
DELETE PreExtrProducto WHERE [Nº Orden] = @p0 AND Cantidad = 0;";

        internal const string SQL_ENTRADAS_DEL_PRODUCTO = @"
SELECT [Nº Orden] AS Linea, CAST(Cantidad AS int) AS Cantidad FROM PreExtrProducto WITH (UPDLOCK)
WHERE Empresa = @p0 AND [NºTraspaso] = @p1 AND [Número] = @p2 AND Cantidad > 0
ORDER BY [Nº Orden] DESC";

        internal const string SQL_MENOS_ENTRADA = @"
UPDATE PreExtrProducto SET Cantidad = Cantidad - @p1 WHERE [Nº Orden] = @p0;
DELETE PreExtrProducto WHERE [Nº Orden] = @p0 AND Cantidad = 0;";

        internal const string SQL_FOTO_PICKING = @"
SELECT 'LinPedidoVta' AS Tabla, CAST(l.[Nº Orden] AS varchar(20)) AS Clave,
       CONCAT('Pedido=', l.[Número], '; Producto=', RTRIM(l.Producto), '; Cantidad=', l.Cantidad, '; Recoger=', l.Recoger,
              '; Estado=', l.Estado, '; Picking=', l.Picking, '; Entrega=', CONVERT(varchar(10), l.[Fecha Entrega], 120),
              '; Base=', l.[Base Imponible], '; Total=', l.Total) AS Datos
FROM LinPedidoVta l
WHERE l.Empresa = @p0 AND l.[Número] IN ({0}) AND l.Estado BETWEEN -1 AND 1
UNION ALL
SELECT 'Ubicaciones', CAST(u.[NºOrden] AS varchar(20)),
       CONCAT('Producto=', RTRIM(u.[Número]), '; Estado=', u.Estado, '; Cantidad=', u.Cantidad,
              '; Hueco=', ISNULL(RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna), '-'),
              '; Linea=', u.[NºOrdenVta], '; Pedido=', u.PedidoVta)
FROM Ubicaciones u
WHERE u.[NºOrdenVta] IN (SELECT x.[Nº Orden] FROM LinPedidoVta x WHERE x.Empresa = @p0 AND x.[Número] IN ({0}))
   OR (u.Estado = 2 AND u.[NºOrdenVta] IS NULL AND u.FechaCreación >= @p1 AND u.[Número] IN ({1}))";

        internal const string SQL_FOTO_REPOSICION = @"
SELECT 'PreExtrProducto' AS Tabla, CAST(p.[Nº Orden] AS varchar(20)) AS Clave,
       CONCAT('Diario=', RTRIM(p.Diario), '; Almacén=', RTRIM(p.[Almacén]), '; Producto=', RTRIM(p.[Número]), '; Cantidad=', p.Cantidad,
              '; Estado=', p.Estado) AS Datos
FROM PreExtrProducto p WHERE p.Empresa = @p0 AND p.[NºTraspaso] = @p1
UNION ALL
SELECT 'ExtractoProducto', CAST(e.[Nº Orden] AS varchar(20)),
       CONCAT('Diario=', RTRIM(e.Diario), '; Almacén=', RTRIM(e.[Almacén]), '; Producto=', RTRIM(e.[Número]), '; Cantidad=', e.Cantidad)
FROM ExtractoProducto e WHERE e.Empresa = @p0 AND e.[NºTraspaso] = @p1
UNION ALL
SELECT 'Ubicaciones', CAST(u.[NºOrden] AS varchar(20)),
       CONCAT('Almacén=', RTRIM(u.[Almacén]), '; Producto=', RTRIM(u.[Número]), '; Estado=', u.Estado, '; Cantidad=', u.Cantidad,
              '; Hueco=', ISNULL(RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna), '-'), '; NºOrdenRepo=', u.[NºOrdenRepo])
FROM Ubicaciones u
WHERE u.[NºTraspasoRepo] = @p1
   OR u.[NºOrdenRepo] IN (SELECT s.[Nº Orden] FROM PreExtrProducto s WHERE s.Empresa = @p0 AND s.[NºTraspaso] = @p1)
   OR (u.Estado = 2 AND u.[NºOrdenVta] IS NULL AND u.FechaCreación >= @p2 AND u.[Número] IN ({0}))";

        private readonly NVEntities db;
        private readonly IServicioExtractoProducto extractos;
        private readonly GestorPedidosVenta gestorPedidos;

        public TransaccionSalidaSql(NVEntities db, IServicioExtractoProducto extractos = null)
        {
            this.db = db;
            this.extractos = extractos ?? new ServicioExtractoProducto();
            gestorPedidos = new GestorPedidosVenta(new ServicioPedidosVenta());
        }

        public Task<List<FaltaSalida>> LeerFaltas(string empresa, string tipoOrigen, int numero)
        {
            return db.Database.SqlQuery<FaltaSalida>(SQL_FALTAS, empresa, tipoOrigen, numero).ToListAsync();
        }

        public Task<List<PiezaSalida>> LeerPiezasPicking(string empresa, int picking)
        {
            return db.Database.SqlQuery<PiezaSalida>(SQL_PIEZAS_PICKING, empresa, picking).ToListAsync();
        }

        public Task<List<int>> PedidosDelPicking(string empresa, int picking)
        {
            return db.Database.SqlQuery<int>(SQL_PEDIDOS_DEL_PICKING, empresa, picking).ToListAsync();
        }

        public Task<List<LineaEnPickingSalida>> LineasQueSiguenEnElPicking(string empresa, int picking, IReadOnlyCollection<int> pedidos)
        {
            var parametros = new List<object> { empresa, picking };
            string listaPedidos = Lista(parametros, (pedidos ?? new int[0]).Cast<object>(), 0);
            return db.Database.SqlQuery<LineaEnPickingSalida>(string.Format(SQL_LINEAS_QUE_SIGUEN_EN_EL_PICKING, listaPedidos), parametros.ToArray()).ToListAsync();
        }

        public async Task QuitarDeLaReserva(PiezaSalida pieza, int cantidad, string usuario)
        {
            if (pieza.Ubicacion == null || cantidad <= 0)
            {
                return;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_RESERVA_A_PENDIENTE_DE_UBICAR, pieza.Ubicacion.Value, cantidad, Usuario(usuario))
                .ConfigureAwait(false);
        }

        public async Task SacarDelPedido(int lineaPedido, int cantidadQueFalta)
        {
            LinPedidoVta linea = await db.LinPedidoVtas.SingleAsync(l => l.Nº_Orden == lineaPedido).ConfigureAwait(false);
            int picking = linea.Picking ?? 0;
            int servida = (linea.Cantidad ?? 0) - linea.Recoger - cantidadQueFalta;

            List<int> reservas = await db.Database.SqlQuery<int>(SQL_RESERVAS_DE_LA_LINEA, lineaPedido).ToListAsync().ConfigureAwait(false);
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_SOLTAR_RESERVAS, lineaPedido).ConfigureAwait(false);

            linea.Picking = 0;
            if (servida <= 0)
            {
                // No sale nada de la línea: se queda entera pendiente y sin picking (su reserva ya ha pasado a
                // «pendiente de ubicar»), como las que GeneradorPendientes deja sin stock al sacar el picking
                linea.Estado = Constantes.EstadosLineaVenta.PENDIENTE;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            else
            {
                LinPedidoVta pendiente = gestorPedidos.DividirLinea(db, linea, (short)(servida + linea.Recoger));
                pendiente.Picking = 0;
                pendiente.Recoger = 0;
                pendiente.Estado = Constantes.EstadosLineaVenta.PENDIENTE;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                linea.Picking = picking;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }

            foreach (int reserva in reservas)
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_ENGANCHAR_RESERVA, lineaPedido, reserva).ConfigureAwait(false);
            }
        }

        public Task<List<PiezaSalida>> LeerPiezasReposicion(string empresa, int traspaso)
        {
            return db.Database.SqlQuery<PiezaSalida>(SQL_PIEZAS_REPOSICION, empresa, traspaso).ToListAsync();
        }

        public async Task QuitarDeLaReposicion(string empresa, int traspaso, PiezaSalida pieza, int cantidad, string usuario)
        {
            if (cantidad <= 0)
            {
                return;
            }
            _ = pieza.Ubicacion != null
                ? await db.Database.ExecuteSqlCommandAsync(SQL_REGISTRO_REPOSICION_A_PENDIENTE_DE_UBICAR, pieza.Ubicacion.Value, cantidad, Usuario(usuario)).ConfigureAwait(false)
                : await db.Database.ExecuteSqlCommandAsync(SQL_PENDIENTE_DE_UBICAR_DE_LA_SALIDA, pieza.Linea, cantidad, Usuario(usuario)).ConfigureAwait(false);
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_MENOS_SALIDA, pieza.Linea, cantidad).ConfigureAwait(false);

            // Lo que no sale tampoco entra en el destino
            int queda = cantidad;
            List<LineaCantidad> entradas = await db.Database.SqlQuery<LineaCantidad>(SQL_ENTRADAS_DEL_PRODUCTO, empresa, traspaso, pieza.Producto)
                .ToListAsync().ConfigureAwait(false);
            foreach (LineaCantidad entrada in entradas)
            {
                if (queda == 0)
                {
                    break;
                }
                int menos = Math.Min(queda, entrada.Cantidad);
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_MENOS_ENTRADA, entrada.Linea, menos).ConfigureAwait(false);
                queda -= menos;
            }
            if (queda > 0)
            {
                throw new NestoBusinessException($"La entrada del traspaso {traspaso} en el destino ya no tiene {queda} de {pieza.Producto} " +
                    "pendiente (¿ya se ha dado entrada?): no se puede quitar la falta sin descuadrar. No se ha hecho nada.");
            }
        }

        public Task<DiarioSalidaReposicion> DiarioDeSalida(string empresa, int traspaso)
        {
            return db.Database.SqlQuery<DiarioSalidaReposicion>(SQL_DIARIO_SALIDA, empresa, traspaso).FirstOrDefaultAsync();
        }

        public Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario)
        {
            return db.Database.SqlQuery<int>(TransaccionCierreReposicionSql.SQL_TRASPASOS_EN_DIARIO, empresa, almacen, diario).ToListAsync();
        }

        public Task Contabilizar(string empresa, string diario, string usuario)
        {
            return extractos.ContabilizarDiario(db, empresa, diario, Usuario(usuario));
        }

        public Task<List<int>> ApartarOtros(string empresa, string diario, int traspaso)
        {
            return ApartadoTraspasosSql.Apartar(db, empresa, diario, traspaso);
        }

        public Task DevolverApartadas(string empresa, string diario, IReadOnlyCollection<int> apartadas)
        {
            return ApartadoTraspasosSql.Devolver(db, empresa, diario, apartadas);
        }

        public Task<DateTime> AhoraEnBaseDeDatos()
        {
            return db.Database.SqlQuery<DateTime>("SELECT GETDATE()").SingleAsync();
        }

        // Si la tabla aún no existe (script de Ariadna#6 sin lanzar), terminar sigue funcionando igual que antes
        internal const string SQL_APUNTAR_TERMINADA = @"
IF OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2)
    INSERT INTO dbo.PreparacionSalidasTerminadas (Empresa, TipoOrigen, NumeroOrigen, Usuario) VALUES (@p0, @p1, @p2, @p3);";

        public async Task ApuntarTerminada(string empresa, string tipo, int numero, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_APUNTAR_TERMINADA, empresa, tipo, numero, Usuario(usuario)).ConfigureAwait(false);
        }

        // UPDLOCK+HOLDLOCK: un segundo «Terminar» a la vez espera a que el primero acabe y entonces ya la ve terminada
        internal const string SQL_ESTA_TERMINADA = @"
IF OBJECT_ID('dbo.PreparacionSalidasTerminadas') IS NULL
    SELECT CAST(0 AS bit)
ELSE
    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.PreparacionSalidasTerminadas WITH (UPDLOCK, HOLDLOCK)
                                  WHERE Empresa = @p0 AND TipoOrigen = @p1 AND NumeroOrigen = @p2) THEN 1 ELSE 0 END AS bit)";

        public async Task<bool> EstaTerminada(string empresa, string tipo, int numero)
        {
            return await db.Database.SqlQuery<bool>(SQL_ESTA_TERMINADA, empresa, tipo, numero).FirstAsync().ConfigureAwait(false);
        }

        public Task<List<FilaEnsayoDTO>> FotoPicking(string empresa, IReadOnlyCollection<int> pedidos, IReadOnlyCollection<string> productos, DateTime desde)
        {
            var parametros = new List<object> { empresa, desde };
            string listaPedidos = Lista(parametros, (pedidos ?? new int[0]).Cast<object>(), 0);
            string listaProductos = Lista(parametros, (productos ?? new string[0]).Cast<object>(), "");
            return db.Database.SqlQuery<FilaEnsayoDTO>(string.Format(SQL_FOTO_PICKING, listaPedidos, listaProductos), parametros.ToArray()).ToListAsync();
        }

        public Task<List<FilaEnsayoDTO>> FotoReposicion(string empresa, int traspaso, IReadOnlyCollection<string> productos, DateTime desde)
        {
            var parametros = new List<object> { empresa, traspaso, desde };
            string listaProductos = Lista(parametros, (productos ?? new string[0]).Cast<object>(), "");
            return db.Database.SqlQuery<FilaEnsayoDTO>(string.Format(SQL_FOTO_REPOSICION, listaProductos), parametros.ToArray()).ToListAsync();
        }

        /// <summary>Añade los valores como parámetros (@pN) y devuelve la lista para un IN; con un valor neutro si no hay ninguno.</summary>
        private static string Lista(List<object> parametros, IEnumerable<object> valores, object neutro)
        {
            List<object> lista = valores.ToList();
            if (lista.Count == 0)
            {
                lista.Add(neutro);
            }
            var nombres = new List<string>();
            foreach (object valor in lista)
            {
                nombres.Add("@p" + parametros.Count);
                parametros.Add(valor);
            }
            return string.Join(", ", nombres);
        }

        private static string Usuario(string usuario)
        {
            return UsuarioAuditoriaHelper.ParaAuditoria(usuario);
        }

        public class LineaCantidad
        {
            public int Linea { get; set; }
            public int Cantidad { get; set; }
        }
    }
}
