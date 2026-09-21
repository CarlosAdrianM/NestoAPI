using NestoAPI.Infraestructure.Buscador;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
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

        private readonly NVEntities db;

        public BuscadorController() : this(new NVEntities()) { }

        // NestoAPI#501: constructor para tests (permite inyectar un NVEntities falso).
        internal BuscadorController(NVEntities db)
        {
            this.db = db;
        }

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
        // Público a propósito (solo ranking, sin precios): lo llama el servidor de la tienda
        // PrestaShop (nestobuscador) sin token. Explícito para que #190 no lo cierre.
        [AllowAnonymous]
        [HttpGet]
        [Route("api/buscador")]
        public IHttpActionResult Buscar(string q, string tipo = null, bool incluirAnulados = false, int skip = 0, int take = TAKE_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Indique qué buscar (q)");
            }
            List<dynamic> resultados = LuceneBuscador.Buscar(
                Parametros(q, tipo, incluirAnulados, skip, take, PuedeVerFamiliasPresenciales()));
            return Ok(resultados);
        }

        /// <summary>
        /// La misma búsqueda, envuelta con los totales para paginar: <c>{ Total, TotalAnulados,
        /// Resultados }</c>. Total cuenta los activos; TotalAnulados solo si se piden. Es lo que
        /// usa el buscador de la tienda PrestaShop (módulo nestobuscador).
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        [Route("api/buscador/paginado")]
        public IHttpActionResult BuscarPaginado(string q, string tipo = null, bool incluirAnulados = false, int skip = 0, int take = TAKE_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Indique qué buscar (q)");
            }
            ResultadoPaginado resultado = LuceneBuscador.BuscarPaginado(
                Parametros(q, tipo, incluirAnulados, skip, take, PuedeVerFamiliasPresenciales()));
            return Ok(resultado);
        }

        /// <summary>
        /// NestoAPI#501: las familias de venta presencial solo las ve un vendedor presencial. Este
        /// endpoint es anónimo (lo llama el buscador de la tienda), así que lo normal es que no.
        /// Si la consulta del vendedor fallara, se oculta: el criterio seguro es no enseñarlas.
        /// </summary>
        private bool PuedeVerFamiliasPresenciales()
        {
            try
            {
                return FamiliasRestringidas.PuedeVerlas(User, db);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static ParametrosBusqueda Parametros(string q, string tipo, bool incluirAnulados, int skip, int take,
            bool incluirSoloPresenciales = false)
        {
            return new ParametrosBusqueda
            {
                Query = q,
                Tipo = tipo,
                IncluirAnulados = incluirAnulados,
                IncluirSoloPresenciales = incluirSoloPresenciales,
                Skip = skip < 0 ? 0 : skip,
                Take = take <= 0 ? TAKE_POR_DEFECTO : (take > TAKE_MAXIMO ? TAKE_MAXIMO : take)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
