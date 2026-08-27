using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Gestor de productos para lógica de negocio relacionada con productos
    /// </summary>
    public class GestorProductos : IGestorProductos
    {
        private readonly SincronizacionEventWrapper _sincronizacionEventWrapper;

        public GestorProductos(SincronizacionEventWrapper sincronizacionEventWrapper)
        {
            _sincronizacionEventWrapper = sincronizacionEventWrapper;
        }

        /// <summary>
        /// Publica un mensaje de sincronización de producto al sistema externo
        /// </summary>
        /// <param name="productoDTO">DTO del producto con toda la información completa</param>
        /// <param name="source">Origen del mensaje (ej: "Nesto viejo")</param>
        /// <param name="usuario">Usuario que realizó la modificación</param>
        public async Task PublicarProductoSincronizar(ProductoDTO productoDTO, string source = "Nesto", string usuario = null)
        {
            // Log para rastrear de dónde viene cada publicación
            var kitsInfo = productoDTO.ProductosKit?.Count > 0
                ? string.Join(", ", productoDTO.ProductosKit)
                : "ninguno";
            var stocksInfo = productoDTO.Stocks?.Count > 0
                ? $"{productoDTO.Stocks.Count} almacenes"
                : "sin stocks";
            string usuarioInfo = !string.IsNullOrWhiteSpace(usuario) ? $", Usuario={usuario}" : "";

            Console.WriteLine($"📤 Publicando mensaje: Producto {productoDTO.Producto?.Trim()}, Source={source}{usuarioInfo}, Kits=[{kitsInfo}], Stocks=[{stocksInfo}]");

            // Publicar evento de sincronización con la estructura completa del ProductoDTO
            var message = new ProductoSyncMessage
            {
                Tabla = "Productos",
                Source = source,
                Usuario = usuario,
                Producto = productoDTO.Producto?.Trim(),
                Nombre = productoDTO.Nombre?.Trim(),
                Tamanno = productoDTO.Tamanno,
                UnidadMedida = productoDTO.UnidadMedida?.Trim(),
                Familia = productoDTO.Familia?.Trim(),
                PrecioProfesional = productoDTO.PrecioProfesional,
                PrecioPublicoFinal = productoDTO.PrecioPublicoFinal,
                Estado = productoDTO.Estado,
                Grupo = productoDTO.Grupo?.Trim(),
                Subgrupo = productoDTO.Subgrupo?.Trim(),
                UrlEnlace = productoDTO.UrlEnlace?.Trim(),
                UrlFoto = productoDTO.UrlFoto?.Trim(),
                RoturaStockProveedor = productoDTO.RoturaStockProveedor,
                ClasificacionMasVendidos = productoDTO.ClasificacionMasVendidos,
                CodigoBarras = productoDTO.CodigoBarras?.Trim(),
                // Textos de la tienda: desde el cutover del 26/08/2026 viajan aquí (el mensaje de
                // tabla PrestashopProductos se retiró). null = no tocar el texto de la tienda.
                NombrePersonalizado = productoDTO.NombrePersonalizado,
                Descripcion = productoDTO.Descripcion,
                DescripcionBreve = productoDTO.DescripcionBreve,
                TipoIva = productoDTO.TipoIva,
                PorcentajeIva = productoDTO.PorcentajeIva,
                DescuentoPorcentajeProfesional = productoDTO.DescuentoPorcentajeProfesional,
                DescuentoPorcentajePublico = productoDTO.DescuentoPorcentajePublico,
                ProductosKit = productoDTO.ProductosKit?.Select(k => k.ProductoId).ToList(),
                // NestoAPI#412: la composición CON cantidades, para que Odoo pueda construir la
                // BoM del kit (la lista plana de arriba se queda por compatibilidad).
                ComponentesKit = productoDTO.ProductosKit?.ToList(),
                CategoriasSecundarias = productoDTO.CategoriasSecundarias?.ToList(),
                Stocks = productoDTO.Stocks?.ToList()
            };

            // Pasar el objeto directamente - GooglePubSubEventPublisher se encarga de serializar
            await _sincronizacionEventWrapper.PublishSincronizacionEventAsync("sincronizacion-tablas", message);
        }
    }
}
