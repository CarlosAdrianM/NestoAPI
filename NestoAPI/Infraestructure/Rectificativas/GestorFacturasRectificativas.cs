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

        public async Task<List<VinculacionRectificativa>> BuscarFacturasOriginales(
            string empresa, string cliente, string producto, decimal cantidadARectificar)
        {
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

            return RepartirEntreCompras(producto, cantidadARectificar, compras);
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
