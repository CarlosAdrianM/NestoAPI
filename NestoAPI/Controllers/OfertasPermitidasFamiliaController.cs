using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.OfertasAutorizadas;
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

            // NestoAPI#564: un Nesto antiguo no manda SubGrupo ni Denegar → toda la familia y autorización.
            string subGrupo = NormalizarSubGrupo(dto.SubGrupo);
            bool denegar = dto.Denegar ?? false;

            var errorSubGrupo = await ValidarSubGrupo(empresaPadded, subGrupo).ConfigureAwait(false);
            if (errorSubGrupo != null)
            {
                return BadRequest(errorSubGrupo);
            }

            // Validar que no existe ya una oferta con misma Familia + FiltroProducto + SubGrupo + Denegar
            string filtro = dto.FiltroProducto?.Trim();
            if (await ExisteDuplicada(empresaPadded, null, familia, filtro, subGrupo, denegar).ConfigureAwait(false))
            {
                return BadRequest(MensajeDuplicada(dto.Familia, denegar));
            }

            var oferta = new OfertaPermitida
            {
                Empresa = empresaPadded,
                Familia = familia,
                CantidadConPrecio = dto.CantidadConPrecio,
                CantidadRegalo = dto.CantidadRegalo,
                FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim(),
                SubGrupo = subGrupo,
                Denegar = denegar,
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

            // NestoAPI#564: si el cliente no manda SubGrupo/Denegar (Nesto antiguo) se conserva lo
            // que ya tenga la fila; así editar la cantidad no convierte una denegación en una
            // autorización ni la extiende a toda la familia. SubGrupo = "" sí lo quita.
            string subGrupo = dto.SubGrupo == null ? NormalizarSubGrupo(oferta.SubGrupo) : NormalizarSubGrupo(dto.SubGrupo);
            bool denegar = dto.Denegar ?? oferta.Denegar;

            var errorSubGrupo = await ValidarSubGrupo(empresaPadded, subGrupo).ConfigureAwait(false);
            if (errorSubGrupo != null)
            {
                return BadRequest(errorSubGrupo);
            }

            // Validar duplicado (excluyendo el registro actual)
            string filtro = dto.FiltroProducto?.Trim();
            if (await ExisteDuplicada(empresaPadded, nOrden, familia, filtro, subGrupo, denegar).ConfigureAwait(false))
            {
                return BadRequest(MensajeDuplicada(dto.Familia, denegar));
            }

            oferta.Familia = familia;
            oferta.CantidadConPrecio = dto.CantidadConPrecio;
            oferta.CantidadRegalo = dto.CantidadRegalo;
            oferta.FiltroProducto = string.IsNullOrWhiteSpace(dto.FiltroProducto) ? null : dto.FiltroProducto.Trim();
            oferta.SubGrupo = subGrupo;
            oferta.Denegar = denegar;
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

            if (dto.SubGrupo != null && dto.SubGrupo.Trim().Length > 3)
            {
                return $"El subgrupo '{dto.SubGrupo.Trim()}' no es válido: tiene como máximo 3 caracteres";
            }

            return null;
        }

        // NestoAPI#564: el subgrupo se guarda recortado y en mayúsculas; vacío = toda la familia.
        internal static string NormalizarSubGrupo(string subGrupo)
        {
            return string.IsNullOrWhiteSpace(subGrupo) ? null : subGrupo.Trim().ToUpper();
        }

        // NestoAPI#564: el subgrupo tiene que existir en SubGruposProducto. Se busca en cualquier grupo
        // porque la regla no guarda el grupo (el filtro compara solo con Productos.SubGrupo).
        private async Task<string> ValidarSubGrupo(string empresaPadded, string subGrupo)
        {
            if (subGrupo == null)
            {
                return null;
            }

            bool existe = await db.SubGruposProductoes
                .AnyAsync(s => s.Empresa == empresaPadded && s.Número.Trim().ToUpper() == subGrupo)
                .ConfigureAwait(false);

            return existe ? null : $"El subgrupo '{subGrupo}' no existe en la empresa '{empresaPadded.Trim()}'";
        }

        // NestoAPI#564: la autorización y la denegación de una misma familia (p. ej. Genéricos 6+1 y
        // Genéricos + DES 6+1 Denegar) conviven, así que el duplicado compara también SubGrupo y Denegar.
        private Task<bool> ExisteDuplicada(string empresaPadded, int? nOrdenExcluido, string familia, string filtro, string subGrupo, bool denegar)
        {
            return db.OfertasPermitidas
                .AnyAsync(o => o.Empresa == empresaPadded
                    && (nOrdenExcluido == null || o.NºOrden != nOrdenExcluido)
                    && o.Cliente == null
                    && o.Número == null
                    && o.Familia == familia
                    && (filtro == null || filtro == ""
                        ? (o.FiltroProducto == null || o.FiltroProducto.Trim() == "")
                        : o.FiltroProducto == filtro)
                    && (subGrupo == null
                        ? (o.SubGrupo == null || o.SubGrupo.Trim() == "")
                        : (o.SubGrupo != null && o.SubGrupo.Trim() == subGrupo))
                    && o.Denegar == denegar);
        }

        private static string MensajeDuplicada(string familia, bool denegar)
        {
            string tipo = denegar ? "denegación" : "oferta";
            return $"Ya existe una {tipo} para la familia '{familia}' con el mismo filtro y subgrupo";
        }

        private static OfertaPermitidaFamiliaDTO MapToDTO(OfertaPermitida oferta, Dictionary<string, string> familias) => MapeadorOfertas.Familia(oferta, familias);
    }
}
