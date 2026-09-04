using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace NestoAPI.Infraestructure.Buscador
{
    /// <summary>Lo que ha facturado un cliente en el periodo.</summary>
    public class VentaCliente
    {
        public string Cliente { get; set; }
        public decimal Venta { get; set; }
    }

    public interface IRankingClientes
    {
        Dictionary<string, int> PosicionesPorVentas(string empresa);
    }

    /// <summary>
    /// NestoAPI#455: coloca a los clientes por lo que han comprado en el último año, para que el
    /// buscador pueda ordenar por ahí. Buscando "Carlos" salen todos los Carlos, del que más
    /// compra al que menos.
    ///
    /// <para><b>Se guarda la POSICIÓN, no los euros</b>, igual que hace el buscador de productos
    /// con <c>PosicionMasVendido</c>. Con importes, un solo cliente desbarata el ranking: hoy
    /// mismo el primero de la lista tiene 800.000 € de una única factura (NV2609667, producto
    /// 77100000, que parece un apunte especial y no una venta comercial), veinte veces más que el
    /// siguiente. Con posiciones, ese cliente es simplemente el primero y nada más.</para>
    ///
    /// <para>Se calcula entero en cada reindexado nocturno —tarda menos de un segundo sobre unos
    /// 3.500 clientes— así que <b>no hace falta ninguna tabla nueva ni script en producción</b>.</para>
    /// </summary>
    public class RankingClientes : IRankingClientes
    {
        internal const int DIAS_DE_VENTAS = 365;

        /// <summary>Solo cuentan las líneas facturadas.</summary>
        internal const int ESTADO_FACTURADO = 4;

        public Dictionary<string, int> PosicionesPorVentas(string empresa)
        {
            return AsignarPosiciones(LeerVentas(empresa));
        }

        /// <summary>
        /// De la lista de ventas a "cliente → puesto", empezando en 1. Se ordena también por
        /// número de cliente para que dos clientes con la misma venta no bailen entre noches.
        /// </summary>
        internal static Dictionary<string, int> AsignarPosiciones(IEnumerable<VentaCliente> ventas)
        {
            Dictionary<string, int> posiciones = new Dictionary<string, int>();
            if (ventas == null)
            {
                return posiciones;
            }

            int puesto = 0;
            foreach (VentaCliente venta in ventas
                .Where(v => !string.IsNullOrWhiteSpace(v?.Cliente))
                .OrderByDescending(v => v.Venta)
                .ThenBy(v => v.Cliente.Trim()))
            {
                string cliente = venta.Cliente.Trim();
                if (posiciones.ContainsKey(cliente))
                {
                    // La consulta agrupa por cliente, pero si alguna vez llegan repetidos nos
                    // quedamos con el mejor puesto en vez de reventar
                    continue;
                }

                posiciones.Add(cliente, ++puesto);
            }

            return posiciones;
        }

        private static List<VentaCliente> LeerVentas(string empresa)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;

                return db.Database.SqlQuery<VentaCliente>(
                    @"SELECT Cliente = LTRIM(RTRIM([Nº Cliente])), Venta = SUM([Base Imponible])
                      FROM LinPedidoVta
                      WHERE Empresa = @empresa
                        AND Estado >= @estado
                        AND [Fecha Factura] >= DATEADD(day, -@dias, GETDATE())
                      GROUP BY LTRIM(RTRIM([Nº Cliente]))",
                    new System.Data.SqlClient.SqlParameter("@empresa", empresa),
                    new System.Data.SqlClient.SqlParameter("@estado", ESTADO_FACTURADO),
                    new System.Data.SqlClient.SqlParameter("@dias", DIAS_DE_VENTAS))
                    .ToList();
            }
        }
    }
}
