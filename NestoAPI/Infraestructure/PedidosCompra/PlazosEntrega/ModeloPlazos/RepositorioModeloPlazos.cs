using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos
{
    /// <summary>NestoAPI#853: una fila de dbo.PlazosProveedoresModeloEntrenamientos (métricas de VALIDACIÓN de modelo y regla).</summary>
    public class EntrenamientoModeloPlazos
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Empresa { get; set; }
        public int Ejemplos { get; set; }
        public int EjemplosEntrenamiento { get; set; }
        public int EjemplosValidacion { get; set; }
        public DateTime? ValidacionDesde { get; set; }
        /// <summary>Pedidos de validación con regla calculable (sobre ellos se miden los dos).</summary>
        public int PedidosComparados { get; set; }
        public double? ErrorMedioModelo { get; set; }
        public double? ErrorMedioRegla { get; set; }
        public double? PorcentajeCortoModelo { get; set; }
        public double? PorcentajeCortoRegla { get; set; }
        public double? DiasDeMasModelo { get; set; }
        public double? DiasDeMasRegla { get; set; }
        public double? MargenLog { get; set; }
        public string Version { get; set; }
        public string Algoritmo { get; set; }
        /// <summary>Su modelo es el que está en uso (solo una fila a la vez).</summary>
        public bool ModeloActivo { get; set; }
        public string Motivo { get; set; }
        public string Origen { get; set; }
        public int? Segundos { get; set; }

        internal MetricasPlazos MetricasModelo() => ErrorMedioModelo.HasValue && PorcentajeCortoModelo.HasValue
            ? new MetricasPlazos { Pedidos = PedidosComparados, ErrorMedio = ErrorMedioModelo.Value, PorcentajeCorto = PorcentajeCortoModelo.Value, DiasDeMasMedio = DiasDeMasModelo }
            : null;
    }

    public interface IRepositorioModeloPlazos
    {
        bool TablaEntrenamientosExiste();
        bool TablaEnviosExiste();
        /// <summary>Pedidos de compra con líneas de producto enviados desde <paramref name="desde"/> (de la empresa y su espejo). SOLO de noche.</summary>
        List<PedidoCompraHistorico> LeerHistorico(string empresa, DateTime desde);
        /// <summary>Si <see cref="EntrenamientoModeloPlazos.ModeloActivo"/>, deja las demás filas a 0 en la misma transacción.</summary>
        void RegistrarEntrenamiento(EntrenamientoModeloPlazos fila);
        EntrenamientoModeloPlazos UltimoEntrenamiento();
        EntrenamientoModeloPlazos UltimoActivo();
        /// <summary>Envíos desde <paramref name="desde"/> con FechaRegla y FechaModelo y ya recibidos (vPedidosCompraEnviosError).</summary>
        List<EnvioRecibidoPlazos> LeerEnviosRecibidos(DateTime desde);
    }

    /// <summary>NestoAPI#853: <see cref="IRepositorioModeloPlazos"/> con ADO.NET (todo fuera del EDMX).</summary>
    public class RepositorioModeloPlazos : IRepositorioModeloPlazos
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-ModeloPlazos";
        private const int TIMEOUT_LECTURA = 30;
        /// <summary>La lectura del histórico es de noche y agregada: margen holgado.</summary>
        private const int TIMEOUT_HISTORICO = 600;

        internal const string SQL_EXISTE_ENTRENAMIENTOS = "SELECT CASE WHEN OBJECT_ID('dbo.PlazosProveedoresModeloEntrenamientos', 'U') IS NULL THEN 0 ELSE 1 END";
        internal const string SQL_EXISTE_ENVIOS = "SELECT CASE WHEN OBJECT_ID('dbo.vPedidosCompraEnviosError', 'V') IS NULL THEN 0 ELSE 1 END";

        /// <summary>
        /// Una fila por pedido de compra y día de albarán de sus líneas de producto (NULL = líneas sin llegar), con los totales del pedido
        /// (líneas e importe de producto) repetidos por ventana. Día del envío = CabPedidoCmp.[Fecha Modificación], como la regla.
        /// </summary>
        internal const string SQL_HISTORICO = @"
