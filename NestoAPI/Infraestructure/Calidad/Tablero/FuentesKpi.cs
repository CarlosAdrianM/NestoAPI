using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    /// <summary>NestoAPI#906: una diferencia de reposición recibida en el periodo (DiferenciasReposiciones), con el coste del producto.</summary>
    public class DiferenciaReposicionKpi
    {
        public int Id { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Producto { get; set; }
        /// <summary>Leído − enviado: negativo falta, positivo sobra.</summary>
        public int Diferencia { get; set; }
        public string Estado { get; set; }
        public string Resolucion { get; set; }
        /// <summary>Productos.PrecioMedio de HOY (como #893).</summary>
        public decimal PrecioMedio { get; set; }
    }

    /// <summary>NestoAPI#906: unidades de una diferencia en desacuerdo que la regla imputó a un almacén (DiferenciasReposicionImputaciones).</summary>
    public class ImputacionReposicionKpi
    {
        public int DiferenciaId { get; set; }
        public string Almacen { get; set; }
        public int Unidades { get; set; }
    }

    public class LecturaDiferenciasReposicionKpi
    {
        public List<DiferenciaReposicionKpi> Diferencias { get; set; } = new List<DiferenciaReposicionKpi>();
        public List<ImputacionReposicionKpi> Imputaciones { get; set; } = new List<ImputacionReposicionKpi>();
        /// <summary>False sin dbo.DiferenciasReposiciones (#634): el KPI no tiene datos.</summary>
        public bool HayDiferencias { get; set; }
    }

    /// <summary>
    /// De dónde leen los KPI. TODO lo que se lee aquí ya lo calculaba otra pieza (no hay consultas paralelas): los movimientos de stock sin
    /// explicar son la consulta de #893 (<see cref="GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA"/>), los pedidos son los del pedido
    /// perfecto (<see cref="ServicioPedidoPerfecto"/>) y las diferencias de reposición, las filas de #634/#906. Solo lectura y con NOLOCK.
    /// </summary>
    public interface IFuentesKpi
    {
        /// <summary>Filas de la consulta de #893 (almacén, mes, producto, diario, altas, bajas, coste) con Fecha en [desde, hasta).</summary>
        List<FilaPerdidaDesconocidaDTO> MovimientosStockSinExplicar(DateTime desde, DateTime hasta);

        /// <summary>Diferencias de reposición con FechaRecepcion en [desde, hasta) y lo imputado de sus desacuerdos.</summary>
        LecturaDiferenciasReposicionKpi DiferenciasReposicion(DateTime desde, DateTime hasta);

        /// <summary>Pedidos del universo del pedido perfecto con Fecha en [desde, hasta), ya evaluados con LA REGLA.</summary>
        List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> PedidosPerfecto(DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// Las fuentes de verdad, con memoria por instancia: en una misma pasada (el job, o el valor en curso) dos KPI que leen lo mismo lo leen
    /// una vez. Los pedidos se leen por MESES naturales enteros (y el servicio del pedido perfecto los guarda 30 min), así que varias semanas
    /// seguidas comparten la lectura.
    /// </summary>
    public class FuentesKpiSql : IFuentesKpi
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (tablero de KPI #914)";
        internal const int TIMEOUT_SEGUNDOS = 180;

        internal const string SQL_TABLAS_REPOSICION = @"
SELECT CAST(CASE WHEN OBJECT_ID('dbo.DiferenciasReposiciones', 'U') IS NULL THEN 0 ELSE 1 END AS bit) AS Diferencias,
       CAST(CASE WHEN OBJECT_ID('dbo.DiferenciasReposicionDiscusiones', 'U') IS NULL OR OBJECT_ID('dbo.DiferenciasReposicionImputaciones', 'U') IS NULL
                 THEN 0 ELSE 1 END AS bit) AS Imputaciones";

        /// <summary>Las diferencias recibidas en el periodo (las mismas filas que lee el indicador de errores de #906, con el coste de hoy).</summary>
        internal const string SQL_DIFERENCIAS_REPOSICION = @"
SELECT d.Id, RTRIM(ISNULL(d.Origen, '')) AS Origen, RTRIM(d.Destino) AS Destino, RTRIM(d.Producto) AS Producto, d.Diferencia,
       RTRIM(d.Estado) AS Estado, RTRIM(ISNULL(d.Resolucion, '')) AS Resolucion,
       CAST(ISNULL(p.PrecioMedio, 0) AS decimal(19, 4)) AS PrecioMedio
FROM dbo.DiferenciasReposiciones d WITH (NOLOCK)
LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @empresa AND p.[Número] = d.Producto
WHERE d.Empresa = @empresa AND d.FechaRecepcion >= @desde AND d.FechaRecepcion < @hasta";

        /// <summary>Lo que la regla de #906 (ReglaImputacionDesacuerdo) imputó a cada almacén, unidad a unidad, de esas diferencias.</summary>
        internal const string SQL_IMPUTACIONES_REPOSICION = @"
SELECT x.DiferenciaId, RTRIM(i.AlmacenImputado) AS Almacen, COUNT(*) AS Unidades
FROM dbo.DiferenciasReposiciones d WITH (NOLOCK)
INNER JOIN dbo.DiferenciasReposicionDiscusiones x WITH (NOLOCK) ON x.DiferenciaId = d.Id
INNER JOIN dbo.DiferenciasReposicionImputaciones i WITH (NOLOCK) ON i.DiscusionId = x.Id
WHERE d.Empresa = @empresa AND d.FechaRecepcion >= @desde AND d.FechaRecepcion < @hasta AND i.AlmacenImputado IS NOT NULL
GROUP BY x.DiferenciaId, i.AlmacenImputado";

        private readonly string cadenaConexion;
        private readonly Func<string, DateTime, DateTime, CalculoPedidoPerfecto> calcularPedidos;
        private readonly string empresa;
        private readonly ConcurrentDictionary<string, Lazy<object>> memoria = new ConcurrentDictionary<string, Lazy<object>>(StringComparer.Ordinal);

        public FuentesKpiSql(string cadenaConexion, Func<string, DateTime, DateTime, CalculoPedidoPerfecto> calcularPedidos = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            this.cadenaConexion = cadenaConexion;
            this.calcularPedidos = calcularPedidos ?? ((e, d, h) => new ServicioPedidoPerfecto(ahora: AhoraMadrid).Calcular(e, d, h));
            this.empresa = empresa;
        }

        public static FuentesKpiSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new FuentesKpiSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        internal static DateTime AhoraMadrid() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular());

        /// <summary>Una lectura por clave y por instancia (también con dos peticiones a la vez); si falla, no se recuerda.</summary>
        private T Recordar<T>(string clave, Func<T> leer)
        {
            Lazy<object> lazy = memoria.GetOrAdd(clave, _ => new Lazy<object>(() => leer()));
            try
            {
                return (T)lazy.Value;
            }
            catch
            {
                _ = memoria.TryRemove(clave, out _);
                throw;
            }
        }

        public List<FilaPerdidaDesconocidaDTO> MovimientosStockSinExplicar(DateTime desde, DateTime hasta) =>
            Recordar($"stock|{desde:yyyyMMdd}|{hasta:yyyyMMdd}", () =>
            {
                using (var conexion = new SqlConnection(cadenaConexion))
                using (var comando = new SqlCommand(GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                {
                    // Parametros(desde, hastaIncluido): la consulta pone hasta + 1 día como límite exclusivo.
                    comando.Parameters.AddRange(GestorPerdidaDesconocida.Parametros(desde.Date, hasta.Date.AddDays(-1)).Cast<SqlParameter>().ToArray());
                    conexion.Open();
                    using (SqlDataReader r = comando.ExecuteReader())
                    {
                        return LeerFilasStock(r);
                    }
                }
            });

        public LecturaDiferenciasReposicionKpi DiferenciasReposicion(DateTime desde, DateTime hasta) =>
            Recordar($"repo|{desde:yyyyMMdd}|{hasta:yyyyMMdd}", () =>
            {
                var lectura = new LecturaDiferenciasReposicionKpi();
                using (var conexion = new SqlConnection(cadenaConexion))
                {
                    conexion.Open();
                    bool diferencias, imputaciones;
                    using (var comando = new SqlCommand(SQL_TABLAS_REPOSICION, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    using (SqlDataReader r = comando.ExecuteReader())
                    {
                        _ = r.Read();
                        diferencias = r.GetBoolean(0);
                        imputaciones = r.GetBoolean(1);
                    }
                    lectura.HayDiferencias = diferencias;
                    if (!diferencias)
                    {
                        return lectura;
                    }
                    using (var comando = new SqlCommand(SQL_DIFERENCIAS_REPOSICION, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                    {
                        AnadirPeriodo(comando, desde, hasta);
                        using (SqlDataReader r = comando.ExecuteReader())
                        {
                            lectura.Diferencias = LeerDiferencias(r);
                        }
                    }
                    if (imputaciones)
                    {
                        using (var comando = new SqlCommand(SQL_IMPUTACIONES_REPOSICION, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                        {
                            AnadirPeriodo(comando, desde, hasta);
                            using (SqlDataReader r = comando.ExecuteReader())
                            {
                                lectura.Imputaciones = LeerImputaciones(r);
                            }
                        }
                    }
                }
                return lectura;
            });

        public List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> PedidosPerfecto(DateTime desde, DateTime hasta)
        {
            var resultado = new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>();
            foreach (DateTime mes in MesesDe(desde, hasta))
            {
                CalculoPedidoPerfecto calculo = Recordar($"pedidos|{mes:yyyyMM}", () => calcularPedidos(empresa, mes, mes.AddMonths(1)));
                resultado.AddRange(calculo.Pedidos.Where(p => p.Key.Fecha >= desde.Date && p.Key.Fecha < hasta.Date));
            }
            return resultado;
        }

        /// <summary>Los meses naturales (día 1) que tocan [desde, hasta).</summary>
        internal static List<DateTime> MesesDe(DateTime desde, DateTime hasta)
        {
            var meses = new List<DateTime>();
            for (DateTime mes = new DateTime(desde.Year, desde.Month, 1); mes < hasta.Date; mes = mes.AddMonths(1))
            {
                meses.Add(mes);
            }
            return meses;
        }

        private void AnadirPeriodo(SqlCommand comando, DateTime desde, DateTime hasta)
        {
            _ = comando.Parameters.Add(new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa });
            _ = comando.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde.Date });
            _ = comando.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta.Date });
        }

        internal static List<FilaPerdidaDesconocidaDTO> LeerFilasStock(IDataReader r)
        {
            var filas = new List<FilaPerdidaDesconocidaDTO>();
            while (r.Read())
            {
                filas.Add(new FilaPerdidaDesconocidaDTO
                {
                    Almacen = Texto(r, "Almacen"),
                    Mes = Convert.ToInt32(r["Mes"]),
                    Producto = Texto(r, "Producto"),
                    Diario = Texto(r, "Diario"),
                    Altas = Convert.ToInt32(r["Altas"]),
                    Bajas = Convert.ToInt32(r["Bajas"]),
                    Nombre = Texto(r, "Nombre"),
                    Familia = Texto(r, "Familia"),
                    PrecioMedio = r["PrecioMedio"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PrecioMedio"])
                });
            }
            return filas;
        }

        internal static List<DiferenciaReposicionKpi> LeerDiferencias(IDataReader r)
        {
            var filas = new List<DiferenciaReposicionKpi>();
            while (r.Read())
            {
                filas.Add(new DiferenciaReposicionKpi
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Origen = Texto(r, "Origen"),
                    Destino = Texto(r, "Destino"),
                    Producto = Texto(r, "Producto"),
                    Diferencia = Convert.ToInt32(r["Diferencia"]),
                    Estado = Texto(r, "Estado"),
                    Resolucion = Texto(r, "Resolucion"),
                    PrecioMedio = r["PrecioMedio"] is DBNull ? 0m : Convert.ToDecimal(r["PrecioMedio"])
                });
            }
            return filas;
        }

        internal static List<ImputacionReposicionKpi> LeerImputaciones(IDataReader r)
        {
            var filas = new List<ImputacionReposicionKpi>();
            while (r.Read())
            {
                filas.Add(new ImputacionReposicionKpi
                {
                    DiferenciaId = Convert.ToInt32(r["DiferenciaId"]),
                    Almacen = Texto(r, "Almacen"),
                    Unidades = Convert.ToInt32(r["Unidades"])
                });
            }
            return filas;
        }

        private static string Texto(IDataRecord r, string columna)
        {
            object valor = r[columna];
            return valor is DBNull ? null : Convert.ToString(valor)?.Trim();
        }
    }
}
