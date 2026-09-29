using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#563: <see cref="IRepositorioSombraModoServicio"/> con ADO.NET (como la sombra de precios medios, #547):
    /// sin tocar el EDMX para la tabla nueva. Lecturas agrupadas por la lista de productos del pedido, todas con
    /// <c>WITH (NOLOCK)</c> (es diagnóstico: no debe bloquear a nadie) y parámetros <c>VarChar</c> (las columnas son
    /// <c>char</c>; un <c>NVarChar</c> obligaría a convertir la columna y perdería el índice). Mismas cuentas que
    /// <see cref="ServicioGestorStocks.LeerResumenStocks"/>, pero con los pendientes SIN el propio pedido.
    /// Lo ÚNICO que escribe es <c>ModoServicioSombra</c>.
    /// </summary>
    public class RepositorioSombraModoServicioSql : IRepositorioSombraModoServicio
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-ModoServicioSombra";
        private const int TIMEOUT_SEGUNDOS = 15;

        internal const string SQL_EXISTE_TABLA = @"SELECT CASE WHEN OBJECT_ID('dbo.ModoServicioSombra', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_STOCK = @"
SELECT RTRIM(e.Número), RTRIM(e.Almacén), ISNULL(SUM(CAST(e.Cantidad AS int)), 0)
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Número IN ({0})
GROUP BY e.Número, e.Almacén";

        internal const string SQL_PENDIENTES = @"
SELECT RTRIM(l.Producto), RTRIM(l.Almacén), ISNULL(SUM(CAST(l.Cantidad AS int)), 0)
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND l.Estado >= -1 AND l.Estado <= 1
  AND NOT (l.Empresa = @empresa AND l.Número = @pedido)
GROUP BY l.Producto, l.Almacén";

        internal const string SQL_REPOSICIONES = @"
SELECT RTRIM(p.Número), ISNULL(SUM(CAST(p.Cantidad AS int)), 0)
FROM PreExtrProducto p WITH (NOLOCK)
WHERE p.Empresa IN (@empresa, @espejo) AND p.Número IN ({0}) AND p.NºTraspaso > 0
GROUP BY p.Número";

        internal const string SQL_ESTADOS = @"
SELECT RTRIM(p.Número), p.Estado
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa AND p.Número IN ({0})";

        internal const string SQL_COMPRAS = @"
SELECT RTRIM(c.Producto), RTRIM(c.Almacén), ISNULL(SUM(CAST(c.Cantidad AS int)), 0), MIN(c.FechaRecepción)
FROM LinPedidoCmp c WITH (NOLOCK)
WHERE c.Empresa IN (@empresa, @espejo) AND c.Producto IN ({0}) AND c.Estado IN (-1, 1) AND c.Enviado = 1
GROUP BY c.Producto, c.Almacén";

        internal const string SQL_GUARDAR = @"
INSERT INTO dbo.ModoServicioSombra (Origen, Empresa, Pedido, EsPresupuesto, Cliente, Contacto, Usuario, ModoPedido,
    ModoColores, PermitidosColores, MotivoColores, ModoCausas, PermitidosCausas, MotivoCausas, MismoModo, MismosPermitidos, Causas)
