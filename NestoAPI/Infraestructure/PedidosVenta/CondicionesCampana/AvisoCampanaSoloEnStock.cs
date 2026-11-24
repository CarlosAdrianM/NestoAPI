using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Lo que hay para servir un producto: stock de las sedes menos lo que deben los OTROS pedidos.</summary>
    public class DisponibleProductoCampana
    {
        public string Producto { get; set; }
        /// <summary>ALG + REI + ALC de las empresas 1 y 3 (ExtractoProducto) más lo que va de camino entre sedes (PreExtrProducto con traspaso).</summary>
        public int Stock { get; set; }
        /// <summary>Líneas -1/1 de otros pedidos (empresas 1 y 3) en esos almacenes.</summary>
        public int PendienteOtrosPedidos { get; set; }
        public int Disponible => Stock - PendienteOtrosPedidos;
    }

    /// <summary>Lee el disponible de varios productos en UNA consulta (sin contar el propio pedido).</summary>
    public interface ILectorDisponibleCampanaStock
    {
        Task<List<DisponibleProductoCampana>> Leer(string empresa, int pedido, IReadOnlyCollection<string> productos);
    }

    /// <summary>
    /// NestoAPI#889 (Black Friday 2026; Manuel 23/11/26, decisión de Carlos en el wizard): el descuento de una campaña marcada «solo en
    /// stock» (<see cref="CampanasCondicionStock"/>) es solo para lo que haya en stock. NO es estricto, como el aviso de contado: al
    /// crear o modificar el pedido, si una línea de producto de esa campaña no se puede servir con lo que hay, la respuesta del
    /// POST/PUT (<c>AvisosGrabacion</c> / <c>Avisos</c>) y el correo del pedido lo dicen, y decide el vendedor. Nunca bloquea, nunca
    /// quita el descuento y nunca rompe el guardado.
    /// <para><b>LA REGLA</b> (<see cref="Avisar"/>, pura): líneas de producto con cantidad, sin servir (estado -1 a 1), que llevan algún
    /// descuento (DescuentoProducto o DescuentoLinea &gt; 0) y cuyo producto está en una fila vigente hoy de la tabla. Se agrupan por
    /// producto (las cantidades se suman) y se avisa si lo disponible no llega a esa cantidad: «hoy no hay» (≤ 0) o «hoy hay 2 de 5».
    /// Disponible (Carlos 24/11/26) = stock de Algete, Reina y Alcobendas de las empresas 1 y 3 (más lo que va de camino entre sedes en
    /// una reposición, como <see cref="ServicioGestorStocks.UnidadesDisponiblesTodosLosAlmacenes"/>) menos lo pendiente de servir de
    /// OTROS pedidos en esos almacenes. Ni presupuestos ni pedidos de la tienda online.</para>
    /// </summary>
    public static class AvisoCampanaSoloEnStock
    {
        public const string TIPO = "CampanaSoloEnStock";

        public static readonly string[] ALMACENES = { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        /// <summary>Una línea que puede llevar el descuento de la campaña y aún no se ha servido.</summary>
        public static bool LineaCuenta(LineaPedidoVentaDTO l) =>
            l != null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0
            && l.estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.estado < Constantes.EstadosLineaVenta.ALBARAN
            && (l.DescuentoProducto > 0M || l.DescuentoLinea > 0M);

        /// <summary>Si el pedido se mira: ni presupuesto ni de la tienda online.</summary>
        public static bool PedidoCuenta(PedidoVentaDTO pedido) =>
            pedido != null && !pedido.EsPresupuesto && !VigenciaPedidoTienda.EsPedidoTiendaOnline(pedido);

        /// <summary>Los productos del pedido (sin repetir) que hay que leer: solo los de una campaña «solo en stock» vigente.</summary>
        public static List<string> ProductosALeer(PedidoVentaDTO pedido, IEnumerable<ProductoCampanaStock> filas, DateTime hoy)
        {
            if (!PedidoCuenta(pedido))
            {
                return new List<string>();
            }
            HashSet<string> enCampana = new HashSet<string>(
                CampanasCondicionStock.Vigentes(filas, pedido.empresa, hoy).Select(f => f.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            if (enCampana.Count == 0)
            {
                return new List<string>();
            }
            return (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => LineaCuenta(l) && enCampana.Contains(l.Producto.Trim()))
                .Select(l => l.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>LA REGLA (pura): el aviso, o null si todo lo que lleva el descuento se puede servir (o no hay nada de esas campañas).</summary>
        public static AvisoPedidoDTO Avisar(IEnumerable<LineaPedidoVentaDTO> lineas, string empresa, IEnumerable<ProductoCampanaStock> filas, DateTime hoy,
            IEnumerable<DisponibleProductoCampana> disponibles)
        {
            List<ProductoCampanaStock> vigentes = CampanasCondicionStock.Vigentes(filas, empresa, hoy);
            if (vigentes.Count == 0)
            {
                return null;
            }
            var campanaDe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductoCampanaStock f in vigentes.OrderBy(f => f.FechaDesde))
            {
                string producto = f.Producto.Trim();
                if (!campanaDe.ContainsKey(producto))
                {
                    campanaDe.Add(producto, f.Campana?.Trim());
                }
            }
            var disponiblePor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisponibleProductoCampana d in (disponibles ?? Enumerable.Empty<DisponibleProductoCampana>()).Where(d => !string.IsNullOrWhiteSpace(d?.Producto)))
            {
                string producto = d.Producto.Trim();
                disponiblePor[producto] = (disponiblePor.TryGetValue(producto, out int ya) ? ya : 0) + d.Disponible;
            }

            var grupos = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => LineaCuenta(l) && campanaDe.ContainsKey(l.Producto.Trim()))
                .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Producto = g.Key,
                    Nombre = g.Select(l => l.texto?.Trim()).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
                    Cantidad = g.Sum(l => l.Cantidad)
                })
                .ToList();

            var mensajes = new List<string>();
            var datos = new List<object>();
            foreach (var g in grupos)
            {
                // Sin fila leída = no hay nada (ni stock ni pendientes).
                int disponible = disponiblePor.TryGetValue(g.Producto, out int d) ? d : 0;
                if (disponible >= g.Cantidad)
                {
                    continue;
                }
                string campana = campanaDe[g.Producto];
                mensajes.Add(Mensaje(campana, g.Producto, g.Nombre, disponible, g.Cantidad));
                datos.Add(new { Producto = g.Producto, Campana = campana, g.Cantidad, Disponible = Math.Max(0, disponible) });
            }
            if (!mensajes.Any())
            {
                return null;
            }
            return new AvisoPedidoDTO
            {
                Tipo = TIPO,
                Mensaje = string.Join(" ", mensajes) + " El pedido se ha guardado igual, con el descuento: decida usted si lo mantiene.",
                Datos = datos
            };
        }

        /// <summary>«El descuento de Black Friday del 45929 (NOMBRE) es solo para lo que haya en stock: hoy no hay.» / «…: hoy hay 2 de 5.».</summary>
        public static string Mensaje(string campana, string producto, string nombre, int disponible, int cantidad)
        {
            string deCampana = string.IsNullOrWhiteSpace(campana) ? "de la campaña" : $"de {campana.Trim()}";
            string conNombre = string.IsNullOrWhiteSpace(nombre) ? producto : $"{producto} ({nombre.Trim()})";
            string cuanto = disponible <= 0
                ? "hoy no hay"
                : $"hoy hay {disponible.ToString(CultureInfo.InvariantCulture)} de {cantidad.ToString(CultureInfo.InvariantCulture)}";
            return $"El descuento {deCampana} del {conNombre} es solo para lo que haya en stock: {cuanto}.";
        }

        /// <summary>
        /// El aviso del pedido ya grabado. Solo lee de la BD si alguna línea es de una campaña «solo en stock» vigente (y entonces UNA
        /// consulta para todos sus productos). Nunca lanza ni bloquea: si algo falla, sin aviso y ELMAH.
        /// </summary>
        public static async Task<AvisoPedidoDTO> Calcular(PedidoVentaDTO pedido, DateTime hoy, ILectorDisponibleCampanaStock lector = null,
            IEnumerable<ProductoCampanaStock> filas = null, Action<Exception> registrar = null)
        {
            try
            {
                IEnumerable<ProductoCampanaStock> lasFilas = filas ?? CampanasCondicionStock.Actuales;
                List<string> productos = ProductosALeer(pedido, lasFilas, hoy);
                if (!productos.Any())
                {
                    return null;
                }
                ILectorDisponibleCampanaStock elLector = lector ?? LectorDisponibleCampanaStockSql.DesdeConfiguracion();
                List<DisponibleProductoCampana> disponibles = await elLector.Leer(pedido.empresa, pedido.numero, productos).ConfigureAwait(false);
                return Avisar(pedido.Lineas, pedido.empresa, lasFilas, hoy, disponibles);
            }
            catch (Exception ex)
            {
                try
                {
                    (registrar ?? (e => ElmahHelper.Log(e)))(new Exception(
                        $"[Campaña solo en stock #889] No se ha podido comprobar el stock del pedido {pedido?.numero}: {ex.Message}", ex));
                }
                catch
                {
                    // El registro nunca puede tumbar el guardado.
                }
                return null;
            }
        }
    }

    /// <summary>
    /// NestoAPI#889: el disponible por ADO, en UNA consulta (NOLOCK, parámetros VarChar como la sombra de #563, sin alistarse en la
    /// transacción que haya). Stock = ExtractoProducto de ALG/REI/ALC de las empresas 1 y 3 + PreExtrProducto con traspaso (lo que va
    /// de camino entre sedes; neto, como <see cref="ServicioGestorStocks.UnidadesDisponiblesTodosLosAlmacenes"/>). Pendiente = LinPedidoVta
    /// -1..1 de las empresas 1 y 3 en esos almacenes, SIN el propio pedido (sus líneas ya están grabadas).
    /// </summary>
    public class LectorDisponibleCampanaStockSql : ILectorDisponibleCampanaStock
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-CampanaSoloEnStock";
        private const int TIMEOUT_SEGUNDOS = 15;

        internal const string SQL_DISPONIBLE = @"
