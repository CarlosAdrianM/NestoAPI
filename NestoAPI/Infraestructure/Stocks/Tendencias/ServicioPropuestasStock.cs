using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>Todo lo que sale de una pasada del motor para un mes.</summary>
    public class ResultadoPropuestasStock
    {
        public DateTime Mes { get; set; }
        public ParametrosTendencias Parametros { get; set; }
        /// <summary>Ordenados por exceso valorado (más euros atrapados primero).</summary>
        public List<ResultadoTendencia> Productos { get; set; } = new List<ResultadoTendencia>();
        public ResultadoCalibracion Calibracion { get; set; } = new ResultadoCalibracion();
        public int ProductosLeidos { get; set; }
        public int LotesConError { get; set; }
        public int ProductosConError { get; set; }
        public int FamiliasConEstacionalidad { get; set; }
    }

    /// <summary>
    /// NestoAPI#630: una pasada del motor de tendencias. Lee los productos, completa sus series por lotes (un lote que falla va a
    /// ELMAH y se reintenta producto a producto; solo se pierde el producto que falle), calcula la estacionalidad de cada familia con la suma de sus productos, y luego los
    /// indicadores, la propuesta y la calibración. No guarda ni manda nada: eso lo hace <see cref="PropuestasStockJobsService"/>.
    /// </summary>
    public class ServicioPropuestasStock
    {
        /// <summary>Si se cae la BD, no llenar ELMAH con miles de errores iguales: a partir de aquí solo se cuentan (salen en el correo).</summary>
        internal const int MAXIMO_ERRORES_A_ELMAH = 20;

        private readonly IRepositorioTendencias repositorio;
        private readonly Action<Exception> registrarError;

        public ServicioPropuestasStock(IRepositorioTendencias repositorio, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrarError = registrarError ?? ElmahHelper.Log;
        }

        /// <param name="mes">Cualquier día del último mes que cuenta (se normaliza al día 1).</param>
        public ResultadoPropuestasStock Calcular(DateTime mes, ParametrosTendencias parametros)
        {
            DateTime mesReferencia = new DateTime(mes.Year, mes.Month, 1);
            parametros = parametros ?? new ParametrosTendencias();
            ResultadoPropuestasStock resultado = new ResultadoPropuestasStock { Mes = mesReferencia, Parametros = parametros };

            List<DatosProductoTendencia> productos = repositorio.LeerProductos();
            List<DatosProductoTendencia> completos = new List<DatosProductoTendencia>();
            for (int i = 0; i < productos.Count; i += RepositorioTendenciasSql.TAMANO_LOTE)
            {
                List<DatosProductoTendencia> lote = productos.Skip(i).Take(RepositorioTendenciasSql.TAMANO_LOTE).ToList();
                try
                {
                    repositorio.CompletarLote(lote, mesReferencia, parametros.MesesFaseA, parametros.DiasRetraso);
                    completos.AddRange(lote);
                }
                catch (Exception ex)
                {
                    // Un lote que falla (un tiempo de espera, casi siempre) se reintenta producto a producto: así solo se pierde
                    // el que de verdad falla, y no los 200.
                    resultado.LotesConError++;
                    registrarError(new Exception($"[Propuestas de stock #630] Falló el lote de productos {lote.First().Producto} a {lote.Last().Producto} " +
                        $"({mesReferencia:yyyy-MM}); se reintenta producto a producto: {ex.Message}", ex));
                    foreach (DatosProductoTendencia producto in lote)
                    {
                        try
                        {
                            repositorio.CompletarLote(new List<DatosProductoTendencia> { producto }, mesReferencia, parametros.MesesFaseA, parametros.DiasRetraso);
                            completos.Add(producto);
                        }
                        catch (Exception exProducto)
                        {
                            resultado.ProductosConError++;
                            if (resultado.ProductosConError <= MAXIMO_ERRORES_A_ELMAH)
                            {
                                registrarError(new Exception($"[Propuestas de stock #630] Falló el producto {producto.Producto} ({mesReferencia:yyyy-MM}); " +
                                    $"se sigue con los demás: {exProducto.Message}", exProducto));
                            }
                        }
                    }
                }
            }
            resultado.ProductosLeidos = completos.Count;

            Dictionary<string, decimal[]> indices = IndicesPorFamilia(completos, mesReferencia, parametros);
            resultado.FamiliasConEstacionalidad = indices.Count(par => par.Value != null);
            decimal[] Indices(string familia) => familia != null && indices.TryGetValue(familia, out decimal[] i) ? i : null;

            List<ResultadoTendencia> calculados = new List<ResultadoTendencia>();
            foreach (DatosProductoTendencia d in completos.Where(MotorTendencias.EsRelevante))
            {
                try
                {
                    calculados.Add(MotorTendencias.Calcular(d, Indices(d.Familia?.Trim()), mesReferencia, parametros));
                }
                catch (Exception ex)
                {
                    resultado.ProductosConError++;
                    if (resultado.ProductosConError <= MAXIMO_ERRORES_A_ELMAH)
                    {
                        registrarError(new Exception($"[Propuestas de stock #630] Falló el cálculo del producto {d.Producto} ({mesReferencia:yyyy-MM}); se sigue con los demás: {ex.Message}", ex));
                    }
                }
            }
            resultado.Productos = MotorTendencias.OrdenarPorExceso(calculados);

            try
            {
                resultado.Calibracion = CalibracionTendencias.Calcular(completos, Indices, mesReferencia, parametros);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Propuestas de stock #630] Falló la calibración ({mesReferencia:yyyy-MM}); el resto sale igual: {ex.Message}", ex));
            }
            return resultado;
        }

        /// <summary>Estacionalidad de cada familia: la serie de la familia es la suma de las de sus productos.</summary>
        public static Dictionary<string, decimal[]> IndicesPorFamilia(IEnumerable<DatosProductoTendencia> productos, DateTime mesReferencia, ParametrosTendencias p)
        {
            Dictionary<string, decimal[]> resultado = new Dictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var familia in productos.Where(d => !string.IsNullOrWhiteSpace(d.Familia)).GroupBy(d => d.Familia.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                int[] serie = new int[DatosProductoTendencia.MESES_SERIE];
                foreach (DatosProductoTendencia d in familia)
                {
                    for (int i = 0; i < serie.Length; i++)
                    {
                        serie[i] += d.Ventas[i];
                    }
                }
                resultado[familia.Key] = IndicesDeSerie(serie, mesReferencia, p);
            }
            return resultado;
        }

        /// <summary>
        /// Estacionalidad de una familia a partir de su serie de ventas (la suma de sus productos). La usan el Excel
        /// (<see cref="IndicesPorFamilia"/>) y la ficha del producto (<see cref="ServicioPropuestaStockProducto"/>): un único cálculo.
        /// </summary>
        public static decimal[] IndicesDeSerie(int[] serie, DateTime mesReferencia, ParametrosTendencias p)
            => CalculadoraSeries.IndicesEstacionales(serie, DatosProductoTendencia.ULTIMO, mesReferencia,
                (p ?? new ParametrosTendencias()).UnidadesMinimasFamiliaEstacionalidad, CalculadoraSeries.PrimerMesConVenta(serie));
    }
}
