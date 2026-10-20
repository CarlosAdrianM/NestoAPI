using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: acceso a datos del motor de tendencias (sustituible en los tests).</summary>
    public interface IRepositorioTendencias
    {
        /// <summary>Los productos que entran (no ficticios, sin MTP ni MMP, empresa 1) con su ficha, proveedor y control de stock de ALG.</summary>
        List<DatosProductoTendencia> LeerProductos();

        /// <summary>
        /// NestoAPI#628 (ficha = Excel): la fila de <see cref="LeerProductos"/> de un solo producto (sin series: se completan con
        /// <see cref="CompletarLote"/>). Null si no entra en el cálculo (ficticio, MTP, MMP) o no existe.
        /// </summary>
        DatosProductoTendencia LeerProducto(string producto);

        /// <summary>
        /// NestoAPI#628 (ficha = Excel): ventas por mes de la familia (la suma de sus productos que entran, cada uno recortado a 0
        /// por mes), alineadas como <see cref="DatosProductoTendencia.Ventas"/>: lo que necesita la estacionalidad de la familia.
        /// </summary>
        int[] LeerVentasFamilia(string familia, DateTime mesReferencia);

        /// <summary>
        /// Rellena series de venta y stock, pendientes (vencidos), servido con retraso (NestoAPI#656, más de
        /// <paramref name="diasRetraso"/> días), compras abiertas y (para los que pueden ser nuevos) ventas por cliente.
        /// </summary>
        void CompletarLote(IList<DatosProductoTendencia> lote, DateTime mesReferencia, int mesesFaseA, int diasRetraso);

        bool ExisteTabla();

        /// <summary>Inserta las filas del mes que aún no existan (nunca pisa). Devuelve cuántas ha insertado.</summary>
        int GuardarPropuestas(DateTime mes, IEnumerable<ResultadoTendencia> resultados, DateTime fechaCalculo);

        /// <summary>Filas guardadas del mes, de más a menos exceso valorado. Sin mes, el último guardado.</summary>
        List<PropuestaStockGuardada> LeerPropuestas(DateTime? mes, int top);

        /// <summary>
        /// NestoAPI#630 (regenerar un mes): cuántas decisiones hay sobre las propuestas del mes (filas con <c>Decision</c> y filas
        /// del histórico PropuestasStockDecisiones). Si no hay columnas o tabla de decisiones (sin el script del corte 3), 0.
        /// </summary>
        int ContarDecisiones(DateTime mes);

        /// <summary>
        /// NestoAPI#630 (regenerar un mes): en una transacción, borra TODAS las filas del mes y guarda las nuevas. Si alguna fila del
        /// mes tiene decisión, no borra nada y lanza <see cref="PropuestasStockConDecisionesException"/>. Devuelve las insertadas.
        /// </summary>
        int RegenerarPropuestas(DateTime mes, IEnumerable<ResultadoTendencia> resultados, DateTime fechaCalculo);
    }

    /// <summary>NestoAPI#630: no se regenera un mes que ya tiene decisiones (se perderían). El mensaje va tal cual al usuario.</summary>
    public class PropuestasStockConDecisionesException : Exception
    {
        public PropuestasStockConDecisionesException(DateTime mes, int decisiones) : base(Mensaje(mes, decisiones))
        {
            Decisiones = decisiones;
        }

        public int Decisiones { get; }

        public static string Mensaje(DateTime mes, int decisiones)
            => $"No se pueden regenerar las propuestas de {mes:yyyy-MM}: ya tienen {decisiones} " +
                $"{(decisiones == 1 ? "decisión tomada" : "decisiones tomadas")} y regenerarlas las borraría. No se ha borrado nada.";
    }

    /// <summary>Una fila de PropuestasStock (lo calculado más la decisión del corte 3).</summary>
    public class PropuestaStockGuardada : ResultadoTendencia
    {
        public int Id { get; set; }
        public DateTime Mes { get; set; }
        public DateTime FechaCalculo { get; set; }
        public string Decision { get; set; }
        public string DecisionUsuario { get; set; }
        public DateTime? DecisionFecha { get; set; }
        public string DecisionMotivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#630: <see cref="IRepositorioTendencias"/> con SQL a mano (<c>db.Database.SqlQuery</c>, sin tocar el EDMX).
    /// <para>CARGA: todo con <c>WITH (NOLOCK)</c>, por lotes de <see cref="TAMANO_LOTE"/> productos (<c>Producto IN (...)</c>, que va
    /// por los índices que empiezan por producto en LinPedidoVta, LinPedidoCmp y ExtractoProducto) y con
    /// <see cref="TIMEOUT_SEGUNDOS"/> de tope por consulta. Recorre en total toda la venta y todo el extracto de 5 años: SOLO
    /// en el job de madrugada.</para>
    /// <para>Lo ÚNICO que escribe es <c>PropuestasStock</c>: inserta y, solo al regenerar un mes sin decisiones
    /// (<see cref="RegenerarPropuestas"/>), borra las filas de ese mes (necesita Scripts/Issue630_RegenerarPropuestasStock.sql).</para>
    /// </summary>
    public class RepositorioTendenciasSql : IRepositorioTendencias
    {
        public const int TAMANO_LOTE = 200;
        internal const int TIMEOUT_SEGUNDOS = 300;
        internal const string TABLA = "dbo.PropuestasStock";

        /// <summary>Ficha, proveedor y control de stock de ALG de los productos que entran (sin ORDER BY: lo comparten
        /// <see cref="SQL_PRODUCTOS"/> y <see cref="SQL_PRODUCTO"/>).</summary>
        private static readonly string SQL_PRODUCTOS_SIN_ORDEN = @"
SELECT RTRIM(p.Número) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia, RTRIM(p.Grupo) AS Grupo,
       RTRIM(p.SubGrupo) AS Subgrupo, p.Estado AS Estado, ISNULL(p.PrecioMedio, 0) AS PrecioMedio,
       prov.Proveedor AS Proveedor, prov.DiasEnServir AS DiasEnServir,
       CAST(cs.StockMáximo AS int) AS StockMaximoAlg, CAST(cs.StockMínimo AS int) AS StockMinimoAlg,
       ISNULL(CAST(cs.Múltiplos AS int), 1) AS MultiplosAlg, RTRIM(fa.TipoExclusiva) AS TipoExclusiva
FROM Productos p WITH (NOLOCK)
LEFT JOIN Familias fa WITH (NOLOCK) ON fa.Empresa = p.Empresa AND fa.Número = p.Familia
OUTER APPLY (SELECT TOP 1 RTRIM(pp.[Nº Proveedor]) AS Proveedor, CAST(pr.DíasEnServir AS int) AS DiasEnServir
             FROM ProveedoresProducto pp WITH (NOLOCK)
             LEFT JOIN Proveedores pr WITH (NOLOCK) ON pr.Empresa = pp.Empresa AND pr.Número = pp.[Nº Proveedor] AND pr.Contacto = pp.ContactoProveedor
             WHERE pp.Empresa = p.Empresa AND pp.[Nº Producto] = p.Número
             ORDER BY pp.Orden) prov
LEFT JOIN ControlesStock cs WITH (NOLOCK) ON cs.Empresa = p.Empresa AND cs.Número = p.Número AND cs.Almacén = 'ALG'
WHERE " + DemandaStock.SQL_FILTRO_PRODUCTO;

        internal static readonly string SQL_PRODUCTOS = SQL_PRODUCTOS_SIN_ORDEN + @"
ORDER BY p.Número";

        /// <summary>NestoAPI#628 (ficha = Excel): la misma fila de <see cref="SQL_PRODUCTOS"/> para un solo producto.</summary>
        internal static readonly string SQL_PRODUCTO = SQL_PRODUCTOS_SIN_ORDEN + " AND p.Número = @producto";

        /// <summary>
        /// NestoAPI#628 (ficha = Excel): los productos de una familia que entran en el cálculo (los mismos que suma
        /// <see cref="ServicioPropuestasStock.IndicesPorFamilia"/>), como subconsulta para el <c>IN ({0})</c> de
        /// <see cref="SQL_VENTAS_MES"/>: una familia puede tener más productos que parámetros admite SQL Server.
        /// </summary>
        internal static readonly string SQL_PRODUCTOS_FAMILIA =
            "SELECT p.Número FROM Productos p WITH (NOLOCK) WHERE " + DemandaStock.SQL_FILTRO_PRODUCTO + " AND p.Familia = @familia";

        internal static readonly string SQL_VENTAS_MES = @"
SELECT RTRIM(l.Producto) AS Producto, YEAR(l.[Fecha Albarán]) AS Anno, MONTH(l.[Fecha Albarán]) AS Mes, SUM(CAST(l.Cantidad AS int)) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_DEMANDA + @"
  AND l.[Fecha Albarán] >= @desde AND l.[Fecha Albarán] < @hasta
GROUP BY RTRIM(l.Producto), YEAR(l.[Fecha Albarán]), MONTH(l.[Fecha Albarán])";

        /// <summary>
        /// NestoAPI#678: hasta <c>@hoy</c> (y no hasta el corte), para ver la primera venta de un nuevo de este mes. El motor solo la
        /// usa como edad si antes del corte no había nada (<see cref="MotorTendencias.Calcular"/>).
        /// </summary>
        internal static readonly string SQL_PRIMERA_VENTA = @"
SELECT RTRIM(l.Producto) AS Producto, MIN(l.[Fecha Albarán]) AS Fecha
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_DEMANDA + @" AND l.Cantidad > 0 AND l.[Fecha Albarán] < @hoy
GROUP BY RTRIM(l.Producto)";

        /// <summary>NestoAPI#678: hasta <c>@hoy</c>, como la velocidad de los nuevos (la recompra se mira hasta hoy).</summary>
        internal static readonly string SQL_VENTAS_CLIENTE = @"
SELECT RTRIM(l.Producto) AS Producto, RTRIM(l.[Nº Cliente]) AS Cliente, l.[Fecha Albarán] AS Fecha
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_DEMANDA + @" AND l.Cantidad > 0 AND l.[Fecha Albarán] < @hoy";

        /// <summary>
        /// NestoAPI#678: venta neta por día de los que pueden ser nuevos (los mismos de <see cref="SQL_VENTAS_CLIENTE"/>), desde 12
        /// meses antes del corte hasta <c>@hoy</c> (a las 00:00: días completos). Agregada por producto y día: como mucho una fila
        /// por día con venta de cada nuevo.
        /// </summary>
        internal static readonly string SQL_VENTAS_DIA = @"
SELECT RTRIM(l.Producto) AS Producto, CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS Fecha, SUM(CAST(l.Cantidad AS int)) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_DEMANDA + @"
  AND l.[Fecha Albarán] >= @desdeDia AND l.[Fecha Albarán] < @hoy
GROUP BY RTRIM(l.Producto), CAST(l.[Fecha Albarán] AS date)";

        internal static readonly string SQL_STOCK_RESUMEN = @"
SELECT RTRIM(e.Número) AS Producto,
       SUM(CASE WHEN e.Almacén <> 'EXT' AND e.Fecha < @desde THEN CAST(e.Cantidad AS int) ELSE 0 END) AS SaldoInicial,
       SUM(CASE WHEN e.Almacén = 'EXT' AND e.Fecha < @hasta THEN CAST(e.Cantidad AS int) ELSE 0 END) AS StockExt,
       MAX(CASE WHEN e.Almacén = 'EXT' AND e.Cantidad > 0 AND e.Fecha < @hasta THEN e.Fecha END) AS UltimaEntradaExt,
       MIN(CASE WHEN e.Cantidad > 0 THEN e.Fecha END) AS PrimeraEntrada
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Número IN ({0}) AND e.Empresa IN " + DemandaStock.SQL_EMPRESAS + @"
GROUP BY RTRIM(e.Número)";

        internal static readonly string SQL_STOCK_MES = @"
SELECT RTRIM(e.Número) AS Producto, YEAR(e.Fecha) AS Anno, MONTH(e.Fecha) AS Mes, SUM(CAST(e.Cantidad AS int)) AS Cantidad
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Número IN ({0}) AND " + DemandaStock.SQL_FILTRO_STOCK + @" AND e.Fecha >= @desde AND e.Fecha < @hasta
GROUP BY RTRIM(e.Número), YEAR(e.Fecha), MONTH(e.Fecha)";

        internal static readonly string SQL_PENDIENTES = @"
SELECT RTRIM(l.Producto) AS Producto, SUM(CAST(l.Cantidad AS int)) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_PENDIENTE + @"
GROUP BY RTRIM(l.Producto)";

        /// <summary>NestoAPI#656 (corte 2): una consulta agregada por producto para todo el lote (Producto IN), como las demás.</summary>
        internal static readonly string SQL_SERVIDO_CON_RETRASO = @"
SELECT RTRIM(l.Producto) AS Producto, SUM(CAST(l.Cantidad AS int)) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND " + DemandaStock.SQL_FILTRO_SERVIDO_CON_RETRASO + @"
GROUP BY RTRIM(l.Producto)";

        internal static readonly string SQL_COMPRAS_ABIERTAS = @"
SELECT RTRIM(l.Producto) AS Producto, SUM(CAST(l.Cantidad AS int)) AS Cantidad
FROM LinPedidoCmp l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND l.Empresa IN " + DemandaStock.SQL_EMPRESAS + @" AND l.Estado IN (-1, 1) AND l.TipoLínea = '1' AND l.Cantidad > 0
GROUP BY RTRIM(l.Producto)";

        internal const string SQL_EXISTE_TABLA = "SELECT CASE WHEN OBJECT_ID('dbo.PropuestasStock', 'U') IS NULL THEN 0 ELSE 1 END";

        /// <summary>
        /// NestoAPI#630 (regenerar): decisiones del mes. Con sp_executesql porque sin el script del corte 3 la columna Decision o la
        /// tabla PropuestasStockDecisiones no existen y la consulta directa no compilaría. Dentro de la transacción de
        /// <see cref="RegenerarPropuestas"/>, con UPDLOCK y HOLDLOCK: nadie decide entre la comprobación y el borrado.
        /// </summary>
        internal const string SQL_CONTAR_DECISIONES = @"
DECLARE @n int = 0, @h int = 0;
IF COL_LENGTH('dbo.PropuestasStock', 'Decision') IS NOT NULL
    EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.PropuestasStock WITH (UPDLOCK, HOLDLOCK) WHERE Mes = @mes AND Decision IS NOT NULL',
        N'@mes date, @n int OUTPUT', @mes = @mes, @n = @n OUTPUT;
IF OBJECT_ID('dbo.PropuestasStockDecisiones', 'U') IS NOT NULL
    EXEC sp_executesql N'SELECT @h = COUNT(*) FROM dbo.PropuestasStockDecisiones WITH (UPDLOCK, HOLDLOCK) WHERE Mes = @mes',
        N'@mes date, @h int OUTPUT', @mes = @mes, @h = @h OUTPUT;
SELECT @n + @h;";

        internal const string SQL_BORRAR_MES = "DELETE FROM dbo.PropuestasStock WHERE Mes = @mes";

        internal const string SQL_YA_GUARDADOS ="SELECT RTRIM(Producto) FROM dbo.PropuestasStock WITH (NOLOCK) WHERE Mes = @mes AND Almacen = @almacen";

        internal const string SQL_LEER = @"
SELECT TOP (@top) Id, Mes, RTRIM(Producto) AS Producto, Nombre, Familia, Grupo, Proveedor, EstadoProducto, RTRIM(Almacen) AS Almacen, Fase,
       MesesDesdePrimeraVenta, Stock, PrecioMedio, Ventas12Meses, VentaMedia6Meses, VentaMediaPonderada, VentaMensualReferencia,
       FactorEstacional, ConEstacionalidad, DiasEnServir, DiasEnServirSupuestos, DiasSeguridad, Multiplos, CoberturaObjetivoDias,
       StockMaximoActual, StockMinimoActual, Formula, CoberturaMeses, CoberturaAlcanzableDias, PendienteRelativa, RatioTrimestre,
       AnioPico, PorcentajeDelPico, AniosDesdePico, Tendencia, Velocidad, TasaRecompra, DemandaNoServida, ServidoConRetraso, PedidoCompraAbierto,
       ExcesoUnidades, ExcesoValorado, Accion, StockMaximoPropuesto, StockMinimoPropuesto, Explicacion, EntradasJson,
       FechaCalculo, Decision, DecisionUsuario, DecisionFecha, DecisionMotivo
FROM dbo.PropuestasStock WITH (NOLOCK)
WHERE Mes = ISNULL(@mes, (SELECT MAX(Mes) FROM dbo.PropuestasStock WITH (NOLOCK)))
ORDER BY ExcesoValorado DESC, Producto";

        private readonly Func<NVEntities> crearContexto;
        private readonly Func<DateTime> ahora;

        public RepositorioTendenciasSql() : this(null)
        {
        }

        /// <param name="ahora">NestoAPI#678: el reloj para saber hasta dónde se leen los días del mes en curso (por defecto, ahora).</param>
        internal RepositorioTendenciasSql(Func<NVEntities> crearContexto, Func<DateTime> ahora = null)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private NVEntities Contexto()
        {
            NVEntities db = crearContexto();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            db.Database.CommandTimeout = TIMEOUT_SEGUNDOS;
            return db;
        }

        public List<DatosProductoTendencia> LeerProductos()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<FilaProducto>(SQL_PRODUCTOS).ToList().Select(Datos).ToList();
            }
        }

        public DatosProductoTendencia LeerProducto(string producto)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }
            using (NVEntities db = Contexto())
            {
                FilaProducto fila = db.Database.SqlQuery<FilaProducto>(SQL_PRODUCTO,
                    new SqlParameter("@producto", SqlDbType.VarChar, 15) { Value = producto.Trim() }).FirstOrDefault();
                return fila == null ? null : Datos(fila);
            }
        }

        public int[] LeerVentasFamilia(string familia, DateTime mesReferencia)
        {
            if (string.IsNullOrWhiteSpace(familia))
            {
                return new int[DatosProductoTendencia.MESES_SERIE];
            }
            DateTime hasta = mesReferencia.AddMonths(1);
            DateTime desde = hasta.AddMonths(-DatosProductoTendencia.MESES_SERIE);
            using (NVEntities db = Contexto())
            {
                List<FilaMes> filas = db.Database.SqlQuery<FilaMes>(string.Format(SQL_VENTAS_MES, SQL_PRODUCTOS_FAMILIA),
                    new SqlParameter("@familia", SqlDbType.VarChar, 10) { Value = familia.Trim() },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta }).ToList();
                return SumarVentasFamilia(filas, desde);
            }
        }

        /// <summary>
        /// La serie de una familia como la suma <see cref="ServicioPropuestasStock.IndicesPorFamilia"/>: por producto y mes, la
        /// venta neta recortada a 0 (igual que <see cref="CompletarLote"/>), y luego la suma de los productos.
        /// </summary>
        internal static int[] SumarVentasFamilia(IEnumerable<FilaMes> filas, DateTime desde)
        {
            int[] serie = new int[DatosProductoTendencia.MESES_SERIE];
            foreach (FilaMes f in filas ?? Enumerable.Empty<FilaMes>())
            {
                int i = Indice(f.Anno, f.Mes, desde);
                if (i >= 0 && i < DatosProductoTendencia.MESES_SERIE)
                {
                    serie[i] += Math.Max(0, f.Cantidad ?? 0);
                }
            }
            return serie;
        }

        private static DatosProductoTendencia Datos(FilaProducto f) => new DatosProductoTendencia
        {
            Producto = f.Producto,
            Nombre = f.Nombre,
            Familia = f.Familia,
            Grupo = f.Grupo,
            Subgrupo = f.Subgrupo,
            Estado = f.Estado,
            PrecioMedio = f.PrecioMedio,
            Proveedor = f.Proveedor,
            DiasEnServir = f.DiasEnServir,
            StockMaximoAlg = f.StockMaximoAlg,
            StockMinimoAlg = f.StockMinimoAlg,
            MultiplosAlg = f.MultiplosAlg <= 0 ? 1 : f.MultiplosAlg,
            TipoExclusiva = f.TipoExclusiva?.Trim()
        };

        public void CompletarLote(IList<DatosProductoTendencia> lote, DateTime mesReferencia, int mesesFaseA, int diasRetraso)
        {
            if (lote == null || lote.Count == 0)
            {
                return;
            }
            DateTime hasta = mesReferencia.AddMonths(1);
            DateTime desde = hasta.AddMonths(-DatosProductoTendencia.MESES_SERIE);
            // NestoAPI#678: los días del mes en curso, hasta hoy (si el mes de referencia es el último cerrado).
            DateTime hoy = MotorTendencias.FechaDatos(mesReferencia, ahora());
            Dictionary<string, object> hastaHoy = new Dictionary<string, object> { ["@hoy"] = hoy };
            // Desde cero: si un lote falló a medias y se reintenta producto a producto, no se duplica nada.
            foreach (DatosProductoTendencia d in lote)
            {
                d.Ventas = new int[DatosProductoTendencia.MESES_SERIE];
                d.StockFinMes = new int[DatosProductoTendencia.MESES_SERIE];
                d.VentasPorCliente = new List<VentaClienteTendencia>();
                d.FechaPrimeraVenta = null;
                d.FechaPrimeraEntrada = null;
                d.StockExt = 0;
                d.UltimaEntradaExt = null;
                d.UnidadesPendientes = 0;
                d.UnidadesServidasConRetraso = 0;
                d.UnidadesPedidoCompraAbierto = 0;
                d.FechaDatos = hoy;
                d.VentasDiarias = null;
            }
            Dictionary<string, DatosProductoTendencia> porProducto = lote.ToDictionary(d => d.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            List<string> numeros = porProducto.Keys.ToList();

            using (NVEntities db = Contexto())
            {
                foreach (FilaMes f in Consultar<FilaMes>(db, SQL_VENTAS_MES, numeros, desde, hasta))
                {
                    int i = Indice(f.Anno, f.Mes, desde);
                    if (i >= 0 && i < DatosProductoTendencia.MESES_SERIE && porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        d.Ventas[i] = Math.Max(0, f.Cantidad ?? 0);
                    }
                }

                foreach (FilaFecha f in Consultar<FilaFecha>(db, SQL_PRIMERA_VENTA, numeros, desde, hasta, hastaHoy))
                {
                    if (porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        d.FechaPrimeraVenta = f.Fecha;
                    }
                }

                Dictionary<string, int> saldoInicial = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (FilaStockResumen f in Consultar<FilaStockResumen>(db, SQL_STOCK_RESUMEN, numeros, desde, hasta))
                {
                    if (porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        saldoInicial[f.Producto] = f.SaldoInicial ?? 0;
                        d.StockExt = f.StockExt ?? 0;
                        d.UltimaEntradaExt = f.UltimaEntradaExt;
                        d.FechaPrimeraEntrada = f.PrimeraEntrada;
                    }
                }

                Dictionary<string, int[]> movimientos = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
                foreach (FilaMes f in Consultar<FilaMes>(db, SQL_STOCK_MES, numeros, desde, hasta))
                {
                    int i = Indice(f.Anno, f.Mes, desde);
                    if (i < 0 || i >= DatosProductoTendencia.MESES_SERIE)
                    {
                        continue;
                    }
                    if (!movimientos.TryGetValue(f.Producto, out int[] serie))
                    {
                        serie = new int[DatosProductoTendencia.MESES_SERIE];
                        movimientos[f.Producto] = serie;
                    }
                    serie[i] += f.Cantidad ?? 0;
                }
                foreach (KeyValuePair<string, DatosProductoTendencia> par in porProducto)
                {
                    saldoInicial.TryGetValue(par.Key, out int saldo);
                    movimientos.TryGetValue(par.Key, out int[] serie);
                    par.Value.StockFinMes = AcumularStock(saldo, serie);
                }

                foreach (FilaCantidad f in Consultar<FilaCantidad>(db, SQL_PENDIENTES, numeros, desde, hasta))
                {
                    if (porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        d.UnidadesPendientes = f.Cantidad ?? 0;
                    }
                }

                Dictionary<string, object> retraso = new Dictionary<string, object> { ["@desde12"] = hasta.AddMonths(-12), ["@dias"] = Math.Max(0, diasRetraso) };
                foreach (FilaCantidad f in Consultar<FilaCantidad>(db, SQL_SERVIDO_CON_RETRASO, numeros, desde, hasta, retraso))
                {
                    if (porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        d.UnidadesServidasConRetraso = Math.Max(0, f.Cantidad ?? 0);
                    }
                }

                foreach (FilaCantidad f in Consultar<FilaCantidad>(db, SQL_COMPRAS_ABIERTAS, numeros, desde, hasta))
                {
                    if (porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                    {
                        d.UnidadesPedidoCompraAbierto = f.Cantidad ?? 0;
                    }
                }

                // Recompra: solo los que pueden ser nuevos (primera venta hace menos de mesesFaseA meses). Son pocos y con poca historia.
                DateTime limiteNuevos = hasta.AddMonths(-mesesFaseA).AddDays(-1);
                List<string> nuevos = porProducto.Values
                    .Where(d => d.FechaPrimeraVenta.HasValue && d.FechaPrimeraVenta.Value >= limiteNuevos)
                    .Select(d => d.Producto.Trim())
                    .ToList();
                if (nuevos.Count > 0)
                {
                    foreach (FilaVentaCliente f in Consultar<FilaVentaCliente>(db, SQL_VENTAS_CLIENTE, nuevos, desde, hasta, hastaHoy))
                    {
                        if (f.Fecha.HasValue && porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                        {
                            d.VentasPorCliente.Add(new VentaClienteTendencia { Cliente = f.Cliente, Fecha = f.Fecha.Value });
                        }
                    }

                    // NestoAPI#678: venta por días de los nuevos (velocidad hasta hoy y caída reciente).
                    foreach (string numero in nuevos)
                    {
                        porProducto[numero].VentasDiarias = new List<VentaDiariaTendencia>();
                    }
                    Dictionary<string, object> dias = new Dictionary<string, object> { ["@hoy"] = hoy, ["@desdeDia"] = limiteNuevos };
                    foreach (FilaVentaDia f in Consultar<FilaVentaDia>(db, SQL_VENTAS_DIA, nuevos, desde, hasta, dias))
                    {
                        if (f.Fecha.HasValue && porProducto.TryGetValue(f.Producto, out DatosProductoTendencia d))
                        {
                            d.VentasDiarias.Add(new VentaDiariaTendencia { Fecha = f.Fecha.Value, Cantidad = f.Cantidad ?? 0 });
                        }
                    }
                }
            }
        }

        /// <summary>Stock a fin de cada mes: saldo antes de la serie más los movimientos acumulados.</summary>
        internal static int[] AcumularStock(int saldoInicial, int[] movimientos)
        {
            int[] stock = new int[DatosProductoTendencia.MESES_SERIE];
            int acumulado = saldoInicial;
            for (int i = 0; i < stock.Length; i++)
            {
                acumulado += movimientos?[i] ?? 0;
                stock[i] = acumulado;
            }
            return stock;
        }

        internal static int Indice(int anno, int mes, DateTime desde) => (anno - desde.Year) * 12 + mes - desde.Month;

        private static List<T> Consultar<T>(NVEntities db, string plantilla, List<string> numeros, DateTime desde, DateTime hasta,
            Dictionary<string, object> otros = null)
        {
            List<object> parametros = new List<object>();
            List<string> nombres = new List<string>();
            for (int i = 0; i < numeros.Count; i++)
            {
                nombres.Add("@n" + i);
                parametros.Add(new SqlParameter("@n" + i, SqlDbType.VarChar, 15) { Value = numeros[i] });
            }
            string sql = string.Format(plantilla, string.Join(", ", nombres));
            if (sql.Contains("@desde"))
            {
                parametros.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde });
            }
            if (sql.Contains("@hasta"))
            {
                parametros.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta });
            }
            foreach (KeyValuePair<string, object> otro in otros ?? new Dictionary<string, object>())
            {
                parametros.Add(otro.Value is DateTime
                    ? new SqlParameter(otro.Key, SqlDbType.DateTime) { Value = otro.Value }
                    : new SqlParameter(otro.Key, otro.Value));
            }
            return db.Database.SqlQuery<T>(sql, parametros.ToArray()).ToList();
        }

        public bool ExisteTabla()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<int>(SQL_EXISTE_TABLA).Single() == 1;
            }
        }

        public int GuardarPropuestas(DateTime mes, IEnumerable<ResultadoTendencia> resultados, DateTime fechaCalculo)
        {
            List<ResultadoTendencia> lista = (resultados ?? Enumerable.Empty<ResultadoTendencia>()).ToList();
            if (lista.Count == 0)
            {
                return 0;
            }
            using (NVEntities db = Contexto())
            {
                HashSet<string> yaGuardados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string almacen in lista.Select(r => r.Almacen).Distinct())
                {
                    foreach (string producto in db.Database.SqlQuery<string>(SQL_YA_GUARDADOS,
                        new SqlParameter("@mes", SqlDbType.Date) { Value = mes },
                        new SqlParameter("@almacen", SqlDbType.VarChar, 3) { Value = almacen }).ToList())
                    {
                        yaGuardados.Add(almacen + "|" + producto);
                    }
                }
                List<ResultadoTendencia> nuevas = lista.Where(r => !yaGuardados.Contains(r.Almacen + "|" + r.Producto)).ToList();
                if (nuevas.Count == 0)
                {
                    return 0;
                }
                DataTable tabla = TablaPropuestas(mes, nuevas, fechaCalculo);
                SqlConnection conexion = (SqlConnection)db.Database.Connection;
                bool abiertaAqui = conexion.State != ConnectionState.Open;
                if (abiertaAqui)
                {
                    conexion.Open();
                }
                try
                {
                    Copiar(conexion, null, tabla);
                }
                finally
                {
                    if (abiertaAqui)
                    {
                        conexion.Close();
                    }
                }
                return nuevas.Count;
            }
        }

        private static void Copiar(SqlConnection conexion, SqlTransaction transaccion, DataTable tabla)
        {
            using (SqlBulkCopy copia = new SqlBulkCopy(conexion, OPCIONES_COPIA_PROPUESTAS, transaccion) { DestinationTableName = TABLA, BulkCopyTimeout = TIMEOUT_SEGUNDOS, BatchSize = 1000 })
            {
                foreach (DataColumn columna in tabla.Columns)
                {
                    copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                }
                copia.WriteToServer(tabla);
            }
        }

        public int ContarDecisiones(DateTime mes)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<int>(SQL_CONTAR_DECISIONES, new SqlParameter("@mes", SqlDbType.Date) { Value = mes }).Single();
            }
        }

        public int RegenerarPropuestas(DateTime mes, IEnumerable<ResultadoTendencia> resultados, DateTime fechaCalculo)
        {
            List<ResultadoTendencia> lista = (resultados ?? Enumerable.Empty<ResultadoTendencia>()).ToList();
            if (lista.Count == 0)
            {
                // Sin nada que guardar no se borra: un cálculo que no ha dado filas (BD caída a medias) no deja el mes vacío.
                return 0;
            }
            DataTable tabla = TablaPropuestas(mes, lista, fechaCalculo);
            using (NVEntities db = Contexto())
            {
                SqlConnection conexion = (SqlConnection)db.Database.Connection;
                bool abiertaAqui = conexion.State != ConnectionState.Open;
                if (abiertaAqui)
                {
                    conexion.Open();
                }
                try
                {
                    using (SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        using (SqlCommand contar = new SqlCommand(SQL_CONTAR_DECISIONES, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                        {
                            contar.Parameters.Add(new SqlParameter("@mes", SqlDbType.Date) { Value = mes });
                            int decisiones = Convert.ToInt32(contar.ExecuteScalar());
                            if (decisiones > 0)
                            {
                                transaccion.Rollback();
                                throw new PropuestasStockConDecisionesException(mes, decisiones);
                            }
                        }
                        using (SqlCommand borrar = new SqlCommand(SQL_BORRAR_MES, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                        {
                            borrar.Parameters.Add(new SqlParameter("@mes", SqlDbType.Date) { Value = mes });
                            _ = borrar.ExecuteNonQuery();
                        }
                        Copiar(conexion, transaccion, tabla);
                        transaccion.Commit();
                    }
                }
                finally
                {
                    if (abiertaAqui)
                    {
                        conexion.Close();
                    }
                }
                return lista.Count;
            }
        }

        /// <summary>
        /// NestoAPI#637: sin CheckConstraints la copia masiva se salta las restricciones de la tabla
        /// (CK_PropuestasStock_Fase) y eso exige ALTER TABLE, que la cuenta del API no tiene: el 15/10/26
        /// las propuestas de 2026-09 no se guardaron («El usuario no tiene el permiso ALTER TABLE»).
        /// FireTriggers por lo mismo, si algún día la tabla lleva trigger. Así basta con INSERT.
        /// </summary>
        internal const SqlBulkCopyOptions OPCIONES_COPIA_PROPUESTAS = SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers;

        /// <summary>Las columnas de PropuestasStock que se rellenan al calcular (las de la decisión quedan a NULL).</summary>
        internal static DataTable TablaPropuestas(DateTime mes, IEnumerable<ResultadoTendencia> filas, DateTime fechaCalculo)
        {
            DataTable t = new DataTable();
            void Col<T>(string nombre) => t.Columns.Add(nombre, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
            Col<DateTime>("Mes"); Col<string>("Producto"); Col<string>("Nombre"); Col<string>("Familia"); Col<string>("Grupo");
            Col<string>("Proveedor"); Col<short?>("EstadoProducto"); Col<string>("Almacen"); Col<string>("Fase");
            Col<int?>("MesesDesdePrimeraVenta"); Col<int>("Stock"); Col<decimal>("PrecioMedio"); Col<int>("Ventas12Meses");
            Col<decimal>("VentaMedia6Meses"); Col<decimal>("VentaMediaPonderada"); Col<decimal>("VentaMensualReferencia");
            Col<decimal>("FactorEstacional"); Col<bool>("ConEstacionalidad"); Col<int>("DiasEnServir"); Col<bool>("DiasEnServirSupuestos");
            Col<int>("DiasSeguridad"); Col<int>("Multiplos"); Col<int>("CoberturaObjetivoDias"); Col<int?>("StockMaximoActual");
            Col<int?>("StockMinimoActual"); Col<string>("Formula"); Col<decimal?>("CoberturaMeses"); Col<int>("CoberturaAlcanzableDias");
            Col<decimal?>("PendienteRelativa"); Col<decimal?>("RatioTrimestre"); Col<int?>("AnioPico"); Col<decimal?>("PorcentajeDelPico");
            Col<int?>("AniosDesdePico"); Col<string>("Tendencia"); Col<decimal?>("Velocidad"); Col<decimal?>("TasaRecompra");
            Col<int>("DemandaNoServida"); Col<int>("ServidoConRetraso"); Col<int>("PedidoCompraAbierto"); Col<int>("ExcesoUnidades"); Col<decimal>("ExcesoValorado");
            Col<string>("Accion"); Col<int?>("StockMaximoPropuesto"); Col<int?>("StockMinimoPropuesto"); Col<string>("Explicacion");
            Col<string>("EntradasJson"); Col<DateTime>("FechaCalculo");
            foreach (ResultadoTendencia r in filas)
            {
                object N(object valor) => valor ?? DBNull.Value;
                t.Rows.Add(mes, r.Producto, N(Cortar(r.Nombre, 50)), N(Cortar(r.Familia, 10)), N(Cortar(r.Grupo, 3)), N(Cortar(r.Proveedor, 10)),
                    N(r.EstadoProducto), r.Almacen, r.Fase, N(r.MesesDesdePrimeraVenta), r.Stock, r.PrecioMedio, r.Ventas12Meses,
                    r.VentaMedia6Meses, r.VentaMediaPonderada, r.VentaMensualReferencia, r.FactorEstacional, r.ConEstacionalidad,
                    r.DiasEnServir, r.DiasEnServirSupuestos, r.DiasSeguridad, r.Multiplos, r.CoberturaObjetivoDias,
                    N(r.StockMaximoActual), N(r.StockMinimoActual), N(Cortar(r.Formula, 40)), N(Acotar(r.CoberturaMeses)),
                    r.CoberturaAlcanzableDias, N(Acotar(r.PendienteRelativa)), N(Acotar(r.RatioTrimestre)), N(r.AnioPico),
                    N(r.PorcentajeDelPico), N(r.AniosDesdePico), N(r.Tendencia), N(r.Velocidad), N(r.TasaRecompra),
                    r.DemandaNoServida, r.ServidoConRetraso, r.PedidoCompraAbierto, r.ExcesoUnidades, r.ExcesoValorado, r.Accion,
                    N(r.StockMaximoPropuesto), N(r.StockMinimoPropuesto), N(Cortar(r.Explicacion, 2000)), N(r.EntradasJson), fechaCalculo);
            }
            return t;
        }

        private static string Cortar(string texto, int longitud) => texto == null || texto.Length <= longitud ? texto : texto.Substring(0, longitud);

        /// <summary>decimal(12,3) admite hasta 999.999.999: una cobertura de 30.000 meses (stock enorme, una venta) cabe; más, se acota.</summary>
        private static decimal? Acotar(decimal? valor) => valor.HasValue ? Math.Max(-999999m, Math.Min(999999m, valor.Value)) : (decimal?)null;

        public List<PropuestaStockGuardada> LeerPropuestas(DateTime? mes, int top)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<PropuestaStockGuardada>(SQL_LEER,
                    new SqlParameter("@top", SqlDbType.Int) { Value = top },
                    new SqlParameter("@mes", SqlDbType.Date) { Value = mes.HasValue ? (object)mes.Value : DBNull.Value }).ToList();
            }
        }

        public class FilaProducto
        {
            public string Producto { get; set; }
            public string Nombre { get; set; }
            public string Familia { get; set; }
            public string Grupo { get; set; }
            public string Subgrupo { get; set; }
            public short? Estado { get; set; }
            public decimal PrecioMedio { get; set; }
            public string Proveedor { get; set; }
            public int? DiasEnServir { get; set; }
            public int? StockMaximoAlg { get; set; }
            public int? StockMinimoAlg { get; set; }
            public int MultiplosAlg { get; set; }
            /// <summary>NestoAPI#655: Familias.TipoExclusiva (PRP = fabricación propia).</summary>
            public string TipoExclusiva { get; set; }
        }

        public class FilaMes
        {
            public string Producto { get; set; }
            public int Anno { get; set; }
            public int Mes { get; set; }
            public int? Cantidad { get; set; }
        }

        public class FilaFecha
        {
            public string Producto { get; set; }
            public DateTime? Fecha { get; set; }
        }

        public class FilaStockResumen
        {
            public string Producto { get; set; }
            public int? SaldoInicial { get; set; }
            public int? StockExt { get; set; }
            public DateTime? UltimaEntradaExt { get; set; }
            public DateTime? PrimeraEntrada { get; set; }
        }

        public class FilaCantidad
        {
            public string Producto { get; set; }
            public int? Cantidad { get; set; }
        }

        public class FilaVentaDia
        {
            public string Producto { get; set; }
            public DateTime? Fecha { get; set; }
            public int? Cantidad { get; set; }
        }

        public class FilaVentaCliente
        {
            public string Producto { get; set; }
            public string Cliente { get; set; }
            public DateTime? Fecha { get; set; }
        }
    }
}
