using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class OfertasPermitidasFamiliaController : ApiController
    {
        private NVEntities db;

        public OfertasPermitidasFamiliaController()
        {
            db = new NVEntities();
        }

        public OfertasPermitidasFamiliaController(NVEntities context)
        {
            db = context;
        }

        [HttpGet]
        [Route("api/OfertasPermitidasFamilia")]
        [ResponseType(typeof(List<OfertaPermitidaFamiliaDTO>))]
        public async Task<IHttpActionResult> GetOfertasPermitidasFamilia(string empresa)
        {
            string empresaPadded = empresa.PadRight(3);
            var ofertas = await db.OfertasPermitidas
                .Where(o => o.Empresa == empresaPadded
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null)
                .OrderBy(o => o.NºOrden)
                .ToListAsync()
                .ConfigureAwait(false);

            // Cargar familias para enriquecer descripción
            var familiaIds = ofertas.Select(o => o.Familia).Distinct().ToList();
            var familias = await db.Familias
                .Where(f => f.Empresa == empresaPadded && familiaIds.Contains(f.Número))
                .ToDictionaryAsync(f => f.Número, f => f.Descripción)
                .ConfigureAwait(false);

            var dtos = ofertas.Select(o => MapToDTO(o, familias)).ToList();
            return Ok(dtos);
        }

        [HttpGet]
        [Route("api/OfertasPermitidasFamilia/{nOrden:int}")]
        [ResponseType(typeof(OfertaPermitidaFamiliaDTO))]
        public async Task<IHttpActionResult> GetOfertaPermitidaFamilia(int nOrden)
        {
            var oferta = await db.OfertasPermitidas
                .FirstOrDefaultAsync(o => o.NºOrden == nOrden
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            var familias = await db.Familias
                .Where(f => f.Empresa == oferta.Empresa && f.Número == oferta.Familia)
                .ToDictionaryAsync(f => f.Número, f => f.Descripción)
                .ConfigureAwait(false);

            return Ok(MapToDTO(oferta, familias));
        }

        [HttpPost]
        [Route("api/OfertasPermitidasFamilia")]
        [ResponseType(typeof(OfertaPermitidaFamiliaDTO))]
        public async Task<IHttpActionResult> PostOfertaPermitidaFamilia([FromBody] OfertaPermitidaFamiliaCreateDTO dto, [FromUri] string usuario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var error = ValidarDTO(dto);
            if (error != null)
            {
                return BadRequest(error);
            }

            string empresaPadded = dto.Empresa.PadRight(3);

            // Validar que la familia existe. NestoAPI#481: se busca sin distinguir mayúsculas
            // (SQL ya lo hacía) y se graba el Número tal como está en Familias, no como lo tecleó
            // el usuario ("staleks" → "Staleks"): así lo que se ve en la pantalla de ofertas y en
            // los informes es siempre el nombre canónico de la familia.
            string familiaBuscada = dto.Familia.Trim().ToUpper();
            string familia = await db.Familias
                .Where(f => f.Empresa == empresaPadded && f.Número.Trim().ToUpper() == familiaBuscada)
                .Select(f => f.Número)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (familia == null)
            {
                return BadRequest($"La familia '{dto.Familia}' no existe en la empresa '{dto.Empresa}'");
            }

            // Validar que no existe ya una oferta con misma Familia + FiltroProducto
            string filtro = dto.FiltroProducto?.Trim();
            var duplicada = await db.OfertasPermitidas
                .AnyAsync(o => o.Empresa == empresaPadded
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia == familia
                    && (filtro == null || filtro == ""
                        ? (o.FiltroProducto == null || o.FiltroProducto.Trim() == "")
                        : o.FiltroProducto == filtro))
                .ConfigureAwait(false);

            if (duplicada)
            {
                return BadRequest($"Ya existe una oferta para la familia '{dto.Familia}' con el mismo filtro");
            }

            var oferta = new OfertaPermitida
            {
                Empresa = empresaPadded,
                Familia = familia,
                CantidadConPrecio = dto.CantidadConPrecio,
                CantidadRegalo = dto.CantidadRegalo,
                FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim(),
                Denegar = false,
                Usuario = UsuarioAuditoriaHelper.Resolver(User, usuario),
                FechaModificación = DateTime.Now
            };

            db.OfertasPermitidas.Add(oferta);
            await db.SaveChangesAsync().ConfigureAwait(false);

            var familias = await db.Familias
                .Where(f => f.Empresa == empresaPadded && f.Número == familia)
                .ToDictionaryAsync(f => f.Número, f => f.Descripción)
                .ConfigureAwait(false);

            return Ok(MapToDTO(oferta, familias));
        }

        [HttpPut]
        [Route("api/OfertasPermitidasFamilia/{nOrden:int}")]
        [ResponseType(typeof(OfertaPermitidaFamiliaDTO))]
        public async Task<IHttpActionResult> PutOfertaPermitidaFamilia(int nOrden, [FromBody] OfertaPermitidaFamiliaCreateDTO dto, [FromUri] string usuario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var error = ValidarDTO(dto);
            if (error != null)
            {
                return BadRequest(error);
            }

            var oferta = await db.OfertasPermitidas
                .FirstOrDefaultAsync(o => o.NºOrden == nOrden
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            string empresaPadded = dto.Empresa.PadRight(3);

            // Validar que la familia existe. NestoAPI#481: se busca sin distinguir mayúsculas
            // (SQL ya lo hacía) y se graba el Número tal como está en Familias, no como lo tecleó
            // el usuario ("staleks" → "Staleks"): así lo que se ve en la pantalla de ofertas y en
            // los informes es siempre el nombre canónico de la familia.
            string familiaBuscada = dto.Familia.Trim().ToUpper();
            string familia = await db.Familias
                .Where(f => f.Empresa == empresaPadded && f.Número.Trim().ToUpper() == familiaBuscada)
                .Select(f => f.Número)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (familia == null)
            {
                return BadRequest($"La familia '{dto.Familia}' no existe en la empresa '{dto.Empresa}'");
            }

            // Validar duplicado (excluyendo el registro actual)
            string filtro = dto.FiltroProducto?.Trim();
            var duplicada = await db.OfertasPermitidas
                .AnyAsync(o => o.Empresa == empresaPadded
                    && o.NºOrden != nOrden
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia == familia
                    && (filtro == null || filtro == ""
                        ? (o.FiltroProducto == null || o.FiltroProducto.Trim() == "")
                        : o.FiltroProducto == filtro))
                .ConfigureAwait(false);

            if (duplicada)
            {
                return BadRequest($"Ya existe una oferta para la familia '{dto.Familia}' con el mismo filtro");
            }

            oferta.Familia = familia;
            oferta.CantidadConPrecio = dto.CantidadConPrecio;
            oferta.CantidadRegalo = dto.CantidadRegalo;
            oferta.FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim();
            oferta.Usuario = UsuarioAuditoriaHelper.Resolver(User, usuario);
            oferta.FechaModificación = DateTime.Now;

            await db.SaveChangesAsync().ConfigureAwait(false);

            var familias = await db.Familias
                .Where(f => f.Empresa == empresaPadded && f.Número == familia)
                .ToDictionaryAsync(f => f.Número, f => f.Descripción)
                .ConfigureAwait(false);

            return Ok(MapToDTO(oferta, familias));
        }

        [HttpDelete]
        [Route("api/OfertasPermitidasFamilia/{nOrden:int}")]
        [ResponseType(typeof(OfertaPermitidaFamiliaDTO))]
        public async Task<IHttpActionResult> DeleteOfertaPermitidaFamilia(int nOrden)
        {
            var oferta = await db.OfertasPermitidas
                .FirstOrDefaultAsync(o => o.NºOrden == nOrden
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia != null)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            var dto = MapToDTO(oferta, new Dictionary<string, string>());
            db.OfertasPermitidas.Remove(oferta);
            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(dto);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private string ValidarDTO(OfertaPermitidaFamiliaCreateDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Familia))
            {
                return "La familia es obligatoria";
            }

            if (dto.CantidadConPrecio < 1)
            {
                return "La cantidad con precio debe ser al menos 1";
            }

            if (dto.CantidadRegalo < 1)
            {
                return "La cantidad de regalo debe ser al menos 1";
            }

            return null;
        }

        private OfertaPermitidaFamiliaDTO MapToDTO(OfertaPermitida oferta, Dictionary<string, string> familias)
        {
            string descripcionFamilia = null;
            if (oferta.Familia != null && familias.ContainsKey(oferta.Familia))
            {
                descripcionFamilia = familias[oferta.Familia]?.Trim();
            }

            return new OfertaPermitidaFamiliaDTO
            {
                NOrden = oferta.NºOrden,
                Empresa = oferta.Empresa?.Trim(),
                Familia = oferta.Familia?.Trim(),
                FamiliaDescripcion = descripcionFamilia,
                CantidadConPrecio = oferta.CantidadConPrecio,
                CantidadRegalo = oferta.CantidadRegalo,
                FiltroProducto = oferta.FiltroProducto?.Trim(),
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificación
            };
        }
    }
}
