using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#630 (corte 2 de la épica #628): de las series de un producto a sus indicadores, su propuesta de máximo y mínimo
    /// para Algete y la frase que la explica. Puro: sin BD (los datos llegan en <see cref="DatosProductoTendencia"/>).
    /// <para><b>Fase</b> (por edad y volumen): A nuevo si hace menos de <see cref="ParametrosTendencias.MesesFaseA"/> meses de la
    /// primera venta (o de la primera entrada, si aún no se ha vendido); si no, C intermitente si en 12 meses vende menos de
    /// <see cref="ParametrosTendencias.UnidadesAnioIntermitente"/> uds; si no, B maduro.</para>
    /// <para><b>Máximo y mínimo</b> (B): misma estructura que la fórmula de hoy (7 + 2 × días en servir), pero con la venta
    /// ponderada reciente en vez de la media de 24 meses y con el factor estacional de la familia para los próximos meses:
    /// D = venta ponderada × factor ÷ 30 (uds/día); mínimo (punto de pedido) = D × (días en servir + seguridad); máximo =
    /// D × (seguridad + 2 × días en servir), al menos el mínimo más un múltiplo, y nunca más que D × cobertura objetivo.</para>
    /// <para>A: D = velocidad demostrada × factor; máximo = D × tope (45 días, 30 si no hay recompra) + lo pendiente de servir.
    /// NestoAPI#678: la velocidad va por días hasta hoy (también la de un nuevo de este mes, con la primera venta después del
    /// corte) y, si las ventas de los últimos días caen, se usa la reciente; con menos de 90 días (recompra sin medir), tope de
    /// 30 días. Ver <see cref="CalcularVelocidadNuevo"/>.
    /// C: máximo 0-2 (0 sin venta en 12 meses; 1 hasta 6 uds/año; 2 a partir de ahí), mínimo 0 o 1.</para>
    /// <para>NestoAPI#655: P (fabricación propia, familias PRP), sea cual sea la fase: ver <see cref="ProponerPropio"/>.</para>
    /// </summary>
    public static class MotorTendencias
    {
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// Lo que merece fila: con stock, con venta en 24 meses, con pendientes o con compras abiertas; NestoAPI#678: o con venta en
        /// los días leídos (un nuevo de este mes que llegó y se vendió después del corte no tiene nada de lo anterior).
        /// </summary>
        public static bool EsRelevante(DatosProductoTendencia d)
            => d != null && (d.Stock > 0 || CalculadoraSeries.Suma(d.Ventas, DatosProductoTendencia.ULTIMO, 24) > 0
                || d.UnidadesPendientes > 0 || d.UnidadesPedidoCompraAbierto > 0
                || (d.VentasDiarias != null && d.VentasDiarias.Sum(v => v.Cantidad) > 0));

        public static string Fase(int? mesesEdad, int ventas12, ParametrosTendencias p)
        {
            if (mesesEdad.HasValue && mesesEdad.Value < p.MesesFaseA)
            {
                return FasesProducto.NUEVO;
            }
            return ventas12 < p.UnidadesAnioIntermitente ? FasesProducto.INTERMITENTE : FasesProducto.MADURO;
        }

        public static string Tendencia(decimal? pendiente, decimal? ratioTrimestre, ContextoCincoAnios contexto, ParametrosTendencias p)
        {
            if (!pendiente.HasValue)
            {
                return TendenciasProducto.SIN_DATOS;
            }
            if (pendiente.Value >= p.PendienteSube)
            {
                return TendenciasProducto.SUBE;
            }
            if (pendiente.Value > p.PendienteBaja)
            {
                return TendenciasProducto.ESTABLE;
            }
            bool contextoConfirma = contexto?.PorcentajeDelPico != null
                && contexto.PorcentajeDelPico.Value <= p.PorcentajePicoDeclive
                && (contexto.AniosDesdePico ?? 0) >= 1
                && (!ratioTrimestre.HasValue || ratioTrimestre.Value < p.RatioTrimestreDeclive);
            return contextoConfirma ? TendenciasProducto.DECLIVE : TendenciasProducto.BAJON_PUNTUAL;
        }

        /// <summary>Factor estacional de los próximos meses (los que cubre el máximo, de 1 a 3), o 1 sin estacionalidad.</summary>
        public static decimal FactorProximoPeriodo(decimal[] indices, DateTime mesReferencia, int diasHorizonte)
        {
            if (indices == null)
            {
                return 1m;
            }
            int meses = Math.Min(3, Math.Max(1, (int)Math.Ceiling(diasHorizonte / 30m)));
            decimal suma = 0m;
            for (int k = 1; k <= meses; k++)
            {
                suma += indices[mesReferencia.AddMonths(k).Month - 1];
            }
            return Math.Round(suma / meses, 2);
        }

        /// <param name="mesReferencia">Primer día del último mes cerrado (el de la posición <see cref="DatosProductoTendencia.ULTIMO"/>).</param>
        /// <param name="indicesFamilia">Índices estacionales de la familia (12, enero primero) o null.</param>
        public static ResultadoTendencia Calcular(DatosProductoTendencia d, decimal[] indicesFamilia, DateTime mesReferencia, ParametrosTendencias p)
        {
            if (d == null)
            {
                throw new ArgumentNullException(nameof(d));
            }
            p = p ?? new ParametrosTendencias();
            const int FIN = DatosProductoTendencia.ULTIMO;
            DateTime fechaCorte = mesReferencia.AddMonths(1);
            DateTime hoy = FechaDatos(d, fechaCorte);
            DateTime? primeraVenta = d.FechaPrimeraVenta.HasValue && d.FechaPrimeraVenta.Value < fechaCorte ? d.FechaPrimeraVenta : null;
            // NestoAPI#678: la primera venta del mes en curso (después del corte, antes de hoy). Solo da la edad si no hay nada antes
            // del corte (ni venta ni entrada): uno que llevaba años en el almacén sin venderse no se vuelve nuevo por venderse hoy.
            DateTime? primeraVentaMesEnCurso = !primeraVenta.HasValue && d.FechaPrimeraVenta.HasValue
                && d.FechaPrimeraVenta.Value >= fechaCorte && d.FechaPrimeraVenta.Value < hoy ? d.FechaPrimeraVenta : null;
            DateTime? origen = primeraVenta ?? (d.FechaPrimeraEntrada.HasValue && d.FechaPrimeraEntrada.Value < fechaCorte ? d.FechaPrimeraEntrada : null)
                ?? primeraVentaMesEnCurso;

            int ventas12 = CalculadoraSeries.Suma(d.Ventas, FIN, 12);
            int? mesesEdad = origen.HasValue ? CalculadoraSeries.MesesEntre(origen.Value, fechaCorte) : (int?)null;

            ResultadoTendencia r = new ResultadoTendencia
            {
                Producto = d.Producto?.Trim(),
                Nombre = d.Nombre?.Trim(),
                Familia = d.Familia?.Trim(),
                Grupo = d.Grupo?.Trim(),
                Proveedor = d.Proveedor?.Trim(),
                EstadoProducto = d.Estado,
                Stock = d.Stock,
                PrecioMedio = d.PrecioMedio,
                Ventas12Meses = ventas12,
                VentaMedia6Meses = Math.Round(CalculadoraSeries.Media(d.Ventas, FIN, 6), 2),
                VentaMediaPonderada = Math.Round(CalculadoraSeries.MediaPonderada(d.Ventas, FIN), 2),
                MesesDesdePrimeraVenta = primeraVenta.HasValue ? CalculadoraSeries.MesesEntre(primeraVenta.Value, fechaCorte) : (int?)null,
                Fase = Fase(mesesEdad, ventas12, p),
                DiasEnServir = d.DiasEnServir ?? p.DiasEnServirPorDefecto,
                DiasEnServirSupuestos = !d.DiasEnServir.HasValue,
                DiasSeguridad = d.EsFabricacionPropia ? p.DiasSeguridadPropios : p.DiasSeguridad,
                Multiplos = Math.Max(1, d.MultiplosAlg),
                StockMaximoActual = d.StockMaximoAlg,
                StockMinimoActual = d.StockMinimoAlg,
                DemandaNoServida = Math.Max(0, d.UnidadesPendientes),
                ServidoConRetraso = Math.Max(0, d.UnidadesServidasConRetraso),
                PedidoCompraAbierto = Math.Max(0, d.UnidadesPedidoCompraAbierto),
                ConEstacionalidad = indicesFamilia != null
            };

            // Indicadores de tendencia
            r.PendienteRelativa = CalculadoraSeries.PendienteRelativa(d.Ventas, FIN, indicesFamilia, mesReferencia);
            r.RatioTrimestre = CalculadoraSeries.RatioTrimestre(d.Ventas, FIN);
            ContextoCincoAnios contexto = CalculadoraSeries.Contexto(d.Ventas, FIN, mesReferencia);
            r.AnioPico = contexto.AnioPico;
            r.PorcentajeDelPico = contexto.PorcentajeDelPico;
            r.AniosDesdePico = contexto.AniosDesdePico;
            r.Tendencia = r.Fase == FasesProducto.MADURO ? Tendencia(r.PendienteRelativa, r.RatioTrimestre, contexto, p) : null;

            VelocidadNuevo velocidadNuevo = null;
            DateTime? lanzamiento = primeraVenta ?? primeraVentaMesEnCurso;
            if (r.Fase == FasesProducto.NUEVO && lanzamiento.HasValue)
            {
                if (!primeraVenta.HasValue)
                {
                    r.MesesDesdePrimeraVenta = 0; // nuevo de este mes
                }
                velocidadNuevo = CalcularVelocidadNuevo(d, lanzamiento.Value, fechaCorte, hoy, p);
                r.Velocidad = velocidadNuevo.Usada;
                // NestoAPI#678: con datos por días, la recompra también se mira hasta hoy.
                r.TasaRecompra = CalculadoraSeries.TasaRecompra(d.VentasPorCliente, lanzamiento.Value, velocidadNuevo.PorDias ? hoy : fechaCorte);
            }
            else if (r.Fase == FasesProducto.NUEVO)
            {
                r.Velocidad = 0m;
            }

            r.VentaMensualReferencia = r.Fase == FasesProducto.NUEVO ? (r.Velocidad ?? 0m)
                : r.Fase == FasesProducto.MADURO ? r.VentaMedia6Meses
                : Math.Round(CalculadoraSeries.Media(d.Ventas, FIN, 12), 2);

            // Cobertura y exceso
            // NestoAPI#630 (Carlos, 16/10/26): el múltiplo de compra (lote) no sube la cobertura alcanzable ni el objetivo: se
            // respeta al hacer el pedido de compra, no en los máximos ni en el exceso.
            r.CoberturaAlcanzableDias = r.DiasEnServir + r.DiasSeguridad;
            r.CoberturaObjetivoDias = Math.Max(p.CoberturaObjetivoDias, r.CoberturaAlcanzableDias);
            r.CoberturaMeses = r.VentaMedia6Meses > 0 ? Math.Round(r.Stock / r.VentaMedia6Meses, 1) : (decimal?)null;
            int stockObjetivo = (int)Math.Ceiling(r.CoberturaObjetivoDias / 30m * r.VentaMensualReferencia);
            r.ExcesoUnidades = Math.Max(0, r.Stock - stockObjetivo);
            r.ExcesoValorado = Math.Round(r.ExcesoUnidades * r.PrecioMedio, 2);

            // Propuesta
            int diasHorizonte = r.DiasSeguridad + 2 * r.DiasEnServir;
            r.FactorEstacional = FactorProximoPeriodo(indicesFamilia, mesReferencia, diasHorizonte);
            if (d.EsFabricacionPropia)
            {
                ProponerPropio(r, p);
            }
            else
            {
                Proponer(r, d, p, velocidadNuevo);
            }
            AvisarLoteGrande(r, p);

            r.EntradasJson = JsonConvert.SerializeObject(new
            {
                mesReferencia = mesReferencia.ToString("yyyy-MM"),
                ventas = d.Ventas,
                stockFinMes12 = d.StockFinMes.Skip(DatosProductoTendencia.MESES_SERIE - 12).ToArray(),
                indicesFamilia,
                ventasPorAnio = contexto.VentasPorAnio,
                primeraVenta = primeraVenta?.ToString("yyyy-MM-dd"),
                diasEnServirSupuestos = r.DiasEnServirSupuestos,
                coberturaObjetivoConfigurada = p.CoberturaObjetivoDias,
                tipoExclusiva = d.TipoExclusiva?.Trim(),
                pendienteVencido = r.DemandaNoServida,
                servidoConRetraso = r.ServidoConRetraso,
                diasRetraso = p.DiasRetraso,
                propios = d.EsFabricacionPropia
                    ? new
                    {
                        lote = r.Multiplos,
                        diasSeguridad = p.DiasSeguridadPropios,
                        grupo = r.Grupo,
                        caduca = p.VidaUtilMeses(r.Grupo).HasValue,
                        vidaUtilMeses = p.VidaUtilMeses(r.Grupo),
                        tope = TopePropios(r.VentaMensualReferencia, p.VidaUtilMeses(r.Grupo))
                    }
                    : null,
                // NestoAPI#678: de dónde sale la velocidad de un nuevo (por días hasta hoy y ventana reciente)
                nuevo = velocidadNuevo == null
                    ? null
                    : new
                    {
                        lanzamiento = velocidadNuevo.Lanzamiento.ToString("yyyy-MM-dd"),
                        datosHasta = hoy.ToString("yyyy-MM-dd"),
                        porDias = velocidadNuevo.PorDias,
                        delMesEnCurso = velocidadNuevo.DelMesEnCurso,
                        unidades = velocidadNuevo.Unidades,
                        dias = velocidadNuevo.EdadDias,
                        diasContados = velocidadNuevo.DiasContados,
                        desdeLanzamiento = velocidadNuevo.DesdeLanzamiento,
                        ventana = velocidadNuevo.Ventana,
                        unidadesVentana = velocidadNuevo.UnidadesVentana,
                        reciente = velocidadNuevo.Reciente,
                        cae = velocidadNuevo.Cae
                    }
            });
            return r;
        }

        /// <summary>
        /// NestoAPI#678: hasta dónde llegan los datos (exclusive): <see cref="DatosProductoTendencia.FechaDatos"/>, nunca antes del
        /// corte del mes de referencia.
        /// </summary>
        internal static DateTime FechaDatos(DatosProductoTendencia d, DateTime fechaCorte)
            => d.FechaDatos.HasValue && d.FechaDatos.Value.Date > fechaCorte ? d.FechaDatos.Value.Date : fechaCorte;

        /// <summary>
        /// NestoAPI#678: la fecha de los datos de un cálculo hecho en <paramref name="ahora"/> para <paramref name="mesReferencia"/>:
        /// hoy (a las 00:00, solo días completos) si el mes de referencia es el último cerrado; si es un mes pasado, como si se
        /// hubiera calculado el día 1 (el corte), para que regenerar un mes antiguo dé lo mismo que el día que se generó.
        /// </summary>
        public static DateTime FechaDatos(DateTime mesReferencia, DateTime ahora)
        {
            DateTime fechaCorte = new DateTime(mesReferencia.Year, mesReferencia.Month, 1).AddMonths(1);
            DateTime hoy = ahora.Date;
            return hoy > fechaCorte && hoy < fechaCorte.AddMonths(1) ? hoy : fechaCorte;
        }

        /// <summary>
        /// NestoAPI#678: días desde la primera venta que necesita la tasa de recompra para medirse (los 60 de lanzamiento + 30 para
        /// volver de <see cref="CalculadoraSeries.TasaRecompra"/>). Antes, el tope del máximo es
        /// <see cref="ParametrosTendencias.TopeDiasFaseASinDatosRecompra"/>.
        /// </summary>
        internal const int DIAS_MEDIR_RECOMPRA = 90;

        /// <summary>NestoAPI#678: de dónde sale la velocidad de un nuevo, en palabras, para la frase de la ficha y del Excel.</summary>
        internal static string TextoVelocidadNuevo(ResultadoTendencia r, VelocidadNuevo v)
        {
            if (v == null || !v.PorDias)
            {
                string edad = r.MesesDesdePrimeraVenta.HasValue ? $"{r.MesesDesdePrimeraVenta} meses desde la primera venta" : "aún sin ventas";
                return $"Nuevo ({edad}): velocidad {Num(r.Velocidad ?? 0m, 1)} uds/mes";
            }
            string dias = v.EdadDias == 1 ? "1 día" : $"{v.EdadDias} días";
            string periodo = v.EdadDias <= 0 ? string.Empty
                : $" (del {v.Lanzamiento.ToString("dd/MM/yyyy", ES)} al {v.DatosHasta.AddDays(-1).ToString("dd/MM/yyyy", ES)})";
            string contados = v.DiasContados > v.EdadDias
                ? $" (se cuentan al menos {v.DiasContados} días, para que el arranque no dispare el máximo)"
                : string.Empty;
            string texto = v.DelMesEnCurso
                ? $"Nuevo de este mes: {Uds(v.Unidades)} en {dias}{periodo} = {Num(v.DesdeLanzamiento, 1)} uds/mes{contados}"
                : $"Nuevo ({r.MesesDesdePrimeraVenta} meses desde la primera venta): {Uds(v.Unidades)} en {dias}{periodo} = {Num(v.DesdeLanzamiento, 1)} uds/mes{contados}";
            if (v.Ventana <= 0)
            {
                texto += $"; aún es pronto para ver si las ventas caen (se mira a partir de 14 días)";
            }
            else if (v.Cae)
            {
                texto += $"; las ventas de los últimos {v.Ventana} días han caído a {Num(v.Reciente ?? 0m, 1)} uds/mes ({Uds(v.UnidadesVentana)}): se usa esa velocidad";
            }
            else
            {
                texto += $"; los últimos {v.Ventana} días van a {Num(v.Reciente ?? 0m, 1)} uds/mes ({Uds(v.UnidadesVentana)}): sin caída";
            }
            return texto + $". Velocidad {Num(v.Usada, 1)} uds/mes";
        }

        private static string Uds(int unidades) => unidades == 1 ? "1 ud" : $"{unidades} uds";

        /// <summary>NestoAPI#678: la velocidad de un nuevo y de dónde sale (para la frase y las entradas).</summary>
        internal class VelocidadNuevo
        {
            public DateTime Lanzamiento { get; set; }
            /// <summary>Hasta dónde llegan los datos (exclusive).</summary>
            public DateTime DatosHasta { get; set; }
            /// <summary>Calculada por días (con <see cref="DatosProductoTendencia.VentasDiarias"/>); si no, por meses cerrados, como antes.</summary>
            public bool PorDias { get; set; }
            /// <summary>La primera venta es de después del corte (del mes en curso).</summary>
            public bool DelMesEnCurso { get; set; }
            /// <summary>Unidades vendidas desde la primera venta hasta los datos (solo por días).</summary>
            public int Unidades { get; set; }
            /// <summary>Días reales desde la primera venta hasta los datos.</summary>
            public int EdadDias { get; set; }
            /// <summary>Días por los que se divide (al menos <see cref="ParametrosTendencias.DiasMinimosVelocidadNuevo"/>).</summary>
            public int DiasContados { get; set; }
            public decimal DesdeLanzamiento { get; set; }
            /// <summary>Días de la ventana reciente (0 si el producto es aún demasiado nuevo para comparar).</summary>
            public int Ventana { get; set; }
            public int UnidadesVentana { get; set; }
            public decimal? Reciente { get; set; }
            /// <summary>La ventana reciente va por debajo de la velocidad desde el lanzamiento: se usa la reciente.</summary>
            public bool Cae { get; set; }
            public decimal Usada => Cae && Reciente.HasValue ? Reciente.Value : DesdeLanzamiento;
        }

        /// <summary>
        /// NestoAPI#678 (Carlos, 20/10/26: «velocidad hasta hoy […] y que el máximo y el mínimo tengan en cuenta cualquier caída sin
        /// esperar a final de mes»). Con ventas por días: velocidad desde el lanzamiento = unidades desde la primera venta hasta
        /// hoy ÷ max(días reales, <see cref="ParametrosTendencias.DiasMinimosVelocidadNuevo"/>) × 30,44; y la ventana reciente
        /// (<see cref="CalculadoraSeries.DiasVentanaReciente"/>): si su velocidad es menor, se usa esa (la más baja). Sin ventas por
        /// días (no leídas), por meses cerrados hasta el corte, como antes (<see cref="CalculadoraSeries.Velocidad"/>).
        /// </summary>
        internal static VelocidadNuevo CalcularVelocidadNuevo(DatosProductoTendencia d, DateTime lanzamiento, DateTime fechaCorte, DateTime hoy, ParametrosTendencias p)
        {
            VelocidadNuevo v = new VelocidadNuevo
            {
                Lanzamiento = lanzamiento.Date,
                DatosHasta = hoy,
                DelMesEnCurso = lanzamiento >= fechaCorte
            };
            if (d.VentasDiarias == null)
            {
                v.EdadDias = CalculadoraSeries.DiasEntre(lanzamiento, fechaCorte);
                v.DesdeLanzamiento = v.DelMesEnCurso ? 0m : CalculadoraSeries.Velocidad(d.Ventas, DatosProductoTendencia.ULTIMO, lanzamiento, fechaCorte);
                return v;
            }
            v.PorDias = true;
            v.EdadDias = CalculadoraSeries.DiasEntre(lanzamiento, hoy);
            v.Unidades = CalculadoraSeries.SumaDiaria(d.VentasDiarias, lanzamiento, hoy);
            v.DiasContados = Math.Max(Math.Max(1, p.DiasMinimosVelocidadNuevo), v.EdadDias);
            v.DesdeLanzamiento = CalculadoraSeries.VelocidadPorDias(v.Unidades, v.DiasContados);
            v.Ventana = CalculadoraSeries.DiasVentanaReciente(v.EdadDias, p.DiasVentanaRecienteNuevos);
            if (v.Ventana > 0)
            {
                v.UnidadesVentana = CalculadoraSeries.SumaDiaria(d.VentasDiarias, hoy.AddDays(-v.Ventana), hoy);
                v.Reciente = CalculadoraSeries.VelocidadPorDias(v.UnidadesVentana, v.Ventana);
                v.Cae = v.Reciente.Value < v.DesdeLanzamiento;
            }
            return v;
        }

        private static void Proponer(ResultadoTendencia r, DatosProductoTendencia d, ParametrosTendencias p, VelocidadNuevo velocidadNuevo)
        {
            string resumen = Resumen(r, p.DiasRetraso);
            bool hayStock = r.Stock > 0;
            // Un intermitente puede pasar medio año sin vender sin estar muerto: a él se le mira un año entero.
            int mesesSinSalida = r.Fase == FasesProducto.INTERMITENTE ? Math.Max(12, p.MesesSinSalidaLiquidar) : p.MesesSinSalidaLiquidar;
            int ventasSinSalida = CalculadoraSeries.Suma(d.Ventas, DatosProductoTendencia.ULTIMO, mesesSinSalida);
            string anularCompra = r.PedidoCompraAbierto > 0 ? $" y anular las {r.PedidoCompraAbierto} uds de pedidos de compra abiertos" : string.Empty;

            // Estados que ya no se trabajan (4 anular, −1 nulo…): si queda stock, a liquidar; nada de máximos.
            if (r.EstadoProducto.HasValue && r.EstadoProducto.Value != 0 && r.EstadoProducto.Value != 1)
            {
                r.Formula = "Estado " + r.EstadoProducto.Value;
                if (hayStock)
                {
                    Fijar(r, AccionesPropuesta.LIQUIDAR, 0, 0,
                        $"Liquidar (Outlet o Ganavisiones){anularCompra}: está en estado {r.EstadoProducto.Value} y quedan {r.Stock} uds ({Euros(r.Stock * r.PrecioMedio)}). {resumen}");
                }
                else
                {
                    Fijar(r, AccionesPropuesta.NADA, null, null, $"Estado {r.EstadoProducto.Value} y sin stock: nada que hacer. {resumen}");
                }
                return;
            }

            if (hayStock && r.Fase != FasesProducto.NUEVO && ventasSinSalida <= 0)
            {
                r.Formula = "Sin salida";
                Fijar(r, AccionesPropuesta.LIQUIDAR, 0, 0,
                    $"Liquidar (Outlet o Ganavisiones){anularCompra}: ninguna venta en {mesesSinSalida} meses y {r.Stock} uds en stock ({Euros(r.ExcesoValorado)}). {resumen}");
                return;
            }

            if (r.Tendencia == TendenciasProducto.DECLIVE && r.CoberturaMeses.HasValue && r.CoberturaMeses.Value > p.MesesCoberturaLiquidar)
            {
                r.Formula = "Declive";
                Fijar(r, AccionesPropuesta.LIQUIDAR, 0, 0,
                    $"Liquidar (Outlet o Ganavisiones){anularCompra}: declive confirmado y {Num(r.CoberturaMeses.Value, 1)} meses de cobertura (más de {p.MesesCoberturaLiquidar}); sobran {r.ExcesoUnidades} uds ({Euros(r.ExcesoValorado)}). {resumen}");
                return;
            }

            if (r.Tendencia == TendenciasProducto.DECLIVE && r.EstadoProducto == 0 && r.CoberturaMeses.HasValue && r.CoberturaMeses.Value > p.MesesCoberturaEstado1)
            {
                r.Formula = "Declive";
                Fijar(r, AccionesPropuesta.ESTADO_1, null, null,
                    $"Pasar a estado 1 (sobre pedido, sin máximos){anularCompra}: declive confirmado y {Num(r.CoberturaMeses.Value, 1)} meses de cobertura (más de {p.MesesCoberturaEstado1}). {resumen}");
                return;
            }

            string calculo;
            int maximo, minimo;
            if (r.Fase == FasesProducto.INTERMITENTE)
            {
                r.Formula = "C: 0-2";
                maximo = r.Ventas12Meses <= 0 ? 0 : r.Ventas12Meses <= 6 ? 1 : 2;
                minimo = maximo > 1 ? 1 : 0;
                calculo = $"Intermitente: {r.Ventas12Meses} uds en 12 meses → máximo {maximo}, mínimo {minimo}";
            }
            else if (r.Fase == FasesProducto.NUEVO)
            {
                bool sinRecompra = r.TasaRecompra.HasValue && r.TasaRecompra.Value < p.RecompraMinima;
                // NestoAPI#678: sin recompra todavía medible (menos de 90 días desde la primera venta, o aún sin vender), prudencia:
                // no se sabe si los que lo han probado repetirán.
                bool recompraPorMedir = !r.TasaRecompra.HasValue && (velocidadNuevo == null || velocidadNuevo.EdadDias < DIAS_MEDIR_RECOMPRA);
                int tope = sinRecompra ? p.TopeDiasFaseASinRecompra : recompraPorMedir ? p.TopeDiasFaseASinDatosRecompra : p.TopeDiasFaseA;
                decimal diaria = (r.Velocidad ?? 0m) * r.FactorEstacional / 30m;
                int porVelocidad = (int)Math.Ceiling(diaria * tope);
                int retrasoMensual = RetrasoMensual(r);
                int noServido = Math.Max(r.DemandaNoServida, retrasoMensual);
                maximo = porVelocidad + noServido;
                minimo = Math.Min(maximo, (int)Math.Ceiling(diaria * (r.DiasEnServir + r.DiasSeguridad)));
                r.Formula = velocidadNuevo != null && velocidadNuevo.Cae ? $"A: velocidad reciente × {tope} días"
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
                calculo = $"{TextoVelocidadNuevo(r, velocidadNuevo)} × {Num(r.FactorEstacional, 2)} estacional; {recompra}; " +
                    $"proveedor {r.DiasEnServir} días{(r.DiasEnServirSupuestos ? " (supuestos)" : "")} + {r.DiasSeguridad} de seguridad; tope {tope} días → " +
                    $"máximo {porVelocidad}{pendientes} = {maximo}, mínimo {minimo}";
            }
            else
            {
                decimal diaria = r.VentaMediaPonderada * r.FactorEstacional / 30m;
                minimo = (int)Math.Ceiling(diaria * (r.DiasEnServir + r.DiasSeguridad));
                int formula = (int)Math.Ceiling(diaria * (r.DiasSeguridad + 2 * r.DiasEnServir));
                // NestoAPI#630 (Carlos, 16/10/26): ni el máximo ni el mínimo incluyen el múltiplo de compra; se respeta al pedir.
                int tope = (int)Math.Ceiling(diaria * r.CoberturaObjetivoDias);
                maximo = Math.Max(minimo, Math.Min(formula, tope));
                if (diaria <= 0)
                {
                    maximo = 0;
                    minimo = 0;
                }
                r.Formula = "B: ponderada × (7 + 2 × días)";
                string lote = r.Multiplos > 1 ? $"; múltiplos de {r.Multiplos} (se respetan al pedir, no en el máximo)" : string.Empty;
                string acotado = formula > maximo ? $" (acotado por el objetivo de {r.CoberturaObjetivoDias} días)" : string.Empty;
                calculo = $"Maduro. 6 meses: {Num(r.VentaMedia6Meses, 1)} uds/mes, ponderada {Num(r.VentaMediaPonderada, 1)} × {Num(r.FactorEstacional, 2)} estacional" +
                    $"{(r.ConEstacionalidad ? "" : " (familia sin 3 años)")}; proveedor {r.DiasEnServir} días{(r.DiasEnServirSupuestos ? " (supuestos)" : "")} + {r.DiasSeguridad} de seguridad{lote}; " +
                    $"objetivo {r.CoberturaObjetivoDias} días → máximo {maximo}{acotado}, mínimo {minimo}";
            }

            string actual = r.StockMaximoActual.HasValue ? $" (hoy {r.StockMaximoActual}/{r.StockMinimoActual})" : " (hoy sin control de stock en ALG)";
            string accion;
            if (r.ExcesoUnidades > 0 && r.PedidoCompraAbierto > 0)
            {
                accion = AccionesPropuesta.PARAR_COMPRAS;
                calculo = $"Parar compras: hay {r.PedidoCompraAbierto} uds en pedidos de compra abiertos y sobran {r.ExcesoUnidades} uds ({Euros(r.ExcesoValorado)}) " +
                    $"sobre el objetivo de {r.CoberturaObjetivoDias} días. " + calculo;
            }
            else if (r.EstadoProducto == 1)
            {
                // Sobre pedido: no tiene máximos que ajustar; solo se avisa si sobra o si hay que parar compras (arriba).
                Fijar(r, AccionesPropuesta.MANTENER, null, null, $"Ya está en estado 1 (sobre pedido). {resumen}");
                return;
            }
            else
            {
                accion = r.StockMaximoActual == maximo && (r.StockMinimoActual ?? 0) == minimo ? AccionesPropuesta.MANTENER : AccionesPropuesta.AJUSTAR;
            }
            Fijar(r, accion, maximo, minimo, calculo + actual + ". " + resumen);
        }

        /// <summary>
        /// NestoAPI#655: lo más que puede haber de un propio: <paramref name="vidaUtilMeses"/> meses de venta
        /// (<see cref="ParametrosTendencias.VidaUtilMeses"/> de su grupo). Null si no caduca (sin tope).
        /// </summary>
        internal static int? TopePropios(decimal ventaMensual, int? vidaUtilMeses)
            => !vidaUtilMeses.HasValue ? (int?)null : ventaMensual <= 0 ? 0 : (int)Math.Ceiling(ventaMensual * Math.Max(1, vidaUtilMeses.Value));

        /// <summary>NestoAPI#655: la vida útil de un propio en texto, para la frase y el Excel.</summary>
        internal static string TextoVidaUtil(string grupo, ParametrosTendencias p)
        {
            int? meses = p.VidaUtilMeses(grupo);
            string nombreGrupo = string.IsNullOrWhiteSpace(grupo) ? "sin grupo" : "grupo " + grupo.Trim();
            if (!meses.HasValue)
            {
                return $"no caduca ({nombreGrupo})";
            }
            return p.TieneReglaVidaUtil(grupo)
                ? $"vida útil {meses} meses ({nombreGrupo})"
                : $"vida útil {meses} meses ({nombreGrupo} sin regla propia: se suponen {meses})";
        }

        /// <summary>
        /// NestoAPI#655: fórmula P (fabricación propia, familias con TipoExclusiva PRP). Se fabrican por lotes, con plazo largo y
        /// sin poder quedarse cortos: lote mínimo = Múltiplos de ALG; mínimo = venta mensual de referencia × (días en servir +
        /// <see cref="ParametrosTendencias.DiasSeguridadPropios"/>) ÷ 30; máximo = venta × (seguridad de propios + 2 × días en
        /// servir) ÷ 30 (NestoAPI#630, 16/10/26: sin el lote, que se respeta al fabricar), y nunca más de los meses de vida
        /// útil de su grupo (<see cref="ParametrosTendencias.VidaUtilMeses"/>; lo que pase, caducará). Exceso = stock − máximo.
        /// Acciones: «Caduca antes de venderse» (stock de más meses de venta que la vida útil), «Potenciar ventas» (sobra, pero se
        /// vende antes de caducar) y «Ajustar el siguiente lote» (cambian máximo y mínimo, o el lote solo ya pasa de la vida útil).
        /// <para>Vida útil por grupo (Carlos, 16/10/26): cosmética 12 meses, peluquería 24; accesorios, aparatos y accesorios de
        /// peluquería NO caducan: sin tope de vida útil y nunca «Caduca antes de venderse»; lo que sobre es «Potenciar
        /// ventas». Sin venta, uno que no caduca tampoco se vuelve a fabricar (máximo y mínimo 0): no tiene sentido otro lote de
        /// algo que no sale.</para>
        /// <para>DECISIÓN (#655): a un propio NUNCA se le propone liquidar (ni Outlet ni Ganavisiones) ni pasar a estado 1: son los
        /// productos que hay que potenciar (circular de los socios, julio 2026). Por eso no pasa por las salidas tempranas de
        /// <see cref="Proponer"/> (estado, sin salida y declive): sin venta, la fórmula da máximo 0 y todo el stock «Caduca antes de
        /// venderse»; en declive, la venta de referencia ya es la reciente y el tope de vida útil recorta el máximo. En un estado que
        /// ya no se trabaja (4, −1…) no se fabrica otro lote: máximo y mínimo 0 y, si queda stock, darle salida con ventas propias.</para>
        /// </summary>
        private static void ProponerPropio(ResultadoTendencia r, ParametrosTendencias p)
        {
            string resumen = Resumen(r, p.DiasRetraso);
            decimal venta = r.VentaMensualReferencia;
            int lote = Math.Max(1, r.Multiplos);
            int? vidaUtil = p.VidaUtilMeses(r.Grupo);
            bool caduca = vidaUtil.HasValue;
            int? tope = TopePropios(venta, vidaUtil);
            string textoVidaUtil = TextoVidaUtil(r.Grupo, p);
            string mesesStock = venta > 0 ? $"{r.Stock} uds son {Num(r.Stock / venta, 1)} meses de venta" : $"no se vende nada y quedan {r.Stock} uds";
            const string NUNCA = "nunca Outlet ni liquidar";

            if (r.EstadoProducto.HasValue && r.EstadoProducto.Value != 0 && r.EstadoProducto.Value != 1)
            {
                r.Formula = FormulasPropuesta.PREFIJO_PROPIOS + " estado " + r.EstadoProducto.Value;
                r.ExcesoUnidades = Math.Max(0, r.Stock);
                r.ExcesoValorado = Math.Round(r.ExcesoUnidades * r.PrecioMedio, 2);
                if (r.Stock > 0)
                {
                    bool caducaEnEstado = caduca && r.Stock > tope.Value;
                    Fijar(r, caducaEnEstado ? AccionesPropuesta.CADUCA : AccionesPropuesta.POTENCIAR, 0, 0,
                        $"{(caducaEnEstado ? "Caduca antes de venderse" : "Potenciar ventas")}: fabricación propia en estado {r.EstadoProducto.Value}, " +
                        $"no se fabrica otro lote (máximo y mínimo 0); {mesesStock} ({Euros(r.ExcesoValorado)}): darles salida con ventas propias, {NUNCA}. {resumen}");
                }
                else
                {
                    Fijar(r, AccionesPropuesta.NADA, null, null, $"Fabricación propia en estado {r.EstadoProducto.Value} y sin stock: nada que hacer. {resumen}");
                }
                return;
            }

            int minimo = (int)Math.Ceiling(venta * (r.DiasEnServir + r.DiasSeguridad) / 30m);
            // NestoAPI#630 (16/10/26): el máximo no incluye el lote (se respeta al fabricar). Sin venta, 0: tampoco otro lote.
            int sinTope = (int)Math.Ceiling(venta * (r.DiasSeguridad + 2 * r.DiasEnServir) / 30m);
            int maximo = caduca ? Math.Min(sinTope, tope.Value) : sinTope;
            minimo = Math.Min(minimo, maximo);
            r.Formula = caduca
                ? $"{FormulasPropuesta.PREFIJO_PROPIOS} seg. + 2 × días, tope {vidaUtil} meses"
                : $"{FormulasPropuesta.PREFIJO_PROPIOS} seg. + 2 × días, no caduca";
            r.ExcesoUnidades = Math.Max(0, r.Stock - maximo);
            r.ExcesoValorado = Math.Round(r.ExcesoUnidades * r.PrecioMedio, 2);
            // La cobertura objetivo de un propio es la de su máximo (la del resto, aquí, no significa nada).
            r.CoberturaObjetivoDias = venta > 0 ? (int)Math.Ceiling(maximo / (venta / 30m)) : 0;

            bool loteExcede = caduca && venta > 0 && lote > tope.Value;
            string acotado = caduca && sinTope > maximo ? $" (acotado a {maximo} por la vida útil de {vidaUtil} meses)" : string.Empty;
            string calculo = $"Fabricación propia ({FasesProducto.Nombre(r.Fase).ToLowerInvariant()}): venta de referencia {Num(venta, 1)} uds/mes; " +
                $"proveedor {r.DiasEnServir} días{(r.DiasEnServirSupuestos ? " (supuestos)" : "")} + {r.DiasSeguridad} de seguridad de propios → mínimo {minimo}; " +
                $"máximo = {Num(venta, 1)} × ({r.DiasSeguridad} + 2 × {r.DiasEnServir}) ÷ 30 = {sinTope}{acotado}; lote mínimo {lote} (se respeta al fabricar, no en el máximo); " +
                (caduca ? $"{textoVidaUtil} = {tope} uds" : $"{textoVidaUtil}: sin tope de vida útil");
            if (loteExcede)
            {
                calculo += $". El lote mínimo son {Num(lote / venta, 1)} meses de venta, más que la vida útil: pedir lote menor";
            }
            else if (venta <= 0)
            {
                calculo += caduca
                    ? ". Sin venta: cualquier lote caduca antes de venderse"
                    : ". Sin venta: no se fabrica otro lote (no caduca, pero no sale)";
            }
            string actual = r.StockMaximoActual.HasValue ? $" (hoy {r.StockMaximoActual}/{r.StockMinimoActual})" : " (hoy sin control de stock en ALG)";
            string compras = r.PedidoCompraAbierto > 0 && r.ExcesoUnidades > 0
                ? $" Hay {r.PedidoCompraAbierto} uds en pedidos de compra abiertos (el siguiente lote): revisarlo."
                : string.Empty;

            if (r.EstadoProducto == 1)
            {
                Fijar(r, AccionesPropuesta.MANTENER, null, null, $"Ya está en estado 1 (sobre pedido). {calculo}.{compras} {resumen}");
                return;
            }
            string accion;
            if (caduca && r.Stock > 0 && r.Stock > tope.Value)
            {
                accion = AccionesPropuesta.CADUCA;
                calculo = $"Caduca antes de venderse: {mesesStock}, más que la vida útil de {vidaUtil} meses; {r.Stock - tope.Value} uds " +
                    $"({Euros((r.Stock - tope.Value) * r.PrecioMedio)}) caducarán si no se potencian las ventas ({NUNCA}). " + calculo;
            }
            else if (r.ExcesoUnidades > 0)
            {
                accion = AccionesPropuesta.POTENCIAR;
                calculo = caduca
                    ? $"Potenciar ventas: sobran {r.ExcesoUnidades} uds ({Euros(r.ExcesoValorado)}) sobre el máximo, pero {mesesStock}: " +
                        $"se venden antes de caducar ({NUNCA}). " + calculo
                    : $"Potenciar ventas: sobran {r.ExcesoUnidades} uds ({Euros(r.ExcesoValorado)}) sobre el máximo; {mesesStock}, " +
                        $"pero no caducan ({NUNCA}). " + calculo;
            }
            else
            {
                // El lote que pasa de la vida útil se dice aunque el máximo y el mínimo no cambien: es el lote lo que hay que ajustar.
                accion = !loteExcede && r.StockMaximoActual == maximo && (r.StockMinimoActual ?? 0) == minimo
                    ? AccionesPropuesta.MANTENER
                    : AccionesPropuesta.AJUSTAR_LOTE;
            }
            Fijar(r, accion, maximo, minimo, calculo + actual + "." + compras + " " + resumen);
        }

        private static void Fijar(ResultadoTendencia r, string accion, int? maximo, int? minimo, string explicacion)
        {
            r.Accion = accion;
            r.StockMaximoPropuesto = maximo;
            r.StockMinimoPropuesto = minimo;
            r.Explicacion = explicacion.Trim();
        }

        /// <summary>
        /// NestoAPI#630 («tercera vía», Carlos 16/10/26): si el múltiplo de compra son más de
        /// <see cref="ParametrosTendencias.MesesLoteGrande"/> meses de venta y se sigue comprando (máximo &gt; 0), lo dice en la frase
        /// para pedir al proveedor un múltiplo menor. No cambia la acción. Un propio cuyo lote ya pasa de la vida útil ya lo dice.
        /// </summary>
        private static void AvisarLoteGrande(ResultadoTendencia r, ParametrosTendencias p)
        {
            if (EsLoteGrande(r, p) && (r.StockMaximoPropuesto ?? 0) > 0 && !(r.Explicacion ?? string.Empty).Contains("pedir lote menor"))
            {
                r.Explicacion = $"{r.Explicacion} {AvisoLoteGrande(r)}".Trim();
            }
        }

        internal static bool EsLoteGrande(ResultadoTendencia r, ParametrosTendencias p)
            => r.MesesVentaMultiplo.HasValue && r.MesesVentaMultiplo.Value > p.MesesLoteGrande;

        internal static string AvisoLoteGrande(ResultadoTendencia r)
            => $"El múltiplo de compra ({r.Multiplos} uds) son {Num(r.MesesVentaMultiplo ?? 0m, 1)} meses de venta: proponga al proveedor un múltiplo menor.";

        /// <summary>NestoAPI#656: meses en los que se mide el retraso (12, o los que tiene si es más nuevo; al menos 1).</summary>
        internal static int MesesRetraso(ResultadoTendencia r) => Math.Max(1, Math.Min(12, r.MesesDesdePrimeraVenta ?? 12));

        /// <summary>
        /// NestoAPI#656 (corte 2): lo servido con retraso, por mes. En la fórmula A el máximo suma lo que no se pudo servir a tiempo:
        /// el mayor entre el pendiente vencido (la foto) y este retraso mensual. El mayor y no la suma: el pendiente vencido de hoy
        /// es el retraso de mañana, y sumarlos lo contaría dos veces.
        /// </summary>
        internal static int RetrasoMensual(ResultadoTendencia r)
            => r.ServidoConRetraso <= 0 ? 0 : (int)Math.Ceiling(r.ServidoConRetraso / (decimal)MesesRetraso(r));

        /// <summary>Los indicadores en una frase: cobertura, tendencia, trimestre y contexto.</summary>
        internal static string Resumen(ResultadoTendencia r, int? diasRetraso = null)
        {
            List<string> partes = new List<string>
            {
                r.CoberturaMeses.HasValue
                    ? $"Cobertura {Num(r.CoberturaMeses.Value, 1)} meses (alcanzable {r.CoberturaAlcanzableDias} días)"
                    : $"Sin venta en 6 meses (alcanzable {r.CoberturaAlcanzableDias} días)"
            };
            if (r.PendienteRelativa.HasValue)
            {
                partes.Add($"pendiente {Num(r.PendienteRelativa.Value, 1)} %/mes");
            }
            if (r.RatioTrimestre.HasValue)
            {
                partes.Add($"trimestre {Num(r.RatioTrimestre.Value, 2)}× el del año anterior");
            }
            if (r.PorcentajeDelPico.HasValue)
            {
                partes.Add(r.AniosDesdePico == 0
                    ? "los últimos 12 meses son el pico de 5 años"
                    : $"12 meses al {Num(r.PorcentajeDelPico.Value, 0)} % del pico ({r.AnioPico})");
            }
            string texto = string.Join("; ", partes) + ".";
            if (r.Tendencia == TendenciasProducto.BAJON_PUNTUAL)
            {
                texto += " Bajón puntual: baja en 12 meses, pero el contexto de 5 años no confirma el declive.";
            }
            else if (r.Tendencia == TendenciasProducto.DECLIVE)
            {
                texto += " Declive confirmado por el contexto de 5 años.";
            }
            if (r.ServidoConRetraso > 0)
            {
                texto += $" Servidas con retraso{(diasRetraso.HasValue ? $" (más de {diasRetraso} días)" : "")} en 12 meses: {r.ServidoConRetraso} uds.";
            }
            return texto;
        }

        internal static string Num(decimal valor, int decimales) => valor.ToString("N" + decimales, ES);

        internal static string Euros(decimal valor) => valor.ToString("N2", ES) + " €";

        /// <summary>Orden de la hoja «Top» y del GET: más euros atrapados primero.</summary>
        public static List<ResultadoTendencia> OrdenarPorExceso(IEnumerable<ResultadoTendencia> resultados)
            => resultados.OrderByDescending(r => r.ExcesoValorado).ThenByDescending(r => r.Stock * r.PrecioMedio).ThenBy(r => r.Producto).ToList();
    }
}
