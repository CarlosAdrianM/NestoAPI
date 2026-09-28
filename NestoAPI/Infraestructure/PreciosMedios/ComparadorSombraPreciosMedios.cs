using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Lo que hay AHORA en la BD para un producto: lo que dejó el SP (Issue #547, corte b).
    /// </summary>
    public sealed class EstadoActualPrecioMedio
    {
        /// <summary><c>Productos.PrecioMedio</c> de la empresa E (nulo si no hay ficha o está a NULL).</summary>
        public decimal? PrecioMedio { get; set; }

        /// <summary>Líneas de compra con su <c>LinPedidoCmp.Coste</c> actual.</summary>
        public List<LineaCompraBDPrecioMedio> Compras { get; set; } = new List<LineaCompraBDPrecioMedio>();

        /// <summary>Muestreo de <c>LinPedidoVta</c>; nulo si no se ha muestreado este producto.</summary>
        public List<LineaVentaPrecioMedio> Ventas { get; set; }

        public static EstadoActualPrecioMedio Desde(DatosProductoPrecioMedio datos)
        {
            return new EstadoActualPrecioMedio
            {
                PrecioMedio = datos?.Ficha?.PrecioMedio,
                Compras = datos?.Compras ?? new List<LineaCompraBDPrecioMedio>(),
                Ventas = datos?.Ventas
            };
        }
    }

    /// <summary>Resultado de comparar lo calculado en C# con lo que dejó el SP.</summary>
    public sealed class DiferenciaPrecioMedio
    {
        public const int MAXIMO_DETALLES = 50;

        public decimal? PrecioMedioBD { get; set; }
        public decimal? PrecioMedioCalculado { get; set; }

        /// <summary>False si la calculadora no da media (producto no procesado): entonces no se compara.</summary>
        public bool MediaComparada { get; set; }
        public bool MediaDistinta { get; set; }

        public int LineasComparadas { get; set; }
        public int LineasDistintas { get; set; }

        /// <summary>NºOrden de la primera línea de compra (por fecha de albarán) con coste distinto: dónde empieza a divergir.</summary>
        public int? PrimeraLineaDistinta { get; set; }

        /// <summary>Nulo si no se han muestreado las ventas de este producto.</summary>
        public int? VentasComparadas { get; set; }
        public int? VentasDistintas { get; set; }

        /// <summary>Ventas cuya fecha cae en varios tramos con precios distintos: el UPDATE del SP coge uno cualquiera.</summary>
        public int? VentasConEmpateDeTramos { get; set; }

        /// <summary>Ventas modificadas después de la pasada del SP: no se comparan (el SP no las vio así).</summary>
        public int? VentasPosterioresAlCorte { get; set; }

        /// <summary>Hasta <see cref="MAXIMO_DETALLES"/> diferencias concretas (compras y ventas).</summary>
        public List<DetalleDiferenciaPrecioMedio> Detalles { get; } = new List<DetalleDiferenciaPrecioMedio>();

        public bool HayDiferencia => MediaDistinta || LineasDistintas > 0 || (VentasDistintas ?? 0) > 0;
    }

    public sealed class DetalleDiferenciaPrecioMedio
    {
        /// <summary>"Compra" o "Venta".</summary>
        public string Tabla { get; set; }
        public int NumeroOrden { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public short Estado { get; set; }
        public decimal? CosteBD { get; set; }
        public decimal? CosteCalculado { get; set; }
    }

    /// <summary>
    /// Compara, SIN tolerancia (igualdad al diezmilésimo, que es lo que guarda <c>money</c>), lo que calcula
    /// <see cref="CalculadoraPrecioMedio"/> con lo que dejó el SP (Issue #547, §4 del plan):
    /// 1. <c>Productos.PrecioMedio</c> vs la media final.
    /// 2. <c>LinPedidoCmp.Coste</c> de cada línea que el SP reescribe vs <c>CostesPorLinea</c>.
    /// 3. <c>LinPedidoVta.Coste</c> (solo si se ha muestreado) vs los tramos: estado ≥ 2 con fecha de albarán en
    ///    [desde, hasta); estados -1 y 1, el último tramo (hasta = 31/12/9999). El resto no lo toca el SP.
    /// Puro: sin BD. No decide si una diferencia es «esperada»: eso lo hace <see cref="ServicioSombraPreciosMedios"/>.
    /// </summary>
    public static class ComparadorSombraPreciosMedios
    {
        public const string TABLA_COMPRA = "Compra";
        public const string TABLA_VENTA = "Venta";

        private const short ESTADO_PENDIENTE = -1;
        private const short ESTADO_EN_CURSO = 1;
        private const short ESTADO_ALBARAN = 2;

        /// <param name="calculado">Resultado de la calculadora.</param>
        /// <param name="bd">Lo que hay en la BD.</param>
        /// <param name="corteSP">Inicio de la última pasada del SP: las ventas modificadas después no se comparan.</param>
        public static DiferenciaPrecioMedio Comparar(ResultadoPrecioMedio calculado, EstadoActualPrecioMedio bd, DateTime? corteSP)
        {
            if (calculado == null)
            {
                throw new ArgumentNullException(nameof(calculado));
            }
            bd = bd ?? new EstadoActualPrecioMedio();

            DiferenciaPrecioMedio diferencia = new DiferenciaPrecioMedio
            {
                PrecioMedioBD = bd.PrecioMedio,
                PrecioMedioCalculado = calculado.PrecioMedioFinal
            };

            if (!calculado.Procesado || !calculado.PrecioMedioFinal.HasValue)
            {
                // El SP no toca el producto: no hay nada con lo que comparar.
                return diferencia;
            }

            diferencia.MediaComparada = true;
            diferencia.MediaDistinta = bd.PrecioMedio != calculado.PrecioMedioFinal.Value;

            CompararCompras(calculado, bd, diferencia);
            if (bd.Ventas != null)
            {
                CompararVentas(calculado, bd.Ventas, corteSP, diferencia);
            }
            return diferencia;
        }

        private static void CompararCompras(ResultadoPrecioMedio calculado, EstadoActualPrecioMedio bd, DiferenciaPrecioMedio diferencia)
        {
            Dictionary<int, LineaCompraBDPrecioMedio> lineasBD = (bd.Compras ?? new List<LineaCompraBDPrecioMedio>())
                .GroupBy(l => l.NumeroOrden)
                .ToDictionary(g => g.Key, g => g.First());

            IEnumerable<KeyValuePair<int, decimal>> costes = calculado.CostesPorLinea
                .OrderBy(c => lineasBD.TryGetValue(c.Key, out LineaCompraBDPrecioMedio l) ? l.FechaAlbaran ?? DateTime.MaxValue : DateTime.MaxValue)
                .ThenBy(c => c.Key);

            foreach (KeyValuePair<int, decimal> coste in costes)
            {
                diferencia.LineasComparadas++;
                lineasBD.TryGetValue(coste.Key, out LineaCompraBDPrecioMedio lineaBD);
                decimal? costeBD = lineaBD?.Coste;
                if (costeBD == coste.Value)
                {
                    continue;
                }
                diferencia.LineasDistintas++;
                if (!diferencia.PrimeraLineaDistinta.HasValue)
                {
                    diferencia.PrimeraLineaDistinta = coste.Key;
                }
                AnadirDetalle(diferencia, new DetalleDiferenciaPrecioMedio
                {
                    Tabla = TABLA_COMPRA,
                    NumeroOrden = coste.Key,
                    FechaAlbaran = lineaBD?.FechaAlbaran,
                    Estado = lineaBD?.Estado ?? 0,
                    CosteBD = costeBD,
                    CosteCalculado = coste.Value
                });
            }
        }

        private static void CompararVentas(ResultadoPrecioMedio calculado, List<LineaVentaPrecioMedio> ventas, DateTime? corteSP,
            DiferenciaPrecioMedio diferencia)
        {
            diferencia.VentasComparadas = 0;
            diferencia.VentasDistintas = 0;
            diferencia.VentasConEmpateDeTramos = 0;
            diferencia.VentasPosterioresAlCorte = 0;
            if (!calculado.Tramos.Any())
            {
                return;
            }

            foreach (LineaVentaPrecioMedio venta in ventas.OrderBy(v => v.FechaAlbaran ?? DateTime.MaxValue).ThenBy(v => v.NumeroOrden))
            {
                List<decimal> candidatos = PreciosDeLosTramos(calculado.Tramos, venta);
                if (candidatos == null || candidatos.Count == 0)
                {
                    continue; // el SP no escribe esta línea
                }
                if (corteSP.HasValue && venta.FechaModificacion.HasValue && venta.FechaModificacion.Value >= corteSP.Value)
                {
                    diferencia.VentasPosterioresAlCorte++;
                    continue;
                }

                diferencia.VentasComparadas++;
                if (candidatos.Count > 1)
                {
                    diferencia.VentasConEmpateDeTramos++;
                }
                if (venta.Coste.HasValue && candidatos.Contains(venta.Coste.Value))
                {
                    continue;
                }
                diferencia.VentasDistintas++;
                AnadirDetalle(diferencia, new DetalleDiferenciaPrecioMedio
                {
                    Tabla = TABLA_VENTA,
                    NumeroOrden = venta.NumeroOrden,
                    FechaAlbaran = venta.FechaAlbaran,
                    Estado = venta.Estado,
                    CosteBD = venta.Coste,
                    CosteCalculado = candidatos.Last()
                });
            }
        }

        /// <summary>
        /// Precios (distintos) de los tramos que el UPDATE del SP casaría con esta venta. Nulo si el SP no la toca.
        /// Varios = empate: el SP coge uno cualquiera, así que cualquiera de ellos se da por bueno.
        /// </summary>
        public static List<decimal> PreciosDeLosTramos(IReadOnlyList<TramoPrecioMedio> tramos, LineaVentaPrecioMedio venta)
        {
            IEnumerable<TramoPrecioMedio> casan;
            if (venta.Estado == ESTADO_PENDIENTE || venta.Estado == ESTADO_EN_CURSO)
            {
                casan = tramos.Where(t => t.FechaHasta == CalculadoraPrecioMedio.FechaFinDeLosTiempos);
            }
            else if (venta.Estado >= ESTADO_ALBARAN && venta.FechaAlbaran.HasValue)
            {
                DateTime fecha = venta.FechaAlbaran.Value;
                casan = tramos.Where(t => fecha >= t.FechaDesde && fecha < t.FechaHasta);
            }
            else
            {
                return null;
            }
            // En orden de inserción: el último es el de la factura mayor (el que decide en C#).
            return casan.Select(t => t.PrecioMedio).Reverse().Distinct().Reverse().ToList();
        }

        private static void AnadirDetalle(DiferenciaPrecioMedio diferencia, DetalleDiferenciaPrecioMedio detalle)
        {
            if (diferencia.Detalles.Count < DiferenciaPrecioMedio.MAXIMO_DETALLES)
            {
                diferencia.Detalles.Add(detalle);
            }
        }
    }
}
