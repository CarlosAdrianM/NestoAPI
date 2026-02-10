using NestoAPI.Models;
using NestoAPI.Models.Ganavisiones;
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
    /// Controlador para gestionar los puntos Ganavisiones asignados a productos.
    /// Los Ganavisiones determinan el importe maximo bonificable para cada producto.
    /// Issue #94: Sistema Ganavisiones - Backend
    /// </summary>
    public class GanavisionesController : ApiController
    {
        private NVEntities db;

        public GanavisionesController()
        {
            db = new NVEntities();
        }

        public GanavisionesController(NVEntities context)
        {
            db = context;
        }

        /// <summary>
        /// Obtiene los registros de Ganavisiones filtrados por empresa y opcionalmente por producto.
        /// </summary>
        /// <param name="empresa">Codigo de empresa</param>
        /// <param name="productoId">ID del producto (opcional)</param>
        /// <param name="soloActivos">Si es true, solo devuelve registros vigentes (FechaDesde <= hoy y FechaHasta == null o >= hoy)</param>
        /// <returns>Lista de GanavisionDTO</returns>
        [HttpGet]
        [Route("api/Ganavisiones")]
        [ResponseType(typeof(List<GanavisionDTO>))]
        public async Task<IHttpActionResult> GetGanavisiones(string empresa, string productoId = null, bool soloActivos = false)
        {
            var query = db.Ganavisiones.AsQueryable();

            // Filtrar por empresa (con padding para comparar con CHAR(3))
            string empresaPadded = empresa.PadRight(3);
            query = query.Where(g => g.Empresa == empresaPadded);

            // Filtrar por producto si se especifica
            if (!string.IsNullOrEmpty(productoId))
            {
                query = query.Where(g => g.ProductoId == productoId);
            }

            // Filtrar solo activos si se solicita
            if (soloActivos)
            {
                var hoy = DateTime.Today;
                query = query.Where(g => g.FechaDesde <= hoy && (g.FechaHasta == null || g.FechaHasta >= hoy));
            }

            var ganavisiones = await query
                .OrderBy(g => g.Id)
                .ToListAsync()
                .ConfigureAwait(false);

            var dtos = ganavisiones.Select(g => MapToDTO(g)).ToList();

            return Ok(dtos);
        }

        /// <summary>
        /// Obtiene un registro de Ganavision por su ID.
        /// </summary>
        /// <param name="id">ID del registro</param>
        /// <returns>GanavisionDTO</returns>
        [HttpGet]
        [Route("api/Ganavisiones/{id:int}")]
        [ResponseType(typeof(GanavisionDTO))]
        public async Task<IHttpActionResult> GetGanavision(int id)
        {
            var ganavision = await db.Ganavisiones.FindAsync(id).ConfigureAwait(false);

            if (ganavision == null)
            {
                return NotFound();
            }

            return Ok(MapToDTO(ganavision));
        }

        /// <summary>
        /// Obtiene los Ganavisiones activos para un producto especifico.
        /// Util para validaciones en tiempo real durante la creacion de pedidos.
        /// </summary>
        /// <param name="empresa">Codigo de empresa</param>
        /// <param name="productoId">ID del producto</param>
        /// <returns>Numero de Ganavisiones o null si no tiene</returns>
        [HttpGet]
        [Route("api/Ganavisiones/Activo")]
        [ResponseType(typeof(int?))]
        public async Task<IHttpActionResult> GetGanavisionesActivoProducto(string empresa, string productoId)
        {
            string empresaPadded = empresa.PadRight(3);
            var hoy = DateTime.Today;

            var ganavision = await db.Ganavisiones
                .Where(g => g.Empresa == empresaPadded &&
                            g.ProductoId == productoId &&
                            g.FechaDesde <= hoy &&
                            (g.FechaHasta == null || g.FechaHasta >= hoy))
                .OrderByDescending(g => g.FechaDesde)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return Ok(ganavision?.Ganavisiones);
        }

        /// <summary>
        /// Obtiene la lista de IDs de productos que estan en la tabla Ganavisiones (activos).
        /// Issue #94: Sistema Ganavisiones - FASE 6
        /// Util para cachear en el cliente y mostrar notificacion cuando el usuario selecciona un producto bonificable.
        /// </summary>
        /// <param name="empresa">Codigo de empresa</param>
        /// <returns>Lista de ProductoIds bonificables</returns>
        [HttpGet]
        [Route("api/Ganavisiones/ProductosIds")]
        [ResponseType(typeof(List<string>))]
        public async Task<IHttpActionResult> GetProductosBonificablesIds(string empresa)
        {
            string empresaPadded = empresa.PadRight(3);
            var hoy = DateTime.Today;

            var productosIds = await db.Ganavisiones
                .Where(g => g.Empresa == empresaPadded &&
                            g.FechaDesde <= hoy &&
                            (g.FechaHasta == null || g.FechaHasta >= hoy))
                .Select(g => g.ProductoId.Trim())
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            return Ok(productosIds);
        }

        /// <summary>
        /// Obtiene los productos que se pueden bonificar dado un importe de base imponible bonificable.
        /// Issue #94: Sistema Ganavisiones - FASE 2/3
        /// </summary>
        /// <param name="empresa">Codigo de empresa</param>
        /// <param name="baseImponibleBonificable">Importe en EUR de la base imponible de grupos bonificables (COS, ACC, PEL)</param>
        /// <param name="almacen">Almacen del pedido (opcional). Si se especifica junto con servirJunto=false, solo devuelve productos con stock en ese almacen</param>
        /// <param name="servirJunto">Si es true (default), devuelve productos con stock en cualquier almacen. Si es false, solo con stock en el almacen especificado</param>
        /// <param name="cliente">Numero de cliente. Si se especifica, excluye productos que el cliente haya comprado (BaseImponible != 0)</param>
        /// <returns>Lista de productos bonificables con sus Ganavisiones y stocks, ordenados por Ganavisiones ascendente</returns>
        [HttpGet]
        [Route("api/Ganavisiones/ProductosBonificables")]
        [ResponseType(typeof(ProductosBonificablesResponse))]
        public async Task<IHttpActionResult> GetProductosBonificables(string empresa, decimal baseImponibleBonificable, string almacen = null, bool servirJunto = true, string cliente = null)
        {
            if (baseImponibleBonificable < 0)
            {
                return BadRequest("La base imponible bonificable no puede ser negativa");
            }

            string empresaPadded = empresa.PadRight(3);
            var hoy = DateTime.Today;

            // Calcular Ganavisiones disponibles (truncado, no redondeado)
            int ganavisionesDisponibles = (int)(baseImponibleBonificable / Constantes.Productos.VALOR_GANAVISION_EN_EUROS);

            if (ganavisionesDisponibles <= 0)
            {
                return Ok(new ProductosBonificablesResponse
                {
                    GanavisionesDisponibles = 0,
                    BaseImponibleBonificable = baseImponibleBonificable,
                    Productos = new List<ProductoBonificableDTO>()
                });
            }

            // Obtener productos con Ganavisiones activos que se pueden bonificar
            var ganavisionesQuery = await db.Ganavisiones
                .Include("Producto")
                .Where(g => g.Empresa == empresaPadded &&
                            g.FechaDesde <= hoy &&
                            (g.FechaHasta == null || g.FechaHasta >= hoy) &&
                            g.Ganavisiones <= ganavisionesDisponibles)
                .OrderBy(g => g.Ganavisiones)
                .ThenBy(g => g.Producto.Nombre)
                .ToListAsync()
                .ConfigureAwait(false);

            // Obtener IDs de productos para consultar stocks
            var productosIds = ganavisionesQuery.Select(g => g.ProductoId).ToList();

            // Excluir productos que el cliente haya comprado (BaseImponible != 0)
            // Si solo los ha recibido como bonificacion (BaseImponible = 0), SI puede volver a recibirlos
            if (!string.IsNullOrEmpty(cliente))
            {
                var productosComprados = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresaPadded &&
                                l.Nº_Cliente == cliente &&
                                productosIds.Contains(l.Producto) &&
                                l.Base_Imponible != 0)
                    .Select(l => l.Producto)
                    .Distinct()
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (productosComprados.Any())
                {
                    ganavisionesQuery = ganavisionesQuery
                        .Where(g => !productosComprados.Contains(g.ProductoId))
                        .ToList();
                    productosIds = ganavisionesQuery.Select(g => g.ProductoId).ToList();
                }
            }

            // Obtener stocks de todos los almacenes para estos productos
            var stocksPorProducto = await db.ExtractosProducto
                .Where(e => productosIds.Contains(e.Número) && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .GroupBy(e => new { e.Número, e.Almacén })
                .Select(g => new { ProductoId = g.Key.Número, Almacen = g.Key.Almacén, Stock = g.Sum(e => (int)e.Cantidad) })
                .ToListAsync()
                .ConfigureAwait(false);

            // Construir DTOs con stocks
            var productosBonificables = new List<ProductoBonificableDTO>();
            foreach (var g in ganavisionesQuery)
            {
                var stocks = Constantes.Sedes.ListaSedes
                    .Select(sede => new StockAlmacenDTO
                    {
                        almacen = sede,
                        stock = stocksPorProducto
                            .Where(s => s.ProductoId == g.ProductoId && s.Almacen == sede)
                            .Select(s => s.Stock)
                            .FirstOrDefault()
                    })
                    .ToList();

                var dto = new ProductoBonificableDTO
                {
                    ProductoId = g.ProductoId?.Trim(),
                    ProductoNombre = g.Producto?.Nombre?.Trim(),
                    Ganavisiones = g.Ganavisiones,
                    PVP = g.Producto?.PVP ?? 0,
                    Iva = g.Producto?.IVA_Repercutido?.Trim(),
                    Stocks = stocks
                };

                productosBonificables.Add(dto);
            }

            // Filtrar por stock segun servirJunto
            if (!servirJunto && !string.IsNullOrEmpty(almacen))
            {
                // Solo productos con stock en el almacen especificado
                productosBonificables = productosBonificables
                    .Where(p => p.Stocks.Any(s => s.almacen == almacen && s.stock > 0))
                    .ToList();
            }
            else
            {
                // Productos con stock en cualquier almacen
                productosBonificables = productosBonificables
                    .Where(p => p.StockTotal > 0)
                    .ToList();
            }

            return Ok(new ProductosBonificablesResponse
            {
                GanavisionesDisponibles = ganavisionesDisponibles,
                BaseImponibleBonificable = baseImponibleBonificable,
                Productos = productosBonificables
            });
        }

        /// <summary>
        /// Valida si se puede desmarcar la opcion ServirJunto en un pedido con productos bonificados.
        /// Issue #94: Sistema Ganavisiones - FASE 3
        ///
        /// Regla: Si los productos bonificados (regalos) no tienen stock en el almacen del pedido,
        /// no se puede desmarcar ServirJunto porque no tiene sentido pagar portes dos veces para enviar un regalo.
        /// </summary>
        /// <param name="request">Almacen del pedido y lista de productos bonificados</param>
        /// <returns>Resultado de la validacion con productos problematicos si los hay</returns>
        [HttpPost]
        [Route("api/Ganavisiones/ValidarServirJunto")]
        [ResponseType(typeof(ValidarServirJuntoResponse))]
        public async Task<IHttpActionResult> ValidarServirJunto([FromBody] ValidarServirJuntoRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Almacen))
            {
                return BadRequest("Debe especificar el almacen del pedido");
            }

            if (request.ProductosBonificados == null || !request.ProductosBonificados.Any())
            {
                // Sin productos bonificados, se puede desmarcar sin problema
                return Ok(new ValidarServirJuntoResponse
                {
                    PuedeDesmarcar = true,
                    ProductosProblematicos = new List<ProductoSinStockDTO>(),
                    Mensaje = null
                });
            }

            // Obtener stocks de los productos bonificados en todos los almacenes
            var productosIds = request.ProductosBonificados;
            var stocksPorProducto = await db.ExtractosProducto
                .Where(e => productosIds.Contains(e.Número) && Constantes.Sedes.ListaSedes.Contains(e.Almacén))
                .GroupBy(e => new { e.Número, e.Almacén })
                .Select(g => new { ProductoId = g.Key.Número, Almacen = g.Key.Almacén, Stock = g.Sum(e => (int)e.Cantidad) })
                .ToListAsync()
                .ConfigureAwait(false);

            // Obtener nombres de productos
            var productos = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && productosIds.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre })
                .ToListAsync()
                .ConfigureAwait(false);

            // Identificar productos sin stock en el almacen del pedido
            var productosProblematicos = new List<ProductoSinStockDTO>();
            foreach (var productoId in productosIds)
            {
                var stockEnAlmacen = stocksPorProducto
                    .Where(s => s.ProductoId == productoId && s.Almacen == request.Almacen)
                    .Select(s => s.Stock)
                    .FirstOrDefault();

                if (stockEnAlmacen <= 0)
                {
                    // Buscar en que almacen SI tiene stock
                    var almacenConStock = stocksPorProducto
                        .Where(s => s.ProductoId == productoId && s.Stock > 0)
                        .Select(s => s.Almacen)
                        .FirstOrDefault();

                    var nombreProducto = productos
                        .Where(p => p.Número == productoId)
                        .Select(p => p.Nombre?.Trim())
                        .FirstOrDefault();

                    productosProblematicos.Add(new ProductoSinStockDTO
                    {
                        ProductoId = productoId?.Trim(),
                        ProductoNombre = nombreProducto,
                        AlmacenConStock = almacenConStock?.Trim()
                    });
                }
            }

            if (productosProblematicos.Any())
            {
                var listaProductos = string.Join(", ", productosProblematicos.Select(p =>
                    string.IsNullOrEmpty(p.AlmacenConStock)
                        ? p.ProductoNombre
                        : $"{p.ProductoNombre} (stock en {p.AlmacenConStock})"));

                return Ok(new ValidarServirJuntoResponse
                {
                    PuedeDesmarcar = false,
                    ProductosProblematicos = productosProblematicos,
                    Mensaje = $"No se puede desmarcar 'Servir junto' porque los siguientes productos bonificados " +
                              $"no tienen stock en {request.Almacen}: {listaProductos}. " +
                              $"Cambie los productos bonificados por otros con stock en {request.Almacen} o mantenga 'Servir junto' marcado."
                });
            }

            return Ok(new ValidarServirJuntoResponse
            {
                PuedeDesmarcar = true,
                ProductosProblematicos = new List<ProductoSinStockDTO>(),
                Mensaje = null
            });
        }

        /// <summary>
        /// Crea un nuevo registro de Ganavision.
        /// Si no se especifica el valor de Ganavisiones, se calcula como Ceiling(PVP del producto).
        /// </summary>
        /// <param name="dto">Datos del Ganavision a crear</param>
        /// <param name="usuario">Usuario que realiza la operacion</param>
        /// <returns>GanavisionDTO del registro creado</returns>
        [HttpPost]
        [Route("api/Ganavisiones")]
        [ResponseType(typeof(GanavisionDTO))]
        public async Task<IHttpActionResult> PostGanavision([FromBody] GanavisionCreateDTO dto, [FromUri] string usuario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Verificar que el producto existe
            string empresaPadded = dto.Empresa.PadRight(3);
            var producto = await db.Productos
                .FirstOrDefaultAsync(p => p.Empresa == empresaPadded && p.Número == dto.ProductoId)
                .ConfigureAwait(false);

            if (producto == null)
            {
                return BadRequest($"El producto '{dto.ProductoId}' no existe en la empresa '{dto.Empresa}'");
            }

            // Verificar que no existe ya un Ganavision para este producto
            var existente = await db.Ganavisiones
                .AnyAsync(g => g.Empresa == empresaPadded && g.ProductoId == dto.ProductoId)
                .ConfigureAwait(false);

            if (existente)
            {
                return BadRequest($"Ya existe un registro de Ganavisiones para el producto '{dto.ProductoId}'. Modifique el registro existente en la lista.");
            }

            // Calcular Ganavisiones por defecto si no se especifica
            int ganavisiones;
            if (dto.Ganavisiones.HasValue)
            {
                ganavisiones = dto.Ganavisiones.Value;
            }
            else
            {
                // Por defecto: precio al alza (Ceiling)
                ganavisiones = (int)Math.Ceiling(producto.PVP ?? 0m);
            }

            var ganavision = new Ganavision
            {
                Empresa = empresaPadded,
                ProductoId = dto.ProductoId,
                Ganavisiones = ganavisiones,
                FechaDesde = dto.FechaDesde,
                FechaHasta = dto.FechaHasta,
                FechaCreacion = DateTime.Now,
                FechaModificacion = DateTime.Now,
                Usuario = usuario
            };

            db.Ganavisiones.Add(ganavision);
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Asignar el producto para que MapToDTO pueda obtener el nombre
            ganavision.Producto = producto;

            return Ok(MapToDTO(ganavision));
        }

        /// <summary>
        /// Actualiza un registro de Ganavision existente.
        /// </summary>
        /// <param name="id">ID del registro a actualizar</param>
        /// <param name="dto">Nuevos datos</param>
        /// <param name="usuario">Usuario que realiza la modificacion</param>
        /// <returns>GanavisionDTO actualizado</returns>
        [HttpPut]
        [Route("api/Ganavisiones/{id:int}")]
        [ResponseType(typeof(GanavisionDTO))]
        public async Task<IHttpActionResult> PutGanavision(int id, [FromBody] GanavisionCreateDTO dto, [FromUri] string usuario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var ganavision = await db.Ganavisiones.FindAsync(id).ConfigureAwait(false);

            if (ganavision == null)
            {
                return NotFound();
            }

            // Actualizar campos
            if (dto.Ganavisiones.HasValue)
            {
                ganavision.Ganavisiones = dto.Ganavisiones.Value;
            }
            ganavision.FechaDesde = dto.FechaDesde;
            ganavision.FechaHasta = dto.FechaHasta;
            ganavision.FechaModificacion = DateTime.Now;
            ganavision.Usuario = usuario;

            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(MapToDTO(ganavision));
        }

        /// <summary>
        /// Elimina un registro de Ganavision.
        /// </summary>
        /// <param name="id">ID del registro a eliminar</param>
        /// <returns>GanavisionDTO del registro eliminado</returns>
        [HttpDelete]
        [Route("api/Ganavisiones/{id:int}")]
        [ResponseType(typeof(GanavisionDTO))]
        public async Task<IHttpActionResult> DeleteGanavision(int id)
        {
            var ganavision = await db.Ganavisiones.FindAsync(id).ConfigureAwait(false);

            if (ganavision == null)
            {
                return NotFound();
            }

            var dto = MapToDTO(ganavision);
            db.Ganavisiones.Remove(ganavision);
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

        private GanavisionDTO MapToDTO(Ganavision ganavision)
        {
            return new GanavisionDTO
            {
                Id = ganavision.Id,
                Empresa = ganavision.Empresa?.Trim(),
                ProductoId = ganavision.ProductoId?.Trim(),
                ProductoNombre = ganavision.Producto?.Nombre?.Trim(),
                Ganavisiones = ganavision.Ganavisiones,
                FechaDesde = ganavision.FechaDesde,
                FechaHasta = ganavision.FechaHasta,
                FechaCreacion = ganavision.FechaCreacion,
                FechaModificacion = ganavision.FechaModificacion,
                Usuario = ganavision.Usuario
            };
        }
    }
}
