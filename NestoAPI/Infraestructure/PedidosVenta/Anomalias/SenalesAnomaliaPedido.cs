using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>
    /// NestoAPI#715: las reglas de anomalía al grabar un pedido. Puras: pedido reducido + histórico cliente-producto de 24 meses →
    /// señales con Regla, Valor, Esperado y Puntuación. Caso real: el pedido 929057 (#712), con cantidades 10 veces mayores por un
    /// error al teclear y descuentos fuera de lo habitual.
    ///
    /// <para>En una modificación (PUT) solo se miran las líneas nuevas y las que cambian de cantidad, precio o descuento; lo que ya
    /// estaba grabado no se vuelve a señalar. No cuentan las líneas excluidas (cheque regalo, cupón, ficticias, bonificados, no
    /// producto) ni las de cantidad negativa (devoluciones). Las reglas de precio y descuento tampoco miran ofertas, muestras (MMP)
    /// ni regalos (precio 0 o descuento del 100 %).</para>
    ///
    /// <para>Los umbrales son los de la issue; el corte 2 los evaluará con el histórico de Modificaciones.</para>
    /// </summary>
    public static class SenalesAnomaliaPedido
    {
        /// <summary>Cantidad &gt; FACTOR_MEDIANA × mediana del cliente (con al menos MINIMO_COMPRAS_MEDIANA compras).</summary>
        public const int FACTOR_MEDIANA = 3;
        public const int MINIMO_COMPRAS_MEDIANA = 3;
        /// <summary>Cantidad &gt; FACTOR_MAXIMO × el máximo anterior del cliente.</summary>
        public const int FACTOR_MAXIMO = 5;
        /// <summary>Precio &lt; FACTOR_PRECIO_BAJO × tarifa.</summary>
        public const decimal FACTOR_PRECIO_BAJO = 0.5m;
        /// <summary>Precio &gt; FACTOR_PRECIO_ALTO × habitual (o tarifa).</summary>
        public const decimal FACTOR_PRECIO_ALTO = 1.5m;
        /// <summary>Descuento &gt; máximo habitual + PUNTOS_DESCUENTO (fracción: 0,10 = 10 puntos).</summary>
        public const decimal PUNTOS_DESCUENTO = 0.10m;
        /// <summary>Importe del pedido &gt; FACTOR_IMPORTE × media del cliente (con al menos MINIMO_PEDIDOS_IMPORTE pedidos).</summary>
        public const int FACTOR_IMPORTE = 4;
        public const int MINIMO_PEDIDOS_IMPORTE = 3;
        /// <summary>«Cantidad alta» en un grupo que el cliente nunca ha comprado: desde media docena.</summary>
        public const int CANTIDAD_ALTA_GRUPO_NUEVO = 6;

        public static List<SenalAnomalia> Evaluar(InstantaneaAnomalias instantanea, HistoricoCliente historico)
        {
            var senales = new List<SenalAnomalia>();
            if (instantanea == null)
            {
                return senales;
            }
            historico = historico ?? new HistoricoCliente();
            List<LineaInstantaneaAnomalia> lineas = LineasAEvaluar(instantanea).ToList();
            foreach (LineaInstantaneaAnomalia linea in lineas)
            {
                HistoricoProductoCliente producto = historico.De(linea.Producto);
                senales.AddRange(EvaluarCantidad(linea, producto));
                senales.AddRange(EvaluarPrecio(linea, producto));
                SenalAnomalia descuento = EvaluarDescuento(linea, producto, instantanea);
                if (descuento != null)
                {
                    senales.Add(descuento);
                }
                SenalAnomalia grupo = EvaluarGrupoNuevo(linea, producto, historico);
                if (grupo != null)
                {
                    senales.Add(grupo);
                }
            }
            if (lineas.Any())
            {
                SenalAnomalia importe = EvaluarImporte(instantanea, historico);
                if (importe != null)
                {
                    senales.Add(importe);
                }
            }
            return senales;
        }

        /// <summary>Las líneas de producto con cantidad positiva que son nuevas o cambian respecto a lo grabado (POST: todas).</summary>
        internal static IEnumerable<LineaInstantaneaAnomalia> LineasAEvaluar(InstantaneaAnomalias instantanea)
        {
            Dictionary<int, LineaGrabadaAnomalia> antes = (instantanea.LineasAntes ?? new List<LineaGrabadaAnomalia>())
                .GroupBy(l => l.Linea)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (LineaInstantaneaAnomalia linea in instantanea.Lineas ?? Enumerable.Empty<LineaInstantaneaAnomalia>())
            {
                if (linea == null || linea.Excluida || linea.Cantidad <= 0)
                {
                    continue;
                }
                if (instantanea.LineasAntes == null || linea.Linea == 0 || !antes.TryGetValue(linea.Linea, out LineaGrabadaAnomalia grabada))
                {
                    yield return linea;
                    continue;
                }
                bool cambia = !string.Equals(grabada.Producto?.Trim(), linea.Producto, StringComparison.OrdinalIgnoreCase)
                    || grabada.Cantidad != linea.Cantidad
                    || grabada.Precio != linea.Precio
                    || Math.Abs(grabada.Descuento - linea.Descuento) > 0.000001m;
                if (cambia)
                {
                    yield return linea;
                }
            }
        }

        internal static IEnumerable<SenalAnomalia> EvaluarCantidad(LineaInstantaneaAnomalia linea, HistoricoProductoCliente producto)
        {
            // Umbral: max(3 × mediana del cliente, p99 de todos). Sin ninguna de las dos no hay con qué comparar.
            decimal? umbralMediana = producto.Compras >= MINIMO_COMPRAS_MEDIANA ? producto.MedianaCantidad * FACTOR_MEDIANA : null;
            decimal? umbral = Maximo(umbralMediana, producto.P99Todos);
            if (umbral.HasValue && umbral.Value > 0 && linea.Cantidad > umbral.Value)
            {
                yield return Senal(ReglasAnomalia.CANTIDAD_ALTA, linea, linea.Cantidad, umbral.Value);
            }
            if (producto.MaximoCantidad.HasValue && producto.MaximoCantidad.Value > 0)
            {
                decimal umbralMaximo = producto.MaximoCantidad.Value * FACTOR_MAXIMO;
                if (linea.Cantidad > umbralMaximo)
                {
                    yield return Senal(ReglasAnomalia.CANTIDAD_VS_MAXIMO, linea, linea.Cantidad, umbralMaximo);
                }
            }
        }

        internal static IEnumerable<SenalAnomalia> EvaluarPrecio(LineaInstantaneaAnomalia linea, HistoricoProductoCliente producto)
        {
            if (EsOfertaMuestraORegalo(linea))
            {
                yield break;
            }
            decimal tarifa = producto.PrecioTarifa ?? linea.PrecioTarifa;
            if (tarifa > 0)
            {
                decimal umbralBajo = tarifa * FACTOR_PRECIO_BAJO;
                if (linea.Precio < umbralBajo)
                {
                    yield return new SenalAnomalia
                    {
                        Regla = ReglasAnomalia.PRECIO_BAJO,
                        Linea = linea.Linea,
                        Producto = linea.Producto,
                        Valor = linea.Precio,
                        Esperado = umbralBajo,
                        Puntuacion = SenalAnomalia.Puntuar(umbralBajo, linea.Precio)
                    };
                }
            }
            decimal? habitual = producto.PrecioHabitual ?? (tarifa > 0 ? tarifa : (decimal?)null);
            if (habitual.HasValue && habitual.Value > 0)
            {
                decimal umbralAlto = habitual.Value * FACTOR_PRECIO_ALTO;
                if (linea.Precio > umbralAlto)
                {
                    yield return Senal(ReglasAnomalia.PRECIO_ALTO, linea, linea.Precio, umbralAlto);
                }
            }
        }

        /// <summary>
        /// Descuento &gt; máximo habitual + 10 puntos Y fuera de lo permitido. «Fuera de lo permitido» es que la validación de precios
        /// haya denegado ese producto o que el pedido no se haya validado (Dirección, Almacén, parámetro de omitir validación: justo
        /// los pedidos que nadie comprueba). Sin compras anteriores el máximo habitual es 0.
        /// </summary>
        internal static SenalAnomalia EvaluarDescuento(LineaInstantaneaAnomalia linea, HistoricoProductoCliente producto, InstantaneaAnomalias instantanea)
        {
            if (EsOfertaMuestraORegalo(linea) || linea.Descuento <= 0)
            {
                return null;
            }
            decimal umbral = (producto.DescuentoMaximo ?? 0) + PUNTOS_DESCUENTO;
            if (linea.Descuento <= umbral)
            {
                return null;
            }
            bool fueraDePermitido = !instantanea.ValidacionHecha
                || (instantanea.ProductosNoPermitidos?.Contains(linea.Producto ?? string.Empty) ?? false);
            if (!fueraDePermitido)
            {
                return null;
            }
            return Senal(ReglasAnomalia.DESCUENTO_ALTO, linea, linea.Descuento, umbral);
        }

        internal static SenalAnomalia EvaluarGrupoNuevo(LineaInstantaneaAnomalia linea, HistoricoProductoCliente producto, HistoricoCliente historico)
        {
            string grupo = (producto.Grupo ?? linea.Grupo)?.Trim();
            if (string.IsNullOrEmpty(grupo) || historico.Pedidos == 0 || historico.Grupos.Count == 0 || historico.Grupos.Contains(grupo))
            {
                return null;
            }
            if (linea.Cantidad < CANTIDAD_ALTA_GRUPO_NUEVO)
            {
                return null;
            }
            return Senal(ReglasAnomalia.GRUPO_NUEVO, linea, linea.Cantidad, CANTIDAD_ALTA_GRUPO_NUEVO);
        }

        internal static SenalAnomalia EvaluarImporte(InstantaneaAnomalias instantanea, HistoricoCliente historico)
        {
            if (historico.Pedidos < MINIMO_PEDIDOS_IMPORTE || historico.ImporteMedio <= 0)
            {
                return null;
            }
            decimal importe = instantanea.ImporteBruto;
            decimal umbral = historico.ImporteMedio * FACTOR_IMPORTE;
            if (importe <= umbral)
            {
                return null;
            }
            return new SenalAnomalia
            {
                Regla = ReglasAnomalia.IMPORTE_PEDIDO,
                Linea = null,
                Producto = null,
                Valor = importe,
                Esperado = umbral,
                Puntuacion = SenalAnomalia.Puntuar(importe, umbral)
            };
        }

        /// <summary>#716 2.10: las líneas evaluadas que se graban con más cantidad que stock en su almacén.</summary>
        public static List<SenalAnomalia> EvaluarStock(InstantaneaAnomalias instantanea, ContextoGrabacion contexto)
        {
            var senales = new List<SenalAnomalia>();
            if (instantanea == null || contexto == null || !contexto.StockLeido)
            {
                return senales;
            }
            foreach (LineaInstantaneaAnomalia linea in LineasAEvaluar(instantanea))
            {
                int? stock = contexto.StockDe(linea.Producto, linea.Almacen);
                if (stock.HasValue && stock.Value < linea.Cantidad)
                {
                    senales.Add(new SenalAnomalia
                    {
                        Regla = ReglasAnomalia.SIN_STOCK,
                        Linea = linea.Linea,
                        Producto = linea.Producto,
                        Valor = stock.Value,
                        Esperado = linea.Cantidad,
                        Puntuacion = SenalAnomalia.Puntuar(linea.Cantidad - stock.Value, linea.Cantidad)
                    });
                }
            }
            return senales;
        }

        internal static bool EsOfertaMuestraORegalo(LineaInstantaneaAnomalia linea)
        {
            return linea.Oferta.HasValue
                || string.Equals(linea.SubGrupo, Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase)
                || linea.Precio <= 0
                || linea.Descuento >= 1;
        }

        private static SenalAnomalia Senal(string regla, LineaInstantaneaAnomalia linea, decimal valor, decimal esperado)
        {
            return new SenalAnomalia
            {
                Regla = regla,
                Linea = linea.Linea,
                Producto = linea.Producto,
                Valor = valor,
                Esperado = esperado,
                Puntuacion = SenalAnomalia.Puntuar(valor, esperado)
            };
        }

        private static decimal? Maximo(decimal? a, int? b)
        {
            if (!a.HasValue)
            {
                return b;
            }
            if (!b.HasValue)
            {
                return a;
            }
            return Math.Max(a.Value, b.Value);
        }
    }
}