SELECT RTRIM(c.Empresa), c.[Número], RTRIM(c.[NºProveedor]), CAST(CAST(c.[Fecha Modificación] AS date) AS datetime), c.[DíasEnServir],
       SUM(COUNT(*)) OVER (PARTITION BY c.Empresa, c.[Número]) AS Lineas,
       SUM(SUM(l.BaseImponible)) OVER (PARTITION BY c.Empresa, c.[Número]) AS Importe,
       CAST(CAST(l.[FechaAlbarán] AS date) AS datetime) AS DiaAlbaran
FROM CabPedidoCmp c WITH (NOLOCK)
INNER JOIN LinPedidoCmp l WITH (NOLOCK) ON l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.[TipoLínea] = '1'
WHERE c.Empresa IN (@empresa, @espejo) AND c.[Fecha Modificación] >= @desde
GROUP BY c.Empresa, c.[Número], c.[NºProveedor], CAST(c.[Fecha Modificación] AS date), c.[DíasEnServir], CAST(l.[FechaAlbarán] AS date)";

        internal const string SQL_INSERTAR = @"
IF @activo = 1
    UPDATE dbo.PlazosProveedoresModeloEntrenamientos SET ModeloActivo = 0 WHERE ModeloActivo = 1;
INSERT INTO dbo.PlazosProveedoresModeloEntrenamientos (Fecha, Empresa, Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, ValidacionDesde,
    PedidosComparados, ErrorMedioModelo, ErrorMedioRegla, PorcentajeCortoModelo, PorcentajeCortoRegla, DiasDeMasModelo, DiasDeMasRegla,
    MargenLog, Version, Algoritmo, ModeloActivo, Motivo, Origen, Segundos)
VALUES (@fecha, @empresa, @ejemplos, @entrenamiento, @validacion, @validacionDesde, @comparados, @errorModelo, @errorRegla, @cortoModelo, @cortoRegla,
    @deMasModelo, @deMasRegla, @margen, @version, @algoritmo, @activo, @motivo, @origen, @segundos);";

        internal const string SQL_COLUMNAS = @"Id, Fecha, RTRIM(Empresa), Ejemplos, EjemplosEntrenamiento, EjemplosValidacion, CAST(ValidacionDesde AS datetime), PedidosComparados,
    ErrorMedioModelo, ErrorMedioRegla, PorcentajeCortoModelo, PorcentajeCortoRegla, DiasDeMasModelo, DiasDeMasRegla, MargenLog, Version, Algoritmo,
    ModeloActivo, Motivo, Origen, Segundos";

        internal const string SQL_ULTIMO = "SELECT TOP 1 " + SQL_COLUMNAS + " FROM dbo.PlazosProveedoresModeloEntrenamientos WITH (NOLOCK) ORDER BY Id DESC";
        internal const string SQL_ULTIMO_ACTIVO = "SELECT TOP 1 " + SQL_COLUMNAS + " FROM dbo.PlazosProveedoresModeloEntrenamientos WITH (NOLOCK) WHERE ModeloActivo = 1 ORDER BY Id DESC";

        internal const string SQL_ENVIOS_RECIBIDOS = @"
