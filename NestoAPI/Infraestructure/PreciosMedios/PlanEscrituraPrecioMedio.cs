using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Lo que el incremental de precios medios (Issue #547, corte c) va a escribir para UN producto: exactamente lo
    /// que escribiría el SP del domingo, y nada más. Objeto plano: lo construye
    /// <see cref="PlanificadorEscrituraPreciosMedios"/> (puro) y lo ejecuta <see cref="EscritorPreciosMedios"/>.
    /// </summary>
    public sealed class PlanEscrituraPrecioMedio
    {
        public string Empresa { get; set; }
        public string EmpresaEspejo { get; set; }
        public string Producto { get; set; }

        /// <summary>False si el SP no toca el producto (ficticio, montajes, sin compras...): no se escribe nada.</summary>
        public bool Procesado { get; set; }

        public MotivoPrecioMedioNoProcesado Motivo { get; set; }

        /// <summary>Media calculada (la tenga ya o no la ficha). Nula si no se procesa.</summary>
        public decimal? PrecioMedioCalculado { get; set; }

        /// <summary>
        /// Nuevo <c>Productos.PrecioMedio</c> de la empresa E (nunca de la espejo). Nulo si ya lo tiene: no se toca.
        /// </summary>
        public decimal? PrecioMedioNuevo { get; set; }

        /// <summary><c>NºOrden</c> → nuevo <c>LinPedidoCmp.Coste</c>, solo de las líneas cuyo coste cambia.</summary>
        public Dictionary<int, decimal> CostesCompra { get; } = new Dictionary<int, decimal>();

        /// <summary>
        /// Coste de las ventas con estado ≥ 2 por fecha de albarán, en rangos [Desde, Hasta) que NO se solapan (los
        /// tramos del SP sí pueden solaparse en los empates; aquí ya está resuelto a favor del último, la factura
        /// mayor). Siempre se mandan todos: no se leen las ventas, y el UPDATE solo toca las filas que cambian.
        /// </summary>
        public List<RangoCosteVenta> RangosVenta { get; } = new List<RangoCosteVenta>();

        /// <summary>Coste de las ventas pendientes y en curso (estados -1 y 1): el último tramo. Nulo si no hay.</summary>
        public decimal? CosteVentasPendientes { get; set; }

        /// <summary>Avisos de la calculadora (empates, media negativa...), para el resumen.</summary>
        public List<AvisoPrecioMedio> Avisos { get; } = new List<AvisoPrecioMedio>();

        /// <summary>Hay algo que mandar a la BD (aunque las ventas puedan acabar sin cambiar ninguna fila).</summary>
        public bool HayQueEscribir => Procesado && (PrecioMedioNuevo.HasValue || CostesCompra.Any() || RangosVenta.Any() || CosteVentasPendientes.HasValue);
    }

    /// <summary>Las ventas (estado ≥ 2) con fecha de albarán en [Desde, Hasta) llevan este coste.</summary>
    public sealed class RangoCosteVenta
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public decimal Coste { get; set; }

        public override string ToString()
        {
            return $"[{Desde:yyyy-MM-dd HH:mm:ss.fff}, {Hasta:yyyy-MM-dd HH:mm:ss.fff}) = {Coste}";
        }
    }

    /// <summary>
    /// Convierte el resultado de la calculadora y lo que hay en la BD en el <see cref="PlanEscrituraPrecioMedio"/>
    /// (Issue #547, corte c). Puro: sin BD. Réplica de las escrituras del SP:
    /// - <c>Productos.PrecioMedio = media final</c> (solo empresa E);
    /// - <c>LinPedidoCmp.Coste</c> de cada línea recalculada (E + espejo);
    /// - <c>LinPedidoVta.Coste</c>: estado ≥ 2 por tramos de fecha de albarán; estados -1 y 1, el último tramo.
    /// En los empates (tramos solapados) el SP deja uno cualquiera; aquí, el último insertado, que es el mismo
    /// criterio que da por bueno el comparador de la sombra.
    /// </summary>
    public static class PlanificadorEscrituraPreciosMedios
    {
        public static PlanEscrituraPrecioMedio Planificar(DatosProductoPrecioMedio datos, ResultadoPrecioMedio calculo)
        {
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }
            if (calculo == null)
            {
                throw new ArgumentNullException(nameof(calculo));
            }

            PlanEscrituraPrecioMedio plan = new PlanEscrituraPrecioMedio
            {
                Empresa = datos.Empresa?.Trim(),
                EmpresaEspejo = datos.EmpresaEspejo?.Trim(),
                Producto = datos.Producto?.Trim(),
                Procesado = calculo.Procesado && calculo.PrecioMedioFinal.HasValue && datos.Ficha != null,
                Motivo = calculo.Motivo,
                PrecioMedioCalculado = calculo.PrecioMedioFinal
            };
            plan.Avisos.AddRange(calculo.Avisos);
            if (!plan.Procesado)
            {
                // El SP no toca el producto (o no hay ficha en E): se queda como está.
                return plan;
            }

            decimal media = calculo.PrecioMedioFinal.Value;
            if (datos.Ficha.PrecioMedio != media)
            {
                plan.PrecioMedioNuevo = media;
            }

            Dictionary<int, LineaCompraBDPrecioMedio> lineas = (datos.Compras ?? new List<LineaCompraBDPrecioMedio>())
                .GroupBy(l => l.NumeroOrden)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (KeyValuePair<int, decimal> coste in calculo.CostesPorLinea.OrderBy(c => c.Key))
            {
                if (!lineas.TryGetValue(coste.Key, out LineaCompraBDPrecioMedio linea) || linea.Coste != coste.Value)
                {
                    plan.CostesCompra[coste.Key] = coste.Value;
                }
            }

            plan.RangosVenta.AddRange(RangosEfectivos(calculo.Tramos));
            TramoPrecioMedio ultimo = calculo.Tramos.LastOrDefault(t => t.FechaHasta == CalculadoraPrecioMedio.FechaFinDeLosTiempos);
            plan.CosteVentasPendientes = ultimo?.PrecioMedio;
            return plan;
        }

        /// <summary>
        /// Rangos de fecha de albarán sin solapes: cada instante lleva el precio del ÚLTIMO tramo (en orden de
        /// inserción) que lo cubre, igual que <see cref="ComparadorSombraPreciosMedios.PreciosDeLosTramos"/>.
        /// Los rangos contiguos con el mismo coste se juntan (menos parámetros en el UPDATE).
        /// </summary>
        public static List<RangoCosteVenta> RangosEfectivos(IReadOnlyList<TramoPrecioMedio> tramos)
        {
            List<RangoCosteVenta> rangos = new List<RangoCosteVenta>();
            if (tramos == null || tramos.Count == 0)
            {
                return rangos;
            }

            List<DateTime> fronteras = tramos
                .SelectMany(t => new[] { t.FechaDesde, t.FechaHasta })
                .Distinct()
                .OrderBy(f => f)
                .ToList();

            for (int i = 0; i < fronteras.Count - 1; i++)
            {
                DateTime desde = fronteras[i];
                DateTime hasta = fronteras[i + 1];
                // Como todo tramo empieza y acaba en una frontera, si cubre «desde» cubre el intervalo entero.
                TramoPrecioMedio decide = tramos.LastOrDefault(t => t.FechaDesde <= desde && desde < t.FechaHasta);
                if (decide == null)
                {
                    continue; // hueco: el SP no escribe esas ventas
                }
                RangoCosteVenta anterior = rangos.LastOrDefault();
                if (anterior != null && anterior.Hasta == desde && anterior.Coste == decide.PrecioMedio)
                {
                    anterior.Hasta = hasta;
                }
                else
                {
                    rangos.Add(new RangoCosteVenta { Desde = desde, Hasta = hasta, Coste = decide.PrecioMedio });
                }
            }
            return rangos;
        }
    }
}
