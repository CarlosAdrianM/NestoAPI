using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#898 (Carlos 26/11/26): lo que hace falta saber de un pedido para decidir si su P.O. se puede facturar.
    /// </summary>
    public class DatosPOEnteraPedido
    {
        public int Pedido { get; set; }
        public string SuPedido { get; set; }
        public string Cliente { get; set; }
        /// <summary>Le queda alguna línea en estado -1..1 con cantidad ≠ 0.</summary>
        public bool QuedanSinEntregar { get; set; }
        /// <summary>Otro pedido del mismo cliente y la misma P.O. con líneas -1..2 con cantidad ≠ 0 (el de menor número), o null.</summary>
        public int? OtroPedido { get; set; }
    }

    /// <summary>
    /// NestoAPI#898 (Carlos 26/11/26): LA REGLA «una P.O. va en una sola factura», la misma que lleva prdCrearFacturaVta
    /// (<c>Scripts/Issue898_FinDeMesNestoViejo_y_POEntera.sql</c> + <c>Issue898b_POEntera_PorPedido.sql</c>), POR PEDIDO: cualquier
    /// pedido con «Su pedido» relleno. No se factura si (a) le queda algo sin entregar (-1..1 con cantidad) o (b) otro pedido del
    /// mismo cliente con la MISMA P.O. (<see cref="ReglaPOAgrupacion.Clave"/>: sin los espacios de los extremos) tiene líneas -1..2.
    /// Pura: sin BD. La API la aplica ANTES del procedimiento, con mensaje propio; el procedimiento sigue siendo la última barrera.
    /// </summary>
    public static class ReglaPOEntera
    {
        public const string CODIGO = "FACTURACION_PO_NO_ENTERA";

        /// <summary>¿Hay que mirar este pedido? Solo si lleva «Su pedido».</summary>
        public static bool Aplica(string suPedido) => ReglaPOAgrupacion.TienePO(suPedido);

        /// <summary>Por qué no se puede facturar el pedido, o <c>null</c> si se puede (o no lleva P.O.).</summary>
        public static string MotivoNoSePuedeFacturar(DatosPOEnteraPedido datos)
        {
            if (datos == null || !Aplica(datos.SuPedido))
            {
                return null;
            }
            string po = datos.SuPedido.Trim();
            if (datos.QuedanSinEntregar)
            {
                return $"El pedido {datos.Pedido} es de la P.O. {po} y no está todo entregado: una P.O. va en una sola factura. Entregue lo que falta, o quítelo del pedido, y vuelva a facturar.";
            }
            if (datos.OtroPedido.HasValue)
            {
                return $"La P.O. {po} tiene más pedidos sin facturar (el {datos.OtroPedido.Value}, por ejemplo): agrúpelos antes por P.O. cuando estén todos entregados.";
            }
            return null;
        }

        /// <summary>
        /// La parte (a) en memoria, para Facturar rutas: con las líneas recargadas después del albarán (antes del albarán, lo
        /// que está en curso todavía cuenta como «sin entregar», así que la lectura de la tanda solo vale para la (b)).
        /// </summary>
        public static bool QuedanSinEntregar(IEnumerable<LinPedidoVta> lineas)
        {
            return (lineas ?? Enumerable.Empty<LinPedidoVta>())
                .Any(l => l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && (l.Cantidad ?? 0) != 0);
        }
    }

    /// <summary>
    /// NestoAPI#898: interruptor ParametrosUsuario empresa 1 «(defecto)» <c>Facturacion.POSoloEntera</c>, como el procedimiento:
    /// sin fila o distinto de «0» = encendido. Se lee sin crear filas, con caché de 1 minuto.
    /// </summary>
    public static class InterruptorPOSoloEntera
    {
        public const string CLAVE = "Facturacion.POSoloEntera";
        internal const string USUARIO_DEFECTO = "(defecto)";
        internal static readonly TimeSpan CACHE = TimeSpan.FromMinutes(1);

        private static readonly object candado = new object();
        private static bool? valor;
        private static DateTime leidoEl;

        /// <summary>Para los tests: null = leer ParametrosUsuario.</summary>
        internal static Func<bool> Forzado { get; set; }

        public static bool Activo
        {
            get
            {
                if (Forzado != null)
                {
                    return Forzado();
                }
                lock (candado)
                {
                    if (valor.HasValue && DateTime.UtcNow - leidoEl < CACHE)
                    {
                        return valor.Value;
                    }
                    bool leido;
                    try
                    {
                        leido = Interpretar(ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE));
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[P.O. entera #898] No se ha podido leer {CLAVE}: se deja encendido. {ex.Message}", ex));
                        leido = true;
                    }
                    valor = leido;
                    leidoEl = DateTime.UtcNow;
                    return leido;
                }
            }
        }

        /// <summary>Como el procedimiento: apagado solo con «0»; sin fila, vacío o cualquier otro valor = encendido.</summary>
        internal static bool Interpretar(string texto) => texto?.Trim() != "0";

        internal static void Reiniciar()
        {
            lock (candado)
            {
                valor = null;
                Forzado = null;
            }
        }
    }

    /// <summary>
    /// NestoAPI#898: lee en UNA consulta los datos de la regla para una tanda de pedidos de una empresa (solo los que llevan
    /// «Su pedido»; los demás no salen). Sin NOLOCK: decide si se factura.
    /// </summary>
    public static class LectorPOEnteraSql
    {
        internal const string SQL = @"SELECT c.[Número] AS Pedido, RTRIM(LTRIM(c.SuPedido)) AS SuPedido, RTRIM(c.[Nº Cliente]) AS Cliente,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número]
        AND l.Estado BETWEEN -1 AND 1 AND l.Cantidad <> 0) THEN 1 ELSE 0 END AS bit) AS QuedanSinEntregar,
    (SELECT TOP 1 c2.[Número] FROM CabPedidoVta c2 INNER JOIN LinPedidoVta l2 ON l2.Empresa = c2.Empresa AND l2.[Número] = c2.[Número]
        WHERE c2.Empresa = c.Empresa AND c2.[Nº Cliente] = c.[Nº Cliente] AND c2.[Número] <> c.[Número]
          AND RTRIM(LTRIM(c2.SuPedido)) = RTRIM(LTRIM(c.SuPedido))
          AND l2.Estado BETWEEN -1 AND 2 AND l2.Cantidad <> 0
        ORDER BY c2.[Número]) AS OtroPedido
FROM CabPedidoVta c
WHERE c.Empresa = @empresa AND c.[Número] IN ({0}) AND c.SuPedido IS NOT NULL AND RTRIM(LTRIM(c.SuPedido)) <> ''";

        /// <summary>Pedido → datos (solo los que llevan P.O.). Lotes de 500 números (enteros: van en el texto).</summary>
        public static async Task<Dictionary<int, DatosPOEnteraPedido>> LeerAsync(NVEntities db, string empresa, IEnumerable<int> pedidos)
        {
            var resultado = new Dictionary<int, DatosPOEnteraPedido>();
            List<int> lista = (pedidos ?? Enumerable.Empty<int>()).Distinct().ToList();
            for (int i = 0; i < lista.Count; i += 500)
            {
                string numeros = string.Join(",", lista.Skip(i).Take(500));
                List<DatosPOEnteraPedido> filas = await db.Database
                    .SqlQuery<DatosPOEnteraPedido>(string.Format(SQL, numeros), new SqlParameter("@empresa", empresa))
                    .ToListAsync()
                    .ConfigureAwait(false);
                foreach (DatosPOEnteraPedido fila in filas)
                {
                    resultado[fila.Pedido] = fila;
                }
            }
            return resultado;
        }
    }
}
