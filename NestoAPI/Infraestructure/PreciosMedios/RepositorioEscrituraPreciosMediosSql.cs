using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// <see cref="IRepositorioEscrituraPreciosMedios"/> con ADO.NET (Issue #547, corte c). Sin EF, como la sombra.
    ///
    /// BLOQUEOS (lo que más importa: LinPedidoVta es la tabla más caliente y tiene trgLinPedidoVtaUpd):
    /// - Una transacción por PRODUCTO, nunca por pasada: lo que se bloquea es un producto un momento.
    /// - <c>sp_getapplock</c> por (empresa, producto): el job nocturno y los encolados al facturar no se pisan.
    /// - <c>SET DEADLOCK_PRIORITY LOW</c>: ante un interbloqueo pierde siempre el incremental (se reintenta), no el
    ///   usuario. <c>SET LOCK_TIMEOUT</c> para no quedarse esperando detrás de nadie: falla y se reintenta luego.
    /// - UPDATE con el predicado «solo lo que cambia» y en lotes de <see cref="SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS"/>
    ///   filas (por debajo del escalado a bloqueo de tabla). Con paridad, casi siempre cambian 0 filas.
    /// - Lecturas del producto dentro de la transacción y SIN NOLOCK: la media no puede salir de datos a medio grabar.
    /// - Nombre de aplicación propio (pool de conexiones propio: los SET de sesión no contaminan a nadie).
    /// </summary>
    public class RepositorioEscrituraPreciosMediosSql : IRepositorioEscrituraPreciosMedios
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-PreciosMediosIncremental";

        private const int TIMEOUT_ESCRITURA_SEGUNDOS = 120;

        /// <summary>
        /// Selecciones nocturnas (de madrugada). ExtractoProducto ya NO se recorre (se busca por Nº Orden, la clave
        /// primaria); lo que sigue yendo por «Fecha Modificación» es LinPedidoCmp/CabFacturaCmp (compras modificadas).
        /// </summary>
        private const int TIMEOUT_SELECCION_SEGUNDOS = 900;

        /// <summary>Esperar un bloqueo como mucho 10 s: mejor fallar y reintentar que hacer cola delante de un usuario.</summary>
        internal const int LOCK_TIMEOUT_MS = 10000;

        /// <summary>Tope de vueltas de un UPDATE en lotes (1.000 × 1.000 filas): red por si algo no converge.</summary>
        internal const int MAXIMO_VUELTAS_LOTE = 1000;

        /// <summary>10000 = <see cref="LOCK_TIMEOUT_MS"/>.</summary>
        internal const string SQL_SESION = "SET LOCK_TIMEOUT 10000; SET DEADLOCK_PRIORITY LOW;";

        internal const string SQL_APPLOCK = @"
DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @espera;
SET @resultado = @r;";

        internal const string SQL_AHORA = "SELECT GETDATE()";

        /// <summary>Motivo recortado (la columna es varchar(200); los parámetros de texto van con tamaño 162).</summary>
        internal const int LONGITUD_MOTIVO_PENDIENTE = 150;

        // Compras modificadas (índice Producto+Enviado+Estado+Fecha Modificación) o de facturas creadas/modificadas
        // desde la última pasada (CabFacturaCmp → LinPedidoCmp por Empresa+NºFactura).
        // Corte (b): una factura DESHECHA (prdDeshacerFacturaCmp, fuera de NestoAPI) borra CabFacturaCmp y deja las líneas
        // en estado 2 sin NºFactura; la ve la primera rama porque el SP pone [Fecha Modificación] en esas líneas
        // (Scripts/Issue547_DeshacerFacturaCmp_FechaModificacion.sql). Por eso esa rama NO filtra por estado ni factura.
        internal const string SQL_PRODUCTOS_COMPRAS_MODIFICADAS = @"
