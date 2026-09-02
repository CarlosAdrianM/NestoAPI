using NestoAPI.Infraestructure.Buscador;
using System.Collections.Generic;
using System.Web.Http;
using static NestoAPI.Infraestructure.Buscador.LuceneBuscador;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// El buscador Lucene de productos y vídeos. Es PÚBLICO a propósito: devuelve solo el ranking
    /// (id, nombre, familia, si está anulado), que es lo mismo que enseña la web a cualquier
    /// visitante; los precios los resuelve cada cliente por su cuenta. Lo usan Nesto
    /// (ServicioBusquedaProductos), la app y el módulo de buscador de la tienda PrestaShop
    /// (servidor a servidor). El tamaño de página está acotado para que nadie vuelque el índice.
    /// </summary>
    public class BuscadorController : ApiController
    {
        internal const int TAKE_POR_DEFECTO = 20;
        internal const int TAKE_MAXIMO = 100;

        [HttpPost]
        [Route("api/buscador/indexar")]
        public IHttpActionResult Indexar()
        {
            LuceneBuscador.IndexarTodo();
            return Ok("Indexación completada.");
        }

        /// <summary>
        /// Busca productos y vídeos. Con <paramref name="incluirAnulados"/> los productos anulados
        /// se devuelven detrás de los activos, marcados con Anulado = true, para que la tienda los
        /// muestre etiquetados en vez de ocultarlos (TiendasNuevaVision#38). Devuelve la lista
        /// pelada de siempre (lo que esperan Nesto y la app); para paginar con el total está
        /// <see cref="BuscarPaginado"/>.
        /// </summary>
        [HttpGet]
        [Route("api/buscador")]
        public IHttpActionResult Buscar(string q, string tipo = null, bool incluirAnulados = false, int skip = 0, int take = TAKE_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Indique qué buscar (q)");
            }
            List<dynamic> resultados = LuceneBuscador.Buscar(Parametros(q, tipo, incluirAnulados, skip, take));
            return Ok(resultados);
        }

        /// <summary>
        /// La misma búsqueda, envuelta con los totales para paginar: <c>{ Total, TotalAnulados,
        /// Resultados }</c>. Total cuenta los activos; TotalAnulados solo si se piden. Es lo que
        /// usa el buscador de la tienda PrestaShop (módulo nestobuscador).
        /// </summary>
        [HttpGet]
        [Route("api/buscador/paginado")]
        public IHttpActionResult BuscarPaginado(string q, string tipo = null, bool incluirAnulados = false, int skip = 0, int take = TAKE_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Indique qué buscar (q)");
            }
            ResultadoPaginado resultado = LuceneBuscador.BuscarPaginado(Parametros(q, tipo, incluirAnulados, skip, take));
            return Ok(resultado);
        }

        internal static ParametrosBusqueda Parametros(string q, string tipo, bool incluirAnulados, int skip, int take)
        {
            return new ParametrosBusqueda
            {
                Query = q,
                Tipo = tipo,
                IncluirAnulados = incluirAnulados,
                Skip = skip < 0 ? 0 : skip,
                Take = take <= 0 ? TAKE_POR_DEFECTO : (take > TAKE_MAXIMO ? TAKE_MAXIMO : take)
            };
        }
    }
}