VALUES (@origen, @empresa, @pedido, @esPresupuesto, @cliente, @contacto, @usuario, @modoPedido,
    @modoColores, @permitidosColores, @motivoColores, @modoCausas, @permitidosCausas, @motivoCausas, @mismoModo, @mismosPermitidos, @causas)";

        private readonly string cadenaConexion;

        public RepositorioSombraModoServicioSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        /// <summary>La cadena de <c>NestoConnection</c> (misma BD NV) con nombre de aplicación propio.</summary>
        public static RepositorioSombraModoServicioSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioSombraModoServicioSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = NOMBRE_APLICACION
            }.ConnectionString);
        }

        public bool ExisteTabla()
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE_TABLA))
            {
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        public DatosSombraModoServicio LeerDatos(string empresa, int? pedidoExcluir, IEnumerable<string> productos)
        {
            var datos = new DatosSombraModoServicio();
            List<string> lista = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!lista.Any())
            {
                return datos;
            }
            string enLista = string.Join(", ", lista.Select((p, i) => "@p" + i));
            List<string> sedes = Constantes.Sedes.ListaSedes;

            using (SqlConnection conexion = Abrir())
            {
                Leer(conexion, SQL_STOCK, lista, enLista, empresa, pedidoExcluir, lector =>
                {
                    string producto = Cadena(lector, 0);
                    string almacen = Cadena(lector, 1);
                    int cantidad = lector.GetInt32(2);
                    ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, almacen), cantidad);
                    if (almacen != null && sedes.Contains(almacen.Trim()))
                    {
                        ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), cantidad);
                    }
                });
                Leer(conexion, SQL_PENDIENTES, lista, enLista, empresa, pedidoExcluir, lector =>
                {
                    string producto = Cadena(lector, 0);
                    int cantidad = lector.GetInt32(2);
                    ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, Cadena(lector, 1)), cantidad);
                    ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto), cantidad);
                });
                Leer(conexion, SQL_REPOSICIONES, lista, enLista, empresa, pedidoExcluir, lector =>
                    ResumenStocksProductos.Sumar(datos.Resumen.PendienteReposicion, ResumenStocksProductos.Clave(Cadena(lector, 0)), lector.GetInt32(1)));
                Leer(conexion, SQL_ESTADOS, lista, enLista, empresa, pedidoExcluir, lector =>
                {
                    if (!lector.IsDBNull(1))
                    {
                        datos.Estados[ResumenStocksProductos.Clave(Cadena(lector, 0))] = lector.GetInt16(1);
                    }
                });
                Leer(conexion, SQL_COMPRAS, lista, enLista, empresa, pedidoExcluir, lector =>
                {
                    string clave = ResumenStocksProductos.Clave(Cadena(lector, 0), Cadena(lector, 1));
                    ResumenStocksProductos.Sumar(datos.PendienteRecibir, clave, lector.GetInt32(2));
                    if (!lector.IsDBNull(3))
                    {
                        DateTime fecha = lector.GetDateTime(3);
                        if (!datos.FechaPrevista.TryGetValue(clave, out DateTime actual) || fecha < actual)
                        {
                            datos.FechaPrevista[clave] = fecha;
                        }
                    }
                });
            }
            return datos;
        }

        public void Guardar(FilaSombraModoServicio fila)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_GUARDAR))
            {
                Texto(comando, "@origen", fila.Origen, 20);
                Texto(comando, "@empresa", fila.Empresa, 3);
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = (object)fila.Pedido ?? DBNull.Value;
                comando.Parameters.Add("@esPresupuesto", SqlDbType.Bit).Value = fila.EsPresupuesto;
                Texto(comando, "@cliente", fila.Cliente, 10);
                Texto(comando, "@contacto", fila.Contacto, 3);
                Texto(comando, "@usuario", fila.Usuario, 50);
                comando.Parameters.Add("@modoPedido", SqlDbType.TinyInt).Value = (object)fila.ModoPedido ?? DBNull.Value;
                comando.Parameters.Add("@modoColores", SqlDbType.TinyInt).Value = fila.ModoColores;
                Texto(comando, "@permitidosColores", fila.PermitidosColores, 10);
                TextoLargo(comando, "@motivoColores", fila.MotivoColores, 400);
                comando.Parameters.Add("@modoCausas", SqlDbType.TinyInt).Value = fila.ModoCausas;
                Texto(comando, "@permitidosCausas", fila.PermitidosCausas, 10);
                TextoLargo(comando, "@motivoCausas", fila.MotivoCausas, 400);
                comando.Parameters.Add("@mismoModo", SqlDbType.Bit).Value = fila.MismoModo;
                comando.Parameters.Add("@mismosPermitidos", SqlDbType.Bit).Value = fila.MismosPermitidos;
                TextoLargo(comando, "@causas", fila.Causas, -1);
                _ = comando.ExecuteNonQuery();
            }
        }

        private static void Leer(SqlConnection conexion, string plantilla, List<string> productos, string enLista, string empresa, int? pedidoExcluir,
            Action<SqlDataReader> fila)
        {
            using (SqlCommand comando = Comando(conexion, string.Format(plantilla, enLista)))
            {
                for (int i = 0; i < productos.Count; i++)
                {
                    comando.Parameters.Add("@p" + i, SqlDbType.VarChar, 15).Value = productos[i];
                }
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                // Sin pedido (plantilla) no hay nada que excluir: 0 no es ningún número de pedido.
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = pedidoExcluir ?? 0;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        fila(lector);
                    }
                }
            }
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql)
            => new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };

        private static string Cadena(SqlDataReader lector, int indice)
            => lector.IsDBNull(indice) ? null : lector.GetString(indice);

        private static void Texto(SqlCommand comando, string nombre, string valor, int longitud)
        {
            string recortado = valor == null ? null : (valor.Length > longitud ? valor.Substring(0, longitud) : valor);
            comando.Parameters.Add(nombre, SqlDbType.VarChar, longitud).Value = (object)recortado ?? DBNull.Value;
        }

        private static void TextoLargo(SqlCommand comando, string nombre, string valor, int longitud)
        {
            string recortado = valor == null || longitud < 0 || valor.Length <= longitud ? valor : valor.Substring(0, longitud);
            comando.Parameters.Add(nombre, SqlDbType.NVarChar, longitud).Value = (object)recortado ?? DBNull.Value;
        }
    }
}
