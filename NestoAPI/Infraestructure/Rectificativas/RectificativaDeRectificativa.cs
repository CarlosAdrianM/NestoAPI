using NestoAPI.Models.Rectificativas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// NestoAPI#892 (caso RV2600189, 24/11/26): rectificar una rectificativa SE PERMITE y se declara bien (Carlos). Alfredo deshizo
    /// el abono RV2600186 copiándolo invertido DENTRO del mismo pedido 931334, de serie RV: salió una RV con las líneas en positivo y
    /// sin vinculaciones, que Verifactu no enviaba. Una RV en positivo es una rectificativa «por diferencias» de la RV que corrige:
    /// cada línea positiva se vincula a la línea negativa de esa RV (mismo producto, cantidad contraria). Reglas puras.
    /// </summary>
    internal static class ReglaRectificativaDeRectificativa
    {
        /// <summary>
        /// Se invierte una rectificativa y el destino también es de serie rectificativa (añadir al pedido RV de la propia
        /// rectificativa). No es deshacer el abono con una factura normal (<see cref="GestorCopiaPedidos.EsDeshacerAbono"/>, que
        /// solo ocurre con pedido nuevo en NV/CV): es una rectificativa de la rectificativa.
        /// </summary>
        internal static bool EsRectificarRectificativa(CopiarFacturaRequest request, string serieOrigen, string serieDestino)
        {
            return request != null && request.InvertirCantidades
                && Models.Facturas.RegistroSeriesVerifactu.EsSerieRectificativa(serieOrigen)
                && Models.Facturas.RegistroSeriesVerifactu.EsSerieRectificativa(serieDestino);
        }

        /// <summary>
        /// ¿Esta línea copiada se vincula a su línea de origen? Lo de siempre: las negativas (abonan una venta). Al rectificar una
        /// rectificativa, las positivas (deshacen una línea del abono); las negativas que salgan de ahí no rectifican ningún abono.
        /// </summary>
        internal static bool SeVincula(LineaCopiadaDTO linea, bool rectificaRectificativa)
        {
            if (linea == null)
            {
                return false;
            }
            return rectificaRectificativa ? linea.CantidadCopiada > 0 : linea.CantidadCopiada < 0;
        }

        /// <summary>Una línea positiva de la rectificativa que hay que vincular.</summary>
        internal class LineaPositiva
        {
            public int NumeroLinea { get; set; }
            public string Producto { get; set; }
            public decimal Cantidad { get; set; }
        }

        /// <summary>Una línea negativa de una rectificativa anterior del mismo cliente (candidata a ser la que se corrige).</summary>
        internal class LineaAbonoAnterior
        {
            public string Factura { get; set; }
            public DateTime Fecha { get; set; }
            public int Pedido { get; set; }
            public int Linea { get; set; }
            public string Producto { get; set; }
            public decimal Cantidad { get; set; }
        }

        /// <summary>Vinculación propuesta: la línea de la rectificativa en positivo con la línea de la rectificativa que corrige.</summary>
        internal class Vinculacion
        {
            public int NumeroLinea { get; set; }
            public string FacturaOriginal { get; set; }
            public int LineaOriginal { get; set; }
            public decimal Cantidad { get; set; }
        }

        /// <summary>
        /// Auto-curado del job: la rectificativa anterior que corrige una rectificativa en positivo sin vinculaciones. CONSERVADOR
        /// (mejor no curar que curar mal): todas las líneas positivas tienen que casar, una a una y con cantidad exactamente
        /// contraria, con líneas de UNA sola rectificativa anterior que nadie haya corregido ya. Gana la del mismo pedido; si no, la
        /// más reciente; si las dos más recientes que casan son del mismo día (y ninguna del mismo pedido), no se sabe cuál es: null.
        /// </summary>
        /// <param name="lineasYaCorregidas">Líneas de rectificativas anteriores que ya rectifica otro documento («RV2600186|3»).</param>
        internal static List<Vinculacion> Casar(IEnumerable<LineaPositiva> positivas, IEnumerable<LineaAbonoAnterior> anteriores,
            int? pedidoDeLaFactura, ICollection<string> lineasYaCorregidas = null)
        {
            List<LineaPositiva> aVincular = (positivas ?? Enumerable.Empty<LineaPositiva>())
                .Where(p => p != null && p.Cantidad > 0)
                .OrderBy(p => p.NumeroLinea)
                .ToList();
            if (!aVincular.Any())
            {
                return null;
            }

            var grupos = (anteriores ?? Enumerable.Empty<LineaAbonoAnterior>())
                .Where(a => a != null && a.Cantidad < 0 && !string.IsNullOrWhiteSpace(a.Factura)
                    && (lineasYaCorregidas == null || !lineasYaCorregidas.Contains(Clave(a.Factura, a.Linea))))
                .GroupBy(a => a.Factura.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Factura = g.Key,
                    MismoPedido = pedidoDeLaFactura.HasValue && g.Any(a => a.Pedido == pedidoDeLaFactura.Value),
                    Fecha = g.Max(a => a.Fecha).Date,
                    Vinculaciones = CasarConUna(aVincular, g.OrderBy(a => a.Linea).ToList())
                })
                .Where(g => g.Vinculaciones != null)
                .OrderBy(g => g.MismoPedido ? 0 : 1)
                .ThenByDescending(g => g.Fecha)
                .ThenByDescending(g => g.Factura, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!grupos.Any())
            {
                return null;
            }
            var mejor = grupos[0];
            if (!mejor.MismoPedido && grupos.Count > 1 && grupos[1].Fecha == mejor.Fecha)
            {
                return null; // dos abonos del mismo día que casan: no se adivina
            }
            return mejor.Vinculaciones;
        }

        internal static string Clave(string factura, int linea) => $"{factura?.Trim().ToUpperInvariant()}|{linea}";

        private static List<Vinculacion> CasarConUna(List<LineaPositiva> positivas, List<LineaAbonoAnterior> lineasDeLaRectificativa)
        {
            var usadas = new HashSet<int>();
            var resultado = new List<Vinculacion>();
            foreach (LineaPositiva positiva in positivas)
            {
                LineaAbonoAnterior casada = lineasDeLaRectificativa.FirstOrDefault(a => !usadas.Contains(a.Linea)
                    && string.Equals(a.Producto?.Trim(), positiva.Producto?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && a.Cantidad == -positiva.Cantidad);
                if (casada == null)
                {
                    return null;
                }
                _ = usadas.Add(casada.Linea);
                resultado.Add(new Vinculacion
                {
                    NumeroLinea = positiva.NumeroLinea,
                    FacturaOriginal = casada.Factura.Trim(),
                    LineaOriginal = casada.Linea,
                    Cantidad = positiva.Cantidad
                });
            }
            return resultado;
        }
    }
}
