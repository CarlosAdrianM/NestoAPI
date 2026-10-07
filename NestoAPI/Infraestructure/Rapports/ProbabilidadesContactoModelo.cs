using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#603: probabilidad de pedido por cliente (clave "cliente/contacto").</summary>
    public interface IProbabilidadesContacto
    {
        Task<Dictionary<string, PrediccionContacto>> Leer(string vendedor, string tipoInteraccion, string grupoSubgrupo);
    }

    /// <summary>
    /// NestoAPI#603 c3b: el modelo de contactos (ModelsIA/modelo_llamadas.zip, entrada <see cref="ModeloContactoEntrada"/>) para
    /// toda la cartera del vendedor. Las features se calculan como en el entrenamiento (<see cref="CalculadoraFeaturesContacto"/>,
    /// con ahora como momento del contacto) sobre los datos crudos de <see cref="IRepositorioFeaturesContacto"/>.
    /// <list type="bullet">
    /// <item>La consulta se cachea 2 h por vendedor y día: el tipo de interacción y el grupo/subgrupo ya no la cambian (son
    /// features que se ponen en memoria), así que cambiar de tipo o de grupo no lanza otra.</item>
    /// <item>Las predicciones se cachean 2 h por vendedor, tipo, grupo/subgrupo, día y mañana/tarde (EsPorLaTarde es feature).</item>
    /// <item>El grupo/subgrupo que se pregunta sustituye a GrupoSubgrupoMasVendido en una copia; el que se devuelve es el real.</item>
    /// </list>
    /// Si el modelo falla se registra en ELMAH y se devuelve vacío (todas las probabilidades a 0: sin prioridad Máxima,
    /// pero la lista sale) y no se cachea.
    /// </summary>
    public class ProbabilidadesContactoModelo : IProbabilidadesContacto
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromHours(2);
        private static readonly MemoryCache cache = MemoryCache.Default;

        private readonly IRepositorioFeaturesContacto repositorio;
        private readonly Func<DateTime> ahora;
        private readonly string rutaModelo;

        public ProbabilidadesContactoModelo() : this(new RepositorioFeaturesContactoSql(), () => DateTime.Now, null)
        {
        }

        internal ProbabilidadesContactoModelo(IRepositorioFeaturesContacto repositorio, Func<DateTime> ahora, string rutaModelo)
        {
            this.repositorio = repositorio;
            this.ahora = ahora;
            this.rutaModelo = rutaModelo;
        }

        internal static string ClaveCacheConsulta(string vendedor, DateTime ahora) =>
            $"SugerenciasContacto:Historial:{vendedor?.Trim().ToUpperInvariant()}:{ahora:yyyyMMdd}";

        internal static string ClaveCache(string vendedor, string tipoInteraccion, string grupoSubgrupo, DateTime ahora) =>
            $"SugerenciasContacto:Prediccion:{vendedor?.Trim().ToUpperInvariant()}:{GestorClientes.NormalizarTipoInteraccion(tipoInteraccion)}:{grupoSubgrupo?.Trim().ToUpperInvariant()}:{ahora:yyyyMMdd}:{(ahora.Hour >= CalculadoraFeaturesContacto.HORA_TARDE ? "T" : "M")}";

        /// <summary>El historial de la cartera del vendedor, de la caché o de la BD (una vez cada 2 h).</summary>
        private List<HistorialContacto> ConsultaCacheada(string vendedor, DateTime momento)
        {
            string clave = ClaveCacheConsulta(vendedor, momento);
            if (cache.Get(clave) is List<HistorialContacto> enCache)
            {
                return enCache;
            }
            List<HistorialContacto> historiales = repositorio.Leer(vendedor, momento.Date);
            _ = cache.Add(clave, historiales, DateTimeOffset.Now.Add(DURACION_CACHE));
            return historiales;
        }

        public async Task<Dictionary<string, PrediccionContacto>> Leer(string vendedor, string tipoInteraccion, string grupoSubgrupo)
        {
            DateTime momento = ahora();
            string clave = ClaveCache(vendedor, tipoInteraccion, grupoSubgrupo, momento);
            if (cache.Get(clave) is Dictionary<string, PrediccionContacto> enCache)
            {
                return enCache;
            }
            try
            {
                Dictionary<string, PrediccionContacto> resultado = await Task.Run(() => Calcular(vendedor, tipoInteraccion, grupoSubgrupo, momento)).ConfigureAwait(false);
                _ = cache.Add(clave, resultado, DateTimeOffset.Now.Add(DURACION_CACHE));
                return resultado;
            }
            catch (Exception ex)
            {
                try
                {
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"NestoAPI#603: no se pudo calcular la probabilidad del modelo para el vendedor {vendedor}; las sugerencias salen sin ella", ex)));
                }
                catch
                {
                    // Sin ELMAH (tests, consola): que no tumbe la lista.
                }
                return new Dictionary<string, PrediccionContacto>();
            }
        }

        internal Dictionary<string, PrediccionContacto> Calcular(string vendedor, string tipoInteraccion, string grupoSubgrupo, DateTime momento)
        {
            List<HistorialContacto> historiales = ConsultaCacheada(vendedor, momento);
            if (!historiales.Any())
            {
                return new Dictionary<string, PrediccionContacto>();
            }

            List<ModeloContactoEntrada> reales = ModeloContacto.Entradas(historiales, momento, tipoInteraccion);
            List<ModeloContactoEntrada> entradas = ModeloContacto.ConGrupoSubgrupo(reales, grupoSubgrupo);
            List<float> probabilidades = ModeloContacto.Puntuar(entradas, rutaModelo);

            var resultado = new Dictionary<string, PrediccionContacto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reales.Count && i < probabilidades.Count; i++)
            {
                // Lo que más compra el cliente de verdad, no el grupo que se pregunta.
                string grupo = reales[i].GrupoSubgrupoMasVendido;
                resultado[reales[i].ClienteId.Trim()] = new PrediccionContacto
                {
                    Probabilidad = probabilidades[i],
                    GrupoSubgrupoMasVendido = string.IsNullOrWhiteSpace(grupo) || grupo == CalculadoraFeaturesContacto.SIN_GRUPO ? null : grupo.Trim()
                };
            }
            return resultado;
        }
    }
}
