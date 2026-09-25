using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public interface ISelectorRecordatoriosReposicion
    {
        /// <summary>
        /// Cálculo en seco (NestoAPI#532): a qué clientes se les recordaría hoy reponer qué productos.
        /// Solo lee: no manda nada ni registra nada.
        /// </summary>
        Task<ResultadoRecordatorioReposicionDTO> Calcular(string empresa, DateTime hoy, string consumibles = null);
    }

    /// <summary>
    /// NestoAPI#532: lee de la BD lo que necesita <see cref="CalculadoraReposicion"/>.
    ///
    /// CUIDADO CON LA CARGA: la consulta de compras recorre 24 meses de LinPedidoVta (unos 2,7 millones de
    /// filas en total). Está escrita para que la cubra ENTERA el índice
    /// <c>_dta_index_LinPedidoVta_..._K7_K4_K1_K35_11_16_21_23_31</c> (Producto, Nº Cliente, Empresa, Estado
    /// + Cantidad, Base Imponible y Fecha Albarán incluidas): un recorrido de ese índice, sin búsquedas en
    /// la tabla. Medido el 25/09/26: ~41.000 lecturas lógicas, ~3 s, 170.000 filas (cliente, producto,
    /// día). NO añadir columnas ni condiciones que no estén en ese índice (TipoLinea, Grupo, Familia...):
    /// cada fila haría una búsqueda en el índice agrupado y la consulta pasaría de segundos a minutos.
    /// El filtro de grupo/subgrupo se hace en memoria con la ficha del producto.
    /// </summary>
    public class SelectorRecordatoriosReposicion : ISelectorRecordatoriosReposicion
    {
        internal const string SQL_COMPRAS = @"
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto,
       CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS Dia, SUM(l.[Base Imponible]) AS BaseImponible
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Estado >= 2 AND l.Cantidad > 0 AND l.Producto IS NOT NULL
  AND l.[Fecha Albarán] >= @desde AND l.[Fecha Albarán] < @hasta
GROUP BY l.[Nº Cliente], l.Producto, CAST(l.[Fecha Albarán] AS date)";

        // Pedidos pendientes/en curso (pocos cientos de líneas) y notas de entrega de los últimos 24 meses.
        // Estado es la clave del índice agrupado: es una búsqueda por rango, no un recorrido.
        internal const string SQL_PENDIENTES = @"
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto, l.Estado, l.[Fecha Entrega] AS Fecha
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Estado BETWEEN -2 AND 1 AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Producto IS NOT NULL
  AND (l.Estado >= -1 OR l.[Fecha Entrega] >= @desde)";

        private const int TAMANO_LOTE_CLIENTES = 500;

        private readonly NVEntities db;
        private readonly Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock;

        public SelectorRecordatoriosReposicion(NVEntities db, Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock = null)
        {
            this.db = db;
            this.leerSinStock = leerSinStock ?? LeerSinStockBd;
        }

        public async Task<ResultadoRecordatorioReposicionDTO> Calcular(string empresa, DateTime hoy, string consumibles = null)
        {
            DateTime fechaHoy = hoy.Date;
            DateTime desde = CalculadoraReposicion.HistorialDesde(fechaHoy);
            ConsumiblesReposicion listaConsumibles = ConsumiblesReposicion.Leer(consumibles);

            List<CompraDiaReposicion> compras = await db.Database.SqlQuery<CompraDiaReposicion>(SQL_COMPRAS,
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@desde", desde),
                    new SqlParameter("@hasta", fechaHoy.AddDays(1)))
                .ToListAsync().ConfigureAwait(false);

            List<LineaPendienteReposicion> pendientes = await db.Database.SqlQuery<LineaPendienteReposicion>(SQL_PENDIENTES,
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@desde", desde))
                .ToListAsync().ConfigureAwait(false);

            // Toda la tabla de productos (pocas decenas de miles de filas estrechas): más barato que un
            // Contains con miles de referencias.
            List<ProductoReposicion> fichas = await db.Productos
                .Where(p => p.Empresa == empresa)
                .Select(p => new ProductoReposicion
                {
                    Producto = p.Número,
                    Nombre = p.Nombre,
                    Grupo = p.Grupo,
                    SubGrupo = p.SubGrupo,
                    Familia = p.Familia,
                    Estado = p.Estado ?? 0,
                    Ficticio = p.Ficticio
                })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, ProductoReposicion> productos = fichas
                .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            List<CandidatoReposicionDTO> candidatos = CalculadoraReposicion.EvaluarPares(compras, pendientes, productos,
                listaConsumibles, fechaHoy);

            List<string> productosAvisables = candidatos.Where(c => c.SeAvisaria)
                .Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (productosAvisables.Any())
            {
                CalculadoraReposicion.AplicarSinStock(candidatos, leerSinStock(productosAvisables));
            }

            List<string> clientesAvisables = candidatos.Where(c => c.SeAvisaria)
                .Select(c => c.Cliente).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, ClienteReposicion> clientes = await LeerClientes(empresa, clientesAvisables).ConfigureAwait(false);

            return CalculadoraReposicion.Agrupar(candidatos, clientes, fechaHoy, listaConsumibles.Definicion);
        }

        /// <summary>
        /// Ficha principal, correo (el primero no vacío de sus personas de contacto, como #74) y nombre
        /// del vendedor. Por lotes para que el IN no se dispare.
        /// </summary>
        private async Task<Dictionary<string, ClienteReposicion>> LeerClientes(string empresa, List<string> ids)
        {
            var resultado = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i += TAMANO_LOTE_CLIENTES)
            {
                List<string> lote = ids.Skip(i).Take(TAMANO_LOTE_CLIENTES).ToList();

                var fichas = await db.Clientes
                    .Where(c => c.Empresa == empresa && c.ClientePrincipal && lote.Contains(c.Nº_Cliente))
                    .Select(c => new
                    {
                        c.Nº_Cliente,
                        c.Nombre,
                        c.Estado,
                        c.Vendedor,
                        VendedorNombre = db.Vendedores
                            .Where(v => v.Empresa == c.Empresa && v.Número == c.Vendedor)
                            .Select(v => v.Descripción)
                            .FirstOrDefault()
                    })
                    .ToListAsync().ConfigureAwait(false);

                var correos = await db.PersonasContactoClientes
                    // Carlos (25/09/26): es un correo comercial: solo a quien tiene marcado «Enviar boletín»
                    .Where(p => p.Empresa == empresa && lote.Contains(p.NºCliente) && p.EnviarBoletin
                        && p.CorreoElectrónico != null && p.CorreoElectrónico.Trim() != "")
                    .GroupBy(p => p.NºCliente)
                    .Select(g => new { Cliente = g.Key, Email = g.FirstOrDefault().CorreoElectrónico })
                    .ToListAsync().ConfigureAwait(false);
                Dictionary<string, string> correoPorCliente = correos
                    .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Email?.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var ficha in fichas)
                {
                    string id = ficha.Nº_Cliente.Trim();
                    if (resultado.ContainsKey(id))
                    {
                        continue;
                    }
                    resultado[id] = new ClienteReposicion
                    {
                        Cliente = id,
                        Nombre = ficha.Nombre?.Trim(),
                        Estado = ficha.Estado ?? 0,
                        Vendedor = ficha.Vendedor?.Trim(),
                        VendedorNombre = ficha.VendedorNombre?.Trim(),
                        Email = correoPorCliente.TryGetValue(id, out string email) ? email : null
                    };
                }
            }
            return resultado;
        }

        private static ISet<string> LeerSinStockBd(IReadOnlyCollection<string> productos)
        {
            ResumenStocksProductos resumen = new ServicioGestorStocks().LeerResumenStocks(productos);
            return CalculadoraReposicion.ProductosSinStock(resumen, productos);
        }
    }
}
