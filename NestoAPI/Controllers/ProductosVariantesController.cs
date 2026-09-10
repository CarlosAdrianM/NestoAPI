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
    /// NestoAPI#477: mantenimiento de las familias de variantes (color, tapizado...) de la tienda.
    /// Una familia es un conjunto de referencias de Nesto que en PrestaShop son UNA ficha (la de
    /// la principal) con una combinación por referencia. Cada referencia solo puede estar en una
    /// familia, y la principal es una más de la familia (con su propio valor).
    ///
    /// El PUT reemplaza la familia COMPLETA (el orden es la posición en la lista recibida) y
    /// encola en Nesto_sync a todas las referencias afectadas, también a las que SALEN de la
    /// familia: su siguiente mensaje viaja sin Variante y el consumidor las vuelve producto plano.
    /// Mismo patrón que ProductosCategoriasSecundarias (#414).
    /// </summary>
    public class ProductosVariantesController : ApiController
    {
        private readonly NVEntities db;

        public ProductosVariantesController()
        {
            db = new NVEntities();
        }

        public ProductosVariantesController(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>La familia cuya principal es <paramref name="principal"/>, en orden. Vacía si no hay.</summary>
        [HttpGet]
        [Route("api/ProductosVariantes/{principal}")]
        [ResponseType(typeof(List<VarianteFamiliaDTO>))]
        public async Task<IHttpActionResult> GetFamilia(string principal)
        {
            principal = principal?.Trim();
            if (string.IsNullOrEmpty(principal))
            {
                return BadRequest("Falta la referencia principal");
            }
            return Ok(await LeerFamilia(principal).ConfigureAwait(false));
        }

        /// <summary>
        /// La familia a la que pertenece una referencia cualquiera (principal o hermana). Es lo que
        /// necesita la ficha de una hermana para enseñar la familia entera. Vacía si no está en ninguna.
        /// </summary>
        [HttpGet]
        [Route("api/ProductosVariantes/DeReferencia/{numero}")]
        [ResponseType(typeof(List<VarianteFamiliaDTO>))]
        public async Task<IHttpActionResult> GetFamiliaDeReferencia(string numero)
        {
            numero = numero?.Trim();
            if (string.IsNullOrEmpty(numero))
            {
                return BadRequest("Falta la referencia");
            }
            string principal = await db.ProductosVariantes
                .Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Número == numero)
                .Select(v => v.NúmeroPrincipal)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (principal == null)
            {
                return Ok(new List<VarianteFamiliaDTO>());
            }
            return Ok(await LeerFamilia(principal.Trim()).ConfigureAwait(false));
        }

        [HttpPut]
        [Route("api/ProductosVariantes/{principal}")]
        public async Task<IHttpActionResult> PutFamilia(string principal, [FromBody] List<VariantePutDTO> variantes)
        {
            principal = principal?.Trim();
            if (string.IsNullOrEmpty(principal))
            {
                return BadRequest("Falta la referencia principal");
            }
            variantes = variantes ?? new List<VariantePutDTO>();

            var limpias = variantes
                .Select(v => new { Numero = v.Numero?.Trim(), Atributo = v.Atributo?.Trim(), Valor = v.Valor?.Trim() })
                .ToList();

            string error = ValidarLista(principal, limpias.Select(v => (v.Numero, v.Atributo, v.Valor)).ToList());
            if (error != null)
            {
                return BadRequest(error);
            }

            List<string> numeros = limpias.Select(v => v.Numero).ToList();
            List<string> numerosYPrincipal = numeros.Concat(new[] { principal }).Distinct().ToList();
            var productos = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && numerosYPrincipal.Contains(p.Número.Trim()))
                .Select(p => new { Numero = p.Número, p.Familia })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, string> familiasDeProducto = productos.ToDictionary(p => p.Numero.Trim(), p => p.Familia?.Trim());

            if (!familiasDeProducto.ContainsKey(principal))
            {
                return NotFound();
            }
            foreach (var variante in limpias)
            {
                if (!familiasDeProducto.ContainsKey(variante.Numero))
                {
                    return BadRequest($"La referencia {variante.Numero} no existe");
                }
                if (!string.Equals(familiasDeProducto[variante.Numero], familiasDeProducto[principal], StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest($"La referencia {variante.Numero} es de otra familia de producto que la principal");
                }
            }

            // Una referencia solo puede estar en una familia: si ya está en OTRA, hay que sacarla de
            // allí primero (a propósito: mover en silencio rompería la otra ficha de la tienda).
            List<ProductoVariante> enOtraFamilia = await db.ProductosVariantes
                .Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && numeros.Contains(v.Número.Trim())
                    && v.NúmeroPrincipal.Trim() != principal)
                .ToListAsync().ConfigureAwait(false);
            if (enOtraFamilia.Any())
            {
                ProductoVariante ajena = enOtraFamilia.First();
                return BadRequest($"La referencia {ajena.Número.Trim()} ya es variante de {ajena.NúmeroPrincipal.Trim()}: quítala de esa familia antes");
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);

            // Reemplazo en dos SaveChanges, como en #414: borrar e insertar la misma PK dentro de un
            // solo SaveChanges no está garantizado en EF6.
            List<ProductoVariante> actuales = await db.ProductosVariantes
                .Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.NúmeroPrincipal.Trim() == principal)
                .ToListAsync().ConfigureAwait(false);
            List<string> salen = actuales.Select(a => a.Número.Trim()).Where(n => !numeros.Contains(n)).ToList();
            db.ProductosVariantes.RemoveRange(actuales);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            int orden = 1;
            foreach (var variante in limpias)
            {
                _ = db.ProductosVariantes.Add(new ProductoVariante
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Número = variante.Numero,
                    NúmeroPrincipal = principal,
                    Atributo = variante.Atributo,
                    Valor = variante.Valor,
                    Orden = orden++,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // Republicar a todos: los que entran (con Variante) y los que salen (sin ella).
            foreach (string numero in numeros.Concat(salen).Distinct())
            {
                _ = await db.EncolarProductoSync(numero, usuario).ConfigureAwait(false);
            }

            return Ok();
        }

        /// <summary>
        /// Las reglas de forma de la lista, puras para poder probarlas sin BD. null si está bien.
        /// </summary>
        internal static string ValidarLista(string principal, List<(string Numero, string Atributo, string Valor)> variantes)
        {
            if (variantes.Count == 0)
            {
                return null; // familia vacía = deshacer la familia; válido
            }
            if (variantes.Any(v => string.IsNullOrEmpty(v.Numero)))
            {
                return "Todas las variantes deben llevar referencia";
            }
            if (variantes.Any(v => string.IsNullOrEmpty(v.Atributo) || string.IsNullOrEmpty(v.Valor)))
            {
                return "Todas las variantes deben llevar atributo y valor";
            }
            if (!variantes.Any(v => string.Equals(v.Numero, principal, StringComparison.OrdinalIgnoreCase)))
            {
                return $"La principal {principal} tiene que estar en la lista, con su propio valor";
            }
            if (variantes.GroupBy(v => v.Numero, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                return "Hay referencias repetidas en la lista";
            }
            if (variantes.GroupBy(v => v.Atributo + "|" + v.Valor, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                return "Hay dos referencias con el mismo valor del mismo atributo";
            }
            return null;
        }

        private async Task<List<VarianteFamiliaDTO>> LeerFamilia(string principal)
        {
            var filas = await db.ProductosVariantes
                .Where(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.NúmeroPrincipal == principal)
                .OrderBy(v => v.Orden)
                .Select(v => new
                {
                    v.Número,
                    v.NúmeroPrincipal,
                    v.Atributo,
                    v.Valor,
                    v.Orden,
                    Nombre = db.Productos
                        .Where(p => p.Empresa == v.Empresa && p.Número == v.Número)
                        .Select(p => p.Nombre)
                        .FirstOrDefault()
                })
                .ToListAsync().ConfigureAwait(false);

            return filas.Select(f => new VarianteFamiliaDTO
            {
                Numero = f.Número?.Trim(),
                Principal = f.NúmeroPrincipal?.Trim(),
                Atributo = f.Atributo?.Trim(),
                Valor = f.Valor?.Trim(),
                Orden = f.Orden,
                Nombre = f.Nombre?.Trim()
            }).ToList();
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

    /// <summary>NestoAPI#477: una fila de la familia tal y como la ve la pantalla de Nesto.</summary>
    public class VarianteFamiliaDTO
    {
        public string Numero { get; set; }
        public string Principal { get; set; }
        public string Atributo { get; set; }
        public string Valor { get; set; }
        public int Orden { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>NestoAPI#477: elemento del PUT. El orden NO viaja: es la posición en la lista.</summary>
    public class VariantePutDTO
    {
        public string Numero { get; set; }
        public string Atributo { get; set; }
        public string Valor { get; set; }
    }
}
