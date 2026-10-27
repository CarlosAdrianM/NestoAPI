using Elmah;
using NestoAPI.Infraestructure.SugerenciasProductos;
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
        // NestoAPI#707: de donde salen pares, afinidad y popularidad para ordenar los recomendados (tablas de #669)
        private readonly IRepositorioSugerenciasProductos _repositorioSugerencias;

        public ServicioRecomendacionesPostCompra() : this(null)
        {
        }

        public ServicioRecomendacionesPostCompra(NVEntities dbExterno, IRepositorioSugerenciasProductos repositorioSugerencias = null)
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
            _repositorioSugerencias = repositorioSugerencias ?? new RepositorioSugerenciasProductosSql(_db);
        }

        public async Task<List<CorreoPostCompraClienteDTO>> ObtenerCorreosSemana(string empresa, DateTime fechaDesde, DateTime fechaHasta)
        {
            try
            {
                // 1. Obtener líneas de albarán de la semana con producto que tiene vídeo
                const decimal PRECIO_MINIMO_CORREO = 30m;

                var lineasSemana = await _db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa &&
                                l.TipoLinea == 1 &&
                                l.Estado >= Constantes.EstadosLineaVenta.ALBARAN &&
                                l.Fecha_Albarán >= fechaDesde &&
                                l.Fecha_Albarán <= fechaHasta &&
                                l.Producto != null &&
                                l.Precio >= PRECIO_MINIMO_CORREO &&
                                _db.VideosProductos.Any(vp => vp.Referencia == l.Producto && vp.Video.EsUnProtocolo && vp.Video.FechaBaja == null))
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
                    // NestoAPI#707: correo y contacto de la MISMA persona (una sola fila por cliente)
                    .Select(g => g.Select(p => new { ClienteId = g.Key, Email = p.CorreoElectrónico, p.Contacto }).FirstOrDefault())
                    .ToListAsync()
                    .ConfigureAwait(false);

                var emailsPorCliente = emails.Where(e => e != null).ToDictionary(e => e.ClienteId, e => e);

                foreach (var cliente in clientes)
                {
                    if (emailsPorCliente.TryGetValue(cliente.ClienteId, out var email))
                    {
                        cliente.Email = email.Email?.Trim();
                        cliente.Contacto = email.Contacto?.Trim();
                    }
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
                    .Where(vp => productosIds.Contains(vp.Referencia) &&
                                 vp.Video.EsUnProtocolo &&
                                 vp.Video.FechaBaja == null)
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
                    .Where(vp => videoYoutubeIds.Contains(vp.Video.VideoId) &&
                                 vp.Video.EsUnProtocolo &&
                                 vp.Video.FechaBaja == null &&
                                 vp.Referencia != null &&
                                 _db.Productos.Any(p => p.Empresa == empresa &&
                                     p.Número == vp.Referencia &&
                                     p.Estado >= 0))
                    .Select(vp => new DatosProductoEnVideo
                    {
                        ProductoId = vp.Referencia,
                        NombreProducto = vp.NombreProducto,
                        VideoYoutubeId = vp.Video.VideoId,
                        VideoTitulo = vp.Video.Titulo,
                        EnlaceVideo = vp.EnlaceVideo,
                        Familia = _db.Productos
                            .Where(p => p.Empresa == empresa && p.Número == vp.Referencia)
                            .Select(p => p.Familia)
                            .FirstOrDefault()
                    })
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Resolver TipoExclusiva siempre desde empresa "1"
                var familiasIds = todosProductosEnVideos
                    .Where(p => !string.IsNullOrWhiteSpace(p.Familia))
                    .Select(p => p.Familia.Trim())
                    .Distinct()
                    .ToList();

                var tiposExclusivaPorFamilia = await _db.Familias
                    .Where(f => f.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                familiasIds.Contains(f.Número))
                    .Select(f => new { f.Número, f.TipoExclusiva })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var mapaTipoExclusiva = tiposExclusivaPorFamilia
                    .ToDictionary(f => f.Número.Trim(), f => f.TipoExclusiva?.Trim(),
                        StringComparer.OrdinalIgnoreCase);

                AsignarTipoExclusiva(todosProductosEnVideos, mapaTipoExclusiva);

                // NestoAPI#707 (corte 2): pares y popularidad de una vez para toda la semana; la afinidad, por cliente.
                // Best-effort: si fallan, se ordena como antes (orden de la consulta).
                List<ParCompradoJunto> paresSemana = await LeerSinRomper(
                    () => _repositorioSugerencias.LeerPares(productosIds), "pares").ConfigureAwait(false);
                List<PopularidadProducto> popularidadSemana = await LeerSinRomper(
                    () => _repositorioSugerencias.LeerPopularidad(todosProductosEnVideos
                        .Where(p => !string.IsNullOrWhiteSpace(p.ProductoId)).Select(p => p.ProductoId.Trim()).Distinct()),
                    "popularidad").ConfigureAwait(false);

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

                    // Familias de los productos comprados por el cliente (para filtrar recomendados)
                    var familiasCompradas = new HashSet<string>(
                        productosEnVideosCliente
                            .Where(p => productosPrincipalesSet.Contains(p.ProductoId?.Trim()))
                            .Where(p => !string.IsNullOrWhiteSpace(p.Familia))
                            .Select(p => p.Familia.Trim()),
                        StringComparer.OrdinalIgnoreCase);

                    List<AfinidadCliente> afinidadCliente = await LeerSinRomper(
                        () => _repositorioSugerencias.LeerAfinidad(cliente.ClienteId?.Trim()), "afinidad").ConfigureAwait(false);

                    var recomendados = SeleccionarProductosRecomendados(
                        productosEnVideosCliente, productosCompradosSet, productosPrincipalesSet,
                        familiasCompradas,
                        new DatosPuntuacionRecomendados
                        {
                            ProductosComprados = productosPrincipalesSet,
                            Pares = paresSemana,
                            Afinidad = afinidadCliente,
                            Popularidad = popularidadSemana
                        });

                    // Filtro geográfico: si algún producto del correo tiene TipoExclusiva MAD,
                    // el cliente debe ser de Madrid/Toledo/Guadalajara
                    var todosProductosCorreo = productosEnVideosCliente
                        .Where(p => productosPrincipalesSet.Contains(p.ProductoId?.Trim()) ||
                                    recomendados.Any(r => r.ProductoId == p.ProductoId?.Trim()))
                        .ToList();

                    if (RequiereFiltroGeografico(todosProductosCorreo) &&
                        !EstaEnZonaExclusivaMadrid(cliente.CodPostal))
                    {
                        continue;
                    }

                    // Obtener URLs de Prestashop para todos los productos (comprados + recomendados)
                    var todosProductoIds = topProductos.Select(p => p.ProductoId)
                        .Concat(recomendados.Select(p => p.ProductoId))
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .ToList();

                    var urlsPrestashop = await ObtenerUrlsPrestashop(todosProductoIds).ConfigureAwait(false);

                    foreach (var producto in topProductos)
                    {
                        producto.EnlaceTienda = urlsPrestashop.TryGetValue(producto.ProductoId, out string url)
                            ? url
                            : GenerarUrlBusquedaTienda(producto.NombreProducto);
                    }

                    foreach (var producto in recomendados)
                    {
                        producto.EnlaceTienda = urlsPrestashop.TryGetValue(producto.ProductoId, out string urlRec)
                            ? urlRec
                            : GenerarUrlBusquedaTienda(producto.NombreProducto);
                    }

                    resultado.Add(new CorreoPostCompraClienteDTO
                    {
                        Empresa = empresa,
                        ClienteId = cliente.ClienteId?.Trim(),
                        ClienteNombre = cliente.Nombre?.Trim(),
                        ClienteEmail = cliente.Email,
                        ClienteContacto = cliente.Contacto,
                        SemanaDesde = fechaDesde,
                        SemanaHasta = fechaHasta,
                        ProductosComprados = topProductos,
                        ProductosRecomendados = recomendados,
                        VersionModelo = PuntuadorRecomendacionesPostCompra.VERSION_MODELO
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

        private bool _avisadoFalloPuntuacion;

        /// <summary>
        /// NestoAPI#707: lee datos de puntuación sin cortar el correo. Si falla, lista vacía (esos candidatos se quedan en el
        /// orden de la consulta) y un solo aviso a ELMAH por ejecución.
        /// </summary>
        private async Task<List<T>> LeerSinRomper<T>(Func<Task<List<T>>> leer, string que)
        {
            try
            {
                return await leer().ConfigureAwait(false) ?? new List<T>();
            }
            catch (Exception ex)
            {
                if (!_avisadoFalloPuntuacion)
                {
                    _avisadoFalloPuntuacion = true;
                    ErrorLog.GetDefault(null)?.Log(new Error(new Exception(
                        $"[CorreosPostCompra] NestoAPI#707: no se han podido leer los datos de puntuación ({que}); los recomendados van en el orden de la consulta. {ex.Message}", ex)));
                }
                return new List<T>();
            }
        }

        /// <summary>
        /// Obtiene las URLs de Prestashop para una lista de productos, en paralelo.
        /// Las URLs se devuelven sin parámetros UTM (LeerUrlTiendaOnline ya las da limpias desde NestoAPI#706;
        /// se limpian igualmente por si acaso; las de esta campaña se añaden después en el generador).
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
        /// Genera una URL de búsqueda en la tienda online para un producto
        /// cuya URL directa no se ha podido obtener de Prestashop.
        /// </summary>
        internal static string GenerarUrlBusquedaTienda(string nombreProducto)
        {
            if (string.IsNullOrWhiteSpace(nombreProducto))
            {
                return null;
            }

            var termino = Uri.EscapeDataString(nombreProducto.Trim());
            return $"https://www.productosdeesteticaypeluqueriaprofesional.com/buscar?controller=search&s={termino}";
        }

        /// <summary>
        /// Elimina los parámetros UTM de una URL para poder añadir los correctos después.
        /// </summary>
        internal static string LimpiarParametrosUtm(string url) => EnlacesTienda.QuitarUtm(url);

        /// <summary>
        /// Filtra clientes que cumplen los requisitos básicos para recibir correo post-compra:
        /// - Tienen email
        /// - Estado distinto de 8 (baja)
        /// Nota: el filtro geográfico se aplica después, por cliente, según los productos
        /// del correo (ver RequiereFiltroGeografico).
        /// </summary>
        internal static List<DatosClienteCorreo> FiltrarClientesValidos(List<DatosClienteCorreo> clientes)
        {
            return clientes
                .Where(c => !string.IsNullOrWhiteSpace(c.Email) &&
                            c.Estado != 8)
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
        /// Selecciona hasta 4 productos de los vídeos que el cliente nunca ha comprado,
        /// que no están entre los productos principales del correo y que cumplen las
        /// reglas de exclusividad: misma familia que algún producto comprado, o
        /// familia con TipoExclusiva PRP (propia) o NAC (nacional).
        /// </summary>
        /// <remarks>
        /// NestoAPI#707 (corte 2): con <paramref name="datosPuntuacion"/>, los candidatos (ya filtrados) se ordenan por
        /// <see cref="PuntuadorRecomendacionesPostCompra"/> ANTES de quitar los de enlace de vídeo repetido y quedarse con 4;
        /// sin datos (o sin puntuación para ninguno), el orden de la consulta, como antes. Cada recomendado lleva su Regla y
        /// Puntuacion.
        /// </remarks>
        internal static List<ProductoRecomendadoDTO> SeleccionarProductosRecomendados(
            List<DatosProductoEnVideo> productosEnVideos,
            HashSet<string> productosCompradosHistorico,
            HashSet<string> productosPrincipales,
            HashSet<string> familiasCompradas,
            DatosPuntuacionRecomendados datosPuntuacion = null)
        {
            var candidatos = productosEnVideos
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductoId) &&
                            !productosCompradosHistorico.Contains(p.ProductoId.Trim()) &&
                            !productosPrincipales.Contains(p.ProductoId.Trim()) &&
                            EsProductoRecomendable(p, familiasCompradas))
                .GroupBy(p => p.ProductoId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(p => new ProductoRecomendadoDTO
                {
                    ProductoId = p.ProductoId?.Trim(),
                    NombreProducto = p.NombreProducto?.Trim(),
                    VideoYoutubeId = p.VideoYoutubeId,
                    VideoTitulo = p.VideoTitulo,
                    EnlaceVideoProducto = p.EnlaceVideo
                })
                .ToList();

            // NestoAPI#707: puntuar y ordenar (sin datos, todos «orden_original» y el orden no cambia)
            Dictionary<string, PuntuacionCandidato> puntuaciones = PuntuadorRecomendacionesPostCompra.Puntuar(
                candidatos.Select(c => c.ProductoId), datosPuntuacion);
            foreach (var candidato in candidatos)
            {
                PuntuacionCandidato puntuacion = PuntuadorRecomendacionesPostCompra.Leer(puntuaciones, candidato.ProductoId);
                candidato.Regla = puntuacion?.Regla ?? PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL;
                candidato.Puntuacion = puntuacion?.Puntuacion;
            }
            candidatos = PuntuadorRecomendacionesPostCompra.Ordenar(candidatos, c => c.ProductoId, puntuaciones);

            // Eliminar duplicados por enlace de vídeo (mismo enlace para productos diferentes)
            var enlacesVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultado = new List<ProductoRecomendadoDTO>();
            foreach (var producto in candidatos)
            {
                string enlace = producto.EnlaceVideoProducto?.Trim() ?? "";
                if (string.IsNullOrEmpty(enlace) || enlacesVistos.Add(enlace))
                {
                    resultado.Add(producto);
                    if (resultado.Count >= 4)
                    {
                        break;
                    }
                }
            }

            return resultado;
        }

        /// <summary>
        /// Asigna TipoExclusiva a cada producto en base al mapa familia → tipoExclusiva.
        /// El mapa debe haberse construido siempre desde la empresa "1".
        /// </summary>
        internal static void AsignarTipoExclusiva(
            List<DatosProductoEnVideo> productos,
            Dictionary<string, string> tipoExclusivaPorFamilia)
        {
            if (productos == null || tipoExclusivaPorFamilia == null)
            {
                return;
            }

            foreach (var producto in productos)
            {
                string familia = producto.Familia?.Trim();
                if (!string.IsNullOrWhiteSpace(familia) &&
                    tipoExclusivaPorFamilia.TryGetValue(familia, out string tipoExclusiva))
                {
                    producto.TipoExclusiva = tipoExclusiva;
                }
            }
        }

        /// <summary>
        /// Un producto es recomendable si pertenece a la misma familia que algún
        /// producto comprado por el cliente, o si su familia tiene TipoExclusiva
        /// PRP (propia) o NAC (nacional). Esto evita recomendar productos con
        /// exclusivas territoriales (MAD, NIG, etc.) a clientes de otras zonas.
        /// </summary>
        internal static bool EsProductoRecomendable(DatosProductoEnVideo producto, HashSet<string> familiasCompradas)
        {
            if (familiasCompradas == null || !familiasCompradas.Any())
            {
                return true;
            }

            string familia = producto.Familia?.Trim();
            string tipoExclusiva = producto.TipoExclusiva?.Trim();

            // Misma familia que algún producto comprado
            if (!string.IsNullOrWhiteSpace(familia) && familiasCompradas.Contains(familia))
            {
                return true;
            }

            // Familia con exclusiva propia o nacional (sin restricción territorial)
            if (string.Equals(tipoExclusiva, "PRP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tipoExclusiva, "NAC", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determina si los productos del correo requieren filtro geográfico.
        /// Si algún producto tiene TipoExclusiva MAD, solo se puede enviar a
        /// clientes de Madrid/Toledo/Guadalajara.
        /// </summary>
        internal static bool RequiereFiltroGeografico(List<DatosProductoEnVideo> productosCorreo)
        {
            if (productosCorreo == null || !productosCorreo.Any())
            {
                return false;
            }

            return productosCorreo.Any(p =>
                string.Equals(p.TipoExclusiva?.Trim(), "MAD", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool EstaEnZonaExclusivaMadrid(string codPostal)
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
        /// <summary>NestoAPI#707: el de la persona de contacto cuyo correo se usa.</summary>
        public string Contacto { get; set; }
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
        public string Familia { get; set; }
        public string TipoExclusiva { get; set; }
    }

    #endregion
}
