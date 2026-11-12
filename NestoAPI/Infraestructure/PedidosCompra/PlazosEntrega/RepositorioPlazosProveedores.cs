using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega
{
    /// <summary>NestoAPI#853: un pedido de compra enviado, para dbo.PedidosCompraEnvios (medir quién acierta la fecha).</summary>
    public class EnvioPedidoCompra
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public DateTime FechaEnvio { get; set; }
        /// <summary>La FechaRecepción que se ha puesto en las líneas.</summary>
        public DateTime FechaRecepcionPuesta { get; set; }
        /// <summary>Hoy + P80 (aunque no se haya usado).</summary>
        public DateTime? FechaRegla { get; set; }
        /// <summary>Reservada para el modelo ML.NET de un corte posterior: hoy siempre null.</summary>
        public DateTime? FechaModelo { get; set; }
        public int DiasEnServir { get; set; }
        /// <summary><see cref="FechaRecepcionEstimada"/>.ORIGEN_* de la fecha puesta (o «Interruptor apagado»).</summary>
        public string Origen { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#853: acceso a datos de los plazos de los proveedores (sustituible en los tests).</summary>
    public interface IRepositorioPlazosProveedores
    {
        bool TablaPlazosExiste();
        /// <summary>Las filas de dbo.ProveedoresPlazosEntrega de la empresa. Vacía sin tabla.</summary>
        List<PlazoProveedor> LeerPlazos(string empresa);
        /// <summary>Recepciones (pedido + día de albarán) de líneas de producto desde <paramref name="desde"/>, de la empresa y su espejo.</summary>
        List<RecepcionCompra> LeerRecepciones(string empresa, DateTime desde);
        /// <summary>Pedidos de compra con líneas de producto desde <paramref name="desde"/> (día en que se pidió), de la empresa y su espejo.</summary>
        List<PedidoCompraDia> LeerPedidos(string empresa, DateTime desde);
        /// <summary>Sustituye las filas de la empresa en una transacción.</summary>
        void SustituirPlazos(string empresa, IReadOnlyCollection<PlazoProveedor> plazos);
        /// <summary>Producto → proveedor habitual (ProveedoresProducto con menor Orden; si no tiene, el de su última compra).</summary>
        Dictionary<string, string> ProveedorHabitual(string empresa, IEnumerable<string> productos);
        bool TablaEnviosExiste();
        /// <summary>Solo la primera vez de cada (Empresa, Pedido): reenviar no duplica ni pisa.</summary>
        void RegistrarEnvio(EnvioPedidoCompra envio);
    }

    /// <summary>
    /// NestoAPI#853: <see cref="IRepositorioPlazosProveedores"/> con ADO.NET (las tablas están fuera del EDMX). Conexiones con
    /// <c>Enlist=false</c> (<see cref="RepositorioSombraModoServicioSql.SinAlistar"/>): se lee desde el cálculo de la fecha de entrega, que
    /// puede ir dentro de una transacción ajena.
    /// </summary>
    public class RepositorioPlazosProveedores : IRepositorioPlazosProveedores
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-PlazosProveedores";
        private const int TIMEOUT_LECTURA = 15;
        private const int TIMEOUT_CALCULO = 120;

        internal const string SQL_EXISTE_PLAZOS = "SELECT CASE WHEN OBJECT_ID('dbo.ProveedoresPlazosEntrega', 'U') IS NULL THEN 0 ELSE 1 END";
        internal const string SQL_EXISTE_ENVIOS = "SELECT CASE WHEN OBJECT_ID('dbo.PedidosCompraEnvios', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_LEER_PLAZOS = @"
SELECT RTRIM(Empresa), RTRIM(Proveedor), Recepciones, DiasMediana, DiasP80, IntervaloPedidosMediano, CAST(UltimoPedido AS datetime), FechaCalculo
FROM dbo.ProveedoresPlazosEntrega WITH (NOLOCK)
WHERE Empresa = @empresa";

        /// <summary>
        /// Una fila por pedido de compra y día de albarán (líneas de producto, TipoLínea '1'; las de cuenta contable no cuentan). El día en
        /// que se pidió es CabPedidoCmp.[Fecha Modificación] (estable en las de producto; CabPedidoCmp.Fecha se mueve después).
        /// </summary>
        internal const string SQL_RECEPCIONES = @"
SELECT RTRIM(l.Empresa), RTRIM(c.[NºProveedor]), l.[Número], CAST(CAST(c.[Fecha Modificación] AS date) AS datetime), CAST(CAST(l.[FechaAlbarán] AS date) AS datetime)
FROM LinPedidoCmp l WITH (NOLOCK)
INNER JOIN CabPedidoCmp c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.[Número] = l.[Número]
WHERE l.Empresa IN (@empresa, @espejo) AND l.[TipoLínea] = '1' AND l.[FechaAlbarán] >= @desde AND c.[Fecha Modificación] IS NOT NULL
GROUP BY l.Empresa, c.[NºProveedor], l.[Número], CAST(c.[Fecha Modificación] AS date), CAST(l.[FechaAlbarán] AS date)";

        internal const string SQL_PEDIDOS = @"
SELECT RTRIM(c.[NºProveedor]), CAST(CAST(c.[Fecha Modificación] AS date) AS datetime)
FROM CabPedidoCmp c WITH (NOLOCK)
WHERE c.Empresa IN (@empresa, @espejo) AND c.[Fecha Modificación] >= @desde
  AND EXISTS (SELECT 1 FROM LinPedidoCmp l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.[TipoLínea] = '1')";

        internal const string SQL_BORRAR_PLAZOS = "DELETE FROM dbo.ProveedoresPlazosEntrega WHERE Empresa = @empresa";

        internal const string SQL_INSERTAR_PLAZO = @"
INSERT INTO dbo.ProveedoresPlazosEntrega (Empresa, Proveedor, Recepciones, DiasMediana, DiasP80, IntervaloPedidosMediano, UltimoPedido, FechaCalculo)
VALUES (@empresa, @proveedor, @recepciones, @mediana, @p80, @intervalo, @ultimo, @fechaCalculo)";

        /// <summary>
        /// El proveedor habitual de cada producto: el de ProveedoresProducto con menor Orden (como la ficha de control de stock y el motor de
        /// tendencias); si el producto no tiene ninguno, el del último pedido de compra en que aparece.
        /// </summary>
        internal const string SQL_PROVEEDOR_HABITUAL = @"
SELECT RTRIM(p.[Número]), COALESCE(
    (SELECT TOP 1 RTRIM(pp.[Nº Proveedor]) FROM ProveedoresProducto pp WITH (NOLOCK)
     WHERE pp.Empresa = p.Empresa AND pp.[Nº Producto] = p.[Número] ORDER BY pp.Orden),
    (SELECT TOP 1 RTRIM(l.[NºProveedor]) FROM LinPedidoCmp l WITH (NOLOCK)
     WHERE l.Empresa IN (@empresa, @espejo) AND l.Producto = p.[Número] AND l.[TipoLínea] = '1' ORDER BY l.[Número] DESC))
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.[Número] IN ({0})";

        internal const string SQL_REGISTRAR_ENVIO = @"
IF NOT EXISTS (SELECT 1 FROM dbo.PedidosCompraEnvios WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Pedido = @pedido)
    INSERT INTO dbo.PedidosCompraEnvios (Empresa, Pedido, Proveedor, FechaEnvio, FechaRecepcionPuesta, FechaRegla, FechaModelo, DiasEnServir, Origen, Usuario)
    VALUES (@empresa, @pedido, @proveedor, @fechaEnvio, @puesta, @regla, @modelo, @diasEnServir, @origen, @usuario)";

        private readonly string cadenaConexion;

        public RepositorioPlazosProveedores(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static RepositorioPlazosProveedores DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioPlazosProveedores(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaPlazosExiste() => Existe(SQL_EXISTE_PLAZOS);

        public bool TablaEnviosExiste() => Existe(SQL_EXISTE_ENVIOS);

        public List<PlazoProveedor> LeerPlazos(string empresa)
        {
            var filas = new List<PlazoProveedor>();
            if (!TablaPlazosExiste())
            {
                return filas;
            }
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_LEER_PLAZOS, TIMEOUT_LECTURA))
            {
                Empresas(comando, empresa);
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new PlazoProveedor
                        {
                            Empresa = Cadena(lector, 0),
                            Proveedor = Cadena(lector, 1),
                            Recepciones = lector.GetInt32(2),
                            DiasMediana = Entero(lector, 3),
                            DiasP80 = Entero(lector, 4),
                            IntervaloPedidosMediano = Entero(lector, 5),
                            UltimoPedido = lector.IsDBNull(6) ? (DateTime?)null : lector.GetDateTime(6),
                            FechaCalculo = lector.GetDateTime(7)
                        });
                    }
                }
            }
            return filas;
        }

        public List<RecepcionCompra> LeerRecepciones(string empresa, DateTime desde)
        {
            var filas = new List<RecepcionCompra>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_RECEPCIONES, TIMEOUT_CALCULO))
            {
                Empresas(comando, empresa);
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new RecepcionCompra
                        {
                            Empresa = Cadena(lector, 0),
                            Proveedor = Cadena(lector, 1),
                            Pedido = lector.GetInt32(2),
                            FechaPedido = lector.GetDateTime(3),
                            FechaAlbaran = lector.GetDateTime(4)
                        });
                    }
                }
            }
            return filas;
        }

        public List<PedidoCompraDia> LeerPedidos(string empresa, DateTime desde)
        {
            var filas = new List<PedidoCompraDia>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_PEDIDOS, TIMEOUT_CALCULO))
            {
                Empresas(comando, empresa);
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new PedidoCompraDia { Proveedor = Cadena(lector, 0), Fecha = lector.GetDateTime(1) });
                    }
                }
            }
            return filas;
        }

        public void SustituirPlazos(string empresa, IReadOnlyCollection<PlazoProveedor> plazos)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                using (SqlCommand borrar = Comando(conexion, SQL_BORRAR_PLAZOS, TIMEOUT_CALCULO))
                {
                    borrar.Transaction = transaccion;
                    borrar.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                    _ = borrar.ExecuteNonQuery();
                }
                foreach (PlazoProveedor plazo in plazos ?? new List<PlazoProveedor>())
                {
                    using (SqlCommand insertar = Comando(conexion, SQL_INSERTAR_PLAZO, TIMEOUT_CALCULO))
                    {
                        insertar.Transaction = transaccion;
                        insertar.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa;
                        insertar.Parameters.Add("@proveedor", SqlDbType.VarChar, 10).Value = Recortar(plazo.Proveedor, 10);
                        insertar.Parameters.Add("@recepciones", SqlDbType.Int).Value = plazo.Recepciones;
                        insertar.Parameters.Add("@mediana", SqlDbType.Int).Value = (object)plazo.DiasMediana ?? DBNull.Value;
                        insertar.Parameters.Add("@p80", SqlDbType.Int).Value = (object)plazo.DiasP80 ?? DBNull.Value;
                        insertar.Parameters.Add("@intervalo", SqlDbType.Int).Value = (object)plazo.IntervaloPedidosMediano ?? DBNull.Value;
                        insertar.Parameters.Add("@ultimo", SqlDbType.Date).Value = (object)plazo.UltimoPedido?.Date ?? DBNull.Value;
                        insertar.Parameters.Add("@fechaCalculo", SqlDbType.DateTime).Value = plazo.FechaCalculo;
                        _ = insertar.ExecuteNonQuery();
                    }
                }
                transaccion.Commit();
            }
        }

        public Dictionary<string, string> ProveedorHabitual(string empresa, IEnumerable<string> productos)
        {
            var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            string sql = string.Format(SQL_PROVEEDOR_HABITUAL, string.Join(", ", lista.Select((p, i) => "@p" + i)));
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, sql, TIMEOUT_LECTURA))
            {
                Empresas(comando, empresa);
                for (int i = 0; i < lista.Count; i++)
                {
                    comando.Parameters.Add("@p" + i, SqlDbType.VarChar, 15).Value = lista[i];
                }
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        string producto = Cadena(lector, 0);
                        string proveedor = Cadena(lector, 1);
                        if (producto != null && !string.IsNullOrWhiteSpace(proveedor))
                        {
                            resultado[producto] = proveedor;
                        }
                    }
                }
            }
            return resultado;
        }

        public void RegistrarEnvio(EnvioPedidoCompra envio)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_REGISTRAR_ENVIO, TIMEOUT_LECTURA))
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = envio.Empresa?.Trim();
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = envio.Pedido;
                comando.Parameters.Add("@proveedor", SqlDbType.VarChar, 10).Value = (object)Recortar(envio.Proveedor, 10) ?? DBNull.Value;
                comando.Parameters.Add("@fechaEnvio", SqlDbType.DateTime).Value = envio.FechaEnvio;
                comando.Parameters.Add("@puesta", SqlDbType.Date).Value = envio.FechaRecepcionPuesta.Date;
                comando.Parameters.Add("@regla", SqlDbType.Date).Value = (object)envio.FechaRegla?.Date ?? DBNull.Value;
                comando.Parameters.Add("@modelo", SqlDbType.Date).Value = (object)envio.FechaModelo?.Date ?? DBNull.Value;
                comando.Parameters.Add("@diasEnServir", SqlDbType.Int).Value = envio.DiasEnServir;
                comando.Parameters.Add("@origen", SqlDbType.VarChar, 20).Value = (object)Recortar(envio.Origen, 20) ?? DBNull.Value;
                comando.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value = (object)Recortar(envio.Usuario, 50) ?? DBNull.Value;
                _ = comando.ExecuteNonQuery();
            }
        }

        private bool Existe(string sql)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, sql, TIMEOUT_LECTURA))
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        private static void Empresas(SqlCommand comando, string empresa)
        {
            comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql, int timeout) => new SqlCommand(sql, conexion) { CommandTimeout = timeout };

        private static string Cadena(SqlDataReader lector, int i) => lector.IsDBNull(i) ? null : Convert.ToString(lector.GetValue(i))?.Trim();

        private static int? Entero(SqlDataReader lector, int i) => lector.IsDBNull(i) ? (int?)null : Convert.ToInt32(lector.GetValue(i));

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
