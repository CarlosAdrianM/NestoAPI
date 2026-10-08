using NestoAPI.Infraestructure.Buscador;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>NestoAPI#609: propuesta de corrección del concepto de un enlace de pago (no cambia nada por sí sola).</summary>
    public interface IRevisorConcepto
    {
        Task<RespuestaRevisarConcepto> Revisar(SolicitudRevisarConcepto solicitud);
    }

    /// <summary>NestoAPI#609: los nombres propios de referencia para la IA (catálogo, eventos y lista fija).</summary>
    public interface IFuenteGlosarioConcepto
    {
        Task<List<string>> ObtenerTerminos(string empresa, string concepto);
    }

    /// <summary>
    /// NestoAPI#609. Primero lo determinista (<see cref="ReglasRevisionConcepto.AplicarDeterminista"/>); luego la IA con el
    /// glosario, con un tope de tiempo (~3 s): si tarda, falla o su propuesta no pasa las guardas (números, longitud,
    /// palabras), se queda lo determinista. Las respuestas de la IA se guardan en caché por texto exacto.
    /// </summary>
    public class RevisorConcepto : IRevisorConcepto
    {
        internal static readonly TimeSpan TIEMPO_MAXIMO_POR_DEFECTO = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan CADUCIDAD_CACHE = TimeSpan.FromHours(12);
        private const string PREFIJO_CACHE = "RevisarConcepto#609|";

        private readonly IFuenteGlosarioConcepto fuenteGlosario;
        private readonly Func<IServicioOpenAI> crearOpenAI;
        private readonly TimeSpan tiempoMaximo;
        private readonly ObjectCache cache;

        public RevisorConcepto() : this(new FuenteGlosarioConcepto(), CrearOpenAISiHayClave, TIEMPO_MAXIMO_POR_DEFECTO, MemoryCache.Default)
        {
        }

        internal RevisorConcepto(IFuenteGlosarioConcepto fuenteGlosario, Func<IServicioOpenAI> crearOpenAI, TimeSpan tiempoMaximo, ObjectCache cache = null)
        {
            this.fuenteGlosario = fuenteGlosario;
            this.crearOpenAI = crearOpenAI ?? (() => null);
            this.tiempoMaximo = tiempoMaximo;
            // Sin caché compartida (tests), una propia de esta instancia.
            this.cache = cache ?? new MemoryCache("RevisorConcepto-" + Guid.NewGuid());
        }

        internal static IServicioOpenAI CrearOpenAISiHayClave()
        {
            return string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["OpenAIKey"]) ? null : new ServicioOpenAI();
        }

        public async Task<RespuestaRevisarConcepto> Revisar(SolicitudRevisarConcepto solicitud)
        {
            string original = solicitud?.Concepto ?? string.Empty;
            string empresa = string.IsNullOrWhiteSpace(solicitud?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : solicitud.Empresa.Trim();
            string determinista = ReglasRevisionConcepto.AplicarDeterminista(original);
            string propuesto = determinista;

            if (!string.IsNullOrWhiteSpace(determinista) && !FormateadorConcepto.EsGenericoOVacio(determinista)
                && determinista.Any(char.IsLetter))
            {
                string deLaIA = await PropuestaIA(empresa, determinista).ConfigureAwait(false);
                if (deLaIA != null)
                {
                    propuesto = deLaIA;
                }
            }

            return Construir(original, propuesto);
        }

        internal static RespuestaRevisarConcepto Construir(string original, string propuesto)
        {
            // Lo que ya arregla el alta (Normalizar) no merece el «¿Quisiste decir…?»: crear lo hace igual.
            string loQueHariaElAlta = FormateadorConcepto.Normalizar(original) ?? string.Empty;
            return new RespuestaRevisarConcepto
            {
                Original = original,
                Propuesto = propuesto,
                HayCambios = !string.Equals(propuesto, loQueHariaElAlta.Trim(), StringComparison.Ordinal),
                Cambios = ReglasRevisionConcepto.CalcularCambios(original, propuesto)
            };
        }

        /// <summary>La propuesta de la IA ya validada y con las reglas deterministas, o null si no hay (o no vale).</summary>
        private async Task<string> PropuestaIA(string empresa, string determinista)
        {
            string clave = PREFIJO_CACHE + empresa + "|" + determinista;
            if (cache?.Get(clave) is string enCache)
            {
                return enCache;
            }

            Task<string> tarea;
            try
            {
                tarea = PedirAOpenAI(empresa, determinista);
            }
            catch (Exception ex)
            {
                Registrar(ex, determinista);
                return null;
            }

            Task terminada = await Task.WhenAny(tarea, Task.Delay(tiempoMaximo)).ConfigureAwait(false);
            if (terminada != tarea)
            {
                // La llamada sigue en segundo plano: que su posible excepción no quede sin observar.
                _ = tarea.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return null;
            }

            string respuesta;
            try
            {
                respuesta = await tarea.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Registrar(ex, determinista);
                return null;
            }
            if (respuesta == null)
            {
                return null;
            }

            string limpia = ReglasRevisionConcepto.AplicarDeterminista(ReglasRevisionConcepto.LimpiarRespuestaIA(respuesta));
            string resultado = ReglasRevisionConcepto.PropuestaValida(determinista, limpia) ? limpia : determinista;
            _ = cache?.Add(clave, resultado, DateTimeOffset.Now.Add(CADUCIDAD_CACHE));
            return resultado;
        }

        /// <summary>Null si OpenAI no contesta (sin clave, error HTTP); la excepción, si la hay, la recoge quien espera.</summary>
        private async Task<string> PedirAOpenAI(string empresa, string determinista)
        {
            IServicioOpenAI openAI = crearOpenAI();
            if (openAI == null)
            {
                return null;
            }
            List<string> glosario;
            try
            {
                glosario = fuenteGlosario == null
                    ? new List<string>()
                    : await fuenteGlosario.ObtenerTerminos(empresa, determinista).ConfigureAwait(false) ?? new List<string>();
            }
            catch (Exception ex)
            {
                Registrar(ex, determinista);
                glosario = new List<string>();
            }
            string respuesta = await openAI.GenerarContenidoAsync(ReglasRevisionConcepto.PROMPT_SISTEMA,
                ReglasRevisionConcepto.MensajeUsuario(determinista, glosario), maxTokens: 120, temperature: 0).ConfigureAwait(false);
            return ReglasRevisionConcepto.LimpiarRespuestaIA(respuesta) == null ? null : respuesta;
        }

        private static void Registrar(Exception ex, string concepto)
        {
            try
            {
                ElmahHelper.Log(new Exception($"[Revisar concepto #609] Sin propuesta de la IA para «{concepto}»: {ex.Message}", ex), "Sistema (revisar concepto)");
            }
            catch
            {
                // Sin contexto HTTP (tests): no pasa nada.
            }
        }
    }

    /// <summary>
    /// NestoAPI#609: el glosario sale de tres sitios (cada uno por su cuenta: si uno falla, siguen los otros):
    /// la lista fija de ParametrosUsuario «(defecto)» (claves que empiezan por <see cref="CLAVE_GLOSARIO"/>),
    /// los títulos de Eventos recientes o futuros y los nombres de productos parecidos a las palabras del concepto (Lucene).
    /// </summary>
    public class FuenteGlosarioConcepto : IFuenteGlosarioConcepto
    {
        /// <summary>Clave en ParametrosUsuario «(defecto)». Valor char(162): si no cabe, GlosarioConceptosPago2, 3…</summary>
        public const string CLAVE_GLOSARIO = "GlosarioConceptosPago";
        internal const int DIAS_EVENTOS_PASADOS = 60;
        internal const int MAXIMO_EVENTOS = 40;
        internal const int CONSULTAS_PRODUCTOS = 3;
        internal const int PRODUCTOS_POR_CONSULTA = 3;
        private static readonly TimeSpan CADUCIDAD = TimeSpan.FromMinutes(10);
        private static readonly MemoryCache cache = MemoryCache.Default;

        public async Task<List<string>> ObtenerTerminos(string empresa, string concepto)
        {
            var terminos = new List<string>();
            terminos.AddRange(await Protegido(() => ListaFija(empresa)).ConfigureAwait(false));
            terminos.AddRange(await Protegido(() => TitulosEventos(empresa)).ConfigureAwait(false));
            terminos.AddRange(await Protegido(() => Task.Run(() => NombresProductos(concepto))).ConfigureAwait(false));
            return terminos
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static async Task<List<string>> Protegido(Func<Task<List<string>>> fuente)
        {
            try
            {
                return await fuente().ConfigureAwait(false) ?? new List<string>();
            }
            catch (Exception ex)
            {
                try
                {
                    ElmahHelper.Log(new Exception($"[Revisar concepto #609] Una fuente del glosario ha fallado: {ex.Message}", ex), "Sistema (revisar concepto)");
                }
                catch
                {
                    // sin contexto HTTP
                }
                return new List<string>();
            }
        }

        private static async Task<List<string>> ListaFija(string empresa)
        {
            string clave = "GlosarioConceptosPago#609|fija|" + empresa;
            if (cache.Get(clave) is List<string> enCache)
            {
                return enCache;
            }
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                List<string> valores = await db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Usuario == Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO
                        && p.Clave.StartsWith(CLAVE_GLOSARIO))
                    .OrderBy(p => p.Clave)
                    .Select(p => p.Valor)
                    .ToListAsync().ConfigureAwait(false);
                List<string> terminos = ReglasRevisionConcepto.ParsearGlosario(valores);
                _ = cache.Add(clave, terminos, DateTimeOffset.Now.Add(CADUCIDAD));
                return terminos;
            }
        }

        private static async Task<List<string>> TitulosEventos(string empresa)
        {
            string clave = "GlosarioConceptosPago#609|eventos|" + empresa;
            if (cache.Get(clave) is List<string> enCache)
            {
                return enCache;
            }
            DateTime desde = DateTime.Today.AddDays(-DIAS_EVENTOS_PASADOS);
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                List<string> titulos = await db.Eventos
                    .Where(e => e.Empresa == empresa && e.Fecha >= desde)
                    .OrderBy(e => e.Fecha)
                    .Take(MAXIMO_EVENTOS)
                    .Select(e => e.Titulo)
                    .ToListAsync().ConfigureAwait(false);
                List<string> terminos = titulos.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
                _ = cache.Add(clave, terminos, DateTimeOffset.Now.Add(CADUCIDAD));
                return terminos;
            }
        }

        private static List<string> NombresProductos(string concepto)
        {
            var nombres = new List<string>();
            foreach (string palabra in ReglasRevisionConcepto.PalabrasParaBuscarProductos(concepto, CONSULTAS_PRODUCTOS))
            {
                List<dynamic> resultados = LuceneBuscador.Buscar(new LuceneBuscador.ParametrosBusqueda
                {
                    Query = palabra,
                    Tipo = "producto",
                    Take = PRODUCTOS_POR_CONSULTA
                });
                foreach (dynamic r in resultados)
                {
                    string nombre = r.Nombre as string;
                    if (!string.IsNullOrWhiteSpace(nombre))
                    {
                        nombres.Add(nombre.Trim());
                    }
                }
            }
            return nombres;
        }
    }
}
