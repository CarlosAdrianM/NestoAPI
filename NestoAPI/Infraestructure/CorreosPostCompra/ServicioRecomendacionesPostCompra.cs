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
        private readonly NVEntities _db;
        private readonly bool _dbEsPropio;

        public ServicioRecomendacionesPostCompra() : this(null)
        {
        }

        public ServicioRecomendacionesPostCompra(NVEntities dbExterno)
        {
            if (dbExterno != null)
            {
                _db = dbExterno;
                _dbEsPropio = false;
            }
            else
            {
                _db = new NVEntities();
                _dbEsPropio = true;
            }
            _db.Configuration.LazyLoadingEnabled = false;
        }

        public async Task<List<CorreoPostCompraClienteDTO>> ObtenerCorreosSemana(string empresa, DateTime fechaDesde, DateTime fechaHasta)
        {
            try
            {
                // 1. Obtener líneas de albarán de la semana con producto que tiene vídeo
                var lineasSemana = await _db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa &&
                                l.TipoLinea == 1 &&
                                l.Estado >= Constantes.EstadosLineaVenta.ALBARAN &&
                                l.Fecha_Albarán >= fechaDesde &&
                                l.Fecha_Albarán <= fechaHasta &&
                                l.Producto != null &&
                                _db.VideosProductos.Any(vp => vp.Referencia == l.Producto))
                    .Select(l => new
                    {
                        l.Nº_Cliente,
                        l.Producto,
                        l.Texto,
                        l.Base_Imponible
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (!lineasSemana.Any())
                {
                    return new List<CorreoPostCompraClienteDTO>();
                }

                // 2. Obtener datos de clientes con contactos
                var clienteIds = lineasSemana.Select(l => l.Nº_Cliente).Distinct().ToList();

                var clientes = await _db.Clientes
                    .Where(c => c.Empresa == empresa && clienteIds.Contains(c.Nº_Cliente))
                    .Select(c => new DatosClienteCorreo
                    {
                        ClienteId = c.Nº_Cliente,
                        Nombre = c.Nombre,
                        Estado = c.Estado ?? 0,
                        CodPostal = c.CodPostal
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Obtener emails de contactos
                var emails = await _db.PersonasContactoClientes
                    .Where(p => p.Empresa == empresa &&
                                clienteIds.Contains(p.NºCliente) &&
                                p.CorreoElectrónico != null &&
                                p.CorreoElectrónico.Trim() != "")
                    .GroupBy(p => p.NºCliente)
                    .Select(g => new { ClienteId = g.Key, Email = g.FirstOrDefault().CorreoElectrónico })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var emailsPorCliente = emails.ToDictionary(e => e.ClienteId, e => e.Email?.Trim());

                foreach (var cliente in clientes)
                {
                    emailsPorCliente.TryGetValue(cliente.ClienteId, out string email);
                    cliente.Email = email;
                }

                // 3. Filtrar clientes válidos
                var clientesValidos = FiltrarClientesValidos(clientes);
                var clientesValidosIds = new HashSet<string>(clientesValidos.Select(c => c.ClienteId));

                if (!clientesValidos.Any())
                {
                    return new List<CorreoPostCompraClienteDTO>();
                }

                // 4. Obtener todos los vídeos con sus productos para los productos de las líneas
                var productosIds = lineasSemana.Select(l => l.Producto.Trim()).Distinct().ToList();

                var videosProductos = await _db.VideosProductos
                    .Where(vp => productosIds.Contains(vp.Referencia))
                    .Select(vp => new
                    {
                        vp.Referencia,
                        vp.NombreProducto,
                        vp.EnlaceVideo,
                        vp.Video.VideoId,
                        VideoTitulo = vp.Video.Titulo,
                        vp.Video.FechaPublicacion
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var videosPorProducto = videosProductos
                    .GroupBy(vp => vp.Referencia?.Trim())
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(vp => new DatosVideoProducto
                        {
                            VideoYoutubeId = vp.VideoId,
                            VideoTitulo = vp.VideoTitulo,
                            FechaPublicacion = vp.FechaPublicacion,
                            EnlaceVideo = vp.EnlaceVideo
                        }).ToList());

                // 5. Obtener todos los productos de los vídeos relevantes (para recomendados)
                var videoYoutubeIds = videosProductos.Select(vp => vp.VideoId).Distinct().ToList();

                var todosProductosEnVideos = await _db.VideosProductos
                    .Where(vp => videoYoutubeIds.Contains(vp.Video.VideoId))
                    .Select(vp => new DatosProductoEnVideo
                    {
                        ProductoId = vp.Referencia,
                        NombreProducto = vp.NombreProducto,
                        VideoYoutubeId = vp.Video.VideoId,
                        VideoTitulo = vp.Video.Titulo,
                        EnlaceVideo = vp.EnlaceVideo
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // 6. Construir resultado por cliente
                var resultado = new List<CorreoPostCompraClienteDTO>();

                foreach (var cliente in clientesValidos)
                {
                    var lineasCliente = lineasSemana
                        .Where(l => l.Nº_Cliente == cliente.ClienteId)
                        .Select(l => new LineaAlbaranConVideo
                        {
                            ProductoId = l.Producto?.Trim(),
                            NombreProducto = l.Texto?.Trim(),
                            BaseImponible = l.Base_Imponible
                        })
                        .ToList();

                    var topProductos = SeleccionarTopProductos(lineasCliente);

                    if (!topProductos.Any())
                    {
                        continue;
                    }

                    // Asignar vídeo más reciente a cada producto
                    foreach (var producto in topProductos)
                    {
                        if (videosPorProducto.TryGetValue(producto.ProductoId, out var videos))
                        {
                            var videoReciente = SeleccionarVideoMasReciente(videos);
                            if (videoReciente != null)
                            {
                                producto.VideoYoutubeId = videoReciente.VideoYoutubeId;
                                producto.VideoTitulo = videoReciente.VideoTitulo;
                                producto.EnlaceVideoProducto = videoReciente.EnlaceVideo;
                            }
                        }
                    }

                    // Filtrar productos sin vídeo asignado
                    topProductos = topProductos.Where(p => p.VideoYoutubeId != null).ToList();

                    if (!topProductos.Any())
                    {
                        continue;
                    }

                    // Historial de compras del cliente para excluir de recomendados
                    var productosCompradosHistorico = await _db.LinPedidoVtas
                        .Where(l => l.Empresa == empresa &&
                                    l.Nº_Cliente == cliente.ClienteId &&
                                    l.TipoLinea == 1 &&
                                    l.Producto != null)
                        .Select(l => l.Producto)
                        .Distinct()
                        .ToListAsync()
                        .ConfigureAwait(false);

                    var productosCompradosSet = new HashSet<string>(
                        productosCompradosHistorico.Select(p => p.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    var productosPrincipalesSet = new HashSet<string>(
                        topProductos.Select(p => p.ProductoId),
                        StringComparer.OrdinalIgnoreCase);

                    // Filtrar productos de los vídeos relevantes para este cliente
                    var videoIdsCliente = topProductos
                        .Select(p => p.VideoYoutubeId)
                        .Distinct()
                        .ToHashSet();

                    var productosEnVideosCliente = todosProductosEnVideos
                        .Where(p => videoIdsCliente.Contains(p.VideoYoutubeId))
                        .ToList();

                    var recomendados = SeleccionarProductosRecomendados(
                        productosEnVideosCliente, productosCompradosSet, productosPrincipalesSet);

                    // Obtener URLs de Prestashop para todos los productos (comprados + recomendados)
                    var todosProductoIds = topProductos.Select(p => p.ProductoId)
                        .Concat(recomendados.Select(p => p.ProductoId))
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .ToList();

                    var urlsPrestashop = await ObtenerUrlsPrestashop(todosProductoIds).ConfigureAwait(false);

                    foreach (var producto in topProductos)
                    {
                        if (urlsPrestashop.TryGetValue(producto.ProductoId, out string url))
                        {
                            producto.EnlaceTienda = url;
                        }
                    }

                    foreach (var producto in recomendados)
                    {
                        if (urlsPrestashop.TryGetValue(producto.ProductoId, out string url))
                        {
                            producto.EnlaceTienda = url;
                        }
                    }

                    resultado.Add(new CorreoPostCompraClienteDTO
                    {
                        Empresa = empresa,
                        ClienteId = cliente.ClienteId?.Trim(),
                        ClienteNombre = cliente.Nombre?.Trim(),
                        ClienteEmail = cliente.Email,
                        SemanaDesde = fechaDesde,
                        SemanaHasta = fechaHasta,
                        ProductosComprados = topProductos,
                        ProductosRecomendados = recomendados
                    });
                }

                // Deduplicar por email: si varios clientes comparten email,
                // quedarse con el que tiene más productos (evita correos duplicados)
                resultado = resultado
                    .GroupBy(c => c.ClienteEmail?.Trim().ToLowerInvariant())
                    .Select(g => g.OrderByDescending(c => c.ProductosComprados.Count).First())
                    .ToList();

                return resultado;
            }
            finally
            {
                if (_dbEsPropio)
                {
                    _db?.Dispose();
                }
            }
        }

        /// <summary>
        /// Obtiene las URLs de Prestashop para una lista de productos, en paralelo.
        /// Las URLs se devuelven sin parámetros UTM (LeerUrlTiendaOnline los añade,
        /// pero los de esta campaña se añaden después en el generador).
        /// </summary>
        private static async Task<Dictionary<string, string>> ObtenerUrlsPrestashop(List<string> productoIds)
        {
            var tareas = productoIds.Select(async productoId =>
            {
                var url = await ProductoDTO.LeerUrlTiendaOnline(productoId).ConfigureAwait(false);
                return new { ProductoId = productoId, Url = LimpiarParametrosUtm(url) };
            });

            var resultados = await Task.WhenAll(tareas).ConfigureAwait(false);

            return resultados
                .Where(r => !string.IsNullOrEmpty(r.Url))
                .ToDictionary(r => r.ProductoId, r => r.Url);
        }

        /// <summary>
        /// Elimina los parámetros UTM de una URL para poder añadir los correctos después.
        /// </summary>
        internal static string LimpiarParametrosUtm(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var indiceQuery = url.IndexOf('?');
            if (indiceQuery < 0)
            {
                return url;
            }

            var urlBase = url.Substring(0, indiceQuery);
            var queryString = url.Substring(indiceQuery + 1);

            var parametros = queryString.Split('&')
                .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!parametros.Any())
            {
                return urlBase;
            }

            return urlBase + "?" + string.Join("&", parametros);
        }

        /// <summary>
        /// Filtra clientes que cumplen los requisitos para recibir correo post-compra:
        /// - Tienen email
        /// - Estado distinto de 8 (baja)
        /// - Código postal empieza por 28, 45 o 19
        /// </summary>
        internal static List<DatosClienteCorreo> FiltrarClientesValidos(List<DatosClienteCorreo> clientes)
        {
            return clientes
                .Where(c => !string.IsNullOrWhiteSpace(c.Email) &&
                            c.Estado != 8 &&
                            EsCodigoPostalValido(c.CodPostal))
                .ToList();
        }

        /// <summary>
        /// Agrupa líneas por producto, suma BaseImponible y devuelve los top 3.
        /// </summary>
        internal static List<ProductoCompradoConVideoDTO> SeleccionarTopProductos(List<LineaAlbaranConVideo> lineas)
        {
            return lineas
                .Where(l => !string.IsNullOrWhiteSpace(l.ProductoId))
                .GroupBy(l => l.ProductoId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProductoCompradoConVideoDTO
                {
                    ProductoId = g.Key,
                    NombreProducto = g.First().NombreProducto,
                    BaseImponibleTotal = g.Sum(l => l.BaseImponible)
                })
                .OrderByDescending(p => p.BaseImponibleTotal)
                .Take(3)
                .ToList();
        }

        /// <summary>
        /// De una lista de vídeos para un producto, devuelve el más reciente por FechaPublicacion.
        /// </summary>
        internal static DatosVideoProducto SeleccionarVideoMasReciente(List<DatosVideoProducto> videos)
        {
            if (videos == null || !videos.Any())
            {
                return null;
            }

            return videos
                .OrderByDescending(v => v.FechaPublicacion ?? DateTime.MinValue)
                .First();
        }

        /// <summary>
        /// Selecciona hasta 4 productos de los vídeos que el cliente nunca ha comprado
        /// y que no están entre los productos principales del correo.
        /// </summary>
        internal static List<ProductoRecomendadoDTO> SeleccionarProductosRecomendados(
            List<DatosProductoEnVideo> productosEnVideos,
            HashSet<string> productosCompradosHistorico,
            HashSet<string> productosPrincipales)
        {
            return productosEnVideos
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductoId) &&
                            !productosCompradosHistorico.Contains(p.ProductoId.Trim()) &&
                            !productosPrincipales.Contains(p.ProductoId.Trim()))
                .GroupBy(p => p.ProductoId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(4)
                .Select(p => new ProductoRecomendadoDTO
                {
                    ProductoId = p.ProductoId?.Trim(),
                    NombreProducto = p.NombreProducto?.Trim(),
                    VideoYoutubeId = p.VideoYoutubeId,
                    VideoTitulo = p.VideoTitulo,
                    EnlaceVideoProducto = p.EnlaceVideo
                })
                .ToList();
        }

        private static bool EsCodigoPostalValido(string codPostal)
        {
            if (string.IsNullOrWhiteSpace(codPostal))
            {
                return false;
            }

            var cp = codPostal.Trim();
            return cp.StartsWith("28") || cp.StartsWith("45") || cp.StartsWith("19");
        }
    }

    #region DTOs internos para datos intermedios

    /// <summary>
    /// Datos de cliente necesarios para el filtrado.
    /// </summary>
    internal class DatosClienteCorreo
    {
        public string ClienteId { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        public int Estado { get; set; }
        public string CodPostal { get; set; }
    }

    /// <summary>
    /// Línea de albarán con datos mínimos para la selección de productos.
    /// </summary>
    internal class LineaAlbaranConVideo
    {
        public string ProductoId { get; set; }
        public string NombreProducto { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>
    /// Datos de un vídeo asociado a un producto, para seleccionar el más reciente.
    /// </summary>
    internal class DatosVideoProducto
    {
        public string VideoYoutubeId { get; set; }
        public string VideoTitulo { get; set; }
        public DateTime? FechaPublicacion { get; set; }
        public string EnlaceVideo { get; set; }
    }

    /// <summary>
    /// Producto que aparece en un vídeo, datos mínimos para seleccionar recomendados.
    /// </summary>
    internal class DatosProductoEnVideo
    {
        public string ProductoId { get; set; }
        public string NombreProducto { get; set; }
        public string VideoYoutubeId { get; set; }
        public string VideoTitulo { get; set; }
        public string EnlaceVideo { get; set; }
    }

    #endregion
}
