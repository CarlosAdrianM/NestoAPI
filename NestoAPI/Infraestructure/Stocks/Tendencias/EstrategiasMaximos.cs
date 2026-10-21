using System;
using static NestoAPI.Infraestructure.Stocks.Tendencias.MotorTendencias;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#632: valores de <see cref="ParametrosTendencias.FormulaMaduros"/> (interruptor para volver atrás).</summary>
    public static class FormulasMaduros
    {
        /// <summary>#632 (por defecto): ponderada sin estacionalidad; a la baja, la menor con la de 6 meses; en declive no sube.</summary>
        public const string RECIENTE = "Reciente";
        /// <summary>#630 (corte 2): ponderada tal cual × factor estacional.</summary>
        public const string PONDERADA = "Ponderada";
        /// <summary>La de siempre de la ficha (ControlStockBase): media de 24 meses × (7 + 2 × días).</summary>
        public const string CLASICA = "Clasica";

        /// <summary>Lo que no se entienda (o vacío), <see cref="RECIENTE"/>.</summary>
        public static string Normalizar(string valor)
        {
            string v = (valor ?? string.Empty).Trim();
            if (v.Equals(PONDERADA, StringComparison.OrdinalIgnoreCase))
            {
                return PONDERADA;
            }
            if (v.Equals(CLASICA, StringComparison.OrdinalIgnoreCase) || v.Equals("Clásica", StringComparison.OrdinalIgnoreCase))
            {
                return CLASICA;
            }
            return RECIENTE;
        }
    }

    /// <summary>
    /// NestoAPI#632: lo que una estrategia calcula para el máximo y el mínimo, con sus entradas (trazabilidad obligatoria de la
    /// épica #628): va a <see cref="ResultadoTendencia.EntradasJson"/> («maximos») y la frase a la explicación.
    /// </summary>
    public class CalculoMaximos
    {
        /// <summary>«A», «B reciente», «B ponderada», «B clásica», «C».</summary>
        public string Estrategia { get; set; }
        public string Formula { get; set; }
        public int Maximo { get; set; }
        public int Minimo { get; set; }
        /// <summary>La venta mensual de la que salen el máximo y el mínimo (antes del factor estacional).</summary>
        public decimal VentaBase { get; set; }
        /// <summary>De dónde sale <see cref="VentaBase"/>, en pocas palabras (columna «Base del máximo» del Excel).</summary>
        public string OrigenVentaBase { get; set; }
        /// <summary>El cálculo en una frase legible, con las cuentas.</summary>
        public string Frase { get; set; }
        /// <summary>Las entradas y los pasos intermedios, para el JSON.</summary>
        public object Entradas { get; set; }
    }

    /// <summary>Lo que necesita una estrategia: el resultado con los indicadores ya calculados y los datos del producto.</summary>
    internal class ContextoMaximos
    {
        public ResultadoTendencia R { get; set; }
        public DatosProductoTendencia D { get; set; }
        public ParametrosTendencias P { get; set; }
        public decimal[] Indices { get; set; }
        public DateTime MesReferencia { get; set; }
        internal VelocidadNuevo VelocidadNuevo { get; set; }
    }

    /// <summary>
    /// NestoAPI#632 (DRY: núcleo + estrategias). El núcleo (<see cref="MotorTendencias"/>) decide las salidas tempranas
    /// (estado, sin salida, declive a liquidar o a estado 1) y la acción; la estrategia de la fase solo calcula el máximo y el
    /// mínimo y lo explica.
    /// </summary>
    internal interface IEstrategiaMaximos
    {
        CalculoMaximos Calcular(ContextoMaximos c);
    }

    internal static class EstrategiasMaximos
    {
        private static readonly IEstrategiaMaximos nuevo = new EstrategiaNuevo();
        private static readonly IEstrategiaMaximos intermitente = new EstrategiaIntermitente();
        private static readonly IEstrategiaMaximos maduroReciente = new EstrategiaMaduroReciente();
        private static readonly IEstrategiaMaximos maduroPonderada = new EstrategiaMaduroPonderada();
        private static readonly IEstrategiaMaximos maduroClasica = new EstrategiaMaduroClasica();

        public static IEstrategiaMaximos Para(string fase, ParametrosTendencias p)
        {
            if (fase == FasesProducto.INTERMITENTE)
            {
                return intermitente;
            }
            if (fase == FasesProducto.NUEVO)
            {
                return nuevo;
            }
            switch (FormulasMaduros.Normalizar(p?.FormulaMaduros))
            {
                case FormulasMaduros.PONDERADA: return maduroPonderada;
                case FormulasMaduros.CLASICA: return maduroClasica;
                default: return maduroReciente;
            }
        }

        internal static int Techo(decimal valor) => (int)Math.Ceiling(valor);

        internal static string Lote(ResultadoTendencia r)
            => r.Multiplos > 1 ? $"; múltiplos de {r.Multiplos} (se respetan al pedir, no en el máximo)" : string.Empty;

        internal static string Proveedor(ResultadoTendencia r)
            => $"proveedor {r.DiasEnServir} días{(r.DiasEnServirSupuestos ? " (supuestos)" : "")} + {r.DiasSeguridad} de seguridad";
    }

    /// <summary>C (intermitentes): máximo 0-2 (0 sin venta en 12 meses; 1 hasta 6 uds/año; 2 a partir de ahí), mínimo 0 o 1.</summary>
    internal sealed class EstrategiaIntermitente : IEstrategiaMaximos
    {
        public CalculoMaximos Calcular(ContextoMaximos c)
        {
            ResultadoTendencia r = c.R;
            int maximo = r.Ventas12Meses <= 0 ? 0 : r.Ventas12Meses <= 6 ? 1 : 2;
            int minimo = maximo > 1 ? 1 : 0;
            return new CalculoMaximos
            {
                Estrategia = "C",
                Formula = "C: 0-2",
                Maximo = maximo,
                Minimo = minimo,
                VentaBase = Math.Round(r.Ventas12Meses / 12m, 2),
                OrigenVentaBase = "12 meses (intermitente: 0-2)",
                Frase = $"Intermitente: {r.Ventas12Meses} uds en 12 meses → máximo {maximo}, mínimo {minimo}",
                Entradas = new { estrategia = "C", ventas12 = r.Ventas12Meses, maximo, minimo }
            };
        }
    }

    /// <summary>
    /// A (nuevos): D = velocidad demostrada × factor; máximo = D × tope (45 días; 30 si la recompra es baja o aún no se puede
    /// medir) + lo que no se pudo servir a tiempo; mínimo = D × (días en servir + seguridad). NestoAPI#678: velocidad por días
    /// hasta hoy y, si las ventas recientes caen más de un 20 %, la reciente.
    /// </summary>
    internal sealed class EstrategiaNuevo : IEstrategiaMaximos
    {
        public CalculoMaximos Calcular(ContextoMaximos c)
        {
            ResultadoTendencia r = c.R;
            ParametrosTendencias p = c.P;
            VelocidadNuevo velocidadNuevo = c.VelocidadNuevo;
            bool sinRecompra = r.TasaRecompra.HasValue && r.TasaRecompra.Value < p.RecompraMinima;
            // NestoAPI#678: sin recompra todavía medible (menos de 90 días desde la primera venta, o aún sin vender), prudencia:
            // no se sabe si los que lo han probado repetirán.
            bool recompraPorMedir = !r.TasaRecompra.HasValue && (velocidadNuevo == null || velocidadNuevo.EdadDias < DIAS_MEDIR_RECOMPRA);
            int tope = sinRecompra ? p.TopeDiasFaseASinRecompra : recompraPorMedir ? p.TopeDiasFaseASinDatosRecompra : p.TopeDiasFaseA;
            decimal diaria = (r.Velocidad ?? 0m) * r.FactorEstacional / 30m;
            int porVelocidad = EstrategiasMaximos.Techo(diaria * tope);
            int retrasoMensual = RetrasoMensual(r);
            int noServido = Math.Max(r.DemandaNoServida, retrasoMensual);
            int maximo = porVelocidad + noServido;
            int minimo = Math.Min(maximo, EstrategiasMaximos.Techo(diaria * (r.DiasEnServir + r.DiasSeguridad)));
            string formula = velocidadNuevo != null && velocidadNuevo.Cae ? $"A: velocidad reciente × {tope} días"
                : velocidadNuevo != null && velocidadNuevo.PorDias ? $"A: velocidad hasta hoy × {tope} días"
                : $"A: velocidad × {tope} días";
            string recompra = r.TasaRecompra.HasValue
                ? $"recompra {Num(r.TasaRecompra.Value, 0)} %{(sinRecompra ? " (baja: tope " + tope + " días)" : "")}"
                : recompraPorMedir
                ? $"recompra aún sin datos (menos de {DIAS_MEDIR_RECOMPRA} días: tope {tope} días)"
                : "recompra aún sin datos";
            string pendientes = noServido <= 0 ? string.Empty
                : r.DemandaNoServida >= retrasoMensual
                ? $" + {r.DemandaNoServida} pendientes de servir (vencidos)"
                : $" + {retrasoMensual} de retraso al mes ({r.ServidoConRetraso} uds servidas con más de {p.DiasRetraso} días de retraso en {MesesRetraso(r)} meses" +
                    $"{(r.DemandaNoServida > 0 ? $"; pendientes vencidos {r.DemandaNoServida}" : "")})";
            string frase = $"{TextoVelocidadNuevo(r, velocidadNuevo)} × {Num(r.FactorEstacional, 2)} estacional; {recompra}; " +
                $"{EstrategiasMaximos.Proveedor(r)}; tope {tope} días → máximo {porVelocidad}{pendientes} = {maximo}, mínimo {minimo}";
            return new CalculoMaximos
            {
                Estrategia = "A",
                Formula = formula,
                Maximo = maximo,
                Minimo = minimo,
                VentaBase = r.Velocidad ?? 0m,
                OrigenVentaBase = velocidadNuevo != null && velocidadNuevo.Cae ? "velocidad reciente (nuevo)" : "velocidad desde el lanzamiento (nuevo)",
                Frase = frase,
                Entradas = new
                {
                    estrategia = "A",
                    ventaBase = r.Velocidad ?? 0m,
                    factorEstacional = r.FactorEstacional,
                    tasaRecompra = r.TasaRecompra,
                    recompraPorMedir,
                    sinRecompra,
                    topeDias = tope,
                    porVelocidad,
                    noServido,
                    diasMinimo = r.DiasEnServir + r.DiasSeguridad,
                    maximo,
                    minimo
                }
            };
        }
    }

    /// <summary>
    /// NestoAPI#632 — B (maduros), por defecto. «Que la compra deje de crecer por la fórmula»:
    /// <list type="number">
    /// <item>Venta de cada mes sin la estacionalidad de su familia (venta ÷ índice del mes; sin índices, tal cual).</item>
    /// <item>Ponderada hacia lo reciente: (3 × media de 3 meses + 2 × media de 6 + media de 12) ÷ 6.</item>
    /// <item>A la baja (pendiente relativa ≤ <see cref="ParametrosTendencias.PendienteBaja"/>: bajón puntual o declive): la menor
    /// entre la ponderada y la media de 6 meses, para que el año bueno de hace 12 meses no tire hacia arriba.</item>
    /// <item>× factor estacional de los meses que cubre el máximo = venta mensual prevista (D × 30).</item>
    /// <item>Mínimo = D × (días en servir + seguridad); máximo = D × (seguridad + 2 × días en servir), nunca más que la
    /// cobertura objetivo y nunca menos que el mínimo.</item>
    /// <item>En declive confirmado, ni el máximo ni el mínimo suben de los de hoy.</item>
    /// </list>
    /// </summary>
    internal sealed class EstrategiaMaduroReciente : IEstrategiaMaximos
    {
        public const string FORMULA = "B: reciente × (7 + 2 × días)";

        public CalculoMaximos Calcular(ContextoMaximos c)
        {
            ResultadoTendencia r = c.R;
            ParametrosTendencias p = c.P;
            const int FIN = DatosProductoTendencia.ULTIMO;
            bool conIndices = c.Indices != null;
            decimal m3 = Math.Round(CalculadoraSeries.MediaDesestacionalizada(c.D.Ventas, FIN, 3, c.Indices, c.MesReferencia), 2);
            decimal m6 = Math.Round(CalculadoraSeries.MediaDesestacionalizada(c.D.Ventas, FIN, 6, c.Indices, c.MesReferencia), 2);
            decimal m12 = Math.Round(CalculadoraSeries.MediaDesestacionalizada(c.D.Ventas, FIN, 12, c.Indices, c.MesReferencia), 2);
            decimal ponderada = Math.Round((3m * m3 + 2m * m6 + m12) / 6m, 2);
            bool aLaBaja = r.PendienteRelativa.HasValue && r.PendienteRelativa.Value <= p.PendienteBaja;
            bool usaSeisMeses = aLaBaja && m6 < ponderada;
            decimal ventaBase = usaSeisMeses ? m6 : ponderada;
            decimal ventaMensual = Math.Round(ventaBase * r.FactorEstacional, 2);
            int diasMinimo = r.DiasEnServir + r.DiasSeguridad;
            int diasMaximo = r.DiasSeguridad + 2 * r.DiasEnServir;
            int minimoFormula = EstrategiasMaximos.Techo(ventaMensual * diasMinimo / 30m);
            int maximoFormula = EstrategiasMaximos.Techo(ventaMensual * diasMaximo / 30m);
            int topeObjetivo = EstrategiasMaximos.Techo(ventaMensual * r.CoberturaObjetivoDias / 30m);
            int maximo = Math.Max(minimoFormula, Math.Min(maximoFormula, topeObjetivo));
            int minimo = minimoFormula;
            if (ventaMensual <= 0)
            {
                maximo = 0;
                minimo = 0;
            }
            bool declive = r.Tendencia == TendenciasProducto.DECLIVE;
            int? maximoCongelado = null;
            int? minimoCongelado = null;
            if (declive && r.StockMaximoActual.HasValue && maximo > r.StockMaximoActual.Value)
            {
                maximoCongelado = maximo;
                maximo = Math.Max(0, r.StockMaximoActual.Value);
            }
            if (declive && r.StockMinimoActual.HasValue && minimo > r.StockMinimoActual.Value)
            {
                minimoCongelado = minimo;
                minimo = Math.Max(0, r.StockMinimoActual.Value);
            }
            minimo = Math.Min(minimo, maximo);

            string sinEstacionalidad = conIndices ? "sin la estacionalidad de la familia" : "tal cual (familia sin 3 años: sin estacionalidad)";
            string frase = $"Maduro. Venta {sinEstacionalidad}: 3 meses {Num(m3, 1)}, 6 meses {Num(m6, 1)}, 12 meses {Num(m12, 1)} uds/mes → " +
                $"ponderada (3 × {Num(m3, 1)} + 2 × {Num(m6, 1)} + {Num(m12, 1)}) ÷ 6 = {Num(ponderada, 1)}";
            if (usaSeisMeses)
            {
                frase += $"; va a la baja (pendiente {Num(r.PendienteRelativa.Value, 1)} %/mes): se toma la de 6 meses, {Num(m6, 1)}, que es menor";
            }
            frase += $"; × {Num(r.FactorEstacional, 2)} estacional de los próximos meses = {Num(ventaMensual, 1)} uds/mes; " +
                $"{EstrategiasMaximos.Proveedor(r)}{EstrategiasMaximos.Lote(r)}; " +
                $"mínimo = {Num(ventaMensual, 1)} × {diasMinimo} ÷ 30 = {minimoFormula}; " +
                $"máximo = {Num(ventaMensual, 1)} × ({r.DiasSeguridad} + 2 × {r.DiasEnServir}) ÷ 30 = {maximoFormula}";
            if (maximoFormula > topeObjetivo && ventaMensual > 0)
            {
                frase += $", acotado por el objetivo de {r.CoberturaObjetivoDias} días a {Math.Max(minimoFormula, topeObjetivo)}";
            }
            if (maximoCongelado.HasValue || minimoCongelado.HasValue)
            {
                frase += $"; en declive no sube: se queda en los {r.StockMaximoActual}/{r.StockMinimoActual} de hoy";
            }
            frase += $" → máximo {maximo}, mínimo {minimo}";

            string origen = (usaSeisMeses ? "6 meses" : "ponderada") + (conIndices ? " sin estacionalidad" : "")
                + (usaSeisMeses ? " (a la baja)" : "") + (maximoCongelado.HasValue ? "; declive: no sube" : "");
            return new CalculoMaximos
            {
                Estrategia = "B reciente",
                Formula = FORMULA,
                Maximo = maximo,
                Minimo = minimo,
                VentaBase = ventaBase,
                OrigenVentaBase = origen,
                Frase = frase,
                Entradas = new
                {
                    estrategia = "B reciente",
                    desestacionalizada = conIndices,
                    media3 = m3,
                    media6 = m6,
                    media12 = m12,
                    ponderada,
                    pendienteRelativa = r.PendienteRelativa,
                    aLaBaja,
                    ventaBase,
                    origenVentaBase = origen,
                    factorEstacional = r.FactorEstacional,
                    ventaMensual,
                    diasMinimo,
                    diasMaximo,
                    coberturaObjetivoDias = r.CoberturaObjetivoDias,
                    minimoFormula,
                    maximoFormula,
                    topeObjetivo,
                    declive,
                    maximoActual = r.StockMaximoActual,
                    minimoActual = r.StockMinimoActual,
                    maximoSinCongelar = maximoCongelado,
                    minimoSinCongelar = minimoCongelado,
                    maximo,
                    minimo
                }
            };
        }
    }

    /// <summary>B del corte 2 (#630), para volver atrás: ponderada tal cual × factor; sin desestacionalizar ni tope de declive.</summary>
    internal sealed class EstrategiaMaduroPonderada : IEstrategiaMaximos
    {
        public const string FORMULA = "B: ponderada × (7 + 2 × días)";

        public CalculoMaximos Calcular(ContextoMaximos c)
        {
            ResultadoTendencia r = c.R;
            decimal diaria = r.VentaMediaPonderada * r.FactorEstacional / 30m;
            int minimo = EstrategiasMaximos.Techo(diaria * (r.DiasEnServir + r.DiasSeguridad));
            int formula = EstrategiasMaximos.Techo(diaria * (r.DiasSeguridad + 2 * r.DiasEnServir));
            // NestoAPI#630 (Carlos, 16/10/26): ni el máximo ni el mínimo incluyen el múltiplo de compra; se respeta al pedir.
            int tope = EstrategiasMaximos.Techo(diaria * r.CoberturaObjetivoDias);
            int maximo = Math.Max(minimo, Math.Min(formula, tope));
            if (diaria <= 0)
            {
                maximo = 0;
                minimo = 0;
            }
            string acotado = formula > maximo ? $" (acotado por el objetivo de {r.CoberturaObjetivoDias} días)" : string.Empty;
            string frase = $"Maduro. 6 meses: {Num(r.VentaMedia6Meses, 1)} uds/mes, ponderada {Num(r.VentaMediaPonderada, 1)} × {Num(r.FactorEstacional, 2)} estacional" +
                $"{(r.ConEstacionalidad ? "" : " (familia sin 3 años)")}; {EstrategiasMaximos.Proveedor(r)}{EstrategiasMaximos.Lote(r)}; " +
                $"objetivo {r.CoberturaObjetivoDias} días → máximo {maximo}{acotado}, mínimo {minimo}";
            return new CalculoMaximos
            {
                Estrategia = "B ponderada",
                Formula = FORMULA,
                Maximo = maximo,
                Minimo = minimo,
                VentaBase = r.VentaMediaPonderada,
                OrigenVentaBase = "ponderada (corte 2)",
                Frase = frase,
                Entradas = new { estrategia = "B ponderada", ventaBase = r.VentaMediaPonderada, factorEstacional = r.FactorEstacional, minimo, maximoFormula = formula, topeObjetivo = tope, maximo }
            };
        }
    }

    /// <summary>
    /// La de siempre de la ficha (ControlStockBase), para volver atrás del todo: media de hasta 24 meses (los que tenga el
    /// producto, al menos 1) ÷ 30 × (seguridad + 2 × días) hacia arriba; mínimo, × (días + seguridad), redondeado (hacia arriba
    /// si no llega a 1). Sin estacionalidad ni objetivo.
    /// </summary>
    internal sealed class EstrategiaMaduroClasica : IEstrategiaMaximos
    {
        public const string FORMULA = "B: media 24 m × (7 + 2 × días)";

        public CalculoMaximos Calcular(ContextoMaximos c)
        {
            ResultadoTendencia r = c.R;
            int meses = Math.Max(1, Math.Min(24, r.MesesDesdePrimeraVenta ?? 24));
            int unidades = CalculadoraSeries.Suma(c.D.Ventas, DatosProductoTendencia.ULTIMO, meses);
            decimal mensual = (decimal)unidades / meses;
            decimal puntoPedido = mensual / 30m * (r.DiasSeguridad + r.DiasEnServir);
            int minimo = puntoPedido < 1m ? EstrategiasMaximos.Techo(puntoPedido) : (int)Math.Round(puntoPedido, 0, MidpointRounding.AwayFromZero);
            int maximo = EstrategiasMaximos.Techo(mensual / 30m * (r.DiasSeguridad + 2 * r.DiasEnServir));
            string frase = $"Maduro (fórmula de siempre). Media de {meses} meses: {unidades} uds, {Num(mensual, 1)} uds/mes; " +
                $"{EstrategiasMaximos.Proveedor(r)}{EstrategiasMaximos.Lote(r)} → máximo {maximo}, mínimo {minimo}";
            return new CalculoMaximos
            {
                Estrategia = "B clásica",
                Formula = FORMULA,
                Maximo = maximo,
                Minimo = minimo,
                VentaBase = Math.Round(mensual, 2),
                OrigenVentaBase = $"media de {meses} meses (fórmula de siempre)",
                Frase = frase,
                Entradas = new { estrategia = "B clásica", meses, unidades, ventaBase = Math.Round(mensual, 2), maximo, minimo }
            };
        }
    }
}
