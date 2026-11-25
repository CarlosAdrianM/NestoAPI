using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// Lo de la campaña que no depende del cliente: las candidatas (con su popularidad) y el disponible de los productos que ya se
    /// han mirado (se va llenando según hace falta; seguro entre hilos porque el endpoint lo comparte unos minutos). Guarda también el
    /// ranking de cada cliente ya calculado: vive lo que vive el contexto (10 minutos en <see cref="CacheContextoOfertasParaTi"/>) y lo
    /// usan la franja, la pantalla, «Ver todas» y la push.
    /// </summary>
    public class ContextoOfertasParaTi
    {
        public string CampanaPedida { get; set; }
        public DateTime Hoy { get; set; }
        public List<CandidataOferta> Candidatas { get; set; } = new List<CandidataOferta>();
        public ConcurrentDictionary<string, int> Disponibles { get; } = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, ResultadoOfertasCliente> Rankings { get; } = new ConcurrentDictionary<string, ResultadoOfertasCliente>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// «Ofertas para ti», el NÚCLEO que comparten el endpoint de la app (un cliente) y la push de campaña (muchos): prepara la campaña
    /// (<see cref="Preparar"/>, una lectura de filas y una de popularidad) y saca el RANKING de cada cliente (<see cref="Calcular"/>:
    /// compras, pendientes y familias por tandas de clientes; por cliente, las fuentes de las sugerencias de la plantilla
    /// (<see cref="IFuentesSugerenciasOfertas"/>) y el modelo 1 de propensión (<see cref="IPuntuadorReposicion"/>, el de la puerta del
    /// recordatorio de reposición) sobre las de sus fuentes y, hasta <see cref="ReglaRankingOfertasParaTi.MAXIMO_PUNTUADOS"/>, las demás).
    /// El orden es el de <see cref="ReglaRankingOfertasParaTi"/>. Sin modelo, o si falla, la regla; sin fuentes, o si fallan, solo las de
    /// <see cref="ReglaOfertasParaTi.Evaluar"/> (nunca rompe). El stock no entra en el ranking: lo mira quien lo recorre, por tandas.
    /// </summary>
    public class ServicioOfertasParaTi
    {
        internal const int TRAMO_STOCK = 60;
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly ILectorOfertasParaTi lector;
        private readonly IPuntuadorReposicion puntuador;
        private readonly IFuentesSugerenciasOfertas fuentes;
        private readonly Action<Exception> registrar;

        public ServicioOfertasParaTi(ILectorOfertasParaTi lector, IPuntuadorReposicion puntuador = null, Action<Exception> registrar = null,
            IFuentesSugerenciasOfertas fuentes = null)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.puntuador = puntuador;
            this.fuentes = fuentes;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>El de producción: el lector SQL, el modelo 1 y las fuentes de las sugerencias de la plantilla (su mismo repositorio).</summary>
        public static ServicioOfertasParaTi Produccion(NVEntities db)
        {
            var repositorio = new SugerenciasProductos.RepositorioSugerenciasProductosSql(db);
            return new ServicioOfertasParaTi(new LectorOfertasParaTiSql(db),
                new PuntuadorReposicionModelo(repositorio, SugerenciasProductos.PredictorSugerenciasProductos.Instancia),
                fuentes: new FuentesSugerenciasOfertasSql(repositorio));
        }

        public async Task<ContextoOfertasParaTi> Preparar(string campana, DateTime hoy)
        {
            List<FilaCampanaOferta> filas = await lector.LeerFilasCampana(EMPRESA, campana, hoy.Date).ConfigureAwait(false)
                ?? new List<FilaCampanaOferta>();
            List<string> productos = filas.Where(f => ReglaOfertasParaTi.FilaValida(f, hoy)).Select(f => f.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, int> popularidad = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (productos.Count > 0)
            {
                try
                {
                    popularidad = await lector.LeerPopularidad(productos).ConfigureAwait(false) ?? popularidad;
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Ofertas para ti] No se ha podido leer la popularidad; se ordena sin ella: {ex.Message}", ex));
                }
            }
            return new ContextoOfertasParaTi
            {
                CampanaPedida = string.IsNullOrWhiteSpace(campana) ? null : campana.Trim(),
                Hoy = hoy.Date,
                Candidatas = ReglaOfertasParaTi.Candidatas(filas, campana, hoy, popularidad)
            };
        }

        /// <summary>
        /// El ranking de cada cliente (todas las ofertas de la campaña que valen para él, ordenadas; las «Personal», las suyas). Con
        /// <paramref name="maximo"/>, solo las primeras «Personal» con stock (sin precio). Un cliente sin ofertas sale con la lista vacía.
        /// Lo ya calculado con este contexto se reutiliza.
        /// </summary>
        public async Task<Dictionary<string, ResultadoOfertasCliente>> Calcular(ContextoOfertasParaTi contexto, IReadOnlyCollection<string> clientes, int? maximo)
        {
            var resultado = new Dictionary<string, ResultadoOfertasCliente>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = ReglaPushCampana.Conjunto(clientes).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string c in lista)
            {
                resultado[c] = new ResultadoOfertasCliente { Cliente = c };
            }
            if (contexto == null || contexto.Candidatas.Count == 0 || lista.Count == 0)
            {
                return resultado;
            }
            var porCalcular = new List<string>();
            foreach (string c in lista)
            {
                if (contexto.Rankings.TryGetValue(c, out ResultadoOfertasCliente guardado))
                {
                    resultado[c] = guardado;
                }
                else
                {
                    porCalcular.Add(c);
                }
            }
            DateTime hoy = contexto.Hoy;
            string versionModelo = ModeloDisponible();
            var familias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var productosCampana = new HashSet<string>(contexto.Candidatas.Select(c => c.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            bool conFuentes = fuentes != null;

            for (int i = 0; i < porCalcular.Count; i += LectorOfertasParaTiSql.LOTE_CLIENTES)
            {
                List<string> lote = porCalcular.Skip(i).Take(LectorOfertasParaTiSql.LOTE_CLIENTES).ToList();
                List<CompraDiaReposicion> compras = await lector.LeerCompras(EMPRESA, lote, hoy.AddMonths(-ReglaOfertasParaTi.MESES_FAMILIAS), hoy.AddDays(1))
                    .ConfigureAwait(false) ?? new List<CompraDiaReposicion>();
                List<PendienteClienteOferta> pendientes = await lector.LeerPendientes(EMPRESA, lote).ConfigureAwait(false)
                    ?? new List<PendienteClienteOferta>();
                List<string> sinFamilia = compras.Where(c => !string.IsNullOrWhiteSpace(c.Producto)).Select(c => c.Producto.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !familias.ContainsKey(p)).ToList();
                if (sinFamilia.Count > 0)
                {
                    foreach (KeyValuePair<string, string> f in await lector.LeerFamilias(EMPRESA, sinFamilia).ConfigureAwait(false)
                        ?? new Dictionary<string, string>())
                    {
                        familias[f.Key.Trim()] = f.Value;
                    }
                    foreach (string p in sinFamilia.Where(p => !familias.ContainsKey(p)))
                    {
                        familias[p] = null;
                    }
                }
                ILookup<string, CompraDiaReposicion> comprasDe = compras.Where(c => c.Cliente != null)
                    .ToLookup(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase);
                ILookup<string, string> pendientesDe = pendientes.Where(p => p?.Cliente != null && p.Producto != null)
                    .ToLookup(p => p.Cliente.Trim(), p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (string cliente in lote)
                {
                    var historial = new HistorialClienteOfertas
                    {
                        Cliente = cliente,
                        Compras = comprasDe[cliente].ToList(),
                        Pendientes = new HashSet<string>(pendientesDe[cliente], StringComparer.OrdinalIgnoreCase),
                        FamiliaDe = familias
                    };
                    List<CandidataEvaluada> evaluadas = ReglaOfertasParaTi.Evaluar(contexto.Candidatas, historial, hoy);
                    FuentesClienteOfertas suyas = null;
                    if (conFuentes && evaluadas.Count > 0)
                    {
                        suyas = await LeerFuentes(cliente, hoy, historial.Compras, productosCampana).ConfigureAwait(false);
                        if (suyas == null)
                        {
                            conFuentes = false; // han fallado: el resto de la tanda, sin ellas
                        }
                    }
                    List<OfertaElegida> clasificadas = ReglaRankingOfertasParaTi.Clasificar(evaluadas, suyas);
                    List<string> paraPuntuar = ReglaRankingOfertasParaTi.ParaPuntuar(clasificadas);
                    IDictionary<string, float> probabilidades = null;
                    if (versionModelo != null && paraPuntuar.Count > 0)
                    {
                        probabilidades = await Puntuar(cliente, hoy, paraPuntuar).ConfigureAwait(false);
                        if (probabilidades == null)
                        {
                            versionModelo = null; // ha fallado: el resto de la tanda, por la regla
                        }
                    }
                    var ranking = new ResultadoOfertasCliente
                    {
                        Cliente = cliente,
                        Ofertas = ReglaRankingOfertasParaTi.Ordenar(clasificadas, probabilidades),
                        VersionModelo = probabilidades != null && probabilidades.Count > 0 ? versionModelo : null,
                        Puntuados = probabilidades == null ? 0 : paraPuntuar.Count
                    };
                    contexto.Rankings[cliente] = ranking;
                    resultado[cliente] = ranking;
                }
            }

            if (maximo.HasValue)
            {
                foreach (string c in lista)
                {
                    ResultadoOfertasCliente r = resultado[c];
                    List<OfertaElegida> conStock = await SiguientesConStock(contexto, r.Ofertas.Where(o => o.Personal), o => o.Candidata.Producto,
                        null, maximo.Value).ConfigureAwait(false);
                    resultado[c] = new ResultadoOfertasCliente { Cliente = c, Ofertas = conStock, VersionModelo = r.VersionModelo, Puntuados = r.Puntuados };
                }
            }
            return resultado;
        }

        /// <summary>Lee el disponible de los productos que aún no se han mirado (por tramos) y lo guarda en el contexto.</summary>
        public async Task AsegurarStock(ContextoOfertasParaTi contexto, IEnumerable<string> productos)
        {
            List<string> faltan = (productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                .Where(p => !contexto.Disponibles.ContainsKey(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < faltan.Count; i += TRAMO_STOCK)
            {
                List<string> tramo = faltan.Skip(i).Take(TRAMO_STOCK).ToList();
                Dictionary<string, int> leidos = await lector.LeerDisponibles(tramo).ConfigureAwait(false) ?? new Dictionary<string, int>();
                foreach (string p in tramo)
                {
                    contexto.Disponibles[p] = leidos.TryGetValue(p, out int d) ? d : 0;
                }
            }
        }

        /// <summary>
        /// Las primeras <paramref name="maximo"/> de <paramref name="enOrden"/> (en ese orden) con stock que no están en
        /// <paramref name="excluir"/>, leyendo el stock por tramos y solo de lo que aún no se ha mirado.
        /// </summary>
        public async Task<List<T>> SiguientesConStock<T>(ContextoOfertasParaTi contexto, IEnumerable<T> enOrden, Func<T, string> producto,
            ISet<string> excluir, int maximo)
        {
            List<T> lista = (enOrden ?? Enumerable.Empty<T>())
                .Where(e => e != null && producto(e) != null && (excluir == null || !excluir.Contains(producto(e).Trim())))
                .ToList();
            var elegidas = new List<T>();
            for (int i = 0; i < lista.Count && elegidas.Count < maximo; i += TRAMO_STOCK)
            {
                List<T> tramo = lista.Skip(i).Take(TRAMO_STOCK).ToList();
                await AsegurarStock(contexto, tramo.Select(producto)).ConfigureAwait(false);
                elegidas.AddRange(tramo.Where(e => contexto.Disponibles.TryGetValue(producto(e).Trim(), out int d) && d > 0)
                    .Take(maximo - elegidas.Count));
            }
            return elegidas;
        }

        private string ModeloDisponible()
        {
            try
            {
                return puntuador?.ModeloDisponible();
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] El modelo de sugerencias no está disponible; se ordena por la regla: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>Las de las fuentes de la plantilla, o null si han fallado (a ELMAH; el ranking sigue sin ellas).</summary>
        private async Task<FuentesClienteOfertas> LeerFuentes(string cliente, DateTime hoy, List<CompraDiaReposicion> compras, ISet<string> productosCampana)
        {
            try
            {
                return await fuentes.Leer(EMPRESA, cliente, hoy, compras, productosCampana).ConfigureAwait(false) ?? new FuentesClienteOfertas();
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] Las fuentes de las sugerencias han fallado con el cliente {cliente}; se ordena sin ellas: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>Las P1 del modelo, o null si ha fallado (a ELMAH; la regla sigue).</summary>
        private async Task<IDictionary<string, float>> Puntuar(string cliente, DateTime hoy, List<string> productos)
        {
            try
            {
                Dictionary<string, float> p = await puntuador.Puntuar(EMPRESA, cliente, hoy, productos).ConfigureAwait(false);
                return p ?? new Dictionary<string, float>();
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] El modelo ha fallado con el cliente {cliente}; se ordena por la regla: {ex.Message}", ex));
                return null;
            }
        }
    }


    /// <summary>
    /// La campaña preparada para el endpoint de la app, guardada unos minutos (todos los clientes comparten filas, popularidad y stock).
    /// </summary>
    public static class CacheContextoOfertasParaTi
    {
        public static readonly TimeSpan DURACION = TimeSpan.FromMinutes(10);
        private static readonly object bloqueo = new object();
        private static readonly Dictionary<string, Tuple<ContextoOfertasParaTi, DateTime>> guardados =
            new Dictionary<string, Tuple<ContextoOfertasParaTi, DateTime>>(StringComparer.OrdinalIgnoreCase);

        public static async Task<ContextoOfertasParaTi> Obtener(string campana, DateTime hoy, Func<Task<ContextoOfertasParaTi>> preparar)
        {
            string clave = (campana?.Trim() ?? string.Empty) + "|" + hoy.Date.ToString("yyyyMMdd");
            DateTime ahora = DateTime.UtcNow;
            lock (bloqueo)
            {
                if (guardados.TryGetValue(clave, out Tuple<ContextoOfertasParaTi, DateTime> g) && g.Item2 > ahora)
                {
                    return g.Item1;
                }
            }
            ContextoOfertasParaTi nuevo = await preparar().ConfigureAwait(false);
            lock (bloqueo)
            {
                foreach (string vieja in guardados.Where(x => x.Value.Item2 <= ahora).Select(x => x.Key).ToList())
                {
                    _ = guardados.Remove(vieja);
                }
                guardados[clave] = Tuple.Create(nuevo, ahora.Add(DURACION));
            }
            return nuevo;
        }
    }
}
