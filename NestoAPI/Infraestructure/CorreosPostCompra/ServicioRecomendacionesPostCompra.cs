using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public class ServicioRecomendacionesPostCompra : IServicioRecomendacionesPostCompra
    {
        public async Task<RecomendacionPostCompraDTO> ObtenerRecomendaciones(string empresa, int pedidoNumero)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;

                // 1. Obtener datos del pedido
                var pedido = await db.CabPedidoVtas
                    .Include(p => p.Cliente)
                    .Include(p => p.Cliente.PersonasContactoClientes)
                    .Include(p => p.LinPedidoVtas)
                    .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Número == pedidoNumero)
                    .ConfigureAwait(false);

                if (pedido == null)
                {
                    return null;
                }

                // Obtener el email del cliente (primer contacto con email)
                string emailCliente = pedido.Cliente?.PersonasContactoClientes?
                    .Where(p => !string.IsNullOrWhiteSpace(p.CorreoElectrónico))
                    .Select(p => p.CorreoElectrónico.Trim())
                    .FirstOrDefault();

                // 2. Productos del pedido actual
                var productosPedido = pedido.LinPedidoVtas
                    .Where(l => l.TipoLinea == 1 && !string.IsNullOrWhiteSpace(l.Producto)) // Solo líneas de producto
                    .Select(l => l.Producto.Trim())
                    .Distinct()
                    .ToList();

                if (!productosPedido.Any())
                {
                    return new RecomendacionPostCompraDTO
                    {
                        Empresa = empresa,
                        ClienteId = pedido.Nº_Cliente?.Trim(),
                        ClienteNombre = pedido.Cliente?.Nombre?.Trim(),
                        ClienteEmail = emailCliente,
                        PedidoNumero = pedidoNumero,
                        FechaPedido = pedido.Fecha ?? DateTime.Today,
                        Videos = new List<VideoRecomendadoDTO>()
                    };
                }

                // 3. Historial de productos comprados por este cliente (para saber qué ya tiene)
                var productosHistorico = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa &&
                                l.Nº_Cliente == pedido.Nº_Cliente &&
                                l.TipoLinea == 1 &&
                                l.Producto != null)
                    .Select(l => l.Producto.Trim())
                    .Distinct()
                    .ToListAsync()
                    .ConfigureAwait(false);

                var productosHistoricoSet = new HashSet<string>(productosHistorico, StringComparer.OrdinalIgnoreCase);
                var productosPedidoSet = new HashSet<string>(productosPedido, StringComparer.OrdinalIgnoreCase);

                // 4. Videos que contienen al menos un producto del pedido
                // Solo traemos los campos necesarios (sin Descripcion que es muy largo)
                var videosConProductos = await db.Videos
                    .Where(v => v.VideosProductos.Any(vp => productosPedido.Contains(vp.Referencia)))
                    .Select(v => new
                    {
                        v.Id,
                        v.VideoId,
                        v.Titulo,
                        // Contar productos del pedido en este video para ordenar por relevancia
                        ProductosDelPedidoEnVideo = v.VideosProductos.Count(vp => productosPedido.Contains(vp.Referencia)),
                        Productos = v.VideosProductos.Select(vp => new
                        {
                            vp.Referencia,
                            vp.NombreProducto,
                            vp.TiempoAparicion,
                            vp.EnlaceVideo,
                            vp.EnlaceTienda
                        }).ToList()
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // 5. Ordenar por relevancia (más productos del pedido primero) y limitar a 2 videos
                var videosOrdenados = videosConProductos
                    .OrderByDescending(v => v.ProductosDelPedidoEnVideo)
                    .Take(2)
                    .ToList();

                // 6. Construir el DTO de respuesta
                var resultado = new RecomendacionPostCompraDTO
                {
                    Empresa = empresa,
                    ClienteId = pedido.Nº_Cliente?.Trim(),
                    ClienteNombre = pedido.Cliente?.Nombre?.Trim(),
                    ClienteEmail = emailCliente,
                    PedidoNumero = pedidoNumero,
                    FechaPedido = pedido.Fecha ?? DateTime.Today,
                    Videos = videosOrdenados.Select(v => new VideoRecomendadoDTO
                    {
                        VideoId = v.Id,
                        VideoYoutubeId = v.VideoId,
                        Titulo = v.Titulo,
                        Descripcion = null, // No se incluye para reducir volumen de datos
                        FechaPublicacion = null, // No se necesita para el correo
                        Productos = v.Productos
                            .GroupBy(p => p.Referencia?.Trim()) // Eliminar duplicados
                            .Select(g => g.First())
                            .Select(p => new ProductoEnVideoDTO
                            {
                                ProductoId = p.Referencia?.Trim(),
                                NombreProducto = p.NombreProducto?.Trim(),
                                TiempoAparicion = p.TiempoAparicion,
                                EnlaceVideo = p.EnlaceVideo,
                                EnlaceTienda = p.EnlaceTienda,
                                YaComprado = productosHistoricoSet.Contains(p.Referencia?.Trim() ?? ""),
                                EnPedidoActual = productosPedidoSet.Contains(p.Referencia?.Trim() ?? "")
                            }).ToList()
                    }).ToList()
                };

                return resultado;
            }
        }
    }
}