SELECT RTRIM(Empresa), Pedido, FechaEnvio, CAST(FechaRegla AS datetime), CAST(FechaModelo AS datetime), CAST(PrimerAlbaran AS datetime)
FROM dbo.vPedidosCompraEnviosError WITH (NOLOCK)
WHERE FechaEnvio >= @desde AND FechaRegla IS NOT NULL AND FechaModelo IS NOT NULL AND PrimerAlbaran IS NOT NULL";

        private readonly string cadenaConexion;

        public RepositorioModeloPlazos(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = RepositorioSombraModoServicioSql.SinAlistar(cadenaConexion);
        }

        public static RepositorioModeloPlazos DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioModeloPlazos(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaEntrenamientosExiste() => Existe(SQL_EXISTE_ENTRENAMIENTOS);

        public bool TablaEnviosExiste() => Existe(SQL_EXISTE_ENVIOS);

        public List<PedidoCompraHistorico> LeerHistorico(string empresa, DateTime desde)
        {
            var porPedido = new Dictionary<string, PedidoCompraHistorico>(StringComparer.OrdinalIgnoreCase);
            var lista = new List<PedidoCompraHistorico>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_HISTORICO, conexion) { CommandTimeout = TIMEOUT_HISTORICO })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        string emp = Cadena(lector, 0);
                        int numero = Convert.ToInt32(lector.GetValue(1));
                        string clave = emp + "|" + numero;
                        if (!porPedido.TryGetValue(clave, out PedidoCompraHistorico pedido))
                        {
                            pedido = new PedidoCompraHistorico
                            {
                                Empresa = emp,
                                Pedido = numero,
                                Proveedor = Cadena(lector, 2),
                                FechaEnvio = lector.GetDateTime(3),
                                DiasEnServir = lector.IsDBNull(4) ? 0 : Convert.ToInt32(lector.GetValue(4)),
                                Lineas = lector.IsDBNull(5) ? 0 : Convert.ToInt32(lector.GetValue(5)),
                                Importe = lector.IsDBNull(6) ? 0 : Convert.ToDouble(lector.GetValue(6))
                            };
                            porPedido[clave] = pedido;
                            lista.Add(pedido);
                        }
                        if (!lector.IsDBNull(7))
                        {
                            pedido.Albaranes.Add(lector.GetDateTime(7).Date);
                        }
                    }
                }
            }
            return lista;
        }

        public void RegistrarEntrenamiento(EntrenamientoModeloPlazos f)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            using (var comando = new SqlCommand(SQL_INSERTAR, conexion, transaccion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@fecha", SqlDbType.DateTime).Value = f.Fecha;
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = (object)f.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                comando.Parameters.Add("@ejemplos", SqlDbType.Int).Value = f.Ejemplos;
                comando.Parameters.Add("@entrenamiento", SqlDbType.Int).Value = f.EjemplosEntrenamiento;
                comando.Parameters.Add("@validacion", SqlDbType.Int).Value = f.EjemplosValidacion;
                comando.Parameters.Add("@validacionDesde", SqlDbType.Date).Value = (object)f.ValidacionDesde?.Date ?? DBNull.Value;
                comando.Parameters.Add("@comparados", SqlDbType.Int).Value = f.PedidosComparados;
                ParametroDecimal(comando, "@errorModelo", f.ErrorMedioModelo);
                ParametroDecimal(comando, "@errorRegla", f.ErrorMedioRegla);
                ParametroDecimal(comando, "@cortoModelo", f.PorcentajeCortoModelo);
                ParametroDecimal(comando, "@cortoRegla", f.PorcentajeCortoRegla);
                ParametroDecimal(comando, "@deMasModelo", f.DiasDeMasModelo);
                ParametroDecimal(comando, "@deMasRegla", f.DiasDeMasRegla);
                comando.Parameters.Add("@margen", SqlDbType.Float).Value = (object)f.MargenLog ?? DBNull.Value;
                comando.Parameters.Add("@version", SqlDbType.VarChar, 20).Value = (object)Recortar(f.Version, 20) ?? DBNull.Value;
                comando.Parameters.Add("@algoritmo", SqlDbType.VarChar, 100).Value = (object)Recortar(f.Algoritmo, 100) ?? DBNull.Value;
                comando.Parameters.Add("@activo", SqlDbType.Bit).Value = f.ModeloActivo;
                comando.Parameters.Add("@motivo", SqlDbType.NVarChar, 500).Value = (object)Recortar(f.Motivo, 500) ?? DBNull.Value;
                comando.Parameters.Add("@origen", SqlDbType.NVarChar, 100).Value = (object)Recortar(f.Origen, 100) ?? DBNull.Value;
                comando.Parameters.Add("@segundos", SqlDbType.Int).Value = (object)f.Segundos ?? DBNull.Value;
                _ = comando.ExecuteNonQuery();
                transaccion.Commit();
            }
        }

        public EntrenamientoModeloPlazos UltimoEntrenamiento() => LeerUno(SQL_ULTIMO);

        public EntrenamientoModeloPlazos UltimoActivo() => LeerUno(SQL_ULTIMO_ACTIVO);

        public List<EnvioRecibidoPlazos> LeerEnviosRecibidos(DateTime desde)
        {
            var filas = new List<EnvioRecibidoPlazos>();
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(SQL_ENVIOS_RECIBIDOS, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new EnvioRecibidoPlazos
                        {
                            Empresa = Cadena(lector, 0),
                            Pedido = Convert.ToInt32(lector.GetValue(1)),
                            FechaEnvio = lector.GetDateTime(2),
                            FechaRegla = lector.GetDateTime(3),
                            FechaModelo = lector.GetDateTime(4),
                            PrimerAlbaran = lector.GetDateTime(5)
                        });
                    }
                }
            }
            return filas;
        }

        private EntrenamientoModeloPlazos LeerUno(string sql)
        {
            if (!TablaEntrenamientosExiste())
            {
                return null;
            }
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                if (!lector.Read())
                {
                    return null;
                }
                return new EntrenamientoModeloPlazos
                {
                    Id = lector.GetInt32(0),
                    Fecha = lector.GetDateTime(1),
                    Empresa = Cadena(lector, 2),
                    Ejemplos = lector.GetInt32(3),
                    EjemplosEntrenamiento = lector.GetInt32(4),
                    EjemplosValidacion = lector.GetInt32(5),
                    ValidacionDesde = lector.IsDBNull(6) ? (DateTime?)null : lector.GetDateTime(6),
                    PedidosComparados = lector.GetInt32(7),
                    ErrorMedioModelo = Doble(lector, 8),
                    ErrorMedioRegla = Doble(lector, 9),
                    PorcentajeCortoModelo = Doble(lector, 10),
                    PorcentajeCortoRegla = Doble(lector, 11),
                    DiasDeMasModelo = Doble(lector, 12),
                    DiasDeMasRegla = Doble(lector, 13),
                    MargenLog = Doble(lector, 14),
                    Version = Cadena(lector, 15),
                    Algoritmo = Cadena(lector, 16),
                    ModeloActivo = lector.GetBoolean(17),
                    Motivo = Cadena(lector, 18),
                    Origen = Cadena(lector, 19),
                    Segundos = lector.IsDBNull(20) ? (int?)null : lector.GetInt32(20)
                };
            }
        }

        private bool Existe(string sql)
        {
            using (SqlConnection conexion = Abrir())
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_LECTURA })
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static void ParametroDecimal(SqlCommand comando, string nombre, double? valor)
        {
            SqlParameter p = comando.Parameters.Add(nombre, SqlDbType.Decimal);
            p.Precision = 9;
            p.Scale = 2;
            p.Value = valor.HasValue ? (object)Math.Round((decimal)valor.Value, 2) : DBNull.Value;
        }

        private static string Cadena(SqlDataReader lector, int i) => lector.IsDBNull(i) ? null : Convert.ToString(lector.GetValue(i))?.Trim();

        private static double? Doble(SqlDataReader lector, int i) => lector.IsDBNull(i) ? (double?)null : Convert.ToDouble(lector.GetValue(i));

        private static string Recortar(string valor, int longitud)
        {
            string limpio = valor?.Trim();
            return limpio == null || limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
