using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.Caching;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#866 (corte 1): los clientes de la cartera con sus productos habituales en oferta hoy.</summary>
    public interface IImpulsoOfertasContacto
    {
        /// <summary>Clave de la cartera → cliente con sus productos (vacío si el interruptor está apagado o no hay ofertas).</summary>
        Task<Dictionary<string, ClienteConOfertas>> Leer(string vendedor, IList<ClienteCarteraContacto> cartera, DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#866 (corte 1): lee lo que necesita <see cref="ReglasImpulsoOfertasContacto"/>.
    /// <list type="bullet">
    /// <item>Interruptor: ParametrosUsuario empresa 1, «(defecto)», <see cref="ReglasImpulsoOfertasContacto.CLAVE_INTERRUPTOR"/>
    /// (sin fila o «1» = encendido; «0»/false/no/off/apagado = apagado al momento).</item>
    /// <item>Ofertas del día (<see cref="LeerOfertasDelDia"/>): DescuentosProducto y OfertasPermitidas con la regla única de vigencia
    /// (<c>Vigencia.Vigentes</c> / <c>Vigencia.EsVigente</c>, la misma que la validación de pedidos y <c>ServicioPrecios.ParaDia</c>) y
    /// los productos a los que alcanzan. Caché de 10 min por día (igual para todos los vendedores).</item>
    /// <item>Compras de esos productos por la cartera del vendedor en 12 meses: UNA consulta por vendedor (<see cref="SQL_COMPRAS"/>,
    /// la misma asignación que la cartera, <see cref="RepositorioCarteraContactoSql.SQL_ASIGNACION"/>).</item>
    /// </list>
    /// </summary>
    public class ImpulsoOfertasContactoSql : IImpulsoOfertasContacto
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);
        private const string USUARIO_DEFECTO = "(defecto)";
        private static readonly MemoryCache cache = MemoryCache.Default;

        private readonly Func<bool> encendido;
        private readonly Func<DateTime, Dictionary<string, OfertaProductoDia>> ofertasDelDia;

        public ImpulsoOfertasContactoSql() : this(null, null)
        {
        }

        internal ImpulsoOfertasContactoSql(Func<bool> encendido, Func<DateTime, Dictionary<string, OfertaProductoDia>> ofertasDelDia)
        {
            this.encendido = encendido ?? Encendido;
            this.ofertasDelDia = ofertasDelDia ?? OfertasDelDiaCacheadas;
        }

        public Task<Dictionary<string, ClienteConOfertas>> Leer(string vendedor, IList<ClienteCarteraContacto> cartera, DateTime hoy)
        {
            return Task.Run(() =>
            {
                if (cartera == null || cartera.Count == 0 || !encendido())
                {
                    return new Dictionary<string, ClienteConOfertas>(StringComparer.OrdinalIgnoreCase);
                }
                Dictionary<string, OfertaProductoDia> ofertas = ofertasDelDia(hoy.Date);
                if (ofertas == null || ofertas.Count == 0)
                {
                    return new Dictionary<string, ClienteConOfertas>(StringComparer.OrdinalIgnoreCase);
                }
                List<CompraProductoCliente> compras = LeerCompras(vendedor, hoy.Date, ofertas.Keys);
                return ReglasImpulsoOfertasContacto.Evaluar(cartera, compras, ofertas, hoy.Date);
            });
        }

        internal static bool Encendido()
        {
            try
            {
                return ReglasImpulsoOfertasContacto.Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, ReglasImpulsoOfertasContacto.CLAVE_INTERRUPTOR));
            }
            catch
            {
                return true;
            }
        }

        private static Dictionary<string, OfertaProductoDia> OfertasDelDiaCacheadas(DateTime dia)
        {
            string clave = $"SugerenciasContacto:OfertasDelDia:{dia:yyyyMMdd}";
            if (cache.Get(clave) is Dictionary<string, OfertaProductoDia> enCache)
            {
                return enCache;
            }
            Dictionary<string, OfertaProductoDia> ofertas = LeerOfertasDelDia(dia);
            _ = cache.Add(clave, ofertas, DateTimeOffset.Now.Add(DURACION_CACHE));
            return ofertas;
        }

        /// <summary>
        /// Las ofertas vigentes <paramref name="dia"/> y los productos a los que alcanzan (ver <see cref="ReglasImpulsoOfertasContacto"/>):
        /// filas globales de DescuentosProducto desde 1 unidad que «parecen campaña» (fechas, audiencia o nombre de campaña, como
        /// GET api/Campanas?soloCampanas=true) y N+M de OfertasPermitidas sin cliente, con fechas o (18/11/26) sin fechas y modificadas en 90 días.
        /// </summary>
        internal static Dictionary<string, OfertaProductoDia> LeerOfertasDelDia(DateTime dia)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;

                List<FilaOfertaDia> filas = Vigencia.Vigentes(db.DescuentosProductoes, dia)
                    .Where(d => d.Empresa == empresa
                        && (d.Nº_Cliente == null || d.Nº_Cliente.Trim() == string.Empty)
                        && (d.NºProveedor == null || d.NºProveedor.Trim() == string.Empty)
                        && d.CantidadMínima < 2
                        && (d.AudienciaOferta > 0 || d.FechaDesde != null || d.FechaHasta != null || d.Campana != null))
                    .Select(d => new FilaOfertaDia
                    {
                        Producto = d.Nº_Producto,
                        Familia = d.Familia,
                        Grupo = d.GrupoProducto,
                        SubGrupo = d.SubGrupoProducto,
                        FiltroProducto = d.FiltroProducto,
                        Descuento = d.Descuento,
                        Precio = d.Precio,
                        FechaDesde = d.FechaDesde,
                        FechaHasta = d.FechaHasta
                    })
                    .ToList();

                // Como ServicioPrecios.BuscarOfertasPermitidas: la vigencia en memoria con la regla única (son pocas filas).
                // 18/11/26: también las N+M SIN fechas modificadas en los últimos 90 días (ReglasImpulsoOfertasContacto.CuentaOfertaNm).
                filas.AddRange(db.OfertasPermitidas
                    .Where(o => o.Empresa == empresa && !o.Denegar && (o.Cliente == null || o.Cliente.Trim() == string.Empty) && o.CantidadRegalo > 0 && o.CantidadConPrecio > 0)
                    .ToList()
                    .Where(o => ReglasImpulsoOfertasContacto.CuentaOfertaNm(o.FechaDesde, o.FechaHasta, o.FechaModificación, dia))
                    .Select(o => new FilaOfertaDia
                    {
                        Producto = o.Número,
                        Familia = o.Familia,
                        SubGrupo = o.SubGrupo,
                        FiltroProducto = o.FiltroProducto,
                        CantidadConPrecio = o.CantidadConPrecio,
                        CantidadRegalo = o.CantidadRegalo,
                        FechaDesde = o.FechaDesde,
                        FechaHasta = o.FechaHasta,
                        FechaModificacion = o.FechaDesde == null && o.FechaHasta == null ? o.FechaModificación : (DateTime?)null
                    }));

                if (!filas.Any())
                {
                    return new Dictionary<string, OfertaProductoDia>(StringComparer.OrdinalIgnoreCase);
                }
                List<string> productos = Distintos(filas.Select(f => f.Producto));
                List<string> familias = Distintos(filas.Where(f => string.IsNullOrWhiteSpace(f.Producto)).Select(f => f.Familia));
                List<string> grupos = Distintos(filas.Where(f => string.IsNullOrWhiteSpace(f.Producto) && string.IsNullOrWhiteSpace(f.Familia)).Select(f => f.Grupo));
                List<ProductoOfertable> candidatos = db.Productos
                    .Where(p => p.Empresa == empresa && (productos.Contains(p.Número) || familias.Contains(p.Familia) || grupos.Contains(p.Grupo)))
                    .Select(p => new ProductoOfertable
                    {
                        Producto = p.Número,
                        Nombre = p.Nombre,
                        Familia = p.Familia,
                        Grupo = p.Grupo,
                        SubGrupo = p.SubGrupo,
                        PVP = p.PVP
                    })
                    .ToList();
                return ReglasImpulsoOfertasContacto.OfertasPorProducto(filas, candidatos);
            }
        }

        private static List<string> Distintos(IEnumerable<string> valores)
            => valores.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Las compras (un día de pedido por fila, facturado o en curso) de los productos en oferta hoy por los clientes de la cartera
        /// del vendedor en 12 meses, con la misma asignación y el mismo filtro de grupo que la cartera. Sin muestras (MMP).
        /// </summary>
        internal const string SQL_COMPRAS = @"
            SET NOCOUNT ON;
            DECLARE @Hace12Meses date = DATEADD(month, -12, @Hoy);
            CREATE TABLE #Ofertas (Producto varchar(20) COLLATE DATABASE_DEFAULT NOT NULL PRIMARY KEY);
            INSERT INTO #Ofertas (Producto)
            SELECT DISTINCT LTRIM(RTRIM(x.p.value('.', 'varchar(20)'))) FROM @Productos.nodes('/o/p') x(p);
