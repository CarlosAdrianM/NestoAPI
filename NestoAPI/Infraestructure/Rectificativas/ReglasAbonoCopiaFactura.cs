using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// 16/11/26 (RV2600152/153/154/155): con «Copiar factura» invirtiendo cantidades se abonó a NATURA STYLE (30976) la
    /// NV2617228 de otro cliente (13465), que no la había comprado, y después se volvió a abonar entera al cliente bueno.
    /// Dos abonos de la misma venta y uno a quien no la compró: rectificativas que no cuadran con ninguna venta y que
    /// Verifactu no puede declarar. Reglas puras de lo que NO se deja abonar.
    /// </summary>
    internal static class ReglasAbonoCopiaFactura
    {
        /// <summary>Una línea vendida (cantidad positiva) de la factura que se abona.</summary>
        internal class LineaVendida
        {
            public string Factura { get; set; }
            public int Linea { get; set; }
            public string Producto { get; set; }
            public decimal Cantidad { get; set; }
        }

        /// <summary>Lo que ya abona otro documento (rectificativa emitida o copia pendiente de facturar) de una línea vendida.</summary>
        internal class AbonoAnterior
        {
            public string FacturaOriginal { get; set; }
            public int LineaOriginal { get; set; }
            public decimal Cantidad { get; set; }
            /// <summary>La rectificativa («RV2600152») o, si aún no se ha facturado, «el pedido 930081».</summary>
            public string Documento { get; set; }
        }

        /// <summary>
        /// El abono va al cliente que compró: si el cliente de alguna factura no es el destino, el motivo; si no, null.
        /// Para pasar una venta a otro cliente está «Crear abono y cargo» (abono al que la compró y cargo al otro).
        /// </summary>
        internal static string MotivoClienteDistinto(IEnumerable<(string Factura, string Cliente)> facturas, string clienteDestino)
        {
            string destino = clienteDestino?.Trim();
            if (string.IsNullOrEmpty(destino))
            {
                return null;
            }
            var ajena = (facturas ?? Enumerable.Empty<(string Factura, string Cliente)>())
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Cliente)
                    && !string.Equals(f.Cliente.Trim(), destino, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(ajena.Cliente))
            {
                return null;
            }
            return $"La factura {ajena.Factura?.Trim()} es del cliente {ajena.Cliente.Trim()}: no se puede abonar al cliente {destino}, " +
                "que no la compró. Si la venta tiene que pasar a otro cliente, use «Crear abono y cargo» " +
                "(abona al que la compró y la carga al otro).";
        }

        /// <summary>
        /// Lo vendido no se abona dos veces: si alguna línea, sumando lo que ya abonan otros documentos y lo que se va a abonar
        /// ahora (la línea entera), pasa de lo vendido, el motivo con lo ya abonado y dónde; si no, null.
        /// </summary>
        internal static string MotivoYaAbonado(IEnumerable<LineaVendida> lineas, IEnumerable<AbonoAnterior> abonosAnteriores)
        {
            List<AbonoAnterior> anteriores = (abonosAnteriores ?? Enumerable.Empty<AbonoAnterior>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.FacturaOriginal) && a.Cantidad > 0)
                .ToList();
            var pasadas = new List<string>();
            var documentos = new List<string>();
            foreach (LineaVendida linea in (lineas ?? Enumerable.Empty<LineaVendida>()).Where(l => l != null && l.Cantidad > 0))
            {
                List<AbonoAnterior> deLaLinea = anteriores
                    .Where(a => a.LineaOriginal == linea.Linea
                        && string.Equals(a.FacturaOriginal.Trim(), linea.Factura?.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
                decimal yaAbonado = deLaLinea.Sum(a => a.Cantidad);
                if (yaAbonado + linea.Cantidad <= linea.Cantidad)
                {
                    continue;
                }
                pasadas.Add($"{linea.Producto?.Trim()} ({Cantidad(yaAbonado)} de {Cantidad(linea.Cantidad)} ya abonadas)");
                documentos.AddRange(deLaLinea.Select(a => a.Documento?.Trim()).Where(d => !string.IsNullOrEmpty(d)));
            }
            if (!pasadas.Any())
            {
                return null;
            }
            List<string> facturas = (lineas ?? Enumerable.Empty<LineaVendida>())
                .Select(l => l?.Factura?.Trim()).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
            string donde = documentos.Any() ? $" en {string.Join(", ", documentos.Distinct())}" : string.Empty;
            string cuales = facturas.Count == 1
                ? $"La factura {facturas[0]} ya está abonada"
                : $"Las facturas {string.Join(", ", facturas)} ya están abonadas";
            return $"{cuales}{donde}: " +
                $"{string.Join(", ", pasadas)}. No se puede abonar dos veces lo mismo. Si el abono anterior estaba mal, " +
                "avise a Informática antes de hacer otro.";
        }

        private static string Cantidad(decimal cantidad) => cantidad.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
    }
}
