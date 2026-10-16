using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.IndicadoresStock
{
    /// <summary>
    /// NestoAPI#629: lo que se ha leído de la BD para calcular un mes. Cada bloque viene de una consulta; si una consulta ha fallado
    /// (o se ha pasado de tiempo) su bloque es null y los indicadores que dependen de él quedan a null, sin tirar el resto.
    /// </summary>
    public class DatosIndicadoresStock
    {
        /// <summary>Día 1 del mes que se calcula.</summary>
        public DateTime Mes { get; set; }
        /// <summary>Obligatorio: sin precio medio no se puede valorar nada.</summary>
        public IList<ProductoIndicadorStock> Productos { get; set; }
        public IList<MovimientoStockTramo> Stock { get; set; }
        public IList<VentaTramo> Ventas { get; set; }
        public IList<ServicioAlmacen> Servicio { get; set; }
        public IList<MovimientoProductoComprado> MovimientosComprados { get; set; }
    }

    /// <summary>
    /// NestoAPI#629 (corte 1 de la épica #628, control de stock obsoleto): calcula los indicadores de un mes por almacén y la fila
    /// TOTAL a partir de lo que devuelven las consultas (<see cref="RepositorioIndicadoresStockSql"/>). No toca la BD.
    ///
    /// <para><b>Ámbito</b> (decisiones de la épica): empresas 1 y 3 con la ficha de producto de la 1, sin productos ficticios y sin
    /// el almacén EXT (extinguidos). <b>Venta</b> = LinPedidoVta con estado >= 2 (lo que ha salido a clientes; fuera presupuestos
    /// −3, notas de entrega −2 y lo pendiente −1/1), TipoLinea 1, por fecha de albarán, sin el grupo MTP ni el subgrupo MMP. En la
    /// fila de un almacén cuentan sus propias ventas (líneas con ese almacén); en la TOTAL, todas («si lo vendo lo tengo que
    /// comprar»). Todo se valora con el <b>precio medio de hoy</b> (Productos.PrecioMedio), también los meses pasados, como hace la
    /// consulta de Carlos; el stock de una fecha pasada es la suma de ExtractoProducto con Fecha &lt; día 1 del mes siguiente.</para>
    ///
    /// <para>Cómo se calcula cada indicador (mes M, «fin» = día 1 de M+1):</para>
    /// <list type="bullet">
    /// <item><b>ValorStock / UnidadesStock</b>: Σ stock a fin × precio medio (todos los productos, también MTP y MMP: es el KPI).</item>
    /// <item><b>Estado 4 / Estado 1</b>: valor y nº de productos con stock &gt; 0 a fin cuyo estado (el de hoy) es 4 (a extinguir) o
    /// 1 (sobre pedido).</item>
    /// <item><b>Sin venta en 6, 12 y 24 meses</b>: productos con stock &gt; 0 en el ámbito, que cuentan en la demanda (sin MTP ni
    /// MMP), que ya existían hace N meses (primer movimiento en la empresa antes de fin − N meses) y sin ninguna línea con cantidad
    /// positiva en TODA la empresa en los últimos N meses (la demanda es de toda la empresa).</item>
    /// <item><b>Coste de ventas 12 meses</b>: Σ unidades vendidas netas (salidas − devoluciones) de los 12 meses que acaban en M ×
    /// precio medio.</item>
    /// <item><b>Stock medio 12 meses</b>: media de los 12 cierres de mes (de M−11 a M) del stock de los productos que cuentan en la
    /// demanda × precio medio. <b>Rotación</b> = coste de ventas 12 meses ÷ stock medio (veces al año); null si el stock medio
    /// no es positivo.</item>
    /// <item><b>Cobertura media (días)</b>: por producto con stock &gt; 0 y venta neta en los 6 últimos meses, stock ÷ venta media
    /// mensual de esos 6 meses, en días (× 365/12); media ponderada por el valor de su stock. Los que no tienen venta en 6 meses
    /// no entran (cobertura infinita): ya cuentan en «sin venta en 6 meses».</item>
    /// <item><b>Días en servir</b>: media de días entre la fecha de entrega y la de albarán de las líneas albaranadas en el mes
    /// (estado >= 2, TipoLinea 1, cantidad &gt; 0). <b>Nivel de servicio</b>: % de esas líneas que salieron enteras, es decir, sin
    /// otra línea partida del mismo pedido, producto y fecha de entrega en otro albarán o aún pendiente.</item>
    /// <item><b>Compras del mes</b>: importe y unidades de los albaranes de compra (_ALBCOMP; las devoluciones a proveedor restan).</item>
    /// <item><b>Bajas a pérdidas</b>: lo que sale por los diarios de mermas (Merma…) y Robo, en unidades y × precio medio (positivo).</item>
    /// <item><b>Altas de referencias</b>: productos cuyo primer movimiento en el ámbito es de este mes. <b>Bajas de referencias</b>:
    /// productos que cerraron el mes anterior con stock &gt; 0 y este con stock &lt;= 0.</item>
    /// <item><b>Compras con cobertura &gt; 6 meses</b>: por cada entrada de compra del mes, el stock de la empresa justo antes de
    /// ella (stock a principio de mes + movimientos anteriores del mes) ÷ la venta media mensual de la empresa de los 6 meses
    /// anteriores al mes. Si pasa de 6 meses (o hay stock y no hay venta), sus unidades cuentan en UnidadesCompradasCoberturaAlta.
    /// El % de 12 meses (la pista de la épica) es Σ altas ÷ Σ compradas de los 12 meses que acaban en M; lo calcula el GET con las
    /// 12 filas (<see cref="CalcularPorcentajeCompras12Meses"/>).</item>
    /// <item><b>Algete vs tiendas</b> (solo en la fila TOTAL): valor de stock de ALG y de REI + ALC.</item>
    /// </list>
    /// <para>NestoAPI#628: además de TOTAL y los almacenes, una fila por agrupación de tipo de exclusiva de la familia
    /// (<see cref="AgrupacionesStock"/>: G:PRP, G:NAC, G:MAD, G:NIG, G:EXT, G:SIN), calculada con el MISMO código que TOTAL pero
    /// solo con los productos de esa agrupación (la rotación de cada una es su coste de ventas de 12 meses ÷ su stock medio de 12
    /// meses). «Sin venta» mira, como siempre, la venta de toda la empresa del producto. Los días en servir y el nivel de servicio
    /// son por almacén y quedan a null; Algete y tiendas, solo en TOTAL. La suma del valor de stock de las agrupaciones es la de
    /// TOTAL (todo producto cae en una, «G:SIN» si su familia no tiene tipo).</para>
    /// </summary>
    public static class CalculadoraIndicadoresStock
    {
        /// <summary>Tramos de stock: −1 (todo lo anterior) y 0..11 (11 = el mes).</summary>
        internal const int TRAMOS_STOCK = 13;
        /// <summary>Tramos de ventas: 0..23 (23 = el mes).</summary>
        internal const int TRAMOS_VENTAS = 24;
        internal const int MESES_COBERTURA_ALTA = 6;
        internal const decimal DIAS_POR_MES = 365m / 12m;

        private sealed class Acumulado
        {
            public readonly int[] Stock = new int[TRAMOS_STOCK];
            public DateTime? PrimeraFecha;
            public int UnidadesCompradas;
            public decimal ImporteCompras;
            public int UnidadesBajas;
            public bool MovimientoEnElMes;
            public readonly int[] VentasNetas = new int[TRAMOS_VENTAS];
            public readonly int[] VentasSalida = new int[TRAMOS_VENTAS];
        }

        public static List<FilaIndicadoresStock> Calcular(DatosIndicadoresStock datos)
        {
            if (datos == null || datos.Productos == null)
            {
                throw new ArgumentException("Sin productos no se pueden calcular los indicadores de stock.", nameof(datos));
            }
            DateTime mes = new DateTime(datos.Mes.Year, datos.Mes.Month, 1);
            Dictionary<string, ProductoIndicadorStock> productos = datos.Productos
                .GroupBy(p => Clave(p.Producto))
                .ToDictionary(g => g.Key, g => g.First());
            // NestoAPI#628: la agrupación por tipo de exclusiva de cada producto («G:NAC»…), que se acumula como un ámbito más
            Dictionary<string, string> agrupacion = productos.ToDictionary(par => par.Key,
                par => AgrupacionesStock.Almacen(AgrupacionesStock.Codigo(par.Value.TipoExclusiva)));

            // (almacén, producto), (TOTAL, producto) y (agrupación, producto)
            var porAmbito = new Dictionary<string, Dictionary<string, Acumulado>>(StringComparer.OrdinalIgnoreCase);
            Acumulado Obtener(string almacen, string producto)
            {
                if (!porAmbito.TryGetValue(almacen, out Dictionary<string, Acumulado> deAmbito))
                {
                    deAmbito = new Dictionary<string, Acumulado>();
                    porAmbito[almacen] = deAmbito;
                }
                if (!deAmbito.TryGetValue(producto, out Acumulado acumulado))
                {
                    acumulado = new Acumulado();
                    deAmbito[producto] = acumulado;
                }
                return acumulado;
            }

            var almacenesConDatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (datos.Stock != null)
            {
                foreach (MovimientoStockTramo m in datos.Stock)
                {
                    string producto = Clave(m.Producto);
                    string almacen = Clave(m.Almacen);
                    if (!productos.ContainsKey(producto) || almacen.Length == 0 || m.Tramo < -1 || m.Tramo > 11)
                    {
                        continue;
                    }
                    foreach (Acumulado a in new[] { Obtener(almacen, producto), Obtener(FilaIndicadoresStock.TOTAL, producto), Obtener(agrupacion[producto], producto) })
                    {
                        a.Stock[m.Tramo + 1] += m.Cantidad;
                        if (!a.PrimeraFecha.HasValue || m.PrimeraFecha < a.PrimeraFecha.Value)
                        {
                            a.PrimeraFecha = m.PrimeraFecha;
                        }
                        if (m.Tramo == 11)
                        {
                            a.UnidadesCompradas += m.UnidadesCompradas;
                            a.ImporteCompras += m.ImporteCompras;
                            a.UnidadesBajas += m.UnidadesBajas;
                            a.MovimientoEnElMes = true;
                        }
                    }
                    if (m.Tramo == 11 || m.Cantidad != 0)
                    {
                        _ = almacenesConDatos.Add(almacen);
                    }
                }
            }

            if (datos.Ventas != null)
            {
                foreach (VentaTramo v in datos.Ventas)
                {
                    string producto = Clave(v.Producto);
                    string almacen = Clave(v.Almacen);
                    if (!productos.ContainsKey(producto) || almacen.Length == 0 || v.Tramo < 0 || v.Tramo >= TRAMOS_VENTAS)
                    {
                        continue;
                    }
                    foreach (Acumulado a in new[] { Obtener(almacen, producto), Obtener(FilaIndicadoresStock.TOTAL, producto), Obtener(agrupacion[producto], producto) })
                    {
                        a.VentasNetas[v.Tramo] += v.UnidadesNetas;
                        a.VentasSalida[v.Tramo] += v.UnidadesSalida;
                    }
                    if (v.Tramo >= TRAMOS_VENTAS - 12)
                    {
                        _ = almacenesConDatos.Add(almacen);
                    }
                }
            }

            Dictionary<string, ServicioAlmacen> servicio = null;
            if (datos.Servicio != null)
            {
                servicio = datos.Servicio
                    .Where(s => Clave(s.Almacen).Length > 0)
                    .GroupBy(s => Clave(s.Almacen), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => new ServicioAlmacen
                    {
                        Almacen = g.Key,
                        Lineas = g.Sum(s => s.Lineas),
                        SumaDias = g.Sum(s => s.SumaDias),
                        LineasCompletas = g.Sum(s => s.LineasCompletas)
                    }, StringComparer.OrdinalIgnoreCase);
                foreach (string almacen in servicio.Keys)
                {
                    _ = almacenesConDatos.Add(almacen);
                }
            }

            Dictionary<string, (int compradas, int altas)> comprasCobertura = null;
            if (datos.MovimientosComprados != null && datos.Stock != null && datos.Ventas != null)
            {
                porAmbito.TryGetValue(FilaIndicadoresStock.TOTAL, out Dictionary<string, Acumulado> empresa);
                comprasCobertura = CalcularComprasConCobertura(datos.MovimientosComprados, productos, empresa ?? new Dictionary<string, Acumulado>(), agrupacion);
                foreach (string almacen in comprasCobertura.Keys.Where(k => k != FilaIndicadoresStock.TOTAL && !AgrupacionesStock.EsAgrupacion(k)))
                {
                    _ = almacenesConDatos.Add(almacen);
                }
            }

            porAmbito.TryGetValue(FilaIndicadoresStock.TOTAL, out Dictionary<string, Acumulado> deLaEmpresa);
            deLaEmpresa = deLaEmpresa ?? new Dictionary<string, Acumulado>();

            // NestoAPI#628: detrás de los almacenes, las agrupaciones por tipo de exclusiva que tienen algún producto con datos
            IEnumerable<string> agrupaciones = AgrupacionesStock.TODAS.Select(AgrupacionesStock.Almacen).Where(porAmbito.ContainsKey);

            var filas = new List<FilaIndicadoresStock>();
            foreach (string almacen in new[] { FilaIndicadoresStock.TOTAL }
                .Concat(almacenesConDatos.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
                .Concat(agrupaciones))
            {
                porAmbito.TryGetValue(almacen, out Dictionary<string, Acumulado> deAmbito);
                FilaIndicadoresStock fila = CalcularFila(mes, almacen, deAmbito ?? new Dictionary<string, Acumulado>(), deLaEmpresa, productos,
                    datos.Stock != null, datos.Ventas != null);

                // Los días en servir y el nivel de servicio son por almacén (líneas de pedido): en las agrupaciones quedan a null.
                if (servicio != null && !AgrupacionesStock.EsAgrupacion(almacen))
                {
                    List<ServicioAlmacen> lineas = almacen == FilaIndicadoresStock.TOTAL
                        ? servicio.Values.ToList()
                        : (servicio.TryGetValue(almacen, out ServicioAlmacen s) ? new List<ServicioAlmacen> { s } : new List<ServicioAlmacen>());
                    int total = lineas.Sum(l => l.Lineas);
                    fila.LineasServidas = total;
                    fila.DiasServirMedia = total > 0 ? Redondear((decimal)lineas.Sum(l => l.SumaDias) / total) : (decimal?)null;
                    fila.NivelServicio = total > 0 ? Redondear(100m * lineas.Sum(l => l.LineasCompletas) / total) : (decimal?)null;
                }

                if (comprasCobertura != null)
                {
                    (int compradas, int altas) c = comprasCobertura.TryGetValue(almacen, out var valor) ? valor : (0, 0);
                    fila.UnidadesCompradasMes = c.compradas;
                    fila.UnidadesCompradasCoberturaAlta = c.altas;
                }
                filas.Add(fila);
            }

            FilaIndicadoresStock filaTotal = filas[0];
            if (datos.Stock != null)
            {
                filaTotal.ValorStockAlgete = filas.FirstOrDefault(f => f.Almacen == Constantes.Almacenes.ALGETE)?.ValorStock ?? 0;
                filaTotal.ValorStockTiendas = filas
                    .Where(f => f.Almacen == Constantes.Almacenes.REINA || f.Almacen == Constantes.Almacenes.ALCOBENDAS)
                    .Sum(f => f.ValorStock ?? 0);
            }
            return filas;
        }

        private static FilaIndicadoresStock CalcularFila(DateTime mes, string almacen, Dictionary<string, Acumulado> deAmbito,
            Dictionary<string, Acumulado> deLaEmpresa, Dictionary<string, ProductoIndicadorStock> productos, bool hayStock, bool hayVentas)
        {
            var fila = new FilaIndicadoresStock { Mes = mes, Almacen = almacen };
            DateTime fin = mes.AddMonths(1);

            decimal valorStock = 0, valorEstado4 = 0, valorEstado1 = 0, costeVentas = 0, valorStockMedio = 0;
            decimal comprasValor = 0, bajasValor = 0, sumaPesos = 0, sumaCoberturaPonderada = 0;
            int unidadesStock = 0, productosEstado4 = 0, productosEstado1 = 0, comprasUnidades = 0, bajasUnidades = 0, altas = 0, bajas = 0;
            var valorSinVenta = new decimal[3];
            var productosSinVenta = new int[3];
            int[] mesesSinVenta = { 6, 12, 24 };

            foreach (KeyValuePair<string, Acumulado> par in deAmbito)
            {
                ProductoIndicadorStock producto = productos[par.Key];
                Acumulado a = par.Value;
                decimal precio = producto.PrecioMedio;
                bool enDemanda = CuentaEnLaDemanda(producto);

                int stockFin = a.Stock.Sum();
                int stockInicio = stockFin - a.Stock[TRAMOS_STOCK - 1];

                if (hayStock)
                {
                    valorStock += stockFin * precio;
                    unidadesStock += stockFin;
                    if (stockFin > 0 && producto.Estado == Constantes.Productos.ESTADO_A_EXTINGUIR)
                    {
                        valorEstado4 += stockFin * precio;
                        productosEstado4++;
                    }
                    if (stockFin > 0 && producto.Estado == Constantes.Productos.ESTADO_SOBRE_PEDIDO)
                    {
                        valorEstado1 += stockFin * precio;
                        productosEstado1++;
                    }
                    comprasValor += a.ImporteCompras;
                    comprasUnidades += a.UnidadesCompradas;
                    bajasUnidades -= a.UnidadesBajas;
                    bajasValor -= a.UnidadesBajas * precio;
                    if (a.MovimientoEnElMes && a.PrimeraFecha.HasValue && a.PrimeraFecha.Value >= mes)
                    {
                        altas++;
                    }
                    if (stockInicio > 0 && stockFin <= 0)
                    {
                        bajas++;
                    }
                    if (enDemanda)
                    {
                        // Los 12 cierres de mes (de M−11 a M): el stock acumulado hasta cada tramo 0..11
                        int acumulado = a.Stock[0];
                        long sumaCierres = 0;
                        for (int t = 1; t < TRAMOS_STOCK; t++)
                        {
                            acumulado += a.Stock[t];
                            sumaCierres += acumulado;
                        }
                        valorStockMedio += sumaCierres / 12m * precio;
                    }
                }

                if (hayVentas && enDemanda)
                {
                    costeVentas += Suma(a.VentasNetas, TRAMOS_VENTAS - 12, 12) * precio;
                }

                if (hayStock && hayVentas && enDemanda && stockFin > 0)
                {
                    deLaEmpresa.TryGetValue(par.Key, out Acumulado empresa);
                    for (int i = 0; i < mesesSinVenta.Length; i++)
                    {
                        int n = mesesSinVenta[i];
                        bool existiaHaceNMeses = empresa?.PrimeraFecha.HasValue == true && empresa.PrimeraFecha.Value < fin.AddMonths(-n);
                        bool vendidoEnNMeses = empresa != null && Suma(empresa.VentasSalida, TRAMOS_VENTAS - n, n) > 0;
                        if (existiaHaceNMeses && !vendidoEnNMeses)
                        {
                            valorSinVenta[i] += stockFin * precio;
                            productosSinVenta[i]++;
                        }
                    }

                    int ventas6 = Suma(a.VentasNetas, TRAMOS_VENTAS - 6, 6);
                    decimal peso = stockFin * precio;
                    if (ventas6 > 0 && peso > 0)
                    {
                        decimal coberturaDias = stockFin / (ventas6 / 6m) * DIAS_POR_MES;
                        sumaPesos += peso;
                        sumaCoberturaPonderada += peso * coberturaDias;
                    }
                }
            }

            if (hayStock)
            {
                fila.ValorStock = Redondear(valorStock);
                fila.UnidadesStock = unidadesStock;
                fila.ValorEstado4 = Redondear(valorEstado4);
                fila.ProductosEstado4 = productosEstado4;
                fila.ValorEstado1 = Redondear(valorEstado1);
                fila.ProductosEstado1 = productosEstado1;
                fila.ValorStockMedio12Meses = Redondear(valorStockMedio);
                fila.ComprasValor = Redondear(comprasValor);
                fila.ComprasUnidades = comprasUnidades;
                fila.BajasPerdidasValor = Redondear(bajasValor);
                fila.BajasPerdidasUnidades = bajasUnidades;
                fila.AltasReferencias = altas;
                fila.BajasReferencias = bajas;
            }
            if (hayVentas)
            {
                fila.CosteVentas12Meses = Redondear(costeVentas);
            }
            if (hayStock && hayVentas)
            {
                fila.ValorSinVenta6Meses = Redondear(valorSinVenta[0]);
                fila.ProductosSinVenta6Meses = productosSinVenta[0];
                fila.ValorSinVenta12Meses = Redondear(valorSinVenta[1]);
                fila.ProductosSinVenta12Meses = productosSinVenta[1];
                fila.ValorSinVenta24Meses = Redondear(valorSinVenta[2]);
                fila.ProductosSinVenta24Meses = productosSinVenta[2];
                fila.Rotacion = valorStockMedio > 0 ? Redondear(costeVentas / valorStockMedio) : (decimal?)null;
                fila.CoberturaMediaDias = sumaPesos > 0 ? Math.Round(sumaCoberturaPonderada / sumaPesos, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
            }
            return fila;
        }

        /// <summary>
        /// Recorre por producto los movimientos del mes en orden (fecha, empresa, nº de orden) partiendo del stock de la empresa a
        /// principio de mes; en cada entrada de compra mira la cobertura que había justo antes. Devuelve por almacén (y TOTAL) las
        /// unidades compradas y las que entraron con más de <see cref="MESES_COBERTURA_ALTA"/> meses de cobertura.
        /// </summary>
        private static Dictionary<string, (int compradas, int altas)> CalcularComprasConCobertura(IEnumerable<MovimientoProductoComprado> movimientos,
            Dictionary<string, ProductoIndicadorStock> productos, Dictionary<string, Acumulado> deLaEmpresa, Dictionary<string, string> agrupacion)
        {
            var resultado = new Dictionary<string, (int compradas, int altas)>(StringComparer.OrdinalIgnoreCase)
            {
                [FilaIndicadoresStock.TOTAL] = (0, 0)
            };
            foreach (IGrouping<string, MovimientoProductoComprado> deProducto in movimientos.GroupBy(m => Clave(m.Producto)))
            {
                if (!productos.TryGetValue(deProducto.Key, out ProductoIndicadorStock producto))
                {
                    continue;
                }
                deLaEmpresa.TryGetValue(deProducto.Key, out Acumulado empresa);
                int stock = empresa == null ? 0 : empresa.Stock.Sum() - empresa.Stock[TRAMOS_STOCK - 1];
                // La venta media mensual de los 6 meses ANTERIORES al mes (tramos 17..22); sin MTP ni MMP no hay demanda
                decimal ventaMedia = empresa == null || !CuentaEnLaDemanda(producto)
                    ? 0
                    : Suma(empresa.VentasNetas, TRAMOS_VENTAS - 1 - 6, 6) / 6m;

                foreach (MovimientoProductoComprado m in deProducto
                    .OrderBy(m => m.Fecha).ThenBy(m => Clave(m.Empresa), StringComparer.Ordinal).ThenBy(m => m.Orden))
                {
                    if (m.EsCompra && m.Cantidad > 0)
                    {
                        bool coberturaAlta = ventaMedia > 0 ? stock / ventaMedia > MESES_COBERTURA_ALTA : stock > 0;
                        string almacen = Clave(m.Almacen);
                        foreach (string ambito in new[] { FilaIndicadoresStock.TOTAL, almacen, agrupacion[deProducto.Key] })
                        {
                            (int compradas, int altas) actual = resultado.TryGetValue(ambito, out var v) ? v : (0, 0);
                            resultado[ambito] = (actual.compradas + m.Cantidad, actual.altas + (coberturaAlta ? m.Cantidad : 0));
                        }
                    }
                    stock += m.Cantidad;
                }
            }
            return resultado;
        }

        /// <summary>
        /// El % de unidades compradas en los 12 meses que acaban en <paramref name="mes"/> que ya tenían más de 6 meses de cobertura.
        /// Necesita las 12 filas de ese almacén con el dato; si falta alguna (o no se compró nada), null.
        /// </summary>
        public static decimal? CalcularPorcentajeCompras12Meses(IEnumerable<FilaIndicadoresStock> filasDelAlmacen, DateTime mes)
        {
            DateTime hasta = new DateTime(mes.Year, mes.Month, 1);
            DateTime desde = hasta.AddMonths(-11);
            List<FilaIndicadoresStock> ventana = filasDelAlmacen
                .Where(f => f.Mes >= desde && f.Mes <= hasta && f.UnidadesCompradasMes.HasValue && f.UnidadesCompradasCoberturaAlta.HasValue)
                .GroupBy(f => f.Mes)
                .Select(g => g.First())
                .ToList();
            if (ventana.Count < 12)
            {
                return null;
            }
            int compradas = ventana.Sum(f => f.UnidadesCompradasMes.Value);
            return compradas > 0 ? Redondear(100m * ventana.Sum(f => f.UnidadesCompradasCoberturaAlta.Value) / compradas) : (decimal?)null;
        }

        /// <summary>Fuera de la demanda: el grupo MTP (materias primas) y el subgrupo MMP (muestras).</summary>
        internal static bool CuentaEnLaDemanda(ProductoIndicadorStock producto)
        {
            return !string.Equals(producto.Grupo?.Trim(), Constantes.Productos.GRUPO_MATERIAS_PRIMAS, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(producto.SubGrupo?.Trim(), Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);
        }

        private static int Suma(int[] tramos, int desde, int cuantos)
        {
            int suma = 0;
            for (int i = Math.Max(0, desde); i < Math.Min(tramos.Length, desde + cuantos); i++)
            {
                suma += tramos[i];
            }
            return suma;
        }

        private static string Clave(string valor) => valor?.Trim() ?? string.Empty;

        private static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }
}