" + RepositorioCarteraContactoSql.SQL_ASIGNACION + @"

            SELECT c.[Nº Cliente] Cliente, c.Contacto, a.TodosLosGrupos
            INTO #Cartera
            FROM Clientes c WITH (NOLOCK)
            INNER JOIN #Asignacion a ON a.Cliente = c.[Nº Cliente] AND a.Contacto = c.Contacto
            WHERE c.Empresa = '1' AND c.Estado >= 0 AND c.Estado NOT IN (" + RepositorioCarteraContactoSql.ESTADOS_EXCLUIDOS_SQL + @");
            CREATE CLUSTERED INDEX IX_Cartera ON #Cartera (Cliente, Contacto);

            SELECT l.[Nº Cliente] Cliente, l.Contacto, RTRIM(l.Producto) Producto, CAST(cab.Fecha AS date) Dia, SUM(l.[Base Imponible]) Importe
            FROM LinPedidoVta l WITH (NOLOCK)
            INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
            INNER JOIN #Cartera x ON x.Cliente = l.[Nº Cliente] AND x.Contacto = l.Contacto
            INNER JOIN #Ofertas o ON o.Producto = l.Producto
            WHERE l.Empresa = '1' AND (l.Estado BETWEEN -1 AND 2 OR l.Estado = 4) AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'
                AND cab.Fecha >= @Hace12Meses AND cab.Fecha < DATEADD(day, 1, @Hoy)
                AND " + RepositorioCarteraContactoSql.FILTRO_GRUPO_LINEA + @"
            GROUP BY l.[Nº Cliente], l.Contacto, RTRIM(l.Producto), CAST(cab.Fecha AS date);";

        /// <summary>«&lt;o&gt;&lt;p&gt;45929&lt;/p&gt;…&lt;/o&gt;» (los productos van en XML: sin STRING_SPLIT ni tipos de tabla en la BD).</summary>
        internal static string XmlProductos(IEnumerable<string> productos)
        {
            var xml = new StringBuilder("<o>");
            foreach (string producto in (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _ = xml.Append("<p>").Append(SecurityElement.Escape(producto.Trim())).Append("</p>");
            }
            return xml.Append("</o>").ToString();
        }

        private static List<CompraProductoCliente> LeerCompras(string vendedor, DateTime hoy, IEnumerable<string> productos)
        {
            return ReintentosSql.ReintentarSiDeadlock(() =>
            {
                var compras = new List<CompraProductoCliente>();
                string cadena;
                using (var contexto = new NVEntities())
                {
                    cadena = contexto.Database.Connection.ConnectionString;
                }
                using (var conexion = new SqlConnection(cadena))
                using (var comando = new SqlCommand(SQL_COMPRAS, conexion) { CommandTimeout = RepositorioCarteraContactoSql.TIMEOUT_SEGUNDOS })
                {
                    conexion.Open();
                    _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = vendedor.Trim() });
                    _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.Date) { Value = hoy.Date });
                    _ = comando.Parameters.Add(new SqlParameter("@Productos", SqlDbType.Xml) { Value = XmlProductos(productos) });
                    using (SqlDataReader lector = comando.ExecuteReader())
                    {
                        while (lector.Read())
                        {
                            compras.Add(new CompraProductoCliente
                            {
                                Cliente = lector["Cliente"].ToString().Trim(),
                                Contacto = lector["Contacto"].ToString().Trim(),
                                Producto = lector["Producto"].ToString().Trim(),
                                Dia = Convert.ToDateTime(lector["Dia"]).Date,
                                Importe = Convert.ToDecimal(lector["Importe"])
                            });
                        }
                    }
                }
                return compras;
            });
        }
    }
}
