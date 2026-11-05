using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#628 (ficha = Excel): la propuesta de stock de ALG de un producto, calculada como en el Excel.</summary>
    public interface IServicioPropuestaStockProducto
    {
        /// <summary>
        /// Lo que saldría hoy en el Excel de propuestas para este producto (mes de referencia: el último cerrado). Null si el
        /// producto no entra en el cálculo (no existe, ficticio, materias primas o muestras).
        /// </summary>
        ResultadoTendencia Calcular(string producto);

        /// <summary>
        /// NestoAPI#738 (sugerencia 793, Manuel): la propuesta de ALG de varios productos COMO SI ya estuvieran en estado 0
        /// (Disponible), para crear sus controles de stock al volver a Disponible con los máximos y mínimos sugeridos y no con los
        /// por defecto del almacén. Sin forzar el estado, el motor no da máximos a los de estado 3, 4 o nulos. Las series se
        /// completan en un solo lote. Solo trae los que entran en el cálculo (los que no, como en <see cref="Calcular"/>, no salen).
        /// </summary>
        Dictionary<string, ResultadoTendencia> CalcularComoDisponibles(IEnumerable<string> productos);
    }

    /// <summary>
    /// NestoAPI#628 (ficha = Excel, Carlos 16/10/26: «Nesto y el Excel deben sugerir lo mismo»): el máximo y el mínimo de ALG que
    /// sugiere la ficha del producto salen de <see cref="MotorTendencias"/>, con los mismos datos que el job del Excel
    /// (<see cref="ServicioPropuestasStock"/>), pero leyendo un solo producto: su fila (<see cref="IRepositorioTendencias.LeerProducto"/>),
    /// sus series (<see cref="IRepositorioTendencias.CompletarLote"/> con un lote de uno) y la estacionalidad de su familia
    /// (<see cref="IRepositorioTendencias.LeerVentasFamilia"/> + <see cref="ServicioPropuestasStock.IndicesDeSerie"/>).
    /// <para>La estacionalidad de la familia solo cambia al cerrar el mes: se guarda en memoria
    /// <see cref="HORAS_CACHE_INDICES"/> horas por familia y mes, porque es la consulta más pesada (suma toda la familia).</para>
    /// </summary>
    public class ServicioPropuestaStockProducto : IServicioPropuestaStockProducto
    {
        internal const int HORAS_CACHE_INDICES = 12;
        private const string PREFIJO_CACHE = "PropuestasStock.IndicesFamilia|";

        private readonly IRepositorioTendencias repositorio;
        private readonly Func<ParametrosTendencias> leerParametros;
        private readonly Func<DateTime> ahora;
        private readonly ObjectCache cache;

        public ServicioPropuestaStockProducto() : this(new RepositorioTendenciasSql())
        {
        }

        public ServicioPropuestaStockProducto(IRepositorioTendencias repositorio, Func<ParametrosTendencias> leerParametros = null,
            Func<DateTime> ahora = null, ObjectCache cache = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.leerParametros = leerParametros ?? ParametrosTendencias.DesdeConfiguracion;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.cache = cache ?? MemoryCache.Default;
        }

        public ResultadoTendencia Calcular(string producto)
        {
            DatosProductoTendencia d = repositorio.LeerProducto(producto);
            if (d == null)
            {
                return null;
            }
            ParametrosTendencias p = leerParametros() ?? new ParametrosTendencias();
            DateTime mesReferencia = PropuestasStockJobsService.MesCerrado(ahora());
            repositorio.CompletarLote(new List<DatosProductoTendencia> { d }, mesReferencia, p.MesesFaseA, p.DiasRetraso);
            decimal[] indices = IndicesFamilia(d.Familia?.Trim(), mesReferencia, p);
            return MotorTendencias.Calcular(d, indices, mesReferencia, p);
        }

        public Dictionary<string, ResultadoTendencia> CalcularComoDisponibles(IEnumerable<string> productos)
        {
            Dictionary<string, ResultadoTendencia> resultado = new Dictionary<string, ResultadoTendencia>(StringComparer.OrdinalIgnoreCase);
            List<DatosProductoTendencia> lote = new List<DatosProductoTendencia>();
            foreach (string producto in (productos ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct())
            {
                DatosProductoTendencia d = repositorio.LeerProducto(producto);
                if (d != null)
                {
                    d.Estado = 0;
                    lote.Add(d);
                }
            }
            if (lote.Count == 0)
            {
                return resultado;
            }
            ParametrosTendencias p = leerParametros() ?? new ParametrosTendencias();
            DateTime mesReferencia = PropuestasStockJobsService.MesCerrado(ahora());
            repositorio.CompletarLote(lote, mesReferencia, p.MesesFaseA, p.DiasRetraso);
            foreach (DatosProductoTendencia d in lote)
            {
                decimal[] indices = IndicesFamilia(d.Familia?.Trim(), mesReferencia, p);
                resultado[d.Producto?.Trim() ?? string.Empty] = MotorTendencias.Calcular(d, indices, mesReferencia, p);
            }
            return resultado;
        }

        private decimal[] IndicesFamilia(string familia, DateTime mesReferencia, ParametrosTendencias p)
        {
            if (string.IsNullOrWhiteSpace(familia))
            {
                return null;
            }
            string clave = $"{PREFIJO_CACHE}{familia.ToUpperInvariant()}|{mesReferencia:yyyy-MM}|{p.UnidadesMinimasFamiliaEstacionalidad}";
            if (cache.Get(clave) is IndicesGuardados guardados)
            {
                return guardados.Indices;
            }
            decimal[] indices = ServicioPropuestasStock.IndicesDeSerie(repositorio.LeerVentasFamilia(familia, mesReferencia), mesReferencia, p);
            // Envuelto: una familia sin estacionalidad (null) también se guarda, y no se vuelve a sumar en cada ficha.
            cache.Set(clave, new IndicesGuardados { Indices = indices }, DateTimeOffset.Now.AddHours(HORAS_CACHE_INDICES));
            return indices;
        }

        private class IndicesGuardados
        {
            public decimal[] Indices { get; set; }
        }
    }
}
