using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.OfertasEscalonadas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    // Endpoint nuevo sin llamantes legacy: puede llevar [Authorize] desde el primer día (su único
    // cliente es el módulo de Ofertas Combinadas de Nesto, que ya manda JWT). Sin token, el
    // usuario de auditoría caería en el fallback del parámetro y grabaría el machine account.
    [Authorize]
    public class OfertasEscalonadasController : ApiController
    {
        private NVEntities db;

        public OfertasEscalonadasController()
        {
            db = new NVEntities();
        }

        public OfertasEscalonadasController(NVEntities context)
        {
            db = context;
        }

        [HttpGet]
        [Route("api/OfertasEscalonadas")]
        [ResponseType(typeof(List<OfertaEscalonadaDTO>))]
        public async Task<IHttpActionResult> GetOfertasEscalonadas(string empresa, bool soloActivas = false)
        {
            string empresaPadded = empresa.PadRight(3);
            var query = db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasProductos.Producto1")
                .Include("OfertasEscalonadasTramos")
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
        [Route("api/OfertasEscalonadas/{id:int}")]
        [ResponseType(typeof(OfertaEscalonadaDTO))]
        public async Task<IHttpActionResult> GetOfertaEscalonada(int id)
        {
            var oferta = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasProductos.Producto1")
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            return Ok(MapToDTO(oferta));
        }

        [HttpPost]
        [Route("api/OfertasEscalonadas")]
        [ResponseType(typeof(OfertaEscalonadaDTO))]
        public async Task<IHttpActionResult> PostOfertaEscalonada([FromBody] OfertaEscalonadaCreateDTO dto, [FromUri] string usuario)
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

            var productosFicha = await CargarProductosFicha(dto, empresaPadded).ConfigureAwait(false);
            if (productosFicha == null)
            {
                return BadRequest(_errorProductoInexistente);
            }

            // El usuario de auditoría se toma del Identity autenticado, no del parámetro (ver UsuarioAuditoriaHelper).
            string usuarioAuditoria = UsuarioAuditoriaHelper.Resolver(User, usuario);

            var ahora = DateTime.Now;
            var oferta = new OfertaEscalonada
            {
                Empresa = empresaPadded,
                Nombre = dto.Nombre,
                FechaDesde = dto.FechaDesde,
                FechaHasta = dto.FechaHasta,
                Usuario = usuarioAuditoria,
                FechaModificacion = ahora
            };

            foreach (var prodDTO in dto.Productos)
            {
                oferta.OfertasEscalonadasProductos.Add(new OfertaEscalonadaProducto
                {
                    Empresa = empresaPadded,
                    Producto = prodDTO.Producto,
                    PrecioBase = PrecioBaseDefinitivo(prodDTO, productosFicha),
                    Usuario = usuarioAuditoria,
                    FechaModificacion = ahora
                });
            }

            foreach (var tramoDTO in dto.Tramos)
            {
                oferta.OfertasEscalonadasTramos.Add(new OfertaEscalonadaTramo
                {
                    CantidadMinima = tramoDTO.CantidadMinima,
                    Descuento = tramoDTO.Descuento,
                    Usuario = usuarioAuditoria,
                    FechaModificacion = ahora
                });
            }

            db.OfertasEscalonadas.Add(oferta);
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Recargar con navegaciones para MapToDTO
            var ofertaCreada = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasProductos.Producto1")
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == oferta.Id)
                .ConfigureAwait(false);

            return Ok(MapToDTO(ofertaCreada));
        }

        [HttpPut]
        [Route("api/OfertasEscalonadas/{id:int}")]
        [ResponseType(typeof(OfertaEscalonadaDTO))]
        public async Task<IHttpActionResult> PutOfertaEscalonada(int id, [FromBody] OfertaEscalonadaCreateDTO dto, [FromUri] string usuario)
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

            var oferta = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            string empresaPadded = dto.Empresa.PadRight(3);

            var productosFicha = await CargarProductosFicha(dto, empresaPadded).ConfigureAwait(false);
            if (productosFicha == null)
            {
                return BadRequest(_errorProductoInexistente);
            }

            // El usuario de auditoría se toma del Identity autenticado, no del parámetro (ver UsuarioAuditoriaHelper).
            string usuarioAuditoria = UsuarioAuditoriaHelper.Resolver(User, usuario);

            var ahora = DateTime.Now;

            // Actualizar cabecera
            oferta.Nombre = dto.Nombre;
            oferta.FechaDesde = dto.FechaDesde;
            oferta.FechaHasta = dto.FechaHasta;
            oferta.Usuario = usuarioAuditoria;
            oferta.FechaModificacion = ahora;

            // Productos: actualizar existentes, crear nuevos, eliminar los que no vienen
            var idsProductosEnRequest = dto.Productos.Where(p => p.Id > 0).Select(p => p.Id).ToList();
            var productosAEliminar = oferta.OfertasEscalonadasProductos
                .Where(p => !idsProductosEnRequest.Contains(p.Id))
                .ToList();
            foreach (var prod in productosAEliminar)
            {
                db.OfertasEscalonadasProductos.Remove(prod);
            }

            foreach (var prodDTO in dto.Productos)
            {
                if (prodDTO.Id > 0)
                {
                    var prodExistente = oferta.OfertasEscalonadasProductos
                        .FirstOrDefault(p => p.Id == prodDTO.Id);
                    if (prodExistente != null)
                    {
                        prodExistente.Producto = prodDTO.Producto;
                        prodExistente.PrecioBase = PrecioBaseDefinitivo(prodDTO, productosFicha);
                        prodExistente.Usuario = usuarioAuditoria;
                        prodExistente.FechaModificacion = ahora;
                    }
                }
                else
                {
                    db.OfertasEscalonadasProductos.Add(new OfertaEscalonadaProducto
                    {
                        Empresa = empresaPadded,
                        OfertaId = oferta.Id,
                        Producto = prodDTO.Producto,
                        PrecioBase = PrecioBaseDefinitivo(prodDTO, productosFicha),
                        Usuario = usuarioAuditoria,
                        FechaModificacion = ahora
                    });
                }
            }

            // Tramos: misma estrategia
            var idsTramosEnRequest = dto.Tramos.Where(t => t.Id > 0).Select(t => t.Id).ToList();
            var tramosAEliminar = oferta.OfertasEscalonadasTramos
                .Where(t => !idsTramosEnRequest.Contains(t.Id))
                .ToList();
            foreach (var tramo in tramosAEliminar)
            {
                db.OfertasEscalonadasTramos.Remove(tramo);
            }

            foreach (var tramoDTO in dto.Tramos)
            {
                if (tramoDTO.Id > 0)
                {
                    var tramoExistente = oferta.OfertasEscalonadasTramos
                        .FirstOrDefault(t => t.Id == tramoDTO.Id);
                    if (tramoExistente != null)
                    {
                        tramoExistente.CantidadMinima = tramoDTO.CantidadMinima;
                        tramoExistente.Descuento = tramoDTO.Descuento;
                        tramoExistente.Usuario = usuarioAuditoria;
                        tramoExistente.FechaModificacion = ahora;
                    }
                }
                else
                {
                    db.OfertasEscalonadasTramos.Add(new OfertaEscalonadaTramo
                    {
                        OfertaId = oferta.Id,
                        CantidadMinima = tramoDTO.CantidadMinima,
                        Descuento = tramoDTO.Descuento,
                        Usuario = usuarioAuditoria,
                        FechaModificacion = ahora
                    });
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Recargar con navegaciones para MapToDTO
            var ofertaActualizada = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasProductos.Producto1")
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            return Ok(MapToDTO(ofertaActualizada));
        }

        [HttpDelete]
        [Route("api/OfertasEscalonadas/{id:int}")]
        [ResponseType(typeof(OfertaEscalonadaDTO))]
        public async Task<IHttpActionResult> DeleteOfertaEscalonada(int id)
        {
            var oferta = await db.OfertasEscalonadas
                .Include("OfertasEscalonadasProductos")
                .Include("OfertasEscalonadasTramos")
                .FirstOrDefaultAsync(o => o.Id == id)
                .ConfigureAwait(false);

            if (oferta == null)
            {
                return NotFound();
            }

            var dto = MapToDTO(oferta);

            // Eliminar hijos primero
            foreach (var producto in oferta.OfertasEscalonadasProductos.ToList())
            {
                db.OfertasEscalonadasProductos.Remove(producto);
            }
            foreach (var tramo in oferta.OfertasEscalonadasTramos.ToList())
            {
                db.OfertasEscalonadasTramos.Remove(tramo);
            }
            db.OfertasEscalonadas.Remove(oferta);

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

        private string _errorProductoInexistente;

        /// <summary>
        /// Carga la ficha de los productos del DTO (para validar que existen y precargar el PVP
        /// cuando no viene precio). Devuelve null si algún producto no existe, dejando el mensaje
        /// en <see cref="_errorProductoInexistente"/>.
        /// </summary>
        private async Task<List<Producto>> CargarProductosFicha(OfertaEscalonadaCreateDTO dto, string empresaPadded)
        {
            var productosIds = dto.Productos.Select(p => p.Producto).ToList();
            var productosExistentes = await db.Productos
                .Where(p => p.Empresa == empresaPadded && productosIds.Contains(p.Número))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var productoId in productosIds)
            {
                if (!productosExistentes.Any(p => p.Número.Trim() == productoId.Trim()))
                {
                    _errorProductoInexistente = $"El producto '{productoId}' no existe en la empresa '{dto.Empresa.Trim()}'";
                    return null;
                }
            }

            return productosExistentes;
        }

        private static decimal PrecioBaseDefinitivo(OfertaEscalonadaProductoCreateDTO prodDTO, List<Producto> productosFicha)
        {
            if (prodDTO.PrecioBase.HasValue)
            {
                return prodDTO.PrecioBase.Value;
            }
            var ficha = productosFicha.First(p => p.Número.Trim() == prodDTO.Producto.Trim());
            return ficha.PVP ?? 0;
        }

        internal static string ValidarDTO(OfertaEscalonadaCreateDTO dto)
        {
            if (dto.Productos == null || dto.Productos.Count == 0)
            {
                return "Una oferta escalonada debe tener al menos un producto";
            }

            if (dto.Tramos == null || dto.Tramos.Count == 0)
            {
                return "Una oferta escalonada debe tener al menos un tramo";
            }

            if (dto.FechaDesde.HasValue && dto.FechaHasta.HasValue && dto.FechaDesde > dto.FechaHasta)
            {
                return "La fecha desde no puede ser posterior a la fecha hasta";
            }

            var productosRepetidos = dto.Productos
                .GroupBy(p => p.Producto?.Trim())
                .FirstOrDefault(g => g.Count() > 1);
            if (productosRepetidos != null)
            {
                return $"El producto '{productosRepetidos.Key}' está repetido en la oferta";
            }

            foreach (var prod in dto.Productos)
            {
                if (prod.PrecioBase.HasValue && prod.PrecioBase < 0)
                {
                    return $"El precio base del producto '{prod.Producto}' no puede ser negativo";
                }
            }

            foreach (var tramo in dto.Tramos)
            {
                if (tramo.CantidadMinima <= 0)
                {
                    return "Las cantidades mínimas de los tramos deben ser mayores que cero";
                }
                if (tramo.Descuento <= 0 || tramo.Descuento > 1)
                {
                    return "Los descuentos de los tramos deben estar entre 0 y 1 (en tanto por uno)";
                }
            }

            var cantidadesRepetidas = dto.Tramos
                .GroupBy(t => t.CantidadMinima)
                .Any(g => g.Count() > 1);
            if (cantidadesRepetidas)
            {
                return "No puede haber dos tramos con la misma cantidad mínima";
            }

            // A más cantidad, más descuento: un escalado no monótono casi siempre es un error de
            // tecleo y daría tramos inalcanzables en la validación de pedidos.
            var tramosOrdenados = dto.Tramos.OrderBy(t => t.CantidadMinima).ToList();
            for (int i = 1; i < tramosOrdenados.Count; i++)
            {
                if (tramosOrdenados[i].Descuento <= tramosOrdenados[i - 1].Descuento)
                {
                    return "El descuento debe ser mayor cuanto mayor sea la cantidad mínima del tramo";
                }
            }

            return null;
        }

        private OfertaEscalonadaDTO MapToDTO(OfertaEscalonada oferta)
        {
            return new OfertaEscalonadaDTO
            {
                Id = oferta.Id,
                Empresa = oferta.Empresa?.Trim(),
                Nombre = oferta.Nombre?.Trim(),
                FechaDesde = oferta.FechaDesde,
                FechaHasta = oferta.FechaHasta,
                Usuario = oferta.Usuario?.Trim(),
                FechaModificacion = oferta.FechaModificacion,
                Productos = oferta.OfertasEscalonadasProductos?.Select(p => new OfertaEscalonadaProductoDTO
                {
                    Id = p.Id,
                    Producto = p.Producto?.Trim(),
                    ProductoNombre = p.Producto1?.Nombre?.Trim(),
                    PrecioBase = p.PrecioBase
                }).ToList() ?? new List<OfertaEscalonadaProductoDTO>(),
                Tramos = oferta.OfertasEscalonadasTramos?
                    .OrderBy(t => t.CantidadMinima)
                    .Select(t => new OfertaEscalonadaTramoDTO
                    {
                        Id = t.Id,
                        CantidadMinima = t.CantidadMinima,
                        Descuento = t.Descuento
                    }).ToList() ?? new List<OfertaEscalonadaTramoDTO>()
            };
        }
    }
}
