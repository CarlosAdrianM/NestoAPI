using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Convierte lo leído de la BD (<see cref="DatosProductoPrecioMedio"/>) en el historial que entiende la
    /// calculadora (Issue #547, corte b). Puro: sin BD.
    /// </summary>
    public static class ConstructorHistorialPrecioMedio
    {
        private const short ESTADO_FACTURADA = 4;
        private const short ESTADO_ALBARAN = 2;

        /// <summary>
        /// Historial del producto tal como está AHORA en la BD.
        /// </summary>
        public static HistorialPrecioMedio Construir(DatosProductoPrecioMedio datos)
        {
            return Construir(datos, null, out _);
        }

        /// <summary>
        /// Historial del producto tal como lo vio el SP en su última pasada: las líneas cuya factura se creó (o
        /// se tocó) en o después de <paramref name="corteSP"/> se tratan como albaranes sin facturar (estado 2,
        /// sin nº de factura), que es lo que eran cuando pasó el SP. Sin corte, es el historial actual.
        /// <paramref name="lineasPosterioresAlCorte"/> cuenta las líneas facturadas así «desfacturadas».
        /// </summary>
        public static HistorialPrecioMedio Construir(DatosProductoPrecioMedio datos, DateTime? corteSP, out int lineasPosterioresAlCorte)
        {
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }

            int posteriores = 0;
            List<LineaCompraPrecioMedio> compras = new List<LineaCompraPrecioMedio>();
            foreach (LineaCompraBDPrecioMedio linea in datos.Compras ?? new List<LineaCompraBDPrecioMedio>())
            {
                short estado = linea.Estado ?? 0;
                int? factura = linea.NumeroFactura;
                if (corteSP.HasValue && EsFacturaPosteriorAlCorte(linea, corteSP.Value))
                {
                    posteriores++;
                    factura = null;
                    estado = ESTADO_ALBARAN;
                }
                compras.Add(new LineaCompraPrecioMedio
                {
                    Empresa = linea.Empresa?.Trim(),
                    NumeroFactura = factura,
                    FechaAlbaran = linea.FechaAlbaran,
                    NumeroAlbaran = linea.NumeroAlbaran,
                    NumeroOrden = linea.NumeroOrden,
                    Cantidad = linea.Cantidad ?? 0,
                    BaseImponible = linea.BaseImponible ?? 0,
                    Coste = linea.Coste,
                    Estado = estado
                });
            }
            lineasPosterioresAlCorte = posteriores;

            List<MovimientoStockPrecioMedio> movimientos = datos.Movimientos ?? new List<MovimientoStockPrecioMedio>();
            return new HistorialPrecioMedio
            {
                Empresa = datos.Empresa?.Trim(),
                EmpresaEspejo = datos.EmpresaEspejo?.Trim(),
                Producto = datos.Producto?.Trim(),
                Ficticio = datos.Ficha?.Ficticio ?? false,
                FechaUltimoMontaje = movimientos.Where(m => m.EsMontaje).Select(m => (DateTime?)m.Fecha).Max(),
                FechaUltimaRecepcion = movimientos.Where(m => m.EsRecepcion).Select(m => (DateTime?)m.Fecha).Max(),
                Compras = compras,
                StockHasta = CrearStockHasta(movimientos)
            };
        }

        /// <summary>
        /// Línea facturada cuya factura se creó (o modificó) en o después del corte: el SP no la vio facturada.
        /// </summary>
        public static bool EsFacturaPosteriorAlCorte(LineaCompraBDPrecioMedio linea, DateTime corteSP)
        {
            return linea.Estado == ESTADO_FACTURADA
                && linea.NumeroFactura.HasValue
                && linea.FechaModificacionFactura.HasValue
                && linea.FechaModificacionFactura.Value >= corteSP;
        }

        /// <summary>
        /// <c>sum(ExtractoProducto.Cantidad)</c> con <c>Fecha &lt;= fecha</c> (incluye todo lo del mismo instante).
        /// Sin apuntes hasta esa fecha → 0 (el NULL del SP acaba en el 0 por defecto de su temporal).
        /// Búsqueda binaria sobre los acumulados: los bestsellers tienen miles de fechas.
        /// </summary>
        public static Func<DateTime, int> CrearStockHasta(IEnumerable<MovimientoStockPrecioMedio> movimientos)
        {
            List<KeyValuePair<DateTime, int>> porFecha = (movimientos ?? Enumerable.Empty<MovimientoStockPrecioMedio>())
                .GroupBy(m => m.Fecha)
                .OrderBy(g => g.Key)
                .Select(g => new KeyValuePair<DateTime, int>(g.Key, g.Sum(m => m.Cantidad)))
                .ToList();

            DateTime[] fechas = new DateTime[porFecha.Count];
            int[] acumulados = new int[porFecha.Count];
            int acumulado = 0;
            for (int i = 0; i < porFecha.Count; i++)
            {
                acumulado += porFecha[i].Value;
                fechas[i] = porFecha[i].Key;
                acumulados[i] = acumulado;
            }

            return fecha =>
            {
                int indice = Array.BinarySearch(fechas, fecha);
                if (indice < 0)
                {
                    // ~indice = primera fecha mayor; la anterior es la última <= fecha
                    indice = ~indice - 1;
                }
                return indice < 0 ? 0 : acumulados[indice];
            };
        }
    }
}
