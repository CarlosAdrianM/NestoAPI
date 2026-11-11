using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#414: mantenimiento de las categorías secundarias de un producto. Grupo/Subgrupo
    /// de la ficha siguen siendo LOS PRINCIPALES; esta lista es la ristra adicional ordenada
    /// (Ofertas del mes, Pack Regalo, Exclusivo Profesional...) que viaja en el mensaje de
    /// Productos y que el legacy mantenía con listas de referencias a mano dentro de un SQL.
    ///
    /// El PUT reemplaza la lista COMPLETA (el orden es la posición en la lista recibida): añadir,
    /// quitar y reordenar son la misma operación, que es lo cómodo para la pantalla. Tras
    /// guardar, encola el producto en Nesto_sync para que la pasada de los 5 minutos lo
    /// republique con las categorías nuevas.
    /// </summary>
    public class ProductosCategoriasSecundariasController : ApiController
    {
        private readonly NVEntities db;

        public ProductosCategoriasSecundariasController()
        {
            db = new NVEntities();
        }

        public ProductosCategoriasSecundariasController(NVEntities db)
        {
            this.db = db;
        }

        [HttpGet]
        [Route("api/ProductosCategoriasSecundarias/{producto}")]
        [ResponseType(typeof(List<CategoriaSecundariaDTO>))]
        public async Task<IHttpActionResult> GetCategoriasSecundarias(string producto)
        {
            ProductoDTO dto = new ProductoDTO { Producto = producto?.Trim() };
            await ProductoDTO.CargarCategoriasSecundarias(dto, db).ConfigureAwait(false);
            return Ok(dto.CategoriasSecundarias.ToList());
        }

        [HttpPut]
        [Route("api/ProductosCategoriasSecundarias/{producto}")]
        public async Task<IHttpActionResult> PutCategoriasSecundarias(string producto,
            [FromBody] List<CategoriaSecundariaPutDTO> categorias)
        {
            producto = producto?.Trim();
            if (string.IsNullOrEmpty(producto))
            {
                return BadRequest("Falta el producto");
            }
            categorias = categorias ?? new List<CategoriaSecundariaPutDTO>();

            var ficha = (await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                .Select(p => new { p.Grupo, p.SubGrupo })
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
            if (ficha == null)
            {
                return NotFound();
            }

            var limpias = categorias
                .Select(c => new { Grupo = c.Grupo?.Trim(), Subgrupo = c.Subgrupo?.Trim() })
                .ToList();

            if (limpias.Any(c => string.IsNullOrEmpty(c.Grupo) || string.IsNullOrEmpty(c.Subgrupo)))
            {
                return BadRequest("Todas las categorías deben llevar grupo y subgrupo");
            }
            if (limpias.GroupBy(c => c.Grupo + "|" + c.Subgrupo).Any(g => g.Count() > 1))
            {
                return BadRequest("Hay categorías repetidas en la lista");
            }
            // Sugerencia 780: Pack Regalo solo como principal y sin otras (la misma regla que la ficha de Nesto)
            string motivoPackRegalo = ReglaPackRegalo.Motivo(ficha.Grupo, ficha.SubGrupo, limpias.Select(c => (c.Grupo, c.Subgrupo)));
            if (motivoPackRegalo != null)
            {
                return BadRequest(motivoPackRegalo);
            }
            foreach (var categoria in limpias)
            {
                bool existeSubgrupo = await db.SubGruposProductoes
                    .AnyAsync(s => s.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                        && s.Grupo == categoria.Grupo && s.Número == categoria.Subgrupo)
                    .ConfigureAwait(false);
                if (!existeSubgrupo)
                {
                    return BadRequest($"El subgrupo {categoria.Grupo}/{categoria.Subgrupo} no existe");
                }
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);

            // Reemplazo en dos SaveChanges: la PK es (Empresa, Número, Orden) y EF6 no garantiza
            // borrar antes de insertar dentro del mismo SaveChanges, así que reutilizar un Orden
            // petaría por PK duplicada. La ventana sin categorías dura milisegundos y, si algo se
            // torciera en medio, la pantalla re-guarda y listo (la publicación va DESPUÉS del
            // segundo save, nunca viaja el estado intermedio).
            List<ProductoCategoriaSecundaria> actuales = await db.ProductosCategoriasSecundarias
                .Where(pcs => pcs.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && pcs.Número == producto)
                .ToListAsync().ConfigureAwait(false);
            db.ProductosCategoriasSecundarias.RemoveRange(actuales);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            int orden = 1;
            foreach (var categoria in limpias)
            {
                _ = db.ProductosCategoriasSecundarias.Add(new ProductoCategoriaSecundaria
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Número = producto,
                    Orden = orden++,
                    Grupo = categoria.Grupo,
                    SubGrupo = categoria.Subgrupo,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            _ = await db.EncolarProductoSync(producto, usuario).ConfigureAwait(false);

            return Ok();
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

    /// <summary>
    /// Sugerencia 780 (Laura, aprobada por Carlos 11/11/26): los productos de «Pack Regalo» (COS/PRG) no pueden estar en ninguna
    /// otra categoría web, y Pack Regalo tiene que ser la categoría PRINCIPAL (el subgrupo de la ficha), nunca una secundaria.
    /// Los textos son los de la ficha de Nesto (Producto/Models/ReglaPackRegalo.cs). Solo el PUT: el cuadre del Outlet escribe por
    /// SQL (RepositorioOutletSql) y no pasa por aquí.
    /// </summary>
    public static class ReglaPackRegalo
    {
        public const string GRUPO = "COS";
        public const string SUBGRUPO = "PRG";

        internal const string MOTIVO_PRINCIPAL_PACK_REGALO =
            "Este producto es un Pack Regalo (COS/PRG): los packs regalo no pueden estar en ninguna otra categoría web.";
        internal const string MOTIVO_PACK_REGALO_COMO_SECUNDARIA =
            "Pack Regalo (COS/PRG) no se puede poner como categoría secundaria: tiene que ser la categoría principal " +
            "del producto (el subgrupo de la ficha).";

        public static bool EsPackRegalo(string grupo, string subgrupo)
            => string.Equals(grupo?.Trim(), GRUPO, StringComparison.OrdinalIgnoreCase)
            && string.Equals(subgrupo?.Trim(), SUBGRUPO, StringComparison.OrdinalIgnoreCase);

        /// <summary>Por qué no vale esa lista de secundarias para un producto con esa principal; null si vale.</summary>
        public static string Motivo(string grupoPrincipal, string subgrupoPrincipal, IEnumerable<(string Grupo, string Subgrupo)> secundarias)
        {
            List<(string Grupo, string Subgrupo)> lista = (secundarias ?? Enumerable.Empty<(string, string)>()).ToList();
            if (EsPackRegalo(grupoPrincipal, subgrupoPrincipal) && lista.Any())
            {
                return MOTIVO_PRINCIPAL_PACK_REGALO;
            }
            return lista.Any(c => EsPackRegalo(c.Grupo, c.Subgrupo)) ? MOTIVO_PACK_REGALO_COMO_SECUNDARIA : null;
        }
    }

    /// <summary>
    /// NestoAPI#414: elemento del PUT de categorías secundarias. El orden NO viaja: es la
    /// posición en la lista.
    /// </summary>
    public class CategoriaSecundariaPutDTO
    {
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
    }
}