SELECT x.Producto, ISNULL(SUM(x.Stock), 0), ISNULL(SUM(x.Pendiente), 0)
FROM (
    SELECT RTRIM(e.Número) AS Producto, CAST(e.Cantidad AS int) AS Stock, 0 AS Pendiente
    FROM ExtractoProducto e WITH (NOLOCK)
    WHERE e.Empresa IN (@empresa1, @empresa3) AND e.Almacén IN (@alg, @rei, @alc) AND e.Número IN ({0})
    UNION ALL
    SELECT RTRIM(p.Número), CAST(p.Cantidad AS int), 0
    FROM PreExtrProducto p WITH (NOLOCK)
    WHERE p.Empresa IN (@empresa1, @empresa3) AND p.Número IN ({0}) AND p.NºTraspaso > 0
    UNION ALL
    SELECT RTRIM(l.Producto), 0, CAST(l.Cantidad AS int)
    FROM LinPedidoVta l WITH (NOLOCK)
    WHERE l.Empresa IN (@empresa1, @empresa3) AND l.Almacén IN (@alg, @rei, @alc) AND l.Producto IN ({0})
      AND l.Estado >= -1 AND l.Estado <= 1
      AND NOT (l.Empresa = @empresa AND l.Número = @pedido)
) x
GROUP BY x.Producto";

        private readonly string cadenaConexion;

        public LectorDisponibleCampanaStockSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion ?? throw new ArgumentNullException(nameof(cadenaConexion));
        }

        public static LectorDisponibleCampanaStockSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new LectorDisponibleCampanaStockSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = NOMBRE_APLICACION,
                Enlist = false
            }.ConnectionString);
        }

        /// <summary>La consulta con la lista de productos (@p0, @p1…).</summary>
        internal static string Sql(int productos) =>
            string.Format(SQL_DISPONIBLE, string.Join(", ", Enumerable.Range(0, Math.Max(1, productos)).Select(i => "@p" + i)));

        public async Task<List<DisponibleProductoCampana>> Leer(string empresa, int pedido, IReadOnlyCollection<string> productos)
        {
            var resultado = new List<DisponibleProductoCampana>();
            List<string> lista = (productos ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(Sql(lista.Count), conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                comando.Parameters.Add("@empresa", SqlDbType.VarChar, 3).Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = pedido;
                comando.Parameters.Add("@empresa1", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                comando.Parameters.Add("@empresa3", SqlDbType.VarChar, 3).Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
                comando.Parameters.Add("@alg", SqlDbType.VarChar, 3).Value = Constantes.Almacenes.ALGETE;
                comando.Parameters.Add("@rei", SqlDbType.VarChar, 3).Value = Constantes.Almacenes.REINA;
                comando.Parameters.Add("@alc", SqlDbType.VarChar, 3).Value = Constantes.Almacenes.ALCOBENDAS;
                for (int i = 0; i < lista.Count; i++)
                {
                    comando.Parameters.Add("@p" + i, SqlDbType.VarChar, 15).Value = lista[i];
                }
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await lector.ReadAsync().ConfigureAwait(false))
                    {
                        resultado.Add(new DisponibleProductoCampana
                        {
                            Producto = lector.IsDBNull(0) ? null : lector.GetString(0),
                            Stock = Convert.ToInt32(lector.GetValue(1)),
                            PendienteOtrosPedidos = Convert.ToInt32(lector.GetValue(2))
                        });
                    }
                }
            }
            return resultado;
        }
    }
}
