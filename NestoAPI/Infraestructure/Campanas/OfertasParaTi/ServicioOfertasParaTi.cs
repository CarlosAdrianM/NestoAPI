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
    /// han mirado (se va llenando según hace falta; seguro entre hilos porque el endpoint lo comparte unos minutos).
    /// </summary>
    public class ContextoOfertasParaTi
    {
        public string CampanaPedida { get; set; }
        public DateTime Hoy { get; set; }
        public List<CandidataOferta> Candidatas { get; set; } = new List<CandidataOferta>();
        public ConcurrentDictionary<string, int> Disponibles { get; } = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// «Ofertas para ti», el NÚCLEO que comparten el endpoint de la app (un cliente) y la push de campaña (muchos): prepara la campaña
    /// (<see cref="Preparar"/>, una lectura de filas y una de popularidad) y elige las de cada cliente (<see cref="Calcular"/>: compras,
    /// pendientes y familias por tandas de clientes; el stock por tandas de productos y solo de lo que hace falta). El orden es el de
    /// <see cref="ReglaOfertasParaTi"/> o, si está, el del modelo 1 de propensión sobre las <see cref="ReglaOfertasParaTi.CANDIDATOS_MODELO"/>
    /// primeras de la regla con stock (<see cref="IPuntuadorReposicion"/>, el mismo que usa la puerta del recordatorio de reposición).
    /// Sin modelo, o si falla, la regla (nunca rompe).
    /// </summary>
    public class ServicioOfertasParaTi
    {
        internal const int TRAMO_STOCK = 60;
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly ILectorOfertasParaTi lector;
        private readonly IPuntuadorReposicion puntuador;
        private readonly Action<Exception> registrar;

        public ServicioOfertasParaTi(ILectorOfertasParaTi lector, IPuntuadorReposicion puntuador = null, Action<Exception> registrar = null)
        {
            this.lector = lector ?? throw new ArgumentNullException(nameof(lector));
            this.puntuador = puntuador;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
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
        /// Las ofertas de cada cliente, ordenadas (con <paramref name="maximo"/>, solo esas; sin él, todas las que se han mirado, como
        /// mucho <see cref="ReglaOfertasParaTi.CANDIDATOS_MODELO"/>). Un cliente sin ofertas sale con la lista vacía.
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
            DateTime hoy = contexto.Hoy;
            string versionModelo = ModeloDisponible();
            var familias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lista.Count; i += LectorOfertasParaTiSql.LOTE_CLIENTES)
            {
                List<string> lote = lista.Skip(i).Take(LectorOfertasParaTiSql.LOTE_CLIENTES).ToList();
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
                    List<CandidataEvaluada> conStock = await ConStock(contexto, evaluadas).ConfigureAwait(false);
                    IDictionary<string, float> probabilidades = null;
                    if (versionModelo != null && conStock.Count > 0)
                    {
                        probabilidades = await Puntuar(cliente, hoy, conStock).ConfigureAwait(false);
                        if (probabilidades == null)
                        {
                            versionModelo = null; // ha fallado: el resto de la tanda, por la regla
                        }
                    }
                    List<OfertaElegida> ordenadas = ReglaOfertasParaTi.Ordenar(conStock, probabilidades);
                    resultado[cliente] = new ResultadoOfertasCliente
                    {
                        Cliente = cliente,
                        Ofertas = maximo.HasValue ? ordenadas.Take(maximo.Value).ToList() : ordenadas,
                        VersionModelo = probabilidades != null && probabilidades.Count > 0 ? versionModelo : null
                    };
                }
            }
            return resultado;
        }

        /// <summary>Las primeras con stock (hasta <see cref="ReglaOfertasParaTi.CANDIDATOS_MODELO"/>), leyendo el stock por tramos y solo lo que falta.</summary>
        private async Task<List<CandidataEvaluada>> ConStock(ContextoOfertasParaTi contexto, List<CandidataEvaluada> evaluadas)
        {
            var elegidas = new List<CandidataEvaluada>();
            for (int i = 0; i < evaluadas.Count && elegidas.Count < ReglaOfertasParaTi.CANDIDATOS_MODELO; i += TRAMO_STOCK)
            {
                List<CandidataEvaluada> tramo = evaluadas.Skip(i).Take(TRAMO_STOCK).ToList();
                List<string> faltan = tramo.Select(e => e.Candidata.Producto.Trim()).Where(p => !contexto.Disponibles.ContainsKey(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (faltan.Count > 0)
                {
                    Dictionary<string, int> leidos = await lector.LeerDisponibles(faltan).ConfigureAwait(false) ?? new Dictionary<string, int>();
                    foreach (string p in faltan)
                    {
                        contexto.Disponibles[p] = leidos.TryGetValue(p, out int d) ? d : 0;
                    }
                }
                elegidas.AddRange(ReglaOfertasParaTi.ConStock(tramo, contexto.Disponibles, ReglaOfertasParaTi.CANDIDATOS_MODELO - elegidas.Count));
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

        /// <summary>Las P1 del modelo, o null si ha fallado (a ELMAH; la regla sigue).</summary>
        private async Task<IDictionary<string, float>> Puntuar(string cliente, DateTime hoy, List<CandidataEvaluada> candidatas)
        {
            try
            {
                Dictionary<string, float> p = await puntuador.Puntuar(EMPRESA, cliente, hoy,
                    candidatas.Select(c => c.Candidata.Producto.Trim()).ToList()).ConfigureAwait(false);
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
