using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Almacen
{
    public interface ILectorCalidadAlmacen
    {
        /// <summary>Los pickings de <paramref name="empresa"/> cuya primera lectura del recorrido en Ariadna cae en [<paramref name="desde"/>, <paramref name="hasta"/>).</summary>
        LecturaCalidadAlmacen Leer(string empresa, DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#914 (corte 2): SOLO LECTURA (READ UNCOMMITTED / NOLOCK), por tandas de un mes, como el lector del pedido perfecto. Cada tanda
    /// es UNA ida con varios resultados sobre la tabla temporal #k (los pickings cuya primera lectura del recorrido cae en la tanda): se
    /// parte de dbo.PreparacionEscaneos por IX_PreparacionEscaneos_Fecha (Empresa, Fase, FechaEscaneo) y de ahí todo va por clave (picking:
    /// IX_PreparacionEscaneos_Origen y LinPedidoVta por Empresa + Picking; rectificativas por factura y línea). Nada recorre LinPedidoVta ni
    /// ExtractoProducto por fechas. Sin TRY_CONVERT, STRING_SPLIT, STRING_AGG ni OFFSET/FETCH (la BD no los tiene). Las tablas fuera del
    /// EDMX que no son imprescindibles (PreparacionSalidasTerminadas, LecturasNoCasadasAlmacen) son opcionales (OBJECT_ID).
    /// </summary>
    public class LectorCalidadAlmacenSql : ILectorCalidadAlmacen
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (calidad almacén #914)";
        internal const int TIMEOUT_SEGUNDOS = 180;
        internal const string SQL_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.PreparacionEscaneos', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Escaneos,
       CAST(CASE WHEN OBJECT_ID('dbo.PreparacionSalidasTerminadas', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Terminadas,
       CAST(CASE WHEN OBJECT_ID('dbo.LecturasNoCasadasAlmacen', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS NoCasadas";

        /// <summary>Desde cuándo hay rastro (primera lectura del recorrido, del packing y primera lectura que no casa). {0}: la de no casadas o NULL.</summary>
        internal const string SQL_DESDE = @"
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT (SELECT TOP 1 e.FechaEscaneo FROM dbo.PreparacionEscaneos e WITH (NOLOCK)
        WHERE e.Empresa = @empresa AND e.Fase = 'PICK' AND e.TipoOrigen = 'PICK' ORDER BY e.FechaEscaneo) AS PrimeraPicking,
       (SELECT TOP 1 e.FechaEscaneo FROM dbo.PreparacionEscaneos e WITH (NOLOCK)
        WHERE e.Empresa = @empresa AND e.Fase = 'PACK' AND e.TipoOrigen = 'PICK' ORDER BY e.FechaEscaneo) AS PrimeraPacking,
       {0} AS PrimeraNoCasada;";
        internal const string SQL_DESDE_NO_CASADAS = "(SELECT TOP 1 n.FechaLectura FROM dbo.LecturasNoCasadasAlmacen n WITH (NOLOCK) WHERE n.Empresa = @empresa ORDER BY n.FechaLectura)";
        internal const string SQL_FECHA_NULA = "CAST(NULL AS datetime)";

        /// <summary>
        /// Los pickings de la tanda en #k: los que tienen alguna lectura del recorrido en la tanda (#c, por fecha) y cuya PRIMERA lectura cae
        /// dentro (así cada picking va a una sola tanda). Las líneas de producto de esos pickings en #l (lo que se recoge, sin ficticios).
        /// </summary>
        internal const string SQL_UNIVERSO = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
IF OBJECT_ID('tempdb..#c') IS NOT NULL DROP TABLE #c;
IF OBJECT_ID('tempdb..#k') IS NOT NULL DROP TABLE #k;
IF OBJECT_ID('tempdb..#l') IS NOT NULL DROP TABLE #l;
SELECT DISTINCT e.NumeroOrigen AS Picking
INTO #c
FROM dbo.PreparacionEscaneos e WITH (NOLOCK)
WHERE e.Empresa = @empresa AND e.Fase = 'PICK' AND e.FechaEscaneo >= @desde AND e.FechaEscaneo < @hasta AND e.TipoOrigen = 'PICK';
SELECT c.Picking, MIN(e.FechaEscaneo) AS Fecha
INTO #k
FROM #c c
INNER JOIN dbo.PreparacionEscaneos e WITH (NOLOCK) ON e.Empresa = @empresa AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = c.Picking AND e.Fase = 'PICK'
GROUP BY c.Picking
HAVING MIN(e.FechaEscaneo) >= @desde;
CREATE CLUSTERED INDEX IX_k ON #k (Picking);
SELECT l.Empresa, l.Picking, l.[Número] AS Pedido, l.[Nº Orden] AS Linea, l.Producto,
       CAST(ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) AS int) AS Cantidad, CAST(l.Estado AS smallint) AS Estado,
       NULLIF(RTRIM(l.[Nº Factura]), '') AS Factura, l.[Almacén] AS Almacen, l.[Nº Cliente] AS Cliente, l.Contacto
INTO #l
FROM #k k
INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = @empresa AND l.Picking = k.Picking
LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.TipoLinea = 1 AND ISNULL(p.Ficticio, 0) = 0 AND ISNULL(l.Cantidad, 0) - ISNULL(l.Recoger, 0) > 0;
";

        /// <summary>Resultado 1: los pickings. {0}: quién lo terminó y cuándo (o NULL sin la tabla).</summary>
        internal const string SQL_PICKINGS = @"
SELECT k.Picking, k.Fecha, {0}
FROM #k k
ORDER BY k.Picking;
";
        internal const string SQL_TERMINADA = @"(SELECT TOP 1 RTRIM(t.Usuario) FROM dbo.PreparacionSalidasTerminadas t WITH (NOLOCK)
         WHERE t.Empresa = @empresa AND t.TipoOrigen = 'PICK' AND t.NumeroOrigen = k.Picking) AS TerminadoPor,
       (SELECT TOP 1 t.Fecha FROM dbo.PreparacionSalidasTerminadas t WITH (NOLOCK)
         WHERE t.Empresa = @empresa AND t.TipoOrigen = 'PICK' AND t.NumeroOrigen = k.Picking) AS TerminadoEl";
        internal const string SQL_SIN_TERMINADA = "CAST(NULL AS varchar(50)) AS TerminadoPor, CAST(NULL AS datetime) AS TerminadoEl";

        /// <summary>Resultados 2-4: lecturas del recorrido (por picking, producto y usuario), del packing (por picking, pedido, producto y usuario) y líneas con lo rectificado.</summary>
        internal const string SQL_LECTURAS_Y_LINEAS = @"
SELECT e.NumeroOrigen AS Picking, RTRIM(e.Producto) AS Producto, RTRIM(e.Usuario) AS Usuario,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Cogidas,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas,
       MAX(e.FechaEscaneo) AS Ultima
FROM #k k
INNER JOIN dbo.PreparacionEscaneos e WITH (NOLOCK) ON e.Empresa = @empresa AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = k.Picking AND e.Fase = 'PICK'
GROUP BY e.NumeroOrigen, e.Producto, e.Usuario;
SELECT e.NumeroOrigen AS Picking, e.Pedido, RTRIM(e.Producto) AS Producto, RTRIM(e.Usuario) AS Usuario,
       CAST(SUM(CASE WHEN e.Metodo <> 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Metidas,
       CAST(SUM(CASE WHEN e.Metodo = 'FALTA' THEN e.Cantidad ELSE 0 END) AS int) AS Faltas
FROM #k k
INNER JOIN dbo.PreparacionEscaneos e WITH (NOLOCK) ON e.Empresa = @empresa AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = k.Picking AND e.Fase = 'PACK'
WHERE e.Pedido IS NOT NULL
GROUP BY e.NumeroOrigen, e.Pedido, e.Producto, e.Usuario;
SELECT l.Picking, l.Pedido, l.Linea, RTRIM(l.Producto) AS Producto, l.Cantidad, l.Estado, l.Factura, RTRIM(l.Almacen) AS Almacen,
       RTRIM(ISNULL(l.Cliente, '')) AS Cliente, RTRIM(ISNULL(l.Contacto, '')) AS Contacto,
       CAST(ISNULL((SELECT SUM(ABS(x.CantidadRectificada)) FROM LinFacturaVtaRectificacion x WITH (NOLOCK)
                    WHERE x.Empresa = l.Empresa AND x.FacturaOriginalNumero = l.Factura AND x.FacturaOriginalLinea = l.Linea
                      AND x.NumeroFactura <> l.Factura), 0) AS decimal(18, 2)) AS Rectificadas
FROM #l l;
";

        /// <summary>
        /// Resultado 5 (con la tabla): lecturas que no casan de esos pickings, por usuario y pantalla (por fecha: IX_LecturasNoCasadasAlmacen_Fecha;
        /// se apuntan mientras se recorre o se empaqueta, así que se buscan hasta 7 días después del final de la tanda).
        /// </summary>
        internal const string SQL_NO_CASADAS = @"
SELECT n.NumeroOrigen AS Picking, RTRIM(n.Usuario) AS Usuario, RTRIM(ISNULL(n.Pantalla, '')) AS Pantalla, COUNT(*) AS Lecturas
FROM dbo.LecturasNoCasadasAlmacen n WITH (NOLOCK)
INNER JOIN #k k ON k.Picking = n.NumeroOrigen
WHERE n.Empresa = @empresa AND n.FechaLectura >= @desde AND n.FechaLectura < DATEADD(day, 7, @hasta) AND n.TipoOrigen = 'PICK'
GROUP BY n.NumeroOrigen, n.Usuario, n.Pantalla;
";

        /// <summary>
        /// Resultado 6: los pickings del rango de números de la tanda que no tienen NINGUNA lectura del recorrido (hechos en papel). Por
        /// Empresa + Picking (rango). Estimación: los números de picking son correlativos.
        /// </summary>
        internal const string SQL_SIN_LECTURAS = @"
DECLARE @min int, @max int;
SELECT @min = MIN(Picking), @max = MAX(Picking) FROM #k;
SELECT l.Picking, COUNT(DISTINCT l.[Número]) AS Pedidos, COUNT(*) AS Lineas, MAX(RTRIM(l.[Almacén])) AS Almacen
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Picking BETWEEN @min AND @max AND l.TipoLinea = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.PreparacionEscaneos e WITH (NOLOCK)
                  WHERE e.Empresa = @empresa AND e.TipoOrigen = 'PICK' AND e.NumeroOrigen = l.Picking AND e.Fase = 'PICK')
GROUP BY l.Picking;
";

        internal const string SQL_FIN_LOTE = @"
DROP TABLE #l;
DROP TABLE #k;
DROP TABLE #c;";

        private readonly string cadenaConexion;

        public LectorCalidadAlmacenSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static LectorCalidadAlmacenSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorCalidadAlmacenSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        /// <summary>El lote de una tanda con las tablas opcionales que haya (público para poder lanzarlo a mano).</summary>
        public static string Lote(bool terminadas, bool noCasadas) =>
            SQL_UNIVERSO
            + string.Format(CultureInfo.InvariantCulture, SQL_PICKINGS, terminadas ? SQL_TERMINADA : SQL_SIN_TERMINADA)
            + SQL_LECTURAS_Y_LINEAS
            + (noCasadas ? SQL_NO_CASADAS : string.Empty)
            + SQL_SIN_LECTURAS
            + SQL_FIN_LOTE;

        public LecturaCalidadAlmacen Leer(string empresa, DateTime desde, DateTime hasta)
        {
            var lectura = new LecturaCalidadAlmacen();
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                bool escaneos, terminadas, noCasadas;
                using (var comando = new SqlCommand(SQL_TABLAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    _ = r.Read();
                    escaneos = r.GetBoolean(0);
                    terminadas = r.GetBoolean(1);
                    noCasadas = r.GetBoolean(2);
                }
                if (!terminadas)
                {
                    lectura.TablasQueFaltan.Add("dbo.PreparacionSalidasTerminadas");
                }
                if (!noCasadas)
                {
                    lectura.TablasQueFaltan.Add("dbo.LecturasNoCasadasAlmacen");
                }
                if (!escaneos)
                {
                    // Sin las lecturas de Ariadna no hay preparador: nada que leer
                    lectura.TablasQueFaltan.Insert(0, "dbo.PreparacionEscaneos");
                    return lectura;
                }

                using (var comando = new SqlCommand(string.Format(CultureInfo.InvariantCulture, SQL_DESDE, noCasadas ? SQL_DESDE_NO_CASADAS : SQL_FECHA_NULA), conexion)
                { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                    using (SqlDataReader r = comando.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            lectura.PrimeraLecturaPicking = FechaNula(r, 0);
                            lectura.PrimeraLecturaPacking = FechaNula(r, 1);
                            lectura.PrimeraNoCasada = FechaNula(r, 2);
                        }
                    }
                }

                string lote = Lote(terminadas, noCasadas);
                foreach (Tuple<DateTime, DateTime> tanda in LectorPedidoPerfectoSql.Tandas(desde, hasta))
                {
                    using (var comando = new SqlCommand(lote, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa;
                        comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = tanda.Item1;
                        comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = tanda.Item2;
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            LeerTanda(r, noCasadas, lectura);
                        }
                    }
                }
            }
            // Un picking en papel puede caer en el rango de números de dos tandas: una vez
            lectura.PickingsSinLecturas = lectura.PickingsSinLecturas.GroupBy(p => p.Picking).Select(g => g.First()).OrderBy(p => p.Picking).ToList();
            return lectura;
        }

        /// <summary>Los resultados de una tanda, en el orden del lote (público para los tests con resultados simulados).</summary>
        public static void LeerTanda(DbDataReader r, bool noCasadas, LecturaCalidadAlmacen lectura)
        {
            // 1. Pickings
            while (r.Read())
            {
                lectura.Pickings.Add(new PickingCalidadAlmacen
                {
                    Picking = Entero(r, 0),
                    Fecha = r.GetDateTime(1),
                    TerminadoPor = Texto(r, 2),
                    TerminadoEl = FechaNula(r, 3)
                });
            }
            // 2. Lecturas del recorrido
            _ = r.NextResult();
            while (r.Read())
            {
                lectura.LecturasPicking.Add(new LecturaPickingCalidadAlmacen
                {
                    Picking = Entero(r, 0),
                    Producto = Texto(r, 1),
                    Usuario = Texto(r, 2),
                    Cogidas = Entero(r, 3),
                    Faltas = Entero(r, 4),
                    Ultima = FechaNula(r, 5) ?? DateTime.MinValue
                });
            }
            // 3. Lecturas del packing
            _ = r.NextResult();
            while (r.Read())
            {
                lectura.LecturasPacking.Add(new LecturaPackingCalidadAlmacen
                {
                    Picking = Entero(r, 0),
                    Pedido = Entero(r, 1),
                    Producto = Texto(r, 2),
                    Usuario = Texto(r, 3),
                    Metidas = Entero(r, 4),
                    Faltas = Entero(r, 5)
                });
            }
            // 4. Líneas
            _ = r.NextResult();
            while (r.Read())
            {
                lectura.Lineas.Add(new LineaPedidoCalidadAlmacen
                {
                    Picking = Entero(r, 0),
                    Pedido = Entero(r, 1),
                    Linea = Entero(r, 2),
                    Producto = Texto(r, 3),
                    Cantidad = Entero(r, 4),
                    Estado = r.IsDBNull(5) ? (short)0 : Convert.ToInt16(r.GetValue(5), CultureInfo.InvariantCulture),
                    Factura = Texto(r, 6),
                    Almacen = Texto(r, 7),
                    Cliente = Texto(r, 8),
                    Contacto = Texto(r, 9),
                    UnidadesRectificadas = r.IsDBNull(10) ? 0 : Convert.ToDecimal(r.GetValue(10), CultureInfo.InvariantCulture)
                });
            }
            // 5. Lecturas que no casan (con la tabla)
            if (noCasadas)
            {
                _ = r.NextResult();
                while (r.Read())
                {
                    lectura.NoCasadas.Add(new NoCasadasCalidadAlmacen
                    {
                        Picking = Entero(r, 0),
                        Usuario = Texto(r, 1),
                        Pantalla = Texto(r, 2),
                        Lecturas = Entero(r, 3)
                    });
                }
            }
            // 6. Pickings sin lecturas del recorrido
            _ = r.NextResult();
            while (r.Read())
            {
                lectura.PickingsSinLecturas.Add(new PickingSinLecturasAlmacen
                {
                    Picking = Entero(r, 0),
                    Pedidos = Entero(r, 1),
                    Lineas = Entero(r, 2),
                    Almacen = Texto(r, 3)
                });
            }
        }

        private static int Entero(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

        private static string Texto(DbDataReader r, int i)
        {
            if (r.IsDBNull(i))
            {
                return null;
            }
            string texto = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrEmpty(texto) ? null : texto;
        }

        private static DateTime? FechaNula(DbDataReader r, int i) => r.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(i), CultureInfo.InvariantCulture);
    }
}
