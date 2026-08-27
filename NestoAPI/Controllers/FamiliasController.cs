using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data.Entity;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#406: mantenimiento de familias. De momento lo ÚNICO editable es
    /// <c>PublicoIgualQueProfesional</c>, la marca de "esta familia se vende al público al mismo
    /// precio que al profesional".
    ///
    /// El resto de campos viajan solo para poder identificar la familia en la pantalla y NO se
    /// pueden modificar a propósito: <c>%ComisiónFija</c> y <c>%DtoMáximoComisión</c> mueven
    /// dinero de las comisiones de los vendedores, y no tiene sentido que se puedan tocar por
    /// error desde una pantalla que existe para marcar una casilla.
    /// </summary>
    [Authorize]
    public class FamiliasController : ApiController
    {
        public FamiliasController() : this(new NVEntities())
        {
        }

        // Constructor para tests, como el resto de controllers
        public FamiliasController(NVEntities db)
        {
            this.db = db;
            this.db.Configuration.LazyLoadingEnabled = false;
        }

        private readonly NVEntities db;

        /// <summary>Todas las familias de la empresa, para la pantalla de mantenimiento.</summary>
        [HttpGet]
        [Route("api/Familias")]
        public async Task<IHttpActionResult> GetFamilias(string empresa = null)
        {
            string empresaBuscada = string.IsNullOrWhiteSpace(empresa)
                ? Constantes.Empresas.EMPRESA_POR_DEFECTO
                : empresa.Trim();

            List<Familia> familias = await db.Familias
                .Where(f => f.Empresa == empresaBuscada)
                .OrderBy(f => f.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            return Ok(familias.Select(ADto).ToList());
        }

        /// <summary>
        /// Marca o desmarca una familia. Solo toca <c>PublicoIgualQueProfesional</c>.
        /// </summary>
        [HttpPut]
        [Route("api/Familias")]
        public async Task<IHttpActionResult> PutFamilia([FromBody] FamiliaMantenimientoDTO dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Numero))
            {
                return BadRequest("Hay que indicar la familia.");
            }

            string empresa = string.IsNullOrWhiteSpace(dto.Empresa)
                ? Constantes.Empresas.EMPRESA_POR_DEFECTO
                : dto.Empresa.Trim();
            string numero = dto.Numero.Trim();

            Familia familia = await db.Familias
                .FirstOrDefaultAsync(f => f.Empresa == empresa && f.Número == numero)
                .ConfigureAwait(false);

            if (familia == null)
            {
                return NotFound();
            }

            if (familia.PublicoIgualQueProfesional == dto.PublicoIgualQueProfesional)
            {
                return Ok(ADto(familia));   // no hay cambio: ni se toca la auditoría
            }

            familia.PublicoIgualQueProfesional = dto.PublicoIgualQueProfesional;
            familia.Usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            familia.Fecha_Modificación = DateTime.Now;

            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // Los productos de la familia cambian de precio público al marcarla o desmarcarla, así
            // que hay que republicarlos: si no, la web se queda con el precio anterior hasta que
            // algo toque cada producto. Se encolan los vivos; el resto no se publica nunca.
            List<string> productos = await db.Productos
                .Where(p => p.Empresa == empresa && p.Familia == numero && p.Estado >= 0)
                .Select(p => p.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (string producto in productos)
            {
                _ = await db.EncolarProductoSync(producto.Trim(), "Mantenimiento familias").ConfigureAwait(false);
            }

            return Ok(ADto(familia));
        }

        private static FamiliaMantenimientoDTO ADto(Familia f)
        {
            return new FamiliaMantenimientoDTO
            {
                Empresa = f.Empresa?.Trim(),
                Numero = f.Número?.Trim(),
                Descripcion = f.Descripción?.Trim(),
                Estado = f.Estado,
                PublicoIgualQueProfesional = f.PublicoIgualQueProfesional
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
