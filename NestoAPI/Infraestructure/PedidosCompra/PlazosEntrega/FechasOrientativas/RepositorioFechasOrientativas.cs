using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas
{
    /// <summary>NestoAPI#861: una foto abierta con el pedido real que se ha encontrado para ella (o null).</summary>
    public class FotoAbiertaConPedido
    {
        public FotoOrientativaAbierta Foto { get; set; }
        public PedidoRealOrientativa Real { get; set; }
    }

    /// <summary>NestoAPI#861: acceso a dbo.FechasOrientativasSinPedir (fuera del EDMX; sustituible en los tests).</summary>
    public interface IRepositorioFechasOrientativas
    {
        /// <summary>¿Existen la tabla, la vista y dbo.PedidosCompraEnvios (Scripts/Issue853)?</summary>
        bool TablaExiste();
        /// <summary>Los pedidos de venta con líneas de producto pendientes (el mismo filtro que las fotos de la fecha de entrega).</summary>
        List<int> LeerPedidosVentaPendientes(string empresa);
        /// <summary>Producto → día de su última foto entre <paramref name="desde"/> y el día anterior a <paramref name="hoy"/>.</summary>
        Dictionary<string, DateTime> UltimasFotosAnteriores(string empresa, DateTime hoy, DateTime desde);
        /// <summary>Inserta la foto si no hay ya una de ese día, empresa y producto. True si la ha insertado.</summary>
        bool Insertar(FotoFechaOrientativa foto);
        /// <summary>Los Id de las fotos abiertas (sin resolver ni caducar), en orden.</summary>
        List<int> IdsAbiertas(string empresa);
        /// <summary>Las fotos abiertas con Id entre los dos dados, cada una con su pedido real (una sola consulta).</summary>
        List<FotoAbiertaConPedido> LeerAbiertas(string empresa, int desdeId, int hastaId);
        /// <summary>Escribe las resoluciones en una transacción (solo las que siguen abiertas).</summary>
        int Actualizar(IReadOnlyCollection<ResolucionFechaOrientativa> resoluciones);
        /// <summary>Primeras fotos de cada episodio ya resueltas desde <paramref name="desde"/>.</summary>
        List<FilaErrorOrientativa> LeerResueltas(string empresa, DateTime desde);
        /// <summary>Primeras fotos desde <paramref name="desde"/>: abiertas y caducadas.</summary>
        (int Abiertas, int Caducadas) ContarPendientes(string empresa, DateTime desde);
    }

    /// <summary>NestoAPI#861: <see cref="IRepositorioFechasOrientativas"/> con ADO.NET. Conexiones con <c>Enlist=false</c>.</summary>
    public class RepositorioFechasOrientativas : IRepositorioFechasOrientativas
    {
        private const int TIMEOUT_LECTURA = 30;
        private const int TIMEOUT_CALCULO = 300;

        internal const string SQL_EXISTE = "SELECT CASE WHEN OBJECT_ID('dbo.FechasOrientativasSinPedir', 'U') IS NULL " +
            "OR OBJECT_ID('dbo.vFechasOrientativasError', 'V') IS NULL OR OBJECT_ID('dbo.PedidosCompraEnvios', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_ULTIMAS_FOTOS = @"
SELECT RTRIM(f.Producto), CAST(MAX(f.DiaFoto) AS datetime)
FROM dbo.FechasOrientativasSinPedir f WITH (NOLOCK)
WHERE f.Empresa = @empresa AND f.DiaFoto < @hoy AND f.DiaFoto >= @desde
GROUP BY f.Producto";

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.FechasOrientativasSinPedir (DiaFoto, Empresa, Producto, Proveedor, Unidades, PedidosVenta, UltimoPedido, Cadencia,
    DiasDesdeUltimo, DiasHastaPedido, FechaPedidoPrevista, PlazoDias, PlazoPropio, FechaLlegadaOrientativa, FechaSalidaOrientativa,
    YaTocabaPedir, PrimeraDelEpisodio)
SELECT @dia, @empresa, @producto, @proveedor, @unidades, @pedidosVenta, @ultimoPedido, @cadencia,
    @desdeUltimo, @hastaPedido, @pedidoPrevista, @plazo, @plazoPropio, @llegada, @salida,
    @yaTocaba, @primera
WHERE NOT EXISTS (SELECT 1 FROM dbo.FechasOrientativasSinPedir WITH (UPDLOCK, HOLDLOCK)
                  WHERE Empresa = @empresa AND Producto = @producto AND DiaFoto = @dia)";

        internal const string SQL_IDS_ABIERTAS = @"
SELECT f.Id FROM dbo.FechasOrientativasSinPedir f WITH (NOLOCK)
WHERE f.Empresa = @empresa AND f.ResueltaEl IS NULL AND f.Caducada = 0
ORDER BY f.Id";

        /// <summary>
        /// Cada foto abierta con el PRIMER pedido de compra a su proveedor (empresa y espejo) que lleva el producto (líneas de producto,
        /// TipoLínea '1') y se envió el día de la foto o después. Día del envío: dbo.PedidosCompraEnvios.FechaEnvio o, si el pedido no está
        /// ahí (enviado antes del 12/11/26 o por fuera de la API), CabPedidoCmp.[Fecha Modificación], como CalculoPlazosProveedores. El
        /// albarán: el primero de las líneas de ESE producto en ESE pedido. Una consulta por lote de Id.
        /// </summary>
        internal const string SQL_ABIERTAS = @"
SELECT f.Id, CAST(f.DiaFoto AS datetime), CAST(f.FechaPedidoPrevista AS datetime), CAST(f.FechaLlegadaOrientativa AS datetime),
    RTRIM(f.EmpresaPedidoReal), f.PedidoReal, CAST(f.FechaPedidoReal AS datetime), CAST(f.FechaAlbaranReal AS datetime),
    RTRIM(x.Empresa), x.Pedido, CAST(x.FechaPedido AS datetime), x.DeEnvios,
    (SELECT CAST(MIN(CAST(l2.[FechaAlbarán] AS date)) AS datetime) FROM LinPedidoCmp l2 WITH (NOLOCK)
     WHERE l2.Empresa = x.Empresa AND l2.[Número] = x.Pedido AND l2.Producto = f.Producto AND l2.[TipoLínea] = '1') AS FechaAlbaran
FROM dbo.FechasOrientativasSinPedir f WITH (NOLOCK)
OUTER APPLY (
    SELECT TOP 1 c.Empresa, c.[Número] AS Pedido, p.FechaPedido, p.DeEnvios
    FROM CabPedidoCmp c WITH (NOLOCK)
    LEFT JOIN dbo.PedidosCompraEnvios e WITH (NOLOCK) ON e.Empresa = c.Empresa AND e.Pedido = c.[Número]
    CROSS APPLY (SELECT CAST(COALESCE(e.FechaEnvio, c.[Fecha Modificación]) AS date) AS FechaPedido,
                        CAST(CASE WHEN e.Pedido IS NULL THEN 0 ELSE 1 END AS bit) AS DeEnvios) p
    WHERE c.Empresa IN (@empresa, @espejo) AND c.[NºProveedor] = f.Proveedor
      AND p.FechaPedido >= f.DiaFoto
      AND EXISTS (SELECT 1 FROM LinPedidoCmp l WITH (NOLOCK)
                  WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.Producto = f.Producto AND l.[TipoLínea] = '1')
    ORDER BY p.FechaPedido, c.[Número]
) x
WHERE f.Empresa = @empresa AND f.Id >= @desdeId AND f.Id <= @hastaId AND f.ResueltaEl IS NULL AND f.Caducada = 0";

        internal const string SQL_ACTUALIZAR = @"
UPDATE dbo.FechasOrientativasSinPedir SET EmpresaPedidoReal = @empresaReal, PedidoReal = @pedidoReal, FechaPedidoReal = @fechaPedidoReal,
    FechaPedidoRealDeEnvios = @deEnvios, FechaAlbaranReal = @albaran, ErrorPedidoDias = @errorPedido, ErrorLlegadaDias = @errorLlegada,
    ResueltaEl = @resueltaEl, Caducada = @caducada
WHERE Id = @id AND ResueltaEl IS NULL AND Caducada = 0";

        internal const string SQL_RESUELTAS = @"
SELECT RTRIM(f.Proveedor), f.YaTocabaPedir, f.ErrorPedidoDias, f.ErrorLlegadaDias
FROM dbo.FechasOrientativasSinPedir f WITH (NOLOCK)
WHERE f.Empresa = @empresa AND f.PrimeraDelEpisodio = 1 AND f.ResueltaEl IS NOT NULL
  AND f.ErrorPedidoDias IS NOT NULL AND f.ErrorLlegadaDias IS NOT NULL AND f.DiaFoto >= @desde";

        internal const string SQL_PENDIENTES = @"
SELECT ISNULL(SUM(CASE WHEN f.ResueltaEl IS NULL AND f.Caducada = 0 THEN 1 ELSE 0 END), 0),
       ISNULL(SUM(CASE WHEN f.Caducada = 1 THEN 1 ELSE 0 END), 0)
FROM dbo.FechasOrientativasSinPedir f WITH (NOLOCK)
WHERE f.Empresa = @empresa AND f.PrimeraDelEpisodio = 1 AND f.DiaFoto >= @desde";

        private readonly string cadenaConexion;

        public RepositorioFechasOrientativas(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static RepositorioFechasOrientativas DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioFechasOrientativas(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI-FechasOrientativas"
            }.ConnectionString);
        }

        public bool TablaExiste()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE, TIMEOUT_LECTURA))
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        public List<int> LeerPedidosVentaPendientes(string empresa)
        {
            var pedidos = new List<int>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, RepositorioFotosFechaEntrega.SQL_PEDIDOS_PENDIENTES, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        pedidos.Add(Convert.ToInt32(lector.GetValue(0)));
                    }
                }
            }
            return pedidos;
        }

        public Dictionary<string, DateTime> UltimasFotosAnteriores(string empresa, DateTime hoy, DateTime desde)
        {
            var resultado = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_ULTIMAS_FOTOS, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                comando.Parameters.Add("@hoy", SqlDbType.Date).Value = hoy.Date;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        string producto = Cadena(lector, 0);
                        if (producto != null && !lector.IsDBNull(1))
                        {
                            resultado[producto] = lector.GetDateTime(1);
                        }
                    }
                }
            }
            return resultado;
        }

        public bool Insertar(FotoFechaOrientativa foto)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_INSERTAR, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@dia", SqlDbType.Date).Value = foto.DiaFoto.Date;
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(foto.Empresa);
                comando.Parameters.Add("@producto", SqlDbType.VarChar, 15).Value = Recortar(foto.Producto, 15);
                comando.Parameters.Add("@proveedor", SqlDbType.VarChar, 10).Value = Recortar(foto.Proveedor, 10);
                comando.Parameters.Add("@unidades", SqlDbType.Int).Value = foto.Unidades;
                comando.Parameters.Add("@pedidosVenta", SqlDbType.Int).Value = foto.PedidosVenta;
                comando.Parameters.Add("@ultimoPedido", SqlDbType.Date).Value = (object)foto.UltimoPedido?.Date ?? DBNull.Value;
                comando.Parameters.Add("@cadencia", SqlDbType.Int).Value = foto.Cadencia;
                comando.Parameters.Add("@desdeUltimo", SqlDbType.Int).Value = foto.DiasDesdeUltimo;
                comando.Parameters.Add("@hastaPedido", SqlDbType.Int).Value = foto.DiasHastaPedido;
                comando.Parameters.Add("@pedidoPrevista", SqlDbType.Date).Value = foto.FechaPedidoPrevista.Date;
                comando.Parameters.Add("@plazo", SqlDbType.Int).Value = foto.PlazoDias;
                comando.Parameters.Add("@plazoPropio", SqlDbType.Bit).Value = foto.PlazoPropio;
                comando.Parameters.Add("@llegada", SqlDbType.Date).Value = foto.FechaLlegadaOrientativa.Date;
                comando.Parameters.Add("@salida", SqlDbType.Date).Value = (object)foto.FechaSalidaOrientativa?.Date ?? DBNull.Value;
                comando.Parameters.Add("@yaTocaba", SqlDbType.Bit).Value = foto.YaTocabaPedir;
                comando.Parameters.Add("@primera", SqlDbType.Bit).Value = foto.PrimeraDelEpisodio;
                return comando.ExecuteNonQuery() > 0;
            }
        }

        public List<int> IdsAbiertas(string empresa)
        {
            var ids = new List<int>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_IDS_ABIERTAS, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        ids.Add(lector.GetInt32(0));
                    }
                }
            }
            return ids;
        }

        public List<FotoAbiertaConPedido> LeerAbiertas(string empresa, int desdeId, int hastaId)
        {
            var filas = new List<FotoAbiertaConPedido>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_ABIERTAS, TIMEOUT_CALCULO))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                comando.Parameters.Add("@espejo", SqlDbType.Char, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                comando.Parameters.Add("@desdeId", SqlDbType.Int).Value = desdeId;
                comando.Parameters.Add("@hastaId", SqlDbType.Int).Value = hastaId;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        var fila = new FotoAbiertaConPedido
                        {
                            Foto = new FotoOrientativaAbierta
                            {
                                Id = lector.GetInt32(0),
                                DiaFoto = lector.GetDateTime(1),
                                FechaPedidoPrevista = lector.GetDateTime(2),
                                FechaLlegadaOrientativa = lector.GetDateTime(3),
                                EmpresaPedidoReal = Cadena(lector, 4),
                                PedidoReal = lector.IsDBNull(5) ? (int?)null : Convert.ToInt32(lector.GetValue(5)),
                                FechaPedidoReal = Fecha(lector, 6),
                                FechaAlbaranReal = Fecha(lector, 7)
                            }
                        };
                        if (!lector.IsDBNull(9) && !lector.IsDBNull(10))
                        {
                            fila.Real = new PedidoRealOrientativa
                            {
                                Empresa = Cadena(lector, 8),
                                Pedido = Convert.ToInt32(lector.GetValue(9)),
                                FechaPedido = lector.GetDateTime(10),
                                DeEnvios = !lector.IsDBNull(11) && lector.GetBoolean(11),
                                FechaAlbaran = Fecha(lector, 12)
                            };
                        }
                        filas.Add(fila);
                    }
                }
            }
            return filas;
        }

        public int Actualizar(IReadOnlyCollection<ResolucionFechaOrientativa> resoluciones)
        {
            List<ResolucionFechaOrientativa> lista = (resoluciones ?? new List<ResolucionFechaOrientativa>()).Where(r => r != null && r.Cambia).ToList();
            if (!lista.Any())
            {
                return 0;
            }
            int escritas = 0;
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                foreach (ResolucionFechaOrientativa r in lista)
                {
                    using (SqlCommand comando = Comando(conexion, SQL_ACTUALIZAR, TIMEOUT_LECTURA))
                    {
                        comando.Transaction = transaccion;
                        comando.Parameters.Add("@id", SqlDbType.Int).Value = r.Id;
                        comando.Parameters.Add("@empresaReal", SqlDbType.Char, 3).Value = (object)r.EmpresaPedidoReal ?? DBNull.Value;
                        comando.Parameters.Add("@pedidoReal", SqlDbType.Int).Value = (object)r.PedidoReal ?? DBNull.Value;
                        comando.Parameters.Add("@fechaPedidoReal", SqlDbType.Date).Value = (object)r.FechaPedidoReal?.Date ?? DBNull.Value;
                        comando.Parameters.Add("@deEnvios", SqlDbType.Bit).Value = (object)r.FechaPedidoRealDeEnvios ?? DBNull.Value;
                        comando.Parameters.Add("@albaran", SqlDbType.Date).Value = (object)r.FechaAlbaranReal?.Date ?? DBNull.Value;
                        comando.Parameters.Add("@errorPedido", SqlDbType.Int).Value = (object)r.ErrorPedidoDias ?? DBNull.Value;
                        comando.Parameters.Add("@errorLlegada", SqlDbType.Int).Value = (object)r.ErrorLlegadaDias ?? DBNull.Value;
                        comando.Parameters.Add("@resueltaEl", SqlDbType.DateTime).Value = (object)r.ResueltaEl ?? DBNull.Value;
                        comando.Parameters.Add("@caducada", SqlDbType.Bit).Value = r.Caducada;
                        escritas += comando.ExecuteNonQuery();
                    }
                }
                transaccion.Commit();
            }
            return escritas;
        }

        public List<FilaErrorOrientativa> LeerResueltas(string empresa, DateTime desde)
        {
            var filas = new List<FilaErrorOrientativa>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_RESUELTAS, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new FilaErrorOrientativa
                        {
                            Proveedor = Cadena(lector, 0),
                            YaTocabaPedir = lector.GetBoolean(1),
                            ErrorPedidoDias = Convert.ToInt32(lector.GetValue(2)),
                            ErrorLlegadaDias = Convert.ToInt32(lector.GetValue(3))
                        });
                    }
                }
            }
            return filas;
        }

        public (int Abiertas, int Caducadas) ContarPendientes(string empresa, DateTime desde)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_PENDIENTES, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = Empresa(empresa);
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    return lector.Read() ? (Convert.ToInt32(lector.GetValue(0)), Convert.ToInt32(lector.GetValue(1))) : (0, 0);
                }
            }
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql, int timeout) => new SqlCommand(sql, conexion) { CommandTimeout = timeout };

        private static string Cadena(SqlDataReader lector, int i) => lector.IsDBNull(i) ? null : Convert.ToString(lector.GetValue(i))?.Trim();

        private static DateTime? Fecha(SqlDataReader lector, int i) => lector.IsDBNull(i) ? (DateTime?)null : lector.GetDateTime(i);

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
