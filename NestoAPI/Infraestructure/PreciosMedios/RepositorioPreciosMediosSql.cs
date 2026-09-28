using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// <see cref="IRepositorioPreciosMedios"/> con ADO.NET (Issue #547, corte b). Sin EF a propósito: consultas
    /// escritas a mano para que vayan por índice, sin tocar el EDMX para la tabla nueva.
    ///
    /// CARGA: todas las lecturas llevan <c>WITH (NOLOCK)</c> (no bloquean a nadie; la sombra tolera leer algo a
    /// medio escribir, el domingo siguiente lo vuelve a mirar) y van por producto:
    /// - LinPedidoCmp por el índice (Producto, Empresa, Enviado, Estado);
    /// - ExtractoProducto por el agrupado (Número), agregado por fecha en el servidor;
    /// - LinPedidoVta (solo el muestreo) por el índice (Producto, Estado, Empresa, Almacén).
    /// Los parámetros de texto van como <c>VarChar</c>: las columnas son <c>char</c> y un <c>NVarChar</c> obligaría
    /// a convertir la columna y podría perder la búsqueda por índice.
    ///
    /// Lo ÚNICO que escribe es <c>PreciosMediosSombra</c>. Nunca Productos, LinPedidoCmp ni LinPedidoVta.
    /// </summary>
    public class RepositorioPreciosMediosSql : IRepositorioPreciosMedios
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-PreciosMediosSombra";
        private const int TIMEOUT_SEGUNDOS = 120;

        internal const string SQL_ESPEJO = @"
SELECT RTRIM([IVA por defecto]) FROM Empresas WITH (NOLOCK) WHERE Número = @empresa";

        // Un recorrido de LinPedidoCmp (~540.000 filas) una vez por empresa y pasada: aceptable de madrugada.
        internal const string SQL_PRODUCTOS = @"
SELECT DISTINCT RTRIM(l.Producto)
FROM LinPedidoCmp l WITH (NOLOCK)
WHERE l.Empresa IN (@empresa, @espejo) AND l.TipoLínea = '1' AND l.Producto IS NOT NULL
  AND EXISTS (SELECT 1 FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.Número = l.Producto)
ORDER BY 1";

        internal const string SQL_FICHA = @"
SELECT p.PrecioMedio, p.Ficticio FROM Productos p WITH (NOLOCK) WHERE p.Empresa = @empresa AND p.Número = @producto";

        internal const string SQL_COMPRAS = @"
SELECT RTRIM(l.Empresa) AS Empresa, TRY_CAST(l.NºFactura AS int) AS NumeroFactura, l.FechaAlbarán AS FechaAlbaran,
       l.NºAlbarán AS NumeroAlbaran, l.NºOrden AS NumeroOrden, CAST(l.Cantidad AS int) AS Cantidad,
       l.BaseImponible, l.Coste, l.Estado, c.[Fecha Modificación] AS FechaModificacionFactura
FROM LinPedidoCmp l WITH (NOLOCK)
LEFT JOIN CabFacturaCmp c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.Número = l.NºFactura
WHERE l.Producto = @producto AND l.Empresa IN (@empresa, @espejo) AND l.TipoLínea = '1'";

        internal const string SQL_MOVIMIENTOS = @"
SELECT e.Fecha, ISNULL(SUM(CAST(e.Cantidad AS int)), 0) AS Cantidad,
       MAX(CASE WHEN e.Diario = '_MontarKit' AND e.Texto LIKE 'Montaje %' AND e.Cantidad > 0 THEN 1 ELSE 0 END) AS EsMontaje,
       MAX(CASE WHEN e.NºProveedor IS NOT NULL AND e.Cantidad > 0 THEN 1 ELSE 0 END) AS EsRecepcion
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Número = @producto AND e.Empresa IN (@empresa, @espejo) AND e.Fecha IS NOT NULL
GROUP BY e.Fecha";

        internal const string SQL_VENTAS = @"
SELECT RTRIM(v.Empresa) AS Empresa, v.Número AS Numero, v.[Nº Orden] AS NumeroOrden, v.Estado,
       v.[Fecha Albarán] AS FechaAlbaran, v.Coste, v.[Fecha Modificación] AS FechaModificacion
FROM LinPedidoVta v WITH (NOLOCK)
WHERE v.Producto = @producto AND v.Estado IN (-1, 1) AND v.Empresa IN (@empresa, @espejo)
UNION ALL
SELECT RTRIM(v.Empresa), v.Número, v.[Nº Orden], v.Estado, v.[Fecha Albarán], v.Coste, v.[Fecha Modificación]
FROM LinPedidoVta v WITH (NOLOCK)
WHERE v.Producto = @producto AND v.Estado >= 2 AND v.Empresa IN (@empresa, @espejo) AND v.[Fecha Albarán] IS NOT NULL";

        // Nombre del job de msdb que lanza prdActualizarPreciosMediosDeTodosLosProductos (plan §0).
        internal const string SQL_ULTIMA_EJECUCION_SP = @"