SELECT RTRIM(l.Producto)
FROM LinPedidoCmp l WITH (NOLOCK)
WHERE l.[Fecha Modificación] >= @desde AND l.Empresa IN (@empresa, @espejo) AND l.TipoLínea = '1' AND l.Producto IS NOT NULL
UNION
SELECT RTRIM(l.Producto)
FROM CabFacturaCmp c WITH (NOLOCK)
JOIN LinPedidoCmp l WITH (NOLOCK) ON l.Empresa = c.Empresa AND l.NºFactura = c.Número
WHERE c.[Fecha Modificación] >= @desde AND c.Empresa IN (@empresa, @espejo) AND l.TipoLínea = '1' AND l.Producto IS NOT NULL";

        // Riesgo 2: apuntes grabados desde la última pasada con fecha en o antes de alguna compra FACTURADA del
        // producto (el stock del SP es sum(Cantidad) con Fecha <= FechaAlbarán, así que un apunte con Fecha igual a la
        // de la compra también la cambia). Una venta de hoy no entra: su fecha es posterior a todas las compras.
        // «Grabados desde la última pasada» = [Nº Orden] (identidad creciente) mayor que el último revisado: la clave
        // primaria es (Empresa, Nº Orden), así que es una búsqueda que solo lee las filas nuevas (antes se filtraba por
        // [Fecha Modificación], sin índice: ~4,7 millones de filas cada noche).
        internal const string SQL_PRODUCTOS_MOVIMIENTOS_FECHA_PASADA = @"
SELECT DISTINCT RTRIM(e.Número)
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa IN (@empresa, @espejo) AND e.[Nº Orden] > @desdeNumOrden AND e.[Nº Orden] <= @hastaNumOrden
  AND EXISTS (SELECT 1 FROM LinPedidoCmp l WITH (NOLOCK)
              WHERE l.Producto = e.Número AND l.Empresa IN (@empresa, @espejo) AND l.TipoLínea = '1'
                AND l.Estado = 4 AND l.FechaAlbarán >= e.Fecha)";

        // Una búsqueda por empresa sobre la clave primaria (Empresa, Nº Orden): instantáneo.
        internal const string SQL_MAXIMO_NUM_ORDEN_EXTRACTO = @"
SELECT MAX([Nº Orden]) FROM ExtractoProducto WITH (NOLOCK) WHERE Empresa = @empresa";

        internal const string SQL_PRODUCTOS_PEDIDO = @"
SELECT DISTINCT RTRIM(l.Producto)
FROM LinPedidoCmp l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Número = @pedido AND l.TipoLínea = '1' AND l.Producto IS NOT NULL";

        internal const string SQL_LEER_ULTIMA_PASADA = @"
SELECT Valor FROM ParámetrosUsuario WHERE Empresa = @empresa AND Usuario = @usuario AND Clave = @clave";

        internal const string SQL_GUARDAR_ULTIMA_PASADA = @"
UPDATE ParámetrosUsuario SET Valor = @valor, Usuario2 = @usuario, [Fecha Modificación] = GETDATE()
WHERE Empresa = @empresa AND Usuario = @usuario AND Clave = @clave;
IF @@ROWCOUNT = 0
    INSERT INTO ParámetrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES (@empresa, @clave, @usuario, @valor, @usuario, GETDATE());";

        // Corte (a): productos que quedaron a medias por el tope de filas (Scripts/Issue547_PreciosMediosPendientes.sql).
        internal const string SQL_PRODUCTOS_PENDIENTES = @"
SELECT RTRIM(Producto) FROM PreciosMediosPendientes WHERE Empresa = @empresa";

        internal const string SQL_MARCAR_PENDIENTE = @"
