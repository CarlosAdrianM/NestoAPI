using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// NestoAPI#899 y #863 (Carlos 24/11/26): copiar o abonar SOLO unos albaranes o unas líneas de una factura. Caso real: para dejar
    /// una factura por P.O. (cliente 27593, NV2616046) hubo que copiar facturas enteras y borrar líneas con un script, porque la API no
    /// dejaba elegir y al guardar el pedido copiado el PUT recalculaba los portes. LA REGLA (pura) de qué líneas entran y cuántas
    /// unidades de cada una. Lo de «no abonar dos veces» lo sigue decidiendo <see cref="ReglasAbonoCopiaFactura"/>, ahora con las
    /// unidades que se abonan.
    /// </summary>
    internal static class ReglaSeleccionCopiaFactura
    {
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Una línea elegida y las unidades que se copian (con el signo de la línea original).</summary>
        internal class LineaElegida
        {
            public LinPedidoVta Linea { get; set; }
            public short Cantidad { get; set; }
            /// <summary>Se copian menos unidades de las que tiene la línea.</summary>
            public bool Parcial { get; set; }
        }

        internal class Resultado
        {
            public List<LineaElegida> Lineas { get; set; } = new List<LineaElegida>();
            /// <summary>El motivo de usted si la petición no vale (nada se copia); null si vale.</summary>
            public string Error { get; set; }
            /// <summary>«albaranes 731200, 731201; líneas 3 (2 de 5 uds.), 7» para el comentario de trazabilidad.</summary>
            public string Descripcion { get; set; }
        }

        /// <summary>Con varias facturas no se puede elegir (los Nº Orden y los albaranes son de una factura): el motivo; si no, null.</summary>
        internal static string MotivoVariasFacturas(CopiarFacturaRequest request)
        {
            if (request == null || !request.HaySeleccion || request.NumerosFactura == null || request.NumerosFactura.Count <= 1)
            {
                return null;
            }
            return "Para copiar o abonar solo unos albaranes o unas líneas seleccione una sola factura.";
        }

        /// <summary>
        /// Las líneas que entran: las de los albaranes pedidos (sin las de cuenta contable: portes y comisión contra reembolso) más las
        /// líneas pedidas (cualquier tipo, con su cantidad). Sin selección, todas enteras, como siempre.
        /// </summary>
        internal static Resultado Elegir(IList<LinPedidoVta> lineasFactura, string factura, IList<int> albaranes, IList<LineaACopiarDTO> lineas)
        {
            List<LinPedidoVta> todas = (lineasFactura ?? new List<LinPedidoVta>()).Where(l => l != null).OrderBy(l => l.Nº_Orden).ToList();
            List<int> albaranesPedidos = (albaranes ?? new List<int>()).Distinct().ToList();
            List<LineaACopiarDTO> lineasPedidas = (lineas ?? new List<LineaACopiarDTO>()).Where(l => l != null).ToList();
            string nombreFactura = string.IsNullOrWhiteSpace(factura) ? "la factura" : $"la factura {factura.Trim()}";

            if (!albaranesPedidos.Any() && !lineasPedidas.Any())
            {
                return new Resultado
                {
                    Lineas = todas.Select(l => new LineaElegida { Linea = l, Cantidad = l.Cantidad ?? 0 }).ToList()
                };
            }

            var errores = new List<string>();

            List<int> albaranesDeLaFactura = todas.Where(l => l.Nº_Albarán.HasValue).Select(l => l.Nº_Albarán.Value).Distinct().OrderBy(a => a).ToList();
            List<int> albaranesAjenos = albaranesPedidos.Where(a => !albaranesDeLaFactura.Contains(a)).ToList();
            if (albaranesAjenos.Any())
            {
                string susAlbaranes = albaranesDeLaFactura.Any() ? $" (sus albaranes son {string.Join(", ", albaranesDeLaFactura)})" : string.Empty;
                errores.Add(albaranesAjenos.Count == 1
                    ? $"El albarán {albaranesAjenos[0]} no es de {nombreFactura}{susAlbaranes}."
                    : $"Los albaranes {string.Join(", ", albaranesAjenos)} no son de {nombreFactura}{susAlbaranes}.");
            }

            // Por la línea misma (una factura puede agrupar varios pedidos: el Nº Orden solo es único dentro de su pedido)
            var cantidadesPedidas = new Dictionary<LinPedidoVta, short?>();
            var repetidas = new List<int>();
            foreach (LineaACopiarDTO pedida in lineasPedidas)
            {
                List<LinPedidoVta> candidatas = todas
                    .Where(l => l.Nº_Orden == pedida.NumeroOrden && (!pedida.Pedido.HasValue || l.Número == pedida.Pedido.Value))
                    .ToList();
                if (!candidatas.Any())
                {
                    errores.Add(pedida.Pedido.HasValue
                        ? $"La línea {pedida.NumeroOrden} del pedido {pedida.Pedido.Value} no es de {nombreFactura}."
                        : $"La línea {pedida.NumeroOrden} no es de {nombreFactura}.");
                    continue;
                }
                if (candidatas.Count > 1)
                {
                    errores.Add($"La línea {pedida.NumeroOrden} está en los pedidos {string.Join(", ", candidatas.Select(c => c.Número).Distinct().OrderBy(n => n))} " +
                        $"de {nombreFactura}: indique de qué pedido es.");
                    continue;
                }
                LinPedidoVta linea = candidatas[0];
                if (cantidadesPedidas.ContainsKey(linea))
                {
                    if (!repetidas.Contains(pedida.NumeroOrden))
                    {
                        repetidas.Add(pedida.NumeroOrden);
                        errores.Add($"La línea {pedida.NumeroOrden} está repetida en la petición.");
                    }
                    continue;
                }
                int deLaLinea = Math.Abs((int)(linea.Cantidad ?? 0));
                if (pedida.Cantidad.HasValue && pedida.Cantidad.Value <= 0)
                {
                    errores.Add($"La cantidad de la línea {pedida.NumeroOrden} ({Nombre(linea)}) tiene que ser mayor que cero.");
                    continue;
                }
                if (pedida.Cantidad.HasValue && pedida.Cantidad.Value > deLaLinea)
                {
                    errores.Add($"La línea {pedida.NumeroOrden} ({Nombre(linea)}) tiene {Unidades(deLaLinea)} en {nombreFactura}: no se pueden copiar {Unidades(pedida.Cantidad.Value)}.");
                    continue;
                }
                cantidadesPedidas[linea] = pedida.Cantidad;
            }

            if (errores.Any())
            {
                return new Resultado { Error = string.Join(" ", errores) };
            }

            var elegidas = new List<LineaElegida>();
            foreach (LinPedidoVta linea in todas)
            {
                bool porLinea = cantidadesPedidas.TryGetValue(linea, out short? cantidadPedida);
                bool porAlbaran = linea.Nº_Albarán.HasValue && albaranesPedidos.Contains(linea.Nº_Albarán.Value)
                    && linea.TipoLinea != Constantes.TiposLineaVenta.CUENTA_CONTABLE;
                if (!porLinea && !porAlbaran)
                {
                    continue;
                }
                short original = linea.Cantidad ?? 0;
                short cantidad = porLinea && cantidadPedida.HasValue
                    ? (short)(Math.Sign(original == 0 ? 1 : (int)original) * cantidadPedida.Value)
                    : original;
                elegidas.Add(new LineaElegida { Linea = linea, Cantidad = cantidad, Parcial = cantidad != original });
            }

            if (!elegidas.Any())
            {
                return new Resultado
                {
                    Error = $"No queda ninguna línea que copiar de {nombreFactura}: los albaranes elegidos solo tienen portes o cuentas contables, que entran solo si se piden como línea."
                };
            }

            var partes = new List<string>();
            if (albaranesPedidos.Any())
            {
                partes.Add((albaranesPedidos.Count == 1 ? "albarán " : "albaranes ") + string.Join(", ", albaranesPedidos.OrderBy(a => a)));
            }
            if (cantidadesPedidas.Any())
            {
                bool variosPedidos = todas.Select(l => l.Número).Distinct().Count() > 1;
                partes.Add((cantidadesPedidas.Count == 1 ? "línea " : "líneas ") + string.Join(", ", elegidas
                    .Where(e => cantidadesPedidas.ContainsKey(e.Linea))
                    .Select(e =>
                    {
                        string numero = variosPedidos ? $"{e.Linea.Número}/{e.Linea.Nº_Orden}" : e.Linea.Nº_Orden.ToString(ES);
                        return e.Parcial
                            ? $"{numero} ({Unidades(Math.Abs((int)e.Cantidad))} de {Math.Abs((int)(e.Linea.Cantidad ?? 0))})"
                            : numero;
                    })));
            }
            return new Resultado { Lineas = elegidas, Descripcion = string.Join("; ", partes) };
        }

        /// <summary>
        /// NestoAPI#863 (corte 1): el texto para el comentario del abono con lo que vuelve estropeado, o null. Solo cuentan las líneas
        /// que se abonan; las demás se ignoran.
        /// </summary>
        internal static string ComentarioNoVendibles(IEnumerable<int> lineasNoVendibles, IEnumerable<LineaElegida> elegidas)
        {
            HashSet<int> noVendibles = new HashSet<int>(lineasNoVendibles ?? Enumerable.Empty<int>());
            List<string> cuales = (elegidas ?? Enumerable.Empty<LineaElegida>())
                .Where(e => e?.Linea != null && noVendibles.Contains(e.Linea.Nº_Orden))
                .Select(e => $"{e.Linea.Producto?.Trim()} ({Unidades(Math.Abs((int)e.Cantidad))})")
                .ToList();
            return cuales.Any()
                ? $"[Vuelve estropeado, no vendible: {string.Join(", ", cuales)}. Hay que regularizar el stock a mano]"
                : null;
        }

        /// <summary>
        /// NestoAPI#899/#863 (pura): las líneas de la factura para elegir, con lo ya abonado de cada una (o, en una rectificativa, lo
        /// ya deshecho) y lo que queda. Las de cuenta contable salen marcadas: por albarán no entran, solo si se piden.
        /// </summary>
        internal static LineasFacturaParaCopiarDTO LineasParaCopiar(string empresa, string factura, IEnumerable<LinPedidoVta> lineas,
            IEnumerable<ReglasAbonoCopiaFactura.AbonoAnterior> anteriores, Func<LinPedidoVta, bool> esGastosEnvio)
        {
            List<ReglasAbonoCopiaFactura.AbonoAnterior> abonos = (anteriores ?? Enumerable.Empty<ReglasAbonoCopiaFactura.AbonoAnterior>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.FacturaOriginal) && a.Cantidad > 0)
                .ToList();
            var resultado = new LineasFacturaParaCopiarDTO { Empresa = empresa, Factura = factura };
            foreach (LinPedidoVta l in (lineas ?? Enumerable.Empty<LinPedidoVta>()).Where(x => x != null).OrderBy(x => x.Número).ThenBy(x => x.Nº_Orden))
            {
                string suFactura = l.Nº_Factura?.Trim();
                decimal cantidad = l.Cantidad ?? 0;
                decimal yaAbonada = abonos
                    .Where(a => a.LineaOriginal == l.Nº_Orden && string.Equals(a.FacturaOriginal.Trim(), suFactura, StringComparison.OrdinalIgnoreCase))
                    .Sum(a => a.Cantidad);
                resultado.Lineas.Add(new LineaFacturaParaCopiarDTO
                {
                    Pedido = l.Número,
                    NumeroOrden = l.Nº_Orden,
                    Albaran = l.Nº_Albarán,
                    FechaAlbaran = l.Fecha_Albarán,
                    TipoLinea = l.TipoLinea,
                    EsCuentaContable = l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                    EsGastosEnvio = esGastosEnvio != null && esGastosEnvio(l),
                    Producto = l.Producto?.Trim(),
                    Texto = l.Texto?.Trim(),
                    Cantidad = cantidad,
                    YaAbonada = yaAbonada,
                    QuedanPorAbonar = Math.Max(0, Math.Abs(cantidad) - yaAbonada),
                    Precio = l.Precio ?? 0,
                    BaseImponible = l.Base_Imponible
                });
            }
            resultado.Albaranes = resultado.Lineas.Where(x => x.Albaran.HasValue).Select(x => x.Albaran.Value).Distinct().OrderBy(a => a).ToList();
            return resultado;
        }

        private static string Nombre(LinPedidoVta linea)
        {
            string producto = linea.Producto?.Trim();
            return string.IsNullOrEmpty(producto) ? linea.Texto?.Trim() : producto;
        }

        private static string Unidades(int unidades) => unidades == 1 ? "1 unidad" : $"{unidades.ToString(ES)} unidades";
    }
}
