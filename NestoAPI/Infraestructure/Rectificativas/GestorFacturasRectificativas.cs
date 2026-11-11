using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// Verifactu #37: encuentra las facturas originales de las que provienen las unidades de
    /// una línea rectificativa (cliente + producto + cantidad), para poder vincularlas en
    /// LinFacturaVtaRectificacion cuando la rectificativa no nace de CopiarFactura (#38/#87).
    /// Criterio LIFO: la última compra primero. No se puede rectificar más de lo comprado ni
    /// lo ya rectificado por otra rectificativa.
    /// </summary>
    public class GestorFacturasRectificativas
    {
        private readonly NVEntities db;

        public GestorFacturasRectificativas(NVEntities db)
        {
            this.db = db;
        }

        /// <param name="facturaPreferida">
        /// NestoAPI#760: la factura que el cliente dice devolver (FacturaOriginal de la nota de recogida). Sus líneas van PRIMERO y
        /// lo que no quepa en ellas sigue por el LIFO de siempre. Null = solo LIFO.
        /// </param>
        /// <param name="importeARectificar">
        /// 11/11/26 (RV2600129): el importe (base, en positivo) de la línea rectificativa. Solo se usa en las cuentas de portes: la
        /// línea original tiene que llegar a ese importe. Null = sin mirar el importe.
        /// </param>
        public async Task<List<VinculacionRectificativa>> BuscarFacturasOriginales(
            string empresa, string cliente, string producto, decimal cantidadARectificar, string facturaPreferida = null,
            decimal? importeARectificar = null)
        {
            if (EsCuentaPortes(producto))
            {
                return await BuscarPortesOriginales(empresa, cliente, producto, cantidadARectificar, facturaPreferida, importeARectificar)
                    .ConfigureAwait(false);
            }

            // Solo líneas FACTURADAS y positivas (una negativa sería otra rectificativa)
            var lineasFacturadas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa
                    && l.Nº_Cliente == cliente
                    && l.Producto == producto
                    && l.Estado == Constantes.EstadosLineaVenta.FACTURA
                    && l.Cantidad > 0
                    && l.Nº_Factura != null)
                .OrderByDescending(l => l.Fecha_Factura)
                .Select(l => new { l.Nº_Factura, l.Nº_Orden, l.Cantidad })
                .ToListAsync().ConfigureAwait(false);

            if (!lineasFacturadas.Any())
            {
                return RepartirEntreCompras(producto, cantidadARectificar, new List<CompraOriginal>());
            }

            // Lo ya rectificado de esas facturas por rectificativas anteriores, por línea original
            List<string> numerosFactura = lineasFacturadas
                .Select(l => l.Nº_Factura.Trim()).Distinct().ToList();
            var yaRectificado = (await db.LinFacturaVtaRectificaciones
                .Where(r => r.Empresa == empresa && numerosFactura.Contains(r.FacturaOriginalNumero.Trim()))
                .ToListAsync().ConfigureAwait(false))
                .GroupBy(r => ClaveLinea(r.FacturaOriginalNumero, r.FacturaOriginalLinea))
                .ToDictionary(g => g.Key, g => g.Sum(r => r.CantidadRectificada));

            List<CompraOriginal> compras = lineasFacturadas
                .Select(l => new CompraOriginal
                {
                    Factura = l.Nº_Factura.Trim(),
                    Linea = l.Nº_Orden,
                    Cantidad = l.Cantidad ?? 0,
                    YaRectificada = yaRectificado.TryGetValue(ClaveLinea(l.Nº_Factura, l.Nº_Orden), out decimal rectificada)
                        ? rectificada : 0
                })
                .ToList();

            return RepartirEntreCompras(producto, cantidadARectificar, PreferidaPrimero(compras, facturaPreferida));
        }

        /// <summary>
        /// NestoAPI#760 (puro): las compras de <paramref name="facturaPreferida"/> delante, en su orden; detrás, las demás en el
        /// orden LIFO en que llegan. Sin factura preferida, la lista tal cual.
        /// </summary>
        internal static List<CompraOriginal> PreferidaPrimero(IEnumerable<CompraOriginal> comprasLifo, string facturaPreferida)
        {
            List<CompraOriginal> compras = (comprasLifo ?? Enumerable.Empty<CompraOriginal>()).ToList();
            string preferida = facturaPreferida?.Trim();
            if (string.IsNullOrEmpty(preferida))
            {
                return compras;
            }
            bool EsPreferida(CompraOriginal c) => string.Equals(c.Factura?.Trim(), preferida, StringComparison.OrdinalIgnoreCase);
            return compras.Where(EsPreferida).Concat(compras.Where(c => !EsPreferida(c))).ToList();
        }

        /// <summary>
        /// 11/11/26 (RV2600129, −5 € en 62400000 contra la NV2617524, que los llevaba en 62400002): las cuentas de portes (624xxxxx). El
        /// abono de unos portes puede ir a la cuenta genérica aunque la factura los llevara en la de la agencia.
        /// </summary>
        internal static bool EsCuentaPortes(string producto)
        {
            string cuenta = producto?.Trim();
            return cuenta != null && cuenta.Length == 8 && cuenta.StartsWith(PREFIJO_CUENTAS_PORTES, StringComparison.Ordinal) && cuenta.All(char.IsDigit);
        }

        internal const string PREFIJO_CUENTAS_PORTES = "624";

        /// <summary>Línea de portes facturada candidata (con su cuenta, su importe y su fecha).</summary>
        internal class PortesOriginales : CompraOriginal
        {
            public string Cuenta { get; set; }
            public decimal BaseImponible { get; set; }
            public DateTime? FechaFactura { get; set; }
        }

        /// <summary>
        /// 11/11/26: los portes del mismo cliente en CUALQUIER cuenta de portes: primero la misma cuenta, luego las demás 624, cada
        /// grupo de la más reciente a la más antigua; solo las que llegan al importe (si se sabe). La factura preferida, delante.
        /// </summary>
        private async Task<List<VinculacionRectificativa>> BuscarPortesOriginales(string empresa, string cliente, string producto,
            decimal cantidadARectificar, string facturaPreferida, decimal? importeARectificar)
        {
            var lineas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa
                    && l.Nº_Cliente == cliente
                    && l.Producto.StartsWith(PREFIJO_CUENTAS_PORTES)
                    && l.Estado == Constantes.EstadosLineaVenta.FACTURA
                    && l.Cantidad > 0
                    && l.Nº_Factura != null)
                .Select(l => new { l.Nº_Factura, l.Nº_Orden, l.Cantidad, l.Producto, l.Base_Imponible, l.Fecha_Factura })
                .ToListAsync().ConfigureAwait(false);
            List<PortesOriginales> candidatas = lineas
                .Where(l => EsCuentaPortes(l.Producto))
                .Select(l => new PortesOriginales
                {
                    Factura = l.Nº_Factura.Trim(),
                    Linea = l.Nº_Orden,
                    Cantidad = l.Cantidad ?? 0,
                    Cuenta = l.Producto.Trim(),
                    BaseImponible = l.Base_Imponible,
                    FechaFactura = l.Fecha_Factura
                })
                .ToList();
            if (candidatas.Count > 0)
            {
                List<string> numerosFactura = candidatas.Select(c => c.Factura).Distinct().ToList();
                Dictionary<string, decimal> yaRectificado = (await db.LinFacturaVtaRectificaciones
                    .Where(r => r.Empresa == empresa && numerosFactura.Contains(r.FacturaOriginalNumero.Trim()))
                    .ToListAsync().ConfigureAwait(false))
                    .GroupBy(r => ClaveLinea(r.FacturaOriginalNumero, r.FacturaOriginalLinea))
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.CantidadRectificada));
                foreach (PortesOriginales c in candidatas)
                {
                    c.YaRectificada = yaRectificado.TryGetValue(ClaveLinea(c.Factura, c.Linea), out decimal r) ? r : 0;
                }
            }
            return RepartirEntreCompras(producto, cantidadARectificar,
                PreferidaPrimero(OrdenarPortes(candidatas, producto, importeARectificar), facturaPreferida));
        }

        /// <summary>11/11/26 (puro): misma cuenta primero, luego las demás 624; más reciente primero; solo las que llegan al importe.</summary>
        internal static List<CompraOriginal> OrdenarPortes(IEnumerable<PortesOriginales> candidatas, string cuenta, decimal? importeARectificar)
        {
            string misma = cuenta?.Trim();
            decimal? importe = importeARectificar.HasValue ? Math.Abs(importeARectificar.Value) : (decimal?)null;
            return (candidatas ?? Enumerable.Empty<PortesOriginales>())
                .Where(c => c != null && (!importe.HasValue || Math.Abs(c.BaseImponible) >= importe.Value))
                .OrderBy(c => string.Equals(c.Cuenta?.Trim(), misma, StringComparison.Ordinal) ? 0 : 1)
                .ThenByDescending(c => c.FechaFactura ?? DateTime.MinValue)
                .ThenByDescending(c => c.Linea)
                .Cast<CompraOriginal>()
                .ToList();
        }

        private static string ClaveLinea(string factura, int linea) => $"{factura?.Trim()}|{linea}";

        /// <summary>Línea facturada candidata, con lo que ya le rectificaron antes.</summary>
        internal class CompraOriginal
        {
            public string Factura { get; set; }
            public int Linea { get; set; }
            public decimal Cantidad { get; set; }
            public decimal YaRectificada { get; set; }
        }

        /// <summary>
        /// Núcleo PURO del reparto: recorre las compras (ya ordenadas: última primero) y
        /// vincula de cada una lo disponible (cantidad - ya rectificada) hasta cubrir la
        /// cantidad a rectificar. Si no llega, lanza con el detalle de lo que falta.
        /// </summary>
        internal static List<VinculacionRectificativa> RepartirEntreCompras(
            string producto, decimal cantidadARectificar, IEnumerable<CompraOriginal> comprasLifo)
        {
            if (cantidadARectificar <= 0)
            {
                throw new ArgumentException(
                    $"La cantidad a rectificar debe ser positiva (recibida: {cantidadARectificar}).");
            }

            var vinculaciones = new List<VinculacionRectificativa>();
            decimal pendiente = cantidadARectificar;
            foreach (CompraOriginal compra in comprasLifo)
            {
                if (pendiente <= 0)
                {
                    break;
                }
                decimal disponible = compra.Cantidad - compra.YaRectificada;
                if (disponible <= 0)
                {
                    continue;
                }
                decimal vincular = Math.Min(disponible, pendiente);
                vinculaciones.Add(new VinculacionRectificativa
                {
                    FacturaOriginalNumero = compra.Factura,
                    FacturaOriginalLinea = compra.Linea,
                    CantidadRectificada = vincular
                });
                pendiente -= vincular;
            }

            if (pendiente > 0)
            {
                throw new InvalidOperationException(
                    $"No se encontraron facturas suficientes para rectificar {cantidadARectificar} " +
                    $"unidades del producto {producto?.Trim()}. Faltan {pendiente} unidades.");
            }

            return vinculaciones;
        }
    }
}
