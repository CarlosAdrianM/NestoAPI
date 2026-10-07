using Microsoft.ML;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.IO;
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
    /// NestoAPI#603: el modelo actual (ModelsIA/modelo_llamadas.zip sobre SQL_OBTENER_CLIENTES, igual que
    /// GET api/Clientes/GetClientesProbabilidadVenta) para toda la cartera del vendedor, sin el filtro fijo de 7 días.
    /// La consulta es la pesada (#401): el resultado se cachea 2 h por vendedor, tipo de interacción y grupo/subgrupo.
    /// Si el modelo falla se registra en ELMAH y se devuelve vacío (todas las probabilidades a 0: sin prioridad Máxima,
    /// pero la lista sale) y no se cachea.
    /// </summary>
    public class ProbabilidadesContactoModelo : IProbabilidadesContacto
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromHours(2);
        private static readonly MemoryCache cache = MemoryCache.Default;

        internal static string ClaveCache(string vendedor, string tipoInteraccion, string grupoSubgrupo) =>
            $"SugerenciasContacto:{vendedor?.Trim().ToUpperInvariant()}:{GestorClientes.NormalizarTipoInteraccion(tipoInteraccion)}:{grupoSubgrupo?.Trim().ToUpperInvariant()}";

        public async Task<Dictionary<string, PrediccionContacto>> Leer(string vendedor, string tipoInteraccion, string grupoSubgrupo)
        {
            string clave = ClaveCache(vendedor, tipoInteraccion, grupoSubgrupo);
            if (cache.Get(clave) is Dictionary<string, PrediccionContacto> enCache)
            {
                return enCache;
            }
            try
            {
                Dictionary<string, PrediccionContacto> resultado = await Task.Run(() => Calcular(vendedor, tipoInteraccion, grupoSubgrupo)).ConfigureAwait(false);
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

        private static Dictionary<string, PrediccionContacto> Calcular(string vendedor, string tipoInteraccion, string grupoSubgrupo)
        {
            List<ClienteInteraccion> clientes = ReintentosSql.ReintentarSiDeadlock(
                () => GestorClientes.ObtenerClientesParaModelo(vendedor, tipoInteraccion));
            if (!clientes.Any())
            {
                return new Dictionary<string, PrediccionContacto>();
            }

            // Lo que más compra el cliente, antes de pisarlo con el grupo/subgrupo que se pregunta (como el endpoint antiguo).
            List<string> grupoReal = clientes.Select(c => c.GrupoSubgrupoMasVendido).ToList();
            if (!string.IsNullOrEmpty(grupoSubgrupo))
            {
                foreach (ClienteInteraccion cliente in clientes)
                {
                    cliente.GrupoSubgrupoMasVendido = grupoSubgrupo;
                }
            }

            var mlContext = new MLContext();
            ITransformer modelo;
            using (var fichero = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "\\ModelsIA\\modelo_llamadas.zip", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                modelo = mlContext.Model.Load(fichero, out _);
            }
            IDataView datos = mlContext.Data.LoadFromEnumerable(clientes);
            List<PrediccionModelo> predicciones = mlContext.Data
                .CreateEnumerable<PrediccionModelo>(modelo.Transform(datos), reuseRowObject: false)
                .ToList();

            var resultado = new Dictionary<string, PrediccionContacto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < clientes.Count && i < predicciones.Count; i++)
            {
                string grupo = grupoReal[i];
                resultado[clientes[i].ClienteId.Trim()] = new PrediccionContacto
                {
                    Probabilidad = predicciones[i].Probability,
                    GrupoSubgrupoMasVendido = string.IsNullOrWhiteSpace(grupo) || grupo == "NADA" ? null : grupo.Trim()
                };
            }
            return resultado;
        }
    }
}