UPDATE PreciosMediosPendientes SET Veces = Veces + 1, Motivo = @motivo, [Fecha Modificación] = GETDATE()
WHERE Empresa = @empresa AND Producto = @producto;
IF @@ROWCOUNT = 0
    INSERT INTO PreciosMediosPendientes (Empresa, Producto, Motivo, Veces, Fecha, [Fecha Modificación])
    VALUES (@empresa, @producto, @motivo, 1, GETDATE(), GETDATE());";

        internal const string SQL_QUITAR_PENDIENTE = @"
DELETE FROM PreciosMediosPendientes WHERE Empresa = @empresa AND Producto = @producto";

        /// <summary>Formato de la marca de la última pasada en ParámetrosUsuario.Valor.</summary>
        internal const string FORMATO_MARCA = "yyyy-MM-ddTHH:mm:ss";

        private readonly string cadenaConexion;

        public RepositorioEscrituraPreciosMediosSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioEscrituraPreciosMediosSql DesdeConfiguracion()
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
            return new RepositorioEscrituraPreciosMediosSql(constructor.ConnectionString);
        }

        public DateTime AhoraServidor()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_AHORA, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                return (DateTime)comando.ExecuteScalar();
            }
        }

        public IReadOnlyList<string> ProductosConComprasModificadas(string empresa, string empresaEspejo, DateTime desde)
        {
            return LeerProductos(SQL_PRODUCTOS_COMPRAS_MODIFICADAS, empresa, empresaEspejo, desde);
        }

        public IReadOnlyList<string> ProductosConMovimientosConFechaPasada(string empresa, string empresaEspejo, int desdeNumOrden, int hastaNumOrden)
        {
            List<string> productos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_PRODUCTOS_MOVIMIENTOS_FECHA_PASADA, TIMEOUT_SELECCION_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa);
                Texto(comando, "@espejo", empresaEspejo ?? empresa);
                comando.Parameters.Add("@desdeNumOrden", SqlDbType.Int).Value = desdeNumOrden;
                comando.Parameters.Add("@hastaNumOrden", SqlDbType.Int).Value = hastaNumOrden;
                LeerCadenas(comando, productos);
            }
            return productos;
        }

        public int? MaximoNumOrdenExtracto(string empresa)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_MAXIMO_NUM_ORDEN_EXTRACTO, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? (int?)null : Convert.ToInt32(valor, CultureInfo.InvariantCulture);
            }
        }

        public IReadOnlyList<string> ProductosDelPedidoCompra(string empresa, int pedido)
        {
            List<string> productos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_PRODUCTOS_PEDIDO, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa);
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = pedido;
                LeerCadenas(comando, productos);
            }
            return productos;
        }

        public IReadOnlyList<string> ProductosPendientes(string empresa)
        {
            List<string> productos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_PRODUCTOS_PENDIENTES, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa?.Trim());
                LeerCadenas(comando, productos);
            }
            return productos;
        }

        public void MarcarPendiente(string empresa, string producto, string motivo)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_MARCAR_PENDIENTE, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa?.Trim());
                Texto(comando, "@producto", producto?.Trim());
                string m = motivo?.Trim();
                Texto(comando, "@motivo", m != null && m.Length > LONGITUD_MOTIVO_PENDIENTE ? m.Substring(0, LONGITUD_MOTIVO_PENDIENTE) : m);
                _ = comando.ExecuteNonQuery();
            }
        }

        public void QuitarPendiente(string empresa, string producto)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_QUITAR_PENDIENTE, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa?.Trim());
                Texto(comando, "@producto", producto?.Trim());
                _ = comando.ExecuteNonQuery();
            }
        }

        public DateTime? LeerUltimaPasada()
        {
            return InterpretarMarca(LeerParametro(Constantes.ParametrosUsuario.PRECIOS_MEDIOS_ULTIMA_PASADA_INCREMENTAL));
        }

        public void GuardarUltimaPasada(DateTime marca)
        {
            GuardarParametro(Constantes.ParametrosUsuario.PRECIOS_MEDIOS_ULTIMA_PASADA_INCREMENTAL, FormatearMarca(marca));
        }

        public int? LeerUltimoNumOrdenExtracto()
        {
            return InterpretarNumOrden(LeerParametro(Constantes.ParametrosUsuario.PRECIOS_MEDIOS_ULTIMO_NUM_ORDEN_EXTRACTO));
        }

        public void GuardarUltimoNumOrdenExtracto(int numOrden)
        {
            GuardarParametro(Constantes.ParametrosUsuario.PRECIOS_MEDIOS_ULTIMO_NUM_ORDEN_EXTRACTO, FormatearNumOrden(numOrden));
        }

        private string LeerParametro(string clave)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_LEER_ULTIMA_PASADA, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                ParametrosMarca(comando, clave);
                object valor = comando.ExecuteScalar();
                return valor == null || valor == DBNull.Value ? null : (string)valor;
            }
        }

        private void GuardarParametro(string clave, string valor)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, SQL_GUARDAR_ULTIMA_PASADA, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                ParametrosMarca(comando, clave);
                Texto(comando, "@valor", valor);
                _ = comando.ExecuteNonQuery();
            }
        }

        internal static string FormatearNumOrden(int numOrden)
        {
            return numOrden.ToString(CultureInfo.InvariantCulture);
        }

        internal static int? InterpretarNumOrden(string valor)
        {
            return int.TryParse(valor?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int numOrden)
                ? numOrden
                : (int?)null;
        }

        internal static string FormatearMarca(DateTime marca)
        {
            return marca.ToString(FORMATO_MARCA, CultureInfo.InvariantCulture);
        }

        internal static DateTime? InterpretarMarca(string valor)
        {
            return DateTime.TryParseExact(valor?.Trim(), FORMATO_MARCA, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime marca)
                ? marca
                : (DateTime?)null;
        }

        public ResultadoEscrituraPrecioMedio RecalcularYEscribir(string empresa, string empresaEspejo, string producto,
            Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> planificar)
        {
            Stopwatch reloj = Stopwatch.StartNew();
            ResultadoEscrituraPrecioMedio resultado = new ResultadoEscrituraPrecioMedio { Empresa = empresa, Producto = producto?.Trim() };
            using (SqlConnection conexion = Abrir())
            {
                using (SqlCommand sesion = Comando(conexion, null, SQL_SESION, TIMEOUT_ESCRITURA_SEGUNDOS))
                {
                    _ = sesion.ExecuteNonQuery();
                }

                using (SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        ObtenerBloqueo(conexion, transaccion, empresa, producto);

                        DatosProductoPrecioMedio datos = RepositorioPreciosMediosSql.LeerDatos(conexion, transaccion, empresa, empresaEspejo,
                            producto, conVentas: false, lecturaSucia: false);
                        PlanEscrituraPrecioMedio plan = planificar(datos);
                        resultado.Plan = plan;

                        // Corte (a): con tope de filas. Si se llega, se confirma lo escrito y el producto queda pendiente.
                        EjecutorComandosPreciosMedios.Ejecutar(SqlEscrituraPreciosMedios.Generar(plan),
                            EjecutorComandosPreciosMedios.TOPE_FILAS_POR_PRODUCTO_Y_PASADA,
                            comando => EjecutarUnaVez(conexion, transaccion, comando), resultado);
                        transaccion.Commit();
                    }
                    catch
                    {
                        try
                        {
                            transaccion.Rollback();
                        }
                        catch
                        {
                            // Transacción ya abortada por el servidor (interbloqueo, timeout): viaja la excepción original.
                        }
                        throw;
                    }
                }
            }
            resultado.Milisegundos = reloj.Elapsed.TotalMilliseconds;
            return resultado;
        }

        /// <summary>Recurso de <c>sp_getapplock</c> (máx. 255 caracteres).</summary>
        internal static string RecursoBloqueo(string empresa, string producto)
        {
            return "PreciosMedios#547:" + empresa?.Trim() + ":" + producto?.Trim();
        }

        private static void ObtenerBloqueo(SqlConnection conexion, SqlTransaction transaccion, string empresa, string producto)
        {
            using (SqlCommand comando = Comando(conexion, transaccion, SQL_APPLOCK, TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                comando.Parameters.Add("@recurso", SqlDbType.NVarChar, 255).Value = RecursoBloqueo(empresa, producto);
                comando.Parameters.Add("@espera", SqlDbType.Int).Value = LOCK_TIMEOUT_MS;
                SqlParameter r = comando.Parameters.Add("@resultado", SqlDbType.Int);
                r.Direction = ParameterDirection.Output;
                _ = comando.ExecuteNonQuery();
                int codigo = r.Value == DBNull.Value ? -999 : (int)r.Value;
                if (codigo < 0)
                {
                    throw new InvalidOperationException($"No se ha podido bloquear el producto {producto?.Trim()} de la empresa {empresa} " +
                        $"(sp_getapplock = {codigo}): otro proceso lo está recalculando");
                }
            }
        }

        /// <summary>
        /// Ejecuta UNA vuelta de una sentencia y devuelve las filas de ESA sentencia (<c>@@ROWCOUNT</c> en un parámetro
        /// de salida: el valor de ExecuteNonQuery sumaría también lo que hagan los triggers). Las vueltas de los lotes y
        /// el tope los decide <see cref="EjecutorComandosPreciosMedios"/>.
        /// </summary>
        private static int EjecutarUnaVez(SqlConnection conexion, SqlTransaction transaccion, ComandoEscrituraPrecioMedio comando)
        {
            using (SqlCommand sql = Comando(conexion, transaccion, comando.Texto + ";\nSET @filas = @@ROWCOUNT;", TIMEOUT_ESCRITURA_SEGUNDOS))
            {
                foreach (ParametroEscrituraPrecioMedio p in comando.Parametros)
                {
                    SqlParameter parametro = p.Tamano > 0 ? sql.Parameters.Add(p.Nombre, p.Tipo, p.Tamano) : sql.Parameters.Add(p.Nombre, p.Tipo);
                    parametro.Value = p.Valor ?? DBNull.Value;
                }
                SqlParameter salida = sql.Parameters.Add("@filas", SqlDbType.Int);
                salida.Direction = ParameterDirection.Output;
                _ = sql.ExecuteNonQuery();
                return salida.Value == DBNull.Value ? 0 : (int)salida.Value;
            }
        }

        private IReadOnlyList<string> LeerProductos(string sqlTexto, string empresa, string empresaEspejo, DateTime desde)
        {
            List<string> productos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, null, sqlTexto, TIMEOUT_SELECCION_SEGUNDOS))
            {
                Texto(comando, "@empresa", empresa);
                Texto(comando, "@espejo", empresaEspejo ?? empresa);
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                LeerCadenas(comando, productos);
            }
            return productos;
        }

        private static void LeerCadenas(SqlCommand comando, List<string> destino)
        {
            using (SqlDataReader lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    if (!lector.IsDBNull(0))
                    {
                        destino.Add(lector.GetString(0));
                    }
                }
            }
        }

        private static void ParametrosMarca(SqlCommand comando, string clave)
        {
            Texto(comando, "@empresa", Constantes.Empresas.EMPRESA_POR_DEFECTO);
            Texto(comando, "@usuario", Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO);
            Texto(comando, "@clave", clave);
        }

        private SqlConnection Abrir()
        {
            SqlConnection conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, SqlTransaction transaccion, string sql, int timeout)
        {
            return new SqlCommand(sql, conexion, transaccion) { CommandTimeout = timeout };
        }

        private static void Texto(SqlCommand comando, string nombre, string valor)
        {
            comando.Parameters.Add(nombre, SqlDbType.VarChar, 162).Value = (object)valor ?? DBNull.Value;
        }
    }
}
