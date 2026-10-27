using ModeloLlamadaPedido.Features;
using NestoAPI.Models.Clientes;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;
using System;
using System.Collections.Generic;
using System.Configuration;
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
    /// NestoAPI#603 c3b: el modelo de contactos (<see cref="ModeloContacto.RutaPorDefecto"/>, entrada <see cref="ModeloContactoEntrada"/>) para
    /// toda la cartera del vendedor. Las features se calculan como en el entrenamiento (<see cref="CalculadoraFeaturesContacto"/>,
    /// con ahora como momento del contacto) sobre los datos crudos de <see cref="IRepositorioFeaturesContacto"/>.
    /// <list type="bullet">
    /// <item>La consulta se cachea 2 h por vendedor y día: el tipo de interacción y el grupo/subgrupo ya no la cambian (son
    /// features que se ponen en memoria), así que cambiar de tipo o de grupo no lanza otra.</item>
    /// <item>Las predicciones se cachean 2 h por vendedor, tipo, grupo/subgrupo, día y mañana/tarde (EsPorLaTarde es feature).</item>
    /// <item>Sin el modelo por subgrupo, el grupo/subgrupo que se pregunta sustituye a GrupoSubgrupoMasVendido en una copia; el que se devuelve es el real.</item>
    /// <item>NestoAPI#618: con un subgrupo elegido, el appSetting <see cref="CLAVE_SUBGRUPO_ACTIVO"/> = true y un modelo por
    /// subgrupo promovido (<see cref="ModeloContacto.RutaSubgrupo"/>), la probabilidad es la de un pedido en 7 días QUE INCLUYA
    /// el subgrupo (<see cref="ModeloContactoSubgrupoEntrada"/>) y cada cliente lleva su motivo legible. Sin el interruptor
    /// (por defecto) el modelo por subgrupo está en sombra: el job lo entrena, lo mide y lo promueve, pero aquí no se usa.
    /// Si falla, a ELMAH y como antes.</item>
    /// </list>
    /// Si el modelo falla se registra en ELMAH y se devuelve vacío (todas las probabilidades a 0: sin prioridad Máxima,
    /// pero la lista sale) y no se cachea.
    /// </summary>
    public class ProbabilidadesContactoModelo : IProbabilidadesContacto
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromHours(2);
        /// <summary>NestoAPI#618: «true» = usar el modelo por subgrupo al elegir un subgrupo. Sin la clave o con otro valor, no (sombra).</summary>
        public const string CLAVE_SUBGRUPO_ACTIVO = "ModeloLlamadas:Subgrupo:Activo";
        private static readonly MemoryCache cache = MemoryCache.Default;

        private readonly IRepositorioFeaturesContacto repositorio;
        private readonly Func<DateTime> ahora;
        private readonly string rutaModelo;
        private readonly Func<bool> subgrupoActivo;
        private readonly Func<string> rutaSubgrupo;
        private readonly Func<IList<ModeloContactoSubgrupoEntrada>, string, List<float>> puntuarSubgrupo;
        private readonly Action<Exception> registrarError;

        public ProbabilidadesContactoModelo() : this(SubgrupoActivo(clave => ConfigurationManager.AppSettings[clave]))
        {
        }

        private ProbabilidadesContactoModelo(bool activo)
            : this(new RepositorioFeaturesContactoSql(conSubgrupos: activo), () => DateTime.Now, null, () => activo, () => ModeloContacto.RutaSubgrupo)
        {
        }

        /// <param name="subgrupoActivo">NestoAPI#618: el interruptor. Null = apagado.</param>
        /// <param name="rutaSubgrupo">NestoAPI#618: el zip del modelo por subgrupo (null si no hay). Null = ninguno.</param>
        /// <param name="puntuarSubgrupo">NestoAPI#618: null = <see cref="ModeloContacto.PuntuarSubgrupo"/>.</param>
        /// <param name="registrarError">Null = ELMAH.</param>
        internal ProbabilidadesContactoModelo(IRepositorioFeaturesContacto repositorio, Func<DateTime> ahora, string rutaModelo,
            Func<bool> subgrupoActivo = null, Func<string> rutaSubgrupo = null,
            Func<IList<ModeloContactoSubgrupoEntrada>, string, List<float>> puntuarSubgrupo = null, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio;
            this.ahora = ahora;
            this.rutaModelo = rutaModelo;
            this.subgrupoActivo = subgrupoActivo ?? (() => false);
            this.rutaSubgrupo = rutaSubgrupo ?? (() => null);
            this.puntuarSubgrupo = puntuarSubgrupo ?? ModeloContacto.PuntuarSubgrupo;
            this.registrarError = registrarError ?? RegistrarEnElmah;
        }

        /// <summary>NestoAPI#618: el interruptor del Web.config (<see cref="CLAVE_SUBGRUPO_ACTIVO"/>); apagado si no está o no es «true».</summary>
        public static bool SubgrupoActivo(Func<string, string> leerParametro)
            => bool.TryParse(leerParametro?.Invoke(CLAVE_SUBGRUPO_ACTIVO)?.Trim(), out bool activo) && activo;

        /// <summary>NestoAPI#618: el modelo por subgrupo solo con un subgrupo elegido, el interruptor puesto y un zip promovido.</summary>
        internal static bool UsarModeloSubgrupo(string grupoSubgrupo, bool activo, string rutaModeloSubgrupo)
            => activo && !string.IsNullOrWhiteSpace(grupoSubgrupo) && !string.IsNullOrWhiteSpace(rutaModeloSubgrupo);

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
                registrarError(new Exception(
                    $"NestoAPI#603: no se pudo calcular la probabilidad del modelo para el vendedor {vendedor}; las sugerencias salen sin ella", ex));
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

            string ruta = rutaSubgrupo();
            if (UsarModeloSubgrupo(grupoSubgrupo, subgrupoActivo(), ruta))
            {
                try
                {
                    return CalcularConSubgrupo(historiales, reales, grupoSubgrupo, momento, ruta);
                }
                catch (Exception ex)
                {
                    registrarError(new Exception(
                        $"NestoAPI#618: no se pudo puntuar con el modelo por subgrupo ({grupoSubgrupo}, vendedor {vendedor}); se usa el general con el subgrupo pisado", ex));
                }
            }

            List<ModeloContactoEntrada> entradas = ModeloContacto.ConGrupoSubgrupo(reales, grupoSubgrupo);
            List<float> probabilidades = ModeloContacto.Puntuar(entradas, rutaModelo);

            var resultado = new Dictionary<string, PrediccionContacto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reales.Count && i < probabilidades.Count; i++)
            {
                resultado[reales[i].ClienteId.Trim()] = new PrediccionContacto
                {
                    Probabilidad = probabilidades[i],
                    GrupoSubgrupoMasVendido = GrupoReal(reales[i])
                };
            }
            return resultado;
        }

        /// <summary>NestoAPI#618: probabilidad de un pedido con el subgrupo y su motivo, para cada cliente de la cartera.</summary>
        private Dictionary<string, PrediccionContacto> CalcularConSubgrupo(List<HistorialContacto> historiales, List<ModeloContactoEntrada> reales,
            string grupoSubgrupo, DateTime momento, string ruta)
        {
            List<ModeloContactoSubgrupoEntrada> entradas = EntradasSubgrupo(historiales, reales, grupoSubgrupo, momento);
            List<float> probabilidades = puntuarSubgrupo(entradas, ruta);
            if (probabilidades == null || probabilidades.Count != entradas.Count)
            {
                throw new InvalidOperationException($"el modelo por subgrupo devolvió {probabilidades?.Count ?? 0} probabilidades para {entradas.Count} clientes");
            }
            var resultado = new Dictionary<string, PrediccionContacto>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entradas.Count; i++)
            {
                resultado[reales[i].ClienteId.Trim()] = new PrediccionContacto
                {
                    Probabilidad = probabilidades[i],
                    GrupoSubgrupoMasVendido = GrupoReal(reales[i]),
                    MotivoSubgrupo = MotivoSubgrupo(entradas[i], probabilidades[i])
                };
            }
            return resultado;
        }

        /// <summary>NestoAPI#618: la entrada del modelo por subgrupo de cada cliente (las generales de hoy + la relación con el subgrupo).</summary>
        internal static List<ModeloContactoSubgrupoEntrada> EntradasSubgrupo(IList<HistorialContacto> historiales, IList<ModeloContactoEntrada> reales,
            string grupoSubgrupo, DateTime momento)
        {
            var entradas = new List<ModeloContactoSubgrupoEntrada>(historiales.Count);
            for (int i = 0; i < historiales.Count && i < reales.Count; i++)
            {
                ResumenSubgrupos resumen = CalculadoraFeaturesSubgrupo.Resumir(historiales[i].PedidosSubgrupo, momento);
                entradas.Add(CalculadoraFeaturesSubgrupo.Calcular<ModeloContactoSubgrupoEntrada>(reales[i], resumen, grupoSubgrupo));
            }
            return entradas;
        }

        /// <summary>
        /// NestoAPI#618: por qué sale (o no) con el subgrupo elegido, de usted y con cifras que el vendedor puede comprobar:
        /// «Con COS007: 4 pedidos en el último año (el último hace 23 días); probabilidad de pedido con COS007 del 18 %».
        /// </summary>
        internal static string MotivoSubgrupo(ModeloContactoSubgrupoEntrada e, float probabilidad)
        {
            string gs = e.GrupoSubgrupo;
            int pedidos = (int)e.PedidosSubgrupo12Meses;
            int dias = (int)e.DiasDesdeUltimoPedidoSubgrupo;
            string relacion;
            if (pedidos > 0)
            {
                relacion = $"Con {gs}: {(pedidos == 1 ? "1 pedido" : $"{pedidos} pedidos")} en el último año (el último hace {dias} {(dias == 1 ? "día" : "días")})";
            }
            else if (e.PedidosSubgrupo24Meses > 0)
            {
                relacion = $"Con {gs}: no lo compra desde hace {dias} días";
            }
            else
            {
                relacion = $"No ha comprado {gs} en los dos últimos años (venta cruzada)";
            }
            return $"{relacion}; probabilidad de pedido con {gs} del {Math.Round(probabilidad * 100, MidpointRounding.AwayFromZero)} %";
        }

        /// <summary>Lo que más compra el cliente de verdad, no el grupo que se pregunta.</summary>
        private static string GrupoReal(ModeloContactoEntrada entrada)
        {
            string grupo = entrada.GrupoSubgrupoMasVendido;
            return string.IsNullOrWhiteSpace(grupo) || grupo == CalculadoraFeaturesContacto.SIN_GRUPO ? null : grupo.Trim();
        }

        private static void RegistrarEnElmah(Exception ex)
        {
            try
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(ex));
            }
            catch
            {
                // Sin ELMAH (tests, consola): que no tumbe la lista.
            }
        }
    }
}
