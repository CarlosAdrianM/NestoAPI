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
    public class OfertasCombinadasController : ApiController
    {
        private NVEntities db;

        public OfertasCombinadasController()
        {
            db = new NVEntities();
        }

        public OfertasCombinadasController(NVEntities context)
        {
            db = context;
        }

        [HttpGet]
        [Route("api/OfertasCombinadas")]
        [ResponseType(typeof(List<OfertaCombinadaDTO>))]
        public async Task<IHttpActionResult> GetOfertasCombinadas(string empresa, bool soloActivas = false)
        {
            string empresaPadded = empresa.PadRight(3);
            var query = db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .Include("OfertasCombinadasDetalles.Producto1")
                .Where(o => o.Empresa == empresaPadded);

            if (soloActivas)
            {
                var hoy = DateTime.Today;
                query = query.Where(o =>
                    (o.FechaDesde == null || o.FechaDesde <= hoy) &&
                    (o.FechaHasta == null || o.FechaHasta >= hoy));
            }

            var ofertas = await query
                .OrderBy(o => o.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var dtos = ofertas.Select(o => MapToDTO(o)).ToList();
            return Ok(dtos);
        }

        [HttpGet]
        [Route("api/OfertasCombinadas/{id:int}")]
        [ResponseType(typeof(OfertaCombinadaDTO))]
        public async Task<IHttpActionResult> GetOfertaCombinada(int id)
        {
            var oferta = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .Include("OfertasCombinadasDetalles.Producto1")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            return Ok(MapToDTO(oferta));
        }

        [HttpPost]
        [Route("api/OfertasCombinadas")]
        [ResponseType(typeof(OfertaCombinadaDTO))]
        public async Task<IHttpActionResult> PostOfertaCombinada([FromBody] OfertaCombinadaCreateDTO dto, [FromUri] string usuario)
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

            // Validar que todos los productos existen
            var productosIds = dto.Detalles.Select(d => d.Producto).ToList();
            var productosExistentes = await db.Productos
                .Where(p => p.Empresa == empresaPadded && productosIds.Contains(p.Número))
                .Select(p => p.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var productoId in productosIds)
            {
                if (!productosExistentes.Any(p => p.Trim() == productoId.Trim()))
                {
                    return BadRequest($"El producto '{productoId}' no existe en la empresa '{dto.Empresa}'");
                }
            }

            var ahora = DateTime.Now;
            var oferta = new OfertaCombinada
            {
                Empresa = empresaPadded,
                Nombre = dto.Nombre,
                ImporteMinimo = dto.ImporteMinimo,
                FechaDesde = dto.FechaDesde,
                FechaHasta = dto.FechaHasta,
                Usuario = usuario,
                FechaModificacion = ahora
            };

            foreach (var detDTO in dto.Detalles)
            {
                oferta.OfertasCombinadasDetalles.Add(new OfertaCombinadaDetalle
                {
                    Empresa = empresaPadded,
                    Producto = detDTO.Producto,
                    Cantidad = detDTO.Cantidad,
                    Precio = detDTO.Precio,
                    Usuario = usuario,
                    FechaModificacion = ahora
                });
            }

            db.OfertasCombinadas.Add(oferta);
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Recargar con navegaciones para MapToDTO
            var ofertaCreada = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .Include("OfertasCombinadasDetalles.Producto1")
                .FirstOrDefaultAsync(o => o.Id == oferta.Id)
                .ConfigureAwait(false);

            return Ok(MapToDTO(ofertaCreada));
        }

        [HttpPut]
        [Route("api/OfertasCombinadas/{id:int}")]
        [ResponseType(typeof(OfertaCombinadaDTO))]
        public async Task<IHttpActionResult> PutOfertaCombinada(int id, [FromBody] OfertaCombinadaCreateDTO dto, [FromUri] string usuario)
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

            var oferta = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            string empresaPadded = dto.Empresa.PadRight(3);

            // Validar que todos los productos existen
            var productosIds = dto.Detalles.Select(d => d.Producto).ToList();
            var productosExistentes = await db.Productos
                .Where(p => p.Empresa == empresaPadded && productosIds.Contains(p.Número))
                .Select(p => p.Número)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var productoId in productosIds)
            {
                if (!productosExistentes.Any(p => p.Trim() == productoId.Trim()))
                {
                    return BadRequest($"El producto '{productoId}' no existe en la empresa '{dto.Empresa}'");
                }
            }

            var ahora = DateTime.Now;

            // Actualizar cabecera
            oferta.Nombre = dto.Nombre;
            oferta.ImporteMinimo = dto.ImporteMinimo;
            oferta.FechaDesde = dto.FechaDesde;
            oferta.FechaHasta = dto.FechaHasta;
            oferta.Usuario = usuario;
            oferta.FechaModificacion = ahora;

            // Gestionar detalles: actualizar existentes, crear nuevos, eliminar los que no vienen
            var idsEnRequest = dto.Detalles.Where(d => d.Id > 0).Select(d => d.Id).ToList();

            // Eliminar detalles que no vienen en el request
            var detallesAEliminar = oferta.OfertasCombinadasDetalles
                .Where(d => !idsEnRequest.Contains(d.Id))
                .ToList();
            foreach (var det in detallesAEliminar)
            {
                db.OfertasCombinadasDetalles.Remove(det);
            }

            // Actualizar existentes y crear nuevos
            foreach (var detDTO in dto.Detalles)
            {
                if (detDTO.Id > 0)
                {
                    // Actualizar existente
                    var detExistente = oferta.OfertasCombinadasDetalles
                        .FirstOrDefault(d => d.Id == detDTO.Id);
                    if (detExistente != null)
                    {
                        detExistente.Producto = detDTO.Producto;
                        detExistente.Cantidad = detDTO.Cantidad;
                        detExistente.Precio = detDTO.Precio;
                        detExistente.Usuario = usuario;
                        detExistente.FechaModificacion = ahora;
                    }
                }
                else
                {
                    // Crear nuevo
                    db.OfertasCombinadasDetalles.Add(new OfertaCombinadaDetalle
                    {
                        Empresa = empresaPadded,
                        OfertaId = oferta.Id,
                        Producto = detDTO.Producto,
                        Cantidad = detDTO.Cantidad,
                        Precio = detDTO.Precio,
                        Usuario = usuario,
                        FechaModificacion = ahora
                    });
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Recargar con navegaciones para MapToDTO
            var ofertaActualizada = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .Include("OfertasCombinadasDetalles.Producto1")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            return Ok(MapToDTO(ofertaActualizada));
        }

        [HttpDelete]
        [Route("api/OfertasCombinadas/{id:int}")]
        [ResponseType(typeof(OfertaCombinadaDTO))]
        public async Task<IHttpActionResult> DeleteOfertaCombinada(int id)
        {
            var oferta = await db.OfertasCombinadas
                .Include("OfertasCombinadasDetalles")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            var dto = MapToDTO(oferta);

            // Eliminar detalles primero
            foreach (var detalle in oferta.OfertasCombinadasDetalles.ToList())
            {
                db.OfertasCombinadasDetalles.Remove(detalle);
            }
            db.OfertasCombinadas.Remove(oferta);

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

        private string ValidarDTO(OfertaCombinadaCreateDTO dto)
        {
            if (dto.Detalles == null || dto.Detalles.Count < 2)
            {
                return "Una oferta combinada debe tener al menos 2 productos";
            }

            var productosDuplicados = dto.Detalles
                .GroupBy(d => d.Producto?.Trim())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (productosDuplicados.Any())
            {
                return $"Producto duplicado en la oferta: '{productosDuplicados.First()}'";
            }

            if (dto.FechaDesde.HasValue && dto.FechaHasta.HasValue && dto.FechaDesde > dto.FechaHasta)
            {
                return "La fecha desde no puede ser posterior a la fecha hasta";
            }

            foreach (var det in dto.Detalles)
            {
                if (det.Precio < 0)
                {
                    return $"El precio del producto '{det.Producto}' no puede ser negativo";
                }
                if (det.Cantidad < 0)
                {
                    return $"La cantidad del producto '{det.Producto}' no puede ser negativa";
                }
            }

            return null;
        }

        private OfertaCombinadaDTO MapToDTO(OfertaCombinada oferta)
        {
            return new OfertaCombinadaDTO
            {
                Id = oferta.Id,
                Empresa = oferta.Empresa?.Trim(),
                Nombre = oferta.Nombre?.Trim(),
                ImporteMinimo = oferta.ImporteMinimo,
                FechaDesde = oferta.FechaDesde,
                FechaHasta = oferta.FechaHasta,
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificacion,
                Detalles = oferta.OfertasCombinadasDetalles?.Select(d => new OfertaCombinadaDetalleDTO
                {
                    Id = d.Id,
                    Producto = d.Producto?.Trim(),
                    ProductoNombre = d.Producto1?.Nombre?.Trim(),
                    Cantidad = d.Cantidad,
                    Precio = d.Precio
                }).ToList() ?? new List<OfertaCombinadaDetalleDTO>()
            };
        }
    }
}
