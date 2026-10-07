using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603 c3b: los datos crudos con los que se calculan las features del modelo de contactos.</summary>
    public interface IRepositorioFeaturesContacto
    {
        /// <summary>Historial (pedidos, rapports y ventas por grupo/mes, todo anterior a hoy) de cada cliente activo del vendedor.</summary>
        List<HistorialContacto> Leer(string vendedor, DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#603 c3b: consulta de inferencia del modelo de contactos. Sustituye a SQL_OBTENER_CLIENTES (#401) y replica la
    /// SQL del entrenamiento (ModeloLlamadaPedido\Datos\RepositorioDatosSql.cs) con hoy como día del contacto: solo trae
    /// datos crudos y las features se calculan en C# con <see cref="CalculadoraFeaturesContacto"/>, igual que al entrenar.
    /// <list type="bullet">
    /// <item>Clientes = los activos del vendedor (empresa 1, Estado ≥ 0, no 7/67), materializados en #Clientes con índice;
    /// todo lo demás se filtra contra ellos (patrón de #401).</item>
    /// <item>Pedidos = días distintos de CabPedidoVta.Fecha con líneas TipoLinea 1, base &gt; 0, NotaEntrega 0 y Estado ≥ -1
    /// (no solo lo facturado), de los últimos 24 meses y anteriores a hoy.</item>
    /// <item>Rapports T/V/W con Estado 0 de 24 meses (tasa de conversión y contactos previos).</item>
    /// <item>Cantidad por grupo+subgrupo y mes (sin MMP) de los 12 meses naturales anteriores al mes en curso.</item>
    /// <item>Última interacción (Estado 0 sin pedido, cualquier tipo): solo para el filtro de 7 días del endpoint antiguo.</item>
    /// </list>
    /// Todo con NOLOCK y parametrizado.
    /// </summary>
    public class RepositorioFeaturesContactoSql : IRepositorioFeaturesContacto
    {
        internal const int TIMEOUT_SEGUNDOS = 120;

        internal const string SQL = @"
            SET NOCOUNT ON;
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

            SELECT c.[Nº Cliente] Cliente, c.Contacto
            INTO #Clientes
            FROM Clientes c WITH (NOLOCK)
            WHERE c.Empresa = '1' AND c.Estado >= 0 AND c.Estado NOT IN (7, 67) AND c.Vendedor = @Vendedor;
            CREATE CLUSTERED INDEX IX_Clientes ON #Clientes (Cliente, Contacto);

            -- 0. Los clientes (también los que no tienen historial)
            SELECT Cliente, Contacto FROM #Clientes;

            -- 1. Rapports T/V/W con Estado 0 (tasa de conversión)
            SELECT s.Número Cliente, s.Contacto, s.Fecha, CAST(s.Pedido AS int) Pedido
            FROM SeguimientoCliente s WITH (NOLOCK)
            INNER JOIN #Clientes x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
            WHERE s.Fecha >= @DesdeHistorial AND s.Fecha < @Hoy AND s.Estado = 0 AND s.Tipo IN ('T', 'V', 'W');

            -- 2. Pedidos: un día distinto por cliente, con su importe
            SELECT cab.[Nº Cliente] Cliente, cab.Contacto, CAST(cab.Fecha AS date) Dia, SUM(l.[Base Imponible]) Importe
            FROM CabPedidoVta cab WITH (NOLOCK)
            INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = cab.Empresa AND l.Número = cab.Número
            INNER JOIN #Clientes x ON x.Cliente = cab.[Nº Cliente] AND x.Contacto = cab.Contacto
            WHERE cab.Empresa = '1' AND cab.Fecha >= @DesdeHistorial AND cab.Fecha < @Hoy
                AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
            GROUP BY cab.[Nº Cliente], cab.Contacto, CAST(cab.Fecha AS date);

            -- 3. Cantidad por grupo+subgrupo y mes (para el subgrupo más vendido)
            SELECT cab.[Nº Cliente] Cliente, cab.Contacto, YEAR(cab.Fecha) * 100 + MONTH(cab.Fecha) AnnoMes,
                l.Grupo + l.SubGrupo GrupoSubgrupo, SUM(l.Cantidad) Cantidad
            FROM CabPedidoVta cab WITH (NOLOCK)
            INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = cab.Empresa AND l.Número = cab.Número
            INNER JOIN #Clientes x ON x.Cliente = cab.[Nº Cliente] AND x.Contacto = cab.Contacto
            WHERE cab.Empresa = '1' AND cab.Fecha >= @DesdeVentas AND cab.Fecha < @InicioMes
                AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
                AND l.SubGrupo <> 'MMP'
            GROUP BY cab.[Nº Cliente], cab.Contacto, YEAR(cab.Fecha) * 100 + MONTH(cab.Fecha), l.Grupo + l.SubGrupo;

            -- 4. Última interacción (endpoint antiguo: Estado 0 sin pedido, cualquier tipo, hoy incluido)
            SELECT s.Número Cliente, s.Contacto, MAX(s.Fecha) Fecha
            FROM SeguimientoCliente s WITH (NOLOCK)
            INNER JOIN #Clientes x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
            WHERE s.Fecha >= @DesdeHistorial AND s.Estado = 0 AND s.Pedido = 0
            GROUP BY s.Número, s.Contacto;";

        /// <summary>Comando con los parámetros (vendedor nunca interpolado). Las fechas llevan un día de margen: el filtro exacto lo hace la calculadora.</summary>
        internal static SqlCommand CrearComando(SqlConnection conexion, string vendedor, DateTime hoy)
        {
            DateTime d = hoy.Date;
            DateTime inicioMes = new DateTime(d.Year, d.Month, 1);
            var comando = new SqlCommand(SQL, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };
            _ = comando.Parameters.Add(new SqlParameter("@Vendedor", SqlDbType.VarChar, 10) { Value = (object)vendedor?.Trim() ?? DBNull.Value });
            _ = comando.Parameters.Add(new SqlParameter("@Hoy", SqlDbType.DateTime) { Value = d });
            _ = comando.Parameters.Add(new SqlParameter("@DesdeHistorial", SqlDbType.DateTime) { Value = d.AddMonths(-CalculadoraFeaturesContacto.MESES_HISTORIAL).AddDays(-1) });
            _ = comando.Parameters.Add(new SqlParameter("@InicioMes", SqlDbType.DateTime) { Value = inicioMes });
            _ = comando.Parameters.Add(new SqlParameter("@DesdeVentas", SqlDbType.DateTime) { Value = inicioMes.AddMonths(-12) });
            return comando;
        }

        internal static string ClienteId(object cliente, object contacto) => $"{Convert.ToString(cliente).Trim()}/{Convert.ToString(contacto).Trim()}";

        public List<HistorialContacto> Leer(string vendedor, DateTime hoy)
        {
            return ReintentosSql.ReintentarSiDeadlock(() => LeerSinReintentos(vendedor, hoy));
        }

        private static List<HistorialContacto> LeerSinReintentos(string vendedor, DateTime hoy)
        {
            var clientes = new List<string>();
            var rapports = new List<KeyValuePair<string, RapportContactoCrudo>>();
            var pedidos = new List<KeyValuePair<string, PedidoDiaContacto>>();
            var ventas = new List<KeyValuePair<string, VentaGrupoMesContacto>>();
            var ultimaInteraccion = new Dictionary<string, DateTime>();

            string cadena;
            using (var contexto = new NVEntities())
            {
                cadena = contexto.Database.Connection.ConnectionString;
            }
            using (var conexion = new SqlConnection(cadena))
            using (SqlCommand comando = CrearComando(conexion, vendedor, hoy))
            {
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        clientes.Add(ClienteId(lector[0], lector[1]));
                    }
                    _ = lector.NextResult();
                    while (lector.Read())
                    {
                        rapports.Add(new KeyValuePair<string, RapportContactoCrudo>(ClienteId(lector[0], lector[1]),
                            new RapportContactoCrudo(lector.GetDateTime(2), Convert.ToInt32(lector[3]) != 0)));
                    }
                    _ = lector.NextResult();
                    while (lector.Read())
                    {
                        pedidos.Add(new KeyValuePair<string, PedidoDiaContacto>(ClienteId(lector[0], lector[1]),
                            new PedidoDiaContacto(lector.GetDateTime(2), Convert.ToDecimal(lector[3]))));
                    }
                    _ = lector.NextResult();
                    while (lector.Read())
                    {
                        ventas.Add(new KeyValuePair<string, VentaGrupoMesContacto>(ClienteId(lector[0], lector[1]),
                            new VentaGrupoMesContacto(Convert.ToInt32(lector[2]), Convert.ToString(lector[3]).Trim(), Convert.ToDecimal(lector[4]))));
                    }
                    _ = lector.NextResult();
                    while (lector.Read())
                    {
                        ultimaInteraccion[ClienteId(lector[0], lector[1])] = lector.GetDateTime(2);
                    }
                }
            }

            return Agrupar(clientes, rapports, pedidos, ventas, ultimaInteraccion);
        }

        internal static List<HistorialContacto> Agrupar(IEnumerable<string> clientes,
            IEnumerable<KeyValuePair<string, RapportContactoCrudo>> rapports,
            IEnumerable<KeyValuePair<string, PedidoDiaContacto>> pedidos,
            IEnumerable<KeyValuePair<string, VentaGrupoMesContacto>> ventas,
            IDictionary<string, DateTime> ultimaInteraccion)
        {
            ILookup<string, RapportContactoCrudo> porRapports = rapports.ToLookup(r => r.Key, r => r.Value);
            ILookup<string, PedidoDiaContacto> porPedidos = pedidos.ToLookup(p => p.Key, p => p.Value);
            ILookup<string, VentaGrupoMesContacto> porVentas = ventas.ToLookup(v => v.Key, v => v.Value);
            return clientes.Distinct().Select(id => new HistorialContacto(id, porPedidos[id], porRapports[id], porVentas[id],
                ultimaInteraccion.TryGetValue(id, out DateTime ultima) ? ultima : (DateTime?)null)).ToList();
        }
    }
}
