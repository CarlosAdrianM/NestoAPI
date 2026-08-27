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

            bool existeProducto = await db.Productos
                .AnyAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                .ConfigureAwait(false);
            if (!existeProducto)
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
    /// NestoAPI#414: elemento del PUT de categorías secundarias. El orden NO viaja: es la
    /// posición en la lista.
    /// </summary>
    public class CategoriaSecundariaPutDTO
    {
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
    }
}
