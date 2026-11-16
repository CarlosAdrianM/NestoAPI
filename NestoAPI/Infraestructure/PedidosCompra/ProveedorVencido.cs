using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>NestoAPI#606 (08/10): una línea de pedido a proveedor enviada, sin recibir y con la fecha prevista ya pasada.</summary>
    public class LineaCompraVencida
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Orden { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaPrevista { get; set; }
        /// <summary>NestoAPI#865: LinPedidoCmp.Estado (-1 o 1).</summary>
        public short Estado { get; set; } = 1;
        /// <summary>NestoAPI#865: el día en que se pidió (CabPedidoCmp.[Fecha Modificación], como los plazos de #853). Null si no consta.</summary>
        public DateTime? FechaPedido { get; set; }

        /// <summary>Clave de la línea para no avisar dos veces el mismo día: «1/220515/3».</summary>
        public string Clave => $"{Empresa?.Trim()}/{Pedido}/{Orden}";
    }

    /// <summary>NestoAPI#606 (08/10): los pedidos de clientes con un producto pendiente en un almacén, frente a su stock.</summary>
    public class EsperaClientesProducto
    {
        public int Stock { get; set; }
        public int Pendientes { get; set; }
        /// <summary>Pedidos de venta con ese producto pendiente (-1 a 1) en ese almacén.</summary>
        public List<int> Pedidos { get; set; } = new List<int>();
        /// <summary>
        /// NestoAPI#865 (corte 2): unidades que ya vienen de camino a ese almacén en una reposición de tienda (traspaso terminado y sin
        /// recibir: PreExtrProducto con NºTraspaso, lo mismo que cuenta el motor de fechas de entrega como «en camino»).
        /// </summary>
        public int EnCaminoReposicion { get; set; }
        public List<int> TraspasosEnCamino { get; set; } = new List<int>();
        /// <summary>NestoAPI#865: cuándo llega esa reposición (el siguiente día de reparto propio). Null si no hay.</summary>
        public DateTime? LlegadaReposicion { get; set; }
        /// <summary>Lo que el stock no cubre.</summary>
        public int SinStock => Math.Max(0, Pendientes - Math.Max(0, Stock));
        /// <summary>NestoAPI#865: de lo que el stock no cubre, lo que trae la reposición en camino (no espera a la compra).</summary>
        public int CubiertoPorReposicion => Math.Min(SinStock, Math.Max(0, EnCaminoReposicion));
        /// <summary>Lo que no cubren ni el stock ni la reposición en camino: hay clientes esperando al proveedor.</summary>
        public int Faltan => SinStock - CubiertoPorReposicion;
    }

    /// <summary>NestoAPI#865 (corte 2): la reposición de tienda en camino en el correo de proveedores vencidos. PURA.</summary>
    public static class ReposicionEnCaminoProveedorVencido
    {
        /// <summary>
        /// Lo que ya viene de camino llega el siguiente día de reparto propio (lo trae la furgoneta, #841): el día después de hoy que no
        /// sea <paramref name="sinLlegada"/> (por defecto, sábados y domingos).
        /// </summary>
        public static DateTime Llegada(DateTime hoy, Func<DateTime, bool> sinLlegada) =>
            ReglaFechaVencida.SiguienteLaborable(hoy, sinLlegada ?? (d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday));

        /// <summary>«3 uds. llegan de una reposición de tienda en camino (traspaso 5123) el 17/11/2026: no esperan a esta compra». Null si no hay.</summary>
        public static string Texto(EsperaClientesProducto espera)
        {
            if (espera == null || espera.CubiertoPorReposicion <= 0)
            {
                return null;
            }
            int unidades = espera.CubiertoPorReposicion;
            List<int> traspasos = (espera.TraspasosEnCamino ?? new List<int>()).Distinct().OrderBy(t => t).ToList();
            string cuales = traspasos.Any() ? $" ({(traspasos.Count == 1 ? "traspaso" : "traspasos")} {string.Join(", ", traspasos)})" : string.Empty;
            string cuando = espera.LlegadaReposicion.HasValue ? " el " + TextosFechaVencida.Dia(espera.LlegadaReposicion.Value) : string.Empty;
            return $"{unidades} {(unidades == 1 ? "ud. llega" : "uds. llegan")} de una reposición de tienda en camino{cuales}{cuando}: no " +
                $"{(unidades == 1 ? "espera" : "esperan")} a esta compra";
        }
    }

    public interface IRepositorioProveedorVencido
    {
        /// <summary>Las líneas enviadas y sin recibir con FechaRecepción anterior a <paramref name="hoy"/>. <paramref name="productos"/> null = todas.</summary>
        List<LineaCompraVencida> LeerLineasVencidas(string empresa, IEnumerable<string> productos, DateTime hoy);
        /// <summary>Clave «producto|almacén» → pedidos de clientes y stock.</summary>
        Dictionary<string, EsperaClientesProducto> LeerEsperas(IEnumerable<string> productos);
        /// <summary>¿Hay ya en el buzón de Nesto, con fecha de hoy, un aviso de esa línea?</summary>
        bool YaAvisadaHoy(string claveLinea, DateTime hoy);
        /// <summary>Destinatarios (sin dominio) del aviso en la campana.</summary>
        List<string> LeerDestinatarios();
        /// <summary>NestoAPI#865: el JSON «Nuevo» de las filas de Modificaciones <see cref="RastroFechaRecalculada.TABLA"/> desde ese día.</summary>
        List<string> LeerRecalculosAnteriores(DateTime desde);
        /// <summary>
        /// NestoAPI#865: en UNA transacción, pone <paramref name="fecha"/> en la línea (si sigue en el aire y con la fecha vencida que se
        /// leyó) y deja el rastro en Modificaciones. False si la línea ya no estaba así (no se toca nada).
        /// </summary>
        bool GuardarFechaRecalculada(LineaCompraVencida linea, DateTime fecha, string usuario, string anterior, string nuevo);
    }

    /// <summary>
    /// NestoAPI#606 (08/10): a quién avisa la campana de Nesto cuando una fecha de entrega a la agencia depende de un pedido a
    /// proveedor con la fecha prevista vencida. Parámetro <see cref="CLAVE"/> de la fila «(defecto)» de la empresa por defecto
    /// (mismo patrón que <c>UsuariosRellenarReposicionManual</c>): lista separada por comas, sin dominio. Sin la fila, la
    /// lista <see cref="POR_DEFECTO"/>; con la fila vacía, nadie.
    /// </summary>
    public static class DestinatariosAvisoProveedorVencido
    {
        public const string CLAVE = "UsuariosAvisoProveedorVencido";
        public const string POR_DEFECTO = "Santiago, Manuel";
        private const string USUARIO_GENERAL = "(defecto)";

        internal static List<string> Lista(string valor)
        {
            return (valor ?? POR_DEFECTO)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Select(u => u.Substring(u.LastIndexOf('\\') + 1).Trim())
                .Where(u => u.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string LeerValor()
        {
            using (var db = new NVEntities())
            {
                ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                    p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO_GENERAL && p.Clave == CLAVE);
                return parametro?.Valor;
            }
        }
    }

    /// <summary>NestoAPI#606 (08/10): ADO.NET con NOLOCK y parámetros VarChar (como la sombra del modo de servicio).</summary>
    public class RepositorioProveedorVencidoSql : IRepositorioProveedorVencido
    {
        private const int TIMEOUT_SEGUNDOS = 15;

        internal const string SQL_LINEAS = @"
SELECT RTRIM(c.Empresa), c.Número, c.NºOrden, RTRIM(c.NºProveedor), RTRIM(pr.Nombre), RTRIM(c.Producto), RTRIM(c.Texto),
       RTRIM(c.Almacén), CAST(c.Cantidad AS int), c.FechaRecepción, CAST(c.Estado AS int), cab.[Fecha Modificación]
FROM LinPedidoCmp c WITH (NOLOCK)
LEFT JOIN CabPedidoCmp cab WITH (NOLOCK) ON cab.Empresa = c.Empresa AND cab.Número = c.Número
OUTER APPLY (SELECT TOP 1 p.Nombre FROM Proveedores p WITH (NOLOCK)
             WHERE p.Empresa = c.Empresa AND p.Número = c.NºProveedor
             ORDER BY CASE WHEN p.Contacto = c.Contacto THEN 0 ELSE 1 END, p.ProveedorPrincipal DESC) pr
WHERE c.Empresa IN (@empresa, @espejo) AND c.Estado IN (-1, 1) AND c.Enviado = 1
  AND c.FechaRecepción IS NOT NULL AND c.FechaRecepción < @hoy AND c.Producto IS NOT NULL{0}
ORDER BY c.FechaRecepción, c.Número, c.NºOrden";

        internal const string SQL_PENDIENTES = @"
SELECT RTRIM(l.Producto), RTRIM(l.Almacén), l.Número, ISNULL(SUM(CAST(l.Cantidad AS int)), 0)
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Producto IN ({0}) AND l.Estado >= -1 AND l.Estado <= 1
GROUP BY l.Producto, l.Almacén, l.Número";

        internal const string SQL_STOCK = @"
SELECT RTRIM(e.Número), RTRIM(e.Almacén), ISNULL(SUM(CAST(e.Cantidad AS int)), 0)
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Número IN ({0})
GROUP BY e.Número, e.Almacén";

        // NestoAPI#865 (corte 2): lo que ya viene de camino en una reposición de tienda (PreExtrProducto con NºTraspaso, como SQL_REPOSICIONES del
        // motor de fechas de entrega). Neto por traspaso y producto: mientras el traspaso tiene la salida y la entrada pendientes suma cero (no
        // cuenta, como en el motor); terminado, queda la entrada en el almacén de destino.
        internal const string SQL_REPOSICIONES_EN_CAMINO = @"
SELECT RTRIM(p.Número), RTRIM(MAX(CASE WHEN p.Cantidad > 0 THEN p.Almacén END)), p.NºTraspaso, SUM(CAST(p.Cantidad AS int))
FROM PreExtrProducto p WITH (NOLOCK)
WHERE p.Empresa IN (@empresa, @espejo) AND p.Número IN ({0}) AND p.NºTraspaso > 0
GROUP BY p.Número, p.NºTraspaso
HAVING SUM(CAST(p.Cantidad AS int)) > 0";

        internal const string SQL_YA_AVISADA = @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM NotificacionesBuzon WITH (NOLOCK)
                         WHERE Aplicacion = @aplicacion AND FechaCreacion >= @hoy AND Datos LIKE @patron) THEN 1 ELSE 0 END";

        internal const string SQL_RECALCULOS_ANTERIORES = @"
SELECT m.Nuevo FROM dbo.Modificaciones m WITH (NOLOCK) WHERE m.Tabla = @tabla AND m.Fecha >= @desde";

        // NestoAPI#865: solo si la línea sigue en el aire y con la fecha que se leyó (si no, alguien la ha cambiado o recibido). LinPedidoCmp no
        // tiene disparador que toque FechaRecepción (lo mismo que la fase 3 de #857): Usuario y [Fecha Modificación] se ponen aquí.
        internal const string SQL_RECALCULAR_FECHA = @"
UPDATE c SET [FechaRecepción] = @fecha, Usuario = @usuario, [Fecha Modificación] = GETDATE()
FROM dbo.LinPedidoCmp c
WHERE c.Empresa = @empresa AND c.[Número] = @pedido AND c.[NºOrden] = @orden AND c.Estado IN (-1, 1) AND c.Enviado = 1
  AND c.[FechaRecepción] IS NOT NULL AND CAST(c.[FechaRecepción] AS date) = CAST(@antes AS date)";

        private readonly string cadenaConexion;

        public RepositorioProveedorVencidoSql(string cadenaConexion)
        {
            this.cadenaConexion = string.IsNullOrWhiteSpace(cadenaConexion) ? throw new ArgumentNullException(nameof(cadenaConexion)) : cadenaConexion;
        }

        public static RepositorioProveedorVencidoSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"]
                ?? throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            return new RepositorioProveedorVencidoSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI-ProveedorVencido",
                // NestoAPI#687: el aviso a Compras sale del correo del pedido, que en UnirPedidos va dentro de un
                // TransactionScope; con otra cadena que la de EF, alistarse escalaría a MSDTC (deshabilitado).
                Enlist = false
            }.ConnectionString);
        }

        public List<LineaCompraVencida> LeerLineasVencidas(string empresa, IEnumerable<string> productos, DateTime hoy)
        {
            List<string> lista = productos == null ? null : Limpiar(productos);
            if (lista != null && !lista.Any())
            {
                return new List<LineaCompraVencida>();
            }
            string filtro = lista == null ? string.Empty : $" AND c.Producto IN ({EnLista(lista)})";
            var lineas = new List<LineaCompraVencida>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, string.Format(SQL_LINEAS, filtro)))
            {
                Productos(comando, lista);
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                comando.Parameters.Add("@hoy", SqlDbType.DateTime).Value = hoy.Date;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        lineas.Add(new LineaCompraVencida
                        {
                            Empresa = Cadena(lector, 0),
                            Pedido = lector.GetInt32(1),
                            Orden = lector.GetInt32(2),
                            Proveedor = Cadena(lector, 3),
                            NombreProveedor = Cadena(lector, 4),
                            Producto = Cadena(lector, 5),
                            Descripcion = Cadena(lector, 6),
                            Almacen = Cadena(lector, 7),
                            Cantidad = lector.IsDBNull(8) ? 0 : lector.GetInt32(8),
                            FechaPrevista = lector.GetDateTime(9),
                            Estado = lector.IsDBNull(10) ? (short)1 : (short)lector.GetInt32(10),
                            FechaPedido = lector.IsDBNull(11) ? (DateTime?)null : lector.GetDateTime(11)
                        });
                    }
                }
            }
            return lineas;
        }

        public Dictionary<string, EsperaClientesProducto> LeerEsperas(IEnumerable<string> productos)
        {
            var esperas = new Dictionary<string, EsperaClientesProducto>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = Limpiar(productos);
            if (!lista.Any())
            {
                return esperas;
            }
            EsperaClientesProducto De(string producto, string almacen)
            {
                string clave = ClaveEspera(producto, almacen);
                if (!esperas.TryGetValue(clave, out EsperaClientesProducto espera))
                {
                    espera = new EsperaClientesProducto();
                    esperas[clave] = espera;
                }
                return espera;
            }
            using (SqlConnection conexion = Abrir())
            {
                using (SqlCommand comando = Comando(conexion, string.Format(SQL_PENDIENTES, EnLista(lista))))
                {
                    Productos(comando, lista);
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            EsperaClientesProducto espera = De(Cadena(lector, 0), Cadena(lector, 1));
                            int unidades = lector.GetInt32(3);
                            espera.Pendientes += unidades;
                            if (unidades > 0)
                            {
                                espera.Pedidos.Add(lector.GetInt32(2));
                            }
                        }
                    }
                }
                using (SqlCommand comando = Comando(conexion, string.Format(SQL_STOCK, EnLista(lista))))
                {
                    Productos(comando, lista);
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            De(Cadena(lector, 0), Cadena(lector, 1)).Stock += lector.GetInt32(2);
                        }
                    }
                }
                using (SqlCommand comando = Comando(conexion, string.Format(SQL_REPOSICIONES_EN_CAMINO, EnLista(lista))))
                {
                    Productos(comando, lista);
                    comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                    comando.Parameters.Add("@espejo", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            if (lector.IsDBNull(1) || lector.IsDBNull(2))
                            {
                                continue;
                            }
                            EsperaClientesProducto espera = De(Cadena(lector, 0), Cadena(lector, 1));
                            espera.EnCaminoReposicion += lector.GetInt32(3);
                            espera.TraspasosEnCamino.Add(lector.GetInt32(2));
                        }
                    }
                }
            }
            foreach (EsperaClientesProducto espera in esperas.Values)
            {
                espera.Pedidos = espera.Pedidos.Distinct().OrderBy(p => p).ToList();
                espera.TraspasosEnCamino = espera.TraspasosEnCamino.Distinct().OrderBy(t => t).ToList();
            }
            return esperas;
        }

        public bool YaAvisadaHoy(string claveLinea, DateTime hoy)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_YA_AVISADA))
            {
                comando.Parameters.Add("@aplicacion", SqlDbType.NVarChar, 50).Value = Constantes.Aplicaciones.NESTO;
                comando.Parameters.Add("@hoy", SqlDbType.DateTime).Value = hoy.Date;
                comando.Parameters.Add("@patron", SqlDbType.NVarChar, 200).Value = "%" + AvisadorProveedorVencido.Marca(claveLinea) + "%";
                return Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
        }

        public List<string> LeerDestinatarios() => DestinatariosAvisoProveedorVencido.Lista(DestinatariosAvisoProveedorVencido.LeerValor());

        public List<string> LeerRecalculosAnteriores(DateTime desde)
        {
            var nuevos = new List<string>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_RECALCULOS_ANTERIORES))
            {
                comando.Parameters.Add("@tabla", SqlDbType.NVarChar, 100).Value = RastroFechaRecalculada.TABLA;
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde;
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        nuevos.Add(lector.IsDBNull(0) ? null : lector.GetString(0));
                    }
                }
            }
            return nuevos;
        }

        public bool GuardarFechaRecalculada(LineaCompraVencida linea, DateTime fecha, string usuario, string anterior, string nuevo)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlTransaction transaccion = conexion.BeginTransaction())
            {
                using (SqlCommand comando = Comando(conexion, SQL_RECALCULAR_FECHA))
                {
                    comando.Transaction = transaccion;
                    comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = linea.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                    comando.Parameters.Add("@pedido", SqlDbType.Int).Value = linea.Pedido;
                    comando.Parameters.Add("@orden", SqlDbType.Int).Value = linea.Orden;
                    comando.Parameters.Add("@antes", SqlDbType.DateTime).Value = linea.FechaPrevista.Date;
                    comando.Parameters.Add("@fecha", SqlDbType.DateTime).Value = fecha.Date;
                    comando.Parameters.Add("@usuario", SqlDbType.VarChar, 30).Value = Recortar(usuario, 30) ?? "DESCONOCIDO";
                    if (comando.ExecuteNonQuery() != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                }
                using (SqlCommand rastro = Comando(conexion, CorreosSeguimiento.RepositorioCorreosSeguimiento.SQL_RASTRO))
                {
                    rastro.Transaction = transaccion;
                    rastro.Parameters.Add("@tabla", SqlDbType.NVarChar, 100).Value = RastroFechaRecalculada.TABLA;
                    rastro.Parameters.Add("@anterior", SqlDbType.NVarChar, -1).Value = (object)anterior ?? DBNull.Value;
                    rastro.Parameters.Add("@nuevo", SqlDbType.NVarChar, -1).Value = (object)nuevo ?? DBNull.Value;
                    rastro.Parameters.Add("@usuarioRastro", SqlDbType.NVarChar, 100).Value = (object)Recortar(usuario, 100) ?? DBNull.Value;
                    _ = rastro.ExecuteNonQuery();
                }
                transaccion.Commit();
                return true;
            }
        }

        private static string Recortar(string texto, int maximo)
        {
            string limpio = texto?.Trim();
            return limpio == null || limpio.Length <= maximo ? limpio : limpio.Substring(0, maximo);
        }

        internal static string ClaveEspera(string producto, string almacen) => $"{producto?.Trim()}|{almacen?.Trim()}".ToUpperInvariant();

        private static List<string> Limpiar(IEnumerable<string> productos) => (productos ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static string EnLista(List<string> lista) => string.Join(", ", lista.Select((p, i) => "@p" + i));

        private static void Productos(SqlCommand comando, List<string> lista)
        {
            for (int i = 0; lista != null && i < lista.Count; i++)
            {
                comando.Parameters.Add("@p" + i, SqlDbType.VarChar, 15).Value = lista[i];
            }
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql) => new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };

        private static string Cadena(SqlDataReader lector, int indice) => lector.IsDBNull(indice) ? null : lector.GetString(indice);
    }

    public interface IAvisadorProveedorVencido
    {
        /// <summary>
        /// Avisa en la campana de Nesto de los pedidos a proveedor vencidos de esos productos que esperan
        /// <paramref name="quienEspera"/> («el pedido 928020», «un pedido en la plantilla…»). Nunca lanza.
        /// </summary>
        Task Avisar(string empresa, IEnumerable<ProveedorVencidoFechaEntregaAgencia> productos, string quienEspera);
    }

    /// <summary>
    /// NestoAPI#606 (08/10), decisión de Carlos: cuando la fecha de entrega a la agencia de un pedido (o de la plantilla) depende
    /// de un pedido a proveedor con la fecha prevista VENCIDA, aviso en la campana de Nesto (buzón + SignalR, #536) a los
    /// usuarios de <see cref="DestinatariosAvisoProveedorVencido"/> (Santiago y Manuel). Un aviso por pedido de compra con sus
    /// líneas vencidas de esos productos, y como mucho UNA vez por línea y día:
    /// <list type="bullet">
    /// <item>en memoria, cada producto se mira una vez al día (la plantilla recalcula a cada cambio: no se consulta la BD
    /// en cada recálculo);</item>
    /// <item>en BD, la notificación lleva en sus Datos la marca de cada línea («|1/220515/3|»), y antes de avisar se mira si
    /// el buzón ya tiene una de hoy con esa marca (sobrevive a un reciclado de la API).</item>
    /// </list>
    /// </summary>
    public class AvisadorProveedorVencido : IAvisadorProveedorVencido
    {
        public const string TIPO_NOTIFICACION = "ProveedorVencido";
        private const string DOMINIO = "NUEVAVISION\\";
        private static readonly CultureInfo es = new CultureInfo("es-ES");
        private static readonly ConcurrentDictionary<string, DateTime> productosMirados = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<IRepositorioProveedorVencido> repositorio;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<DateTime> reloj;
        private readonly Action<Exception> registrar;

        public AvisadorProveedorVencido()
            : this(RepositorioProveedorVencidoSql.DesdeConfiguracion, new ServicioNotificacionesPush(), () => DateTime.Now, ex => ElmahHelper.Log(ex))
        {
        }

        internal AvisadorProveedorVencido(Func<IRepositorioProveedorVencido> repositorio, IServicioNotificacionesPush notificaciones,
            Func<DateTime> reloj, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.notificaciones = notificaciones ?? throw new ArgumentNullException(nameof(notificaciones));
            this.reloj = reloj ?? (() => DateTime.Now);
            this.registrar = registrar ?? (_ => { });
        }

        /// <summary>La marca de una línea dentro de los Datos de la notificación.</summary>
        internal static string Marca(string claveLinea) => "|" + claveLinea + "|";

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarProductosMirados() => productosMirados.Clear();

        public async Task Avisar(string empresa, IEnumerable<ProveedorVencidoFechaEntregaAgencia> productos, string quienEspera)
        {
            try
            {
                DateTime hoy = reloj().Date;
                string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                List<string> nuevos = (productos ?? Enumerable.Empty<ProveedorVencidoFechaEntregaAgencia>())
                    .Select(p => p?.Producto?.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(p => !(productosMirados.TryGetValue(empresaLimpia + "|" + p, out DateTime dia) && dia == hoy))
                    .ToList();
                if (!nuevos.Any())
                {
                    return;
                }

                IRepositorioProveedorVencido datos = repositorio();
                List<LineaCompraVencida> lineas = datos.LeerLineasVencidas(empresaLimpia, nuevos, hoy)
                    .Where(l => string.Equals(l.Almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                List<string> destinatarios = lineas.Any() ? datos.LeerDestinatarios() : new List<string>();
                if (lineas.Any() && !destinatarios.Any())
                {
                    registrar(new Exception($"[Proveedor vencido #606] No hay a quién avisar (parámetro {DestinatariosAvisoProveedorVencido.CLAVE} vacío)."));
                }

                foreach (IGrouping<string, LineaCompraVencida> pedido in lineas.GroupBy(l => $"{l.Empresa?.Trim()}/{l.Pedido}"))
                {
                    List<LineaCompraVencida> sinAvisar = pedido.Where(l => !datos.YaAvisadaHoy(l.Clave, hoy)).ToList();
                    if (!sinAvisar.Any() || !destinatarios.Any())
                    {
                        continue;
                    }
                    NotificacionPushDTO notificacion = Notificacion(sinAvisar, quienEspera);
                    foreach (string usuario in destinatarios)
                    {
                        await notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    }
                }
                foreach (string producto in nuevos)
                {
                    productosMirados[empresaLimpia + "|" + producto] = hoy;
                }
            }
            catch (Exception ex)
            {
                // El aviso es un extra: calcular la fecha (y crear el pedido) no puede fallar por esto.
                try
                {
                    registrar(new Exception($"[Proveedor vencido #606] No se ha podido avisar: {ex.Message}", ex));
                }
                catch
                {
                    // nada
                }
            }
        }

        internal static NotificacionPushDTO Notificacion(List<LineaCompraVencida> lineas, string quienEspera)
        {
            LineaCompraVencida primera = lineas.First();
            string proveedor = string.IsNullOrWhiteSpace(primera.NombreProveedor)
                ? $"el proveedor {primera.Proveedor}"
                : $"{primera.NombreProveedor} ({primera.Proveedor})";
            var cuerpo = new StringBuilder();
            _ = cuerpo.AppendLine($"El pedido de compra {primera.Pedido} a {proveedor} tenía que haber llegado y aún no se ha recibido:");
            foreach (LineaCompraVencida l in lineas)
            {
                string descripcion = string.IsNullOrWhiteSpace(l.Descripcion) ? string.Empty : " " + l.Descripcion.Trim();
                _ = cuerpo.AppendLine($"· {l.Producto}{descripcion}: {l.Cantidad} {(l.Cantidad == 1 ? "ud." : "uds.")}, prevista el {l.FechaPrevista.ToString("dd/MM/yyyy", es)}.");
            }
            _ = cuerpo.AppendLine($"Hay pedidos de clientes esperándolo ({(string.IsNullOrWhiteSpace(quienEspera) ? "un pedido de venta" : quienEspera.Trim())}). " +
                "Mientras tanto, la fecha de entrega a la agencia supone que llega mañana. Si tiene otra fecha, ponedla en el pedido de compra.");
            string titulo = $"Pedido a proveedor {primera.Pedido} vencido sin recibir ({(string.IsNullOrWhiteSpace(primera.NombreProveedor) ? primera.Proveedor : primera.NombreProveedor)})";
            return new NotificacionPushDTO
            {
                Titulo = titulo.Length > 200 ? titulo.Substring(0, 200) : titulo,
                Cuerpo = cuerpo.ToString().TrimEnd(),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["pedidoCompra"] = primera.Pedido.ToString(CultureInfo.InvariantCulture),
                    ["lineas"] = "|" + string.Join("|", lineas.Select(l => l.Clave)) + "|"
                }
            };
        }
    }

    /// <summary>Dependencias del job, inyectables para testearlo sin BD ni SMTP.</summary>
    internal class DependenciasProveedoresVencidos
    {
        public IRepositorioProveedorVencido Repositorio { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        public DateTime Ahora { get; set; } = DateTime.Now;
        /// <summary>NestoAPI#780: sin él (null), no se miran los festivos.</summary>
        public Func<DateTime, bool> EsFestivo { get; set; }
        /// <summary>NestoAPI#780: la traza del festivo (null = ELMAH).</summary>
        public Action<Exception, string> Trazar { get; set; }
        /// <summary>NestoAPI#865: recalcular las fechas vencidas antes de mandar el correo (null = no, el correo de siempre).</summary>
        public ServicioFechasVencidas FechasVencidas { get; set; }
        /// <summary>NestoAPI#865 (corte 2): días sin llegada de reposición (por defecto, sábados y domingos; en producción, sin reparto propio).</summary>
        public Func<DateTime, bool> SinLlegadaReposicion { get; set; }
    }

    /// <summary>
    /// NestoAPI#606 (08/10), decisión de Carlos: correo diario por la mañana a Compras con los pedidos a proveedor enviados, sin
    /// recibir y con la fecha prevista ya pasada, primero los que tienen pedidos de clientes esperando (el stock no cubre lo
    /// pendiente de ese producto en ese almacén). Se calcula en el momento del correo: si ya han puesto otra fecha o lo han
    /// recibido, no sale. Sin nada vencido, no se manda nada.
    /// </summary>
    public static class ProveedoresVencidosJobsService
    {
        public const string CRON = "0 8 * * 1-5"; // de lunes a viernes a las 8:00
        internal const string MENSAJE_FESTIVO = "[Proveedor vencido #606] No se manda el correo a Compras: el día es festivo";
        internal const string USUARIO_SISTEMA = "Sistema (proveedores con fecha vencida)";
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static void Procesar()
        {
            try
            {
                RepositorioProveedorVencidoSql repositorio = RepositorioProveedorVencidoSql.DesdeConfiguracion();
                _ = Procesar(new DependenciasProveedoresVencidos
                {
                    Repositorio = repositorio,
                    Correo = new ServicioCorreoElectronico(),
                    Ahora = DateTime.Now,
                    EsFestivo = JobsFestivos.EsFestivoAlgete,
                    SinLlegadaReposicion = d => Models.RecursosHumanos.GestorFestivos.EsFestivoRepartoPropio(d),
                    FechasVencidas = new ServicioFechasVencidas(repositorio, PlazosEntrega.RepositorioPlazosProveedores.DesdeConfiguracion(),
                        new CorreosSeguimiento.RepositorioCorreosSeguimiento(), JobsFestivos.EsFestivoAlgete,
                        registrar: ex => ElmahHelper.Log(ex, USUARIO_SISTEMA))
                });
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Proveedor vencido #606] Error en el job del correo a Compras: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job. Devuelve true si ha mandado el correo.</summary>
        internal static bool Procesar(DependenciasProveedoresVencidos deps)
        {
            // NestoAPI#780: en festivo se salta (es un correo a Compras y nadie lo lee); sale el siguiente laborable con lo que siga vencido.
            if (JobsFestivos.SaltarSiFestivo(MENSAJE_FESTIVO, USUARIO_SISTEMA, deps.EsFestivo, () => deps.Ahora, deps.Trazar))
            {
                return false;
            }
            DateTime hoy = deps.Ahora.Date;
            List<LineaCompraVencida> lineas = deps.Repositorio.LeerLineasVencidas(Constantes.Empresas.EMPRESA_POR_DEFECTO, null, hoy)
                ?? new List<LineaCompraVencida>();
            if (!lineas.Any())
            {
                return false;
            }
            // NestoAPI#865: antes del correo, la fecha nueva de cada línea (con el interruptor apagado, null: el correo de siempre)
            Dictionary<string, ResultadoFechaVencida> recalculos = null;
            if (deps.FechasVencidas != null)
            {
                try
                {
                    recalculos = deps.FechasVencidas.Recalcular(lineas, hoy);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Fechas vencidas #865] No se han podido recalcular las fechas vencidas: " + ex.Message, ex), USUARIO_SISTEMA);
                }
            }
            Dictionary<string, EsperaClientesProducto> esperas = deps.Repositorio.LeerEsperas(lineas.Select(l => l.Producto))
                ?? new Dictionary<string, EsperaClientesProducto>();
            DateTime? llegadaReposicion = null;
            foreach (EsperaClientesProducto espera in esperas.Values.Where(e => e != null && e.EnCaminoReposicion > 0))
            {
                espera.LlegadaReposicion = llegadaReposicion ?? (llegadaReposicion = ReposicionEnCaminoProveedorVencido.Llegada(hoy, deps.SinLlegadaReposicion));
            }
            using (MailMessage correo = ConstruirCorreo(lineas, esperas, hoy, recalculos))
            {
                if (!deps.Correo.EnviarCorreoSMTP(correo))
                {
                    ElmahHelper.Log(new Exception("[Proveedor vencido #606] No se ha podido mandar el correo a Compras."));
                    return false;
                }
            }
            return true;
        }

        internal static EsperaClientesProducto Espera(Dictionary<string, EsperaClientesProducto> esperas, LineaCompraVencida linea)
            => esperas != null && esperas.TryGetValue(RepositorioProveedorVencidoSql.ClaveEspera(linea.Producto, linea.Almacen), out EsperaClientesProducto e) ? e : null;

        internal static MailMessage ConstruirCorreo(List<LineaCompraVencida> lineas, Dictionary<string, EsperaClientesProducto> esperas, DateTime hoy,
            Dictionary<string, ResultadoFechaVencida> recalculos = null)
        {
            int conEspera = lineas.Count(l => (Espera(esperas, l)?.Faltan ?? 0) > 0);
            int pedidos = lineas.Select(l => $"{l.Empresa}/{l.Pedido}").Distinct().Count();
            int llamar = recalculos == null ? 0 : lineas.Count(l => Recalculo(recalculos, l)?.HayQueLlamar == true);
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Pedidos a proveedor con la fecha prevista vencida ({hoy.ToString("dd/MM/yyyy", es)}): {pedidos} " +
                    $"{(pedidos == 1 ? "pedido" : "pedidos")}, {conEspera} {(conEspera == 1 ? "línea" : "líneas")} con clientes esperando" +
                    (llamar > 0 ? $", {llamar} {(llamar == 1 ? "línea" : "líneas")} para llamar al proveedor" : string.Empty),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.COMPRAS);
            mail.Body = GenerarHtml(lineas, esperas, hoy, recalculos);
            return mail;
        }

        internal static ResultadoFechaVencida Recalculo(Dictionary<string, ResultadoFechaVencida> recalculos, LineaCompraVencida linea) =>
            recalculos != null && recalculos.TryGetValue(linea.Clave, out ResultadoFechaVencida r) ? r : null;

        internal const string INTRODUCCION = "<p>Estos pedidos a proveedor están enviados, no se han recibido y su fecha prevista ya ha pasado. Mientras no se " +
            "cambie, la fecha de entrega a la agencia que ven los vendedores supone que llegan mañana. Si tenéis otra fecha, ponedla en " +
            "el pedido de compra; si ya ha llegado, recibidlo.</p>";

        /// <summary>NestoAPI#865: con las fechas recalculadas (de usted, sin nombres de tablas).</summary>
        internal const string INTRODUCCION_RECALCULADA = "<p>Estos pedidos a proveedor están enviados y no se han recibido, y su fecha prevista ya había " +
            "pasado. A cada línea le hemos puesto una fecha nueva (columna «Nueva fecha», con el porqué), que es la que ven ya los vendedores: " +
            "no tiene que hacer nada, salvo llamar al proveedor en las líneas que lo indican (se les ha pasado la fecha más de una vez). " +
            "Si sabe otra fecha, póngala en el pedido de compra; si ya ha llegado, recíbalo.</p>";

        internal static string GenerarHtml(List<LineaCompraVencida> lineas, Dictionary<string, EsperaClientesProducto> esperas, DateTime hoy,
            Dictionary<string, ResultadoFechaVencida> recalculos = null)
        {
            List<LineaCompraVencida> conEspera = lineas.Where(l => (Espera(esperas, l)?.Faltan ?? 0) > 0)
                .OrderBy(l => l.FechaPrevista).ThenBy(l => l.Pedido).ThenBy(l => l.Orden).ToList();
            List<LineaCompraVencida> resto = lineas.Except(conEspera)
                .OrderBy(l => l.FechaPrevista).ThenBy(l => l.Pedido).ThenBy(l => l.Orden).ToList();
            // NestoAPI#865 (corte 2): en el resto, la columna de clientes solo si alguno espera a una reposición de tienda en camino
            bool restoConReposicion = resto.Any(l => (Espera(esperas, l)?.CubiertoPorReposicion ?? 0) > 0);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine(recalculos == null ? INTRODUCCION : INTRODUCCION_RECALCULADA);
            sb.AppendLine("<h2 style='color:#b00'>Con pedidos de clientes esperando</h2>");
            if (conEspera.Any())
            {
                Tabla(sb, conEspera, esperas, hoy, true, recalculos);
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }
            sb.AppendLine("<h2>Resto</h2>");
            if (resto.Any())
            {
                Tabla(sb, resto, esperas, hoy, restoConReposicion, recalculos);
            }
            else
            {
                sb.AppendLine("<p>Ninguno.</p>");
            }
            sb.AppendLine(recalculos == null
                ? "<p style='color:#666'>Se calcula cada mañana en el momento de mandar el correo (NestoAPI#606).</p>"
                : "<p style='color:#666'>Se calcula cada mañana en el momento de mandar el correo (NestoAPI#606 y #865).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void Tabla(StringBuilder sb, List<LineaCompraVencida> lineas, Dictionary<string, EsperaClientesProducto> esperas, DateTime hoy, bool conClientes,
            Dictionary<string, ResultadoFechaVencida> recalculos)
        {
            sb.AppendLine("<table style='border-collapse: collapse;' cellpadding='4' border='1'>");
            sb.AppendLine("<tr style='background:#eee'><th>Pedido</th><th>Proveedor</th><th>Producto</th><th>Descripción</th><th>Almacén</th>" +
                "<th>Uds.</th><th>Prevista</th><th>Días de retraso</th>" + (recalculos != null ? "<th>Nueva fecha</th>" : string.Empty) +
                (conClientes ? "<th>Clientes esperando</th>" : string.Empty) + "</tr>");
            foreach (LineaCompraVencida l in lineas)
            {
                int retraso = (hoy.Date - l.FechaPrevista.Date).Days;
                sb.Append("<tr>")
                    .Append($"<td>{l.Pedido}</td>")
                    .Append($"<td>{Html(l.NombreProveedor)} ({Html(l.Proveedor)})</td>")
                    .Append($"<td>{Html(l.Producto)}</td>")
                    .Append($"<td>{Html(l.Descripcion)}</td>")
                    .Append($"<td>{Html(l.Almacen)}</td>")
                    .Append($"<td style='text-align:right'>{l.Cantidad}</td>")
                    .Append($"<td>{l.FechaPrevista.ToString("dd/MM/yyyy", es)}</td>")
                    .Append($"<td style='text-align:right'>{retraso}</td>");
                if (recalculos != null)
                {
                    sb.Append("<td>").Append(CeldaNuevaFecha(Recalculo(recalculos, l))).Append("</td>");
                }
                if (conClientes)
                {
                    sb.Append("<td>").Append(CeldaClientes(Espera(esperas, l))).Append("</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
        }

        /// <summary>
        /// La columna «Clientes esperando»: lo que falta de la compra y, aparte (NestoAPI#865, corte 2), lo que cubre una reposición de
        /// tienda en camino, con su llegada (no cuenta como falta de la compra).
        /// </summary>
        internal static string CeldaClientes(EsperaClientesProducto e)
        {
            if (e == null)
            {
                return string.Empty;
            }
            string pedidos = $"{(e.Pedidos.Count == 1 ? "pedido" : "pedidos")} {string.Join(", ", e.Pedidos.Take(10))}{(e.Pedidos.Count > 10 ? "…" : string.Empty)}";
            string reposicion = ReposicionEnCaminoProveedorVencido.Texto(e);
            if (e.Faltan <= 0 && reposicion == null)
            {
                return string.Empty;
            }
            var celda = new StringBuilder();
            if (e.Faltan > 0)
            {
                celda.Append($"<b>faltan {e.Faltan} {(e.Faltan == 1 ? "ud." : "uds.")}</b> (stock {e.Stock}, pendientes {e.Pendientes}); {pedidos}");
            }
            if (reposicion != null)
            {
                celda.Append(e.Faltan > 0 ? "<br>Aparte: " : string.Empty)
                    .Append(Html(reposicion + "."))
                    .Append(e.Faltan > 0 ? string.Empty : $" ({pedidos})");
            }
            return celda.ToString();
        }

        /// <summary>NestoAPI#865: la fecha nueva y por qué; desde el 2.º vencimiento, en rojo, que hay que llamar al proveedor.</summary>
        internal static string CeldaNuevaFecha(ResultadoFechaVencida r)
        {
            if (r == null)
            {
                return Html(TextosFechaVencida.SIN_RECALCULAR);
            }
            string texto = Html(r.Texto);
            if (r.Cambiada)
            {
                texto = $"<b>{Html(TextosFechaVencida.Dia(r.Recalculo.FechaNueva))}</b><br>{texto}";
            }
            return r.HayQueLlamar
                ? $"{texto}<br><b style='color:#b00'>{Html(TextosFechaVencida.Llamar(r.Vencimiento))}</b>"
                : texto;
        }

        private static string Html(string texto) => WebUtility.HtmlEncode(texto?.Trim() ?? string.Empty);
    }
}