SELECT TOP 1 a.start_execution_date, a.stop_execution_date
FROM msdb.dbo.sysjobactivity a
JOIN msdb.dbo.sysjobs j ON j.job_id = a.job_id
WHERE j.name = 'Precios Medios' AND a.start_execution_date IS NOT NULL
ORDER BY a.start_execution_date DESC";

        internal const string SQL_EXISTE_TABLA = @"SELECT CASE WHEN OBJECT_ID('dbo.PreciosMediosSombra', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_BORRAR_PASADA = @"
DELETE FROM dbo.PreciosMediosSombra WHERE FechaPasada = @fechaPasada AND Empresa = @empresa";

        internal const string SQL_GUARDAR_FILA = @"
DELETE FROM dbo.PreciosMediosSombra WHERE FechaPasada = @fechaPasada AND Empresa = @empresa AND Producto = @producto;
INSERT INTO dbo.PreciosMediosSombra (FechaPasada, Empresa, Producto, EsResumen, Clasificacion, PrecioMedioBD, PrecioMedioCalculado,
    LineasComparadas, LineasDistintas, PrimeraLineaDistinta, LineasPendientes, VentasComparadas, VentasDistintas, Avisos, Detalle)
VALUES (@fechaPasada, @empresa, @producto, @esResumen, @clasificacion, @precioMedioBD, @precioMedioCalculado,
    @lineasComparadas, @lineasDistintas, @primeraLineaDistinta, @lineasPendientes, @ventasComparadas, @ventasDistintas, @avisos, @detalle);";

        private readonly string cadenaConexion;

        public RepositorioPreciosMediosSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        /// <summary>
        /// La cadena de <c>NestoConnection</c> (la de Hangfire: misma BD NV) con nombre de aplicación propio, para
        /// reconocer estas lecturas en el monitor de actividad.
        /// </summary>
        public static RepositorioPreciosMediosSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            SqlConnectionStringBuilder constructor = new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = NOMBRE_APLICACION
            };
            return new RepositorioPreciosMediosSql(constructor.ConnectionString);
        }

        public string EmpresaEspejo(string empresa)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_ESPEJO))
            {
                Texto(comando, "@empresa", empresa);
                object valor = comando.ExecuteScalar();
                string espejo = valor == null || valor == DBNull.Value ? null : ((string)valor).Trim();
                return string.IsNullOrEmpty(espejo) ? null : espejo;
            }
        }

        public IReadOnlyList<string> ProductosConCompras(string empresa, string empresaEspejo)
        {
            List<string> productos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_PRODUCTOS))
            {
                Texto(comando, "@empresa", empresa);
                Texto(comando, "@espejo", empresaEspejo);
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        productos.Add(lector.GetString(0));
                    }
                }
            }
            return productos;
        }

        public DatosProductoPrecioMedio LeerDatos(string empresa, string empresaEspejo, string producto, bool conVentas)
        {
            DatosProductoPrecioMedio datos = new DatosProductoPrecioMedio
            {
                Empresa = empresa,
                EmpresaEspejo = empresaEspejo,
                Producto = producto?.Trim()
            };
            using (SqlConnection conexion = Abrir())
            {
                using (SqlCommand comando = ComandoProducto(conexion, SQL_FICHA, empresa, empresaEspejo, producto))
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    if (lector.Read())
                    {
                        datos.Ficha = new FichaProductoPrecioMedio
                        {
                            PrecioMedio = Nulable<decimal>(lector, 0),
                            Ficticio = Nulable<bool>(lector, 1)
                        };
                    }
                }

                using (SqlCommand comando = ComandoProducto(conexion, SQL_COMPRAS, empresa, empresaEspejo, producto))
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        datos.Compras.Add(new LineaCompraBDPrecioMedio
                        {
                            Empresa = lector.IsDBNull(0) ? null : lector.GetString(0),
                            NumeroFactura = Nulable<int>(lector, 1),
                            FechaAlbaran = Nulable<DateTime>(lector, 2),
                            NumeroAlbaran = Nulable<int>(lector, 3),
                            NumeroOrden = lector.GetInt32(4),
                            Cantidad = Nulable<int>(lector, 5),
                            BaseImponible = Nulable<decimal>(lector, 6),
                            Coste = Nulable<decimal>(lector, 7),
                            Estado = Nulable<short>(lector, 8),
                            FechaModificacionFactura = Nulable<DateTime>(lector, 9)
                        });
                    }
                }

                using (SqlCommand comando = ComandoProducto(conexion, SQL_MOVIMIENTOS, empresa, empresaEspejo, producto))
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        datos.Movimientos.Add(new MovimientoStockPrecioMedio
                        {
                            Fecha = lector.GetDateTime(0),
                            Cantidad = lector.GetInt32(1),
                            EsMontaje = lector.GetInt32(2) == 1,
                            EsRecepcion = lector.GetInt32(3) == 1
                        });
                    }
                }

                if (conVentas)
                {
                    datos.Ventas = new List<LineaVentaPrecioMedio>();
                    using (SqlCommand comando = ComandoProducto(conexion, SQL_VENTAS, empresa, empresaEspejo, producto))
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            datos.Ventas.Add(new LineaVentaPrecioMedio
                            {
                                Empresa = lector.IsDBNull(0) ? null : lector.GetString(0),
                                Numero = lector.GetInt32(1),
                                NumeroOrden = lector.GetInt32(2),
                                Estado = lector.GetInt16(3),
                                FechaAlbaran = Nulable<DateTime>(lector, 4),
                                Coste = Nulable<decimal>(lector, 5),
                                FechaModificacion = Nulable<DateTime>(lector, 6)
                            });
                        }
                    }
                }
            }
            return datos;
        }

        public EjecucionSPPreciosMedios UltimaEjecucionSP()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_ULTIMA_EJECUCION_SP))
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                if (!lector.Read())
                {
                    return null;
                }
                return new EjecucionSPPreciosMedios
                {
                    Inicio = lector.GetDateTime(0),
                    Fin = Nulable<DateTime>(lector, 1)
                };
            }
        }

        public bool ExisteTablaSombra()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE_TABLA))
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        public void BorrarPasada(DateTime fechaPasada, string empresa)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_BORRAR_PASADA))
            {
                comando.Parameters.Add("@fechaPasada", SqlDbType.Date).Value = fechaPasada.Date;
                Texto(comando, "@empresa", empresa);
                comando.ExecuteNonQuery();
            }
        }

        public void GuardarFila(FilaPreciosMediosSombra fila)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_GUARDAR_FILA))
            {
                comando.Parameters.Add("@fechaPasada", SqlDbType.Date).Value = fila.FechaPasada.Date;
                Texto(comando, "@empresa", fila.Empresa);
                Texto(comando, "@producto", fila.Producto);
                comando.Parameters.Add("@esResumen", SqlDbType.Bit).Value = fila.EsResumen;
                Texto(comando, "@clasificacion", fila.Clasificacion);
                comando.Parameters.Add("@precioMedioBD", SqlDbType.Money).Value = (object)fila.PrecioMedioBD ?? DBNull.Value;
                comando.Parameters.Add("@precioMedioCalculado", SqlDbType.Money).Value = (object)fila.PrecioMedioCalculado ?? DBNull.Value;
                comando.Parameters.Add("@lineasComparadas", SqlDbType.Int).Value = fila.LineasComparadas;
                comando.Parameters.Add("@lineasDistintas", SqlDbType.Int).Value = fila.LineasDistintas;
                comando.Parameters.Add("@primeraLineaDistinta", SqlDbType.Int).Value = (object)fila.PrimeraLineaDistinta ?? DBNull.Value;
                comando.Parameters.Add("@lineasPendientes", SqlDbType.Int).Value = fila.LineasPendientes;
                comando.Parameters.Add("@ventasComparadas", SqlDbType.Int).Value = (object)fila.VentasComparadas ?? DBNull.Value;
                comando.Parameters.Add("@ventasDistintas", SqlDbType.Int).Value = (object)fila.VentasDistintas ?? DBNull.Value;
                comando.Parameters.Add("@avisos", SqlDbType.NVarChar, -1).Value = (object)fila.Avisos ?? DBNull.Value;
                comando.Parameters.Add("@detalle", SqlDbType.NVarChar, -1).Value = (object)fila.Detalle ?? DBNull.Value;
                comando.ExecuteNonQuery();
            }
        }

        private SqlConnection Abrir()
        {
            SqlConnection conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql)
        {
            return new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };
        }

        private static SqlCommand ComandoProducto(SqlConnection conexion, string sql, string empresa, string espejo, string producto)
        {
            SqlCommand comando = Comando(conexion, sql);
            Texto(comando, "@empresa", empresa);
            Texto(comando, "@espejo", espejo);
            Texto(comando, "@producto", producto?.Trim());
            return comando;
        }

        private static void Texto(SqlCommand comando, string nombre, string valor)
        {
            comando.Parameters.Add(nombre, SqlDbType.VarChar, 50).Value = (object)valor ?? DBNull.Value;
        }

        private static T? Nulable<T>(SqlDataReader lector, int columna) where T : struct
        {
            return lector.IsDBNull(columna) ? (T?)null : (T)lector.GetValue(columna);
        }
    }
}
