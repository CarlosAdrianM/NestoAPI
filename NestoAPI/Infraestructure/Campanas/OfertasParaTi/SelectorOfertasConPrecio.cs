using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// «Ofertas para ti»: LA REGLA del ranking de un cliente (pura). Carlos 25/11/26: siempre por lo que le cuadra a ESE cliente, con los
    /// criterios de las sugerencias de la plantilla, nunca con lo más vendido de la campaña como relleno.
    /// <list type="bullet">
    /// <item>Fuentes (grupos, en este orden): 1) le toca y 2) lo suele pedir (<see cref="ReglaOfertasParaTi.Evaluar"/>), 3) comprado junto con
    /// lo suyo, 4) parecidos (afinidad), 5) de sus marcas, 6) lo compran los de su perfil; 7) ninguna (solo para «Ver todas»).</item>
    /// <item>Al modelo 1 se le pasan las de sus fuentes y, hasta <see cref="MAXIMO_PUNTUADOS"/>, las demás de la campaña por popularidad (así
    /// un cliente sin historial se ordena por lo que el modelo sabe de su perfil: la popularidad por estado, #683).</item>
    /// <item>Orden: las que puntúa el modelo, de más a menos probabilidad; detrás las de sus fuentes que no puntúa, por la regla; al final
    /// las del grupo 7 (por descuento). «Personal» = de sus fuentes o puntuada: solo esas van a la franja y a la push.</item>
    /// </list>
    /// </summary>
    public static class ReglaRankingOfertasParaTi
    {
        public const int GRUPO_LE_TOCA = 1;
        public const int GRUPO_HABITUAL = 2;
        public const int GRUPO_COMPRADO_JUNTO = 3;
        public const int GRUPO_PARECIDO = 4;
        public const int GRUPO_SUS_MARCAS = 5;
        public const int GRUPO_PERFIL = 6;
        public const int GRUPO_NINGUNA = 7;
        /// <summary>Productos que se le pasan al modelo por cliente.</summary>
        public const int MAXIMO_PUNTUADOS = 300;

        public const string TEXTO_PARECIDO = "Lo piden profesionales que compran lo mismo que tú";
        public const string TEXTO_PERFIL = "Lo piden profesionales como tú";
        public const string TEXTO_TE_PUEDE_INTERESAR = "Una oferta que te puede interesar";

        public static string TextoCompradoJunto(string nombreSuyo)
            => $"Quienes piden {TextosPushOfertas.NombreCorto(nombreSuyo)} suelen pedir también este";

        /// <summary>Los productos suyos que hacen de «carrito» para los pares: los de más días de compra en 12 meses.</summary>
        public static List<string> Antecedentes(IEnumerable<CompraDiaReposicion> compras, DateTime hoy, int maximo)
        {
            DateTime desde = hoy.Date.AddMonths(-ReglaOfertasParaTi.MESES_HABITUAL);
            return (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto) && c.Dia.Date >= desde && c.Dia.Date < hoy.Date)
                .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Select(c => c.Dia.Date).Distinct().Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maximo))
                .Select(g => g.Key)
                .ToList();
        }

        /// <summary>Las evaluadas (en el orden de la regla) con su grupo, motivo y texto según la fuente que las trae, ordenadas por la regla.</summary>
        public static List<OfertaElegida> Clasificar(IEnumerable<CandidataEvaluada> evaluadas, FuentesClienteOfertas fuentes)
        {
            FuentesClienteOfertas f = fuentes ?? new FuentesClienteOfertas();
            var lista = new List<Tuple<OfertaElegida, decimal, int>>();
            int i = 0;
            foreach (CandidataEvaluada e in (evaluadas ?? Enumerable.Empty<CandidataEvaluada>()).Where(x => x?.Candidata?.Producto != null))
            {
                string producto = e.Candidata.Producto.Trim();
                var o = new OfertaElegida
                {
                    Candidata = e.Candidata,
                    Motivo = e.Motivo,
                    Texto = e.Texto,
                    CantidadSugerida = Math.Max(1, e.CantidadSugerida),
                    Habitual = e.Habitual,
                    Personal = true
                };
                decimal puntuacion = 0;
                if (e.Grupo == 1 || e.Grupo == 2)
                {
                    o.Grupo = e.Grupo == 1 ? GRUPO_LE_TOCA : GRUPO_HABITUAL;
                }
                else if (f.CompradoJunto.TryGetValue(producto, out Tuple<decimal, string> par))
                {
                    o.Grupo = GRUPO_COMPRADO_JUNTO;
                    o.Motivo = MotivosOfertaParaTi.COMPRADO_JUNTO;
                    o.Texto = TextoCompradoJunto(par.Item2);
                    puntuacion = par.Item1;
                }
                else if (f.Parecidos.TryGetValue(producto, out decimal afinidad))
                {
                    o.Grupo = GRUPO_PARECIDO;
                    o.Motivo = MotivosOfertaParaTi.PARECIDO;
                    o.Texto = TEXTO_PARECIDO;
                    puntuacion = afinidad;
                }
                else if (e.Grupo == 3)
                {
                    o.Grupo = GRUPO_SUS_MARCAS;
                }
                else if (f.Perfil.TryGetValue(producto, out int clientes))
                {
                    o.Grupo = GRUPO_PERFIL;
                    o.Motivo = MotivosOfertaParaTi.TE_PUEDE_INTERESAR;
                    o.Texto = TEXTO_PERFIL;
                    puntuacion = clientes;
                }
                else
                {
                    o.Grupo = GRUPO_NINGUNA;
                    o.Motivo = MotivosOfertaParaTi.TE_PUEDE_INTERESAR;
                    o.Texto = TEXTO_TE_PUEDE_INTERESAR;
                    o.Personal = false;
                }
                lista.Add(Tuple.Create(o, puntuacion, i++));
            }
            return lista
                .OrderBy(x => x.Item1.Grupo)
                .ThenByDescending(x => x.Item1.Grupo == GRUPO_NINGUNA ? x.Item1.Candidata.Descuento : x.Item2)
                .ThenBy(x => x.Item3)
                .Select(x => x.Item1)
                .ToList();
        }

        /// <summary>Lo que se le pasa al modelo: las de sus fuentes y, hasta el máximo, las demás de la campaña por popularidad y descuento.</summary>
        public static List<string> ParaPuntuar(IEnumerable<OfertaElegida> clasificadas, int maximo = MAXIMO_PUNTUADOS)
        {
            List<OfertaElegida> lista = (clasificadas ?? Enumerable.Empty<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            return lista.Where(o => o.Grupo < GRUPO_NINGUNA)
                .Concat(lista.Where(o => o.Grupo >= GRUPO_NINGUNA)
                    .OrderByDescending(o => o.Candidata.Popularidad)
                    .ThenByDescending(o => o.Candidata.Descuento)
                    .ThenBy(o => o.Candidata.Producto, StringComparer.OrdinalIgnoreCase))
                .Select(o => o.Candidata.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maximo))
                .ToList();
        }

        /// <summary>
        /// El ranking: con probabilidades, las puntuadas de más a menos (a igualdad, la regla) y son «Personal»; detrás las de sus fuentes
        /// sin puntuar por la regla; al final las del grupo 7 sin puntuar. Sin probabilidades, el orden de <see cref="Clasificar"/>.
        /// </summary>
        public static List<OfertaElegida> Ordenar(IReadOnlyList<OfertaElegida> clasificadas, IDictionary<string, float> probabilidades)
        {
            List<OfertaElegida> lista = (clasificadas ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            bool conModelo = probabilidades != null && probabilidades.Count > 0;
            for (int k = 0; k < lista.Count; k++)
            {
                OfertaElegida o = lista[k];
                o.Probabilidad = conModelo && probabilidades.TryGetValue(o.Candidata.Producto.Trim(), out float p) ? (float?)p : null;
                o.Personal = o.Grupo < GRUPO_NINGUNA || o.Probabilidad.HasValue;
            }
            return lista
                .Select((o, k) => new { o, k })
                .OrderBy(x => x.o.Probabilidad.HasValue ? 0 : (x.o.Grupo < GRUPO_NINGUNA ? 1 : 2))
                .ThenByDescending(x => x.o.Probabilidad ?? 0)
                .ThenBy(x => x.k)
                .Select(x => x.o)
                .ToList();
        }

        /// <summary>Sin mayúsculas, sin tildes y sin espacios de más (para buscar por nombre o referencia).</summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ");
        }

        /// <summary>El nombre de la familia que se enseña (y por el que se filtra): la descripción en formato título o, sin ella, el código.</summary>
        public static string NombreFamilia(CandidataOferta c)
            => ReglaOfertasParaTi.FormatoTitulo(string.IsNullOrWhiteSpace(c?.NombreFamilia) ? c?.Familia : c.NombreFamilia);

        /// <summary>Filtro de «Ver todas»: familia (código o nombre) y texto (en el nombre o la referencia), sin tildes ni mayúsculas.</summary>
        public static bool PasaFiltro(OfertaElegida o, string familia, string texto)
        {
            if (o?.Candidata == null)
            {
                return false;
            }
            string f = Normalizar(familia);
            if (f.Length > 0 && Normalizar(o.Candidata.Familia) != f && Normalizar(NombreFamilia(o.Candidata)) != f)
            {
                return false;
            }
            string t = Normalizar(texto);
            return t.Length == 0 || Normalizar(o.Candidata.Nombre).Contains(t) || Normalizar(o.Candidata.Producto).Contains(t);
        }

        /// <summary>Las familias de la campaña con cuántos productos (de más a menos y por nombre).</summary>
        public static List<FamiliaOfertasDTO> Familias(IEnumerable<CandidataOferta> candidatas)
            => (candidatas ?? Enumerable.Empty<CandidataOferta>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(NombreFamilia(c)))
                .GroupBy(NombreFamilia, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FamiliaOfertasDTO { Familia = g.Key, Productos = g.Count() })
                .OrderByDescending(x => x.Productos)
                .ThenBy(x => x.Familia, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>
    /// LA REGLA de la selección final con precio (pura): se hace DESPUÉS de saber qué ofertas mejoran el precio del cliente (el 15191, con
    /// 255 descuentos propios, veía UN producto porque se elegían 5 y luego se quitaban las que no mejoraban). Se recorre su ranking, se
    /// salta lo que no le mejora y se para al tener N con la diversidad (como mucho 2 por familia si hay alternativas) o al llegar a los topes.
    /// </summary>
    public static class ReglaSeleccionOfertasParaTi
    {
        /// <summary>Franja y push: como mucho estas consultas de precio por cliente.</summary>
        public const int TOPE_CONSULTAS_PRECIO = 60;
        /// <summary>Franja y push: como mucho estos productos mirados (con o sin stock).</summary>
        public const int TOPE_EXAMINADOS = 200;
        /// <summary>«Ver todas»: como mucho estos productos mirados por petición.</summary>
        public const int TOPE_EXAMINADOS_TODAS = 150;
        public const int CUANTAS_TODAS_POR_DEFECTO = 20;
        public const int CUANTAS_TODAS_TOPE = 50;
        /// <summary>De cuántas en cuántas se mira el stock.</summary>
        public const int TANDA = 20;

        public const string MENSAJE_SIN_OFERTAS = "Ahora mismo no hay ofertas que mejoren tus precios habituales.";

        /// <summary>1..50; sin dato o fuera de rango, el más cercano (por defecto 20).</summary>
        public static int CuantasTodas(int? pedido)
            => pedido == null ? CUANTAS_TODAS_POR_DEFECTO : Math.Max(1, Math.Min(CUANTAS_TODAS_TOPE, pedido.Value));

        /// <summary>Las de lo suyo: todo lo que no es «te puede interesar». Solo esas se nombran en la push.</summary>
        public static bool EsSuya(OfertaElegida oferta)
            => oferta?.Candidata != null && oferta.Motivo != MotivosOfertaParaTi.TE_PUEDE_INTERESAR;

        /// <summary>Lo que se va quedando, con la diversidad: como mucho 2 por familia delante; las que se pasan, aparte.</summary>
        public class Acumulador
        {
            private readonly int maximo;
            private readonly bool diversidad;
            private readonly List<OfertaConPrecio> delante = new List<OfertaConPrecio>();
            private readonly List<OfertaConPrecio> aparte = new List<OfertaConPrecio>();
            private readonly Dictionary<string, int> porFamilia = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public Acumulador(int maximo, bool diversidad = true)
            {
                this.maximo = Math.Max(0, maximo);
                this.diversidad = diversidad;
            }

            /// <summary>Ya hay N (que cumplen la diversidad): no hace falta mirar más.</summary>
            public bool Completo => delante.Count >= maximo;

            /// <summary>Hay más de N que mejoran (alguna apartada por la diversidad sobra).</summary>
            public bool Sobran => delante.Count + aparte.Count > maximo;

            public void Anadir(OfertaConPrecio oferta)
            {
                string familia = oferta?.Oferta?.Candidata?.Familia?.Trim() ?? string.Empty;
                porFamilia.TryGetValue(familia, out int n);
                if (diversidad && familia.Length > 0 && n >= ReglaOfertasParaTi.MAXIMO_POR_FAMILIA)
                {
                    aparte.Add(oferta);
                    return;
                }
                porFamilia[familia] = n + 1;
                delante.Add(oferta);
            }

            public List<OfertaConPrecio> Final() => delante.Concat(aparte).Take(maximo).ToList();
        }
    }

    /// <summary>Lo que ha dado un recorrido del ranking.</summary>
    public class RecorridoOfertas
    {
        public List<OfertaConPrecio> Ofertas { get; set; } = new List<OfertaConPrecio>();
        /// <summary>La posición por la que seguir; null si se ha llegado al final.</summary>
        public int? Siguiente { get; set; }
        /// <summary>Hay más de las pedidas que mejoran (apartadas por la diversidad).</summary>
        public bool Sobran { get; set; }
        public int Examinados { get; set; }
        public int ConsultasPrecio { get; set; }
    }

    /// <summary>
    /// El paso que comparten la franja, «Ver todas» y la push: recorre el ranking de un cliente desde una posición, mira el stock por tandas,
    /// calcula el precio SOLO de lo que hace falta (precio a precio: el motor no calcula por tandas; lo ya calculado se reutiliza del ranking
    /// guardado) y se queda con lo que le mejora el precio. Nunca lanza por un precio (a ELMAH y la siguiente).
    /// </summary>
    public class SelectorOfertasConPrecio
    {
        private readonly ServicioOfertasParaTi nucleo;
        private readonly ICalculadorPreciosOferta precios;
        private readonly Action<Exception> registrar;

        public SelectorOfertasConPrecio(ServicioOfertasParaTi nucleo, ICalculadorPreciosOferta precios, Action<Exception> registrar = null)
        {
            this.nucleo = nucleo ?? throw new ArgumentNullException(nameof(nucleo));
            this.precios = precios ?? throw new ArgumentNullException(nameof(precios));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Las N de la franja y la push: de lo «Personal» del ranking, con diversidad y los topes de <see cref="ReglaSeleccionOfertasParaTi"/>.</summary>
        public Task<RecorridoOfertas> Seleccionar(ContextoOfertasParaTi contexto, ResultadoOfertasCliente resultado, string empresa,
            string cliente, string contacto, int maximo)
            => Recorrer(contexto, resultado, (resultado?.Ofertas ?? new List<OfertaElegida>()).Where(o => o?.Personal == true).ToList(), 0,
                empresa, cliente, contacto, maximo, true, ReglaSeleccionOfertasParaTi.TOPE_CONSULTAS_PRECIO, ReglaSeleccionOfertasParaTi.TOPE_EXAMINADOS);

        public async Task<RecorridoOfertas> Recorrer(ContextoOfertasParaTi contexto, ResultadoOfertasCliente resultado, IReadOnlyList<OfertaElegida> lista,
            int desde, string empresa, string cliente, string contacto, int cuantas, bool diversidad, int topeConsultas, int topeExaminados)
        {
            var recorrido = new RecorridoOfertas();
            var acumulador = new ReglaSeleccionOfertasParaTi.Acumulador(cuantas, diversidad);
            List<OfertaElegida> todas = (lista ?? new List<OfertaElegida>()).Where(o => o?.Candidata?.Producto != null).ToList();
            int i = Math.Max(0, desde);
            if (contexto == null || resultado == null || cuantas <= 0 || string.IsNullOrWhiteSpace(contacto))
            {
                recorrido.Siguiente = i < todas.Count ? (int?)i : null;
                return recorrido;
            }
            while (i < todas.Count && !acumulador.Completo && recorrido.Examinados < topeExaminados && recorrido.ConsultasPrecio < topeConsultas)
            {
                List<OfertaElegida> tanda = todas.Skip(i).Take(Math.Min(ReglaSeleccionOfertasParaTi.TANDA, topeExaminados - recorrido.Examinados)).ToList();
                await nucleo.AsegurarStock(contexto, tanda.Select(o => o.Candidata.Producto)).ConfigureAwait(false);
                foreach (OfertaElegida o in tanda)
                {
                    if (acumulador.Completo || recorrido.ConsultasPrecio >= topeConsultas)
                    {
                        break;
                    }
                    i++;
                    recorrido.Examinados++;
                    string producto = o.Candidata.Producto.Trim();
                    if (!contexto.Disponibles.TryGetValue(producto, out int disponible) || disponible <= 0)
                    {
                        continue;
                    }
                    if (!resultado.Precios.TryGetValue(producto, out PreciosOferta p))
                    {
                        recorrido.ConsultasPrecio++;
                        try
                        {
                            p = await precios.Calcular(empresa, cliente, contacto, o.Candidata.Producto, o.Candidata.Campana).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            registrar(new Exception($"[Ofertas para ti] No se ha podido calcular el precio del {producto} para el cliente {cliente}: {ex.Message}", ex));
                            continue;
                        }
                        resultado.Precios[producto] = p;
                    }
                    if (ReglaPreciosOfertaParaTi.Mejora(p))
                    {
                        acumulador.Anadir(new OfertaConPrecio { Oferta = o, Precios = p });
                    }
                    // si no mejora (su precio de siempre ya es mejor), no es una oferta para él
                }
            }
            recorrido.Ofertas = acumulador.Final();
            recorrido.Sobran = acumulador.Sobran;
            recorrido.Siguiente = i < todas.Count ? (int?)i : null;
            return recorrido;
        }
    }
}
