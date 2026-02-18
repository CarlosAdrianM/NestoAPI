using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Data.Entity;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Handler de sincronización para la tabla Productos
    /// Procesa actualizaciones de productos desde sistemas externos
    /// </summary>
    public class ProductosSyncHandler : ISyncTableHandler<ProductoSyncMessage>
    {
        private readonly ProductoChangeDetector _changeDetector;

        public string TableName => "Productos";

        public ProductosSyncHandler()
        {
            _changeDetector = new ProductoChangeDetector();
        }

        // Implementación base polimórfica
        Task<bool> ISyncTableHandlerBase.HandleAsync(SyncMessageBase message)
        {
            return HandleAsync(message as ProductoSyncMessage);
        }

        string ISyncTableHandlerBase.GetMessageKey(SyncMessageBase message)
        {
            return GetMessageKey(message as ProductoSyncMessage);
        }

        string ISyncTableHandlerBase.GetLogInfo(SyncMessageBase message)
        {
            return GetLogInfo(message as ProductoSyncMessage);
        }

        public SyncMessageBase Deserialize(string json, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<ProductoSyncMessage>(json, options);
        }

        // Implementación tipada
        public string GetMessageKey(ProductoSyncMessage message)
        {
            var producto = message?.Producto?.Trim() ?? "NULL";
            var source = message?.Source?.Trim() ?? "NULL";
            return $"PRODUCTO|{producto}|{source}";
        }

        public string GetLogInfo(ProductoSyncMessage message)
        {
            var info = $"Producto {message?.Producto?.Trim() ?? "NULL"}";

            if (!string.IsNullOrEmpty(message?.Nombre))
            {
                info += $" ({message.Nombre.Trim()})";
            }

            if (!string.IsNullOrEmpty(message?.Source))
            {
                info += $", Source={message.Source}";
            }

            if (message?.Estado.HasValue == true)
            {
                info += $", Estado={message.Estado.Value}";
            }

            if (message?.PrecioProfesional.HasValue == true)
            {
                info += $", PVP={message.PrecioProfesional.Value}";
            }

            return info;
        }

        public async Task<bool> HandleAsync(ProductoSyncMessage message)
        {
            try
            {
                if (message == null)
                {
                    Console.WriteLine("⚠️ Mensaje nulo, omitiendo");
                    return false;
                }

                var productoId = message.Producto?.Trim();

                if (string.IsNullOrEmpty(productoId))
                {
                    Console.WriteLine($"⚠️ Producto vacío en el mensaje");
                    return false;
                }

                Console.WriteLine($"🔍 Procesando Producto {productoId} (Nombre={message.Nombre}, Source={message.Source})");

                using (var db = new NVEntities())
                {
                    // Buscar el producto en Nesto (empresa por defecto "1")
                    var productoNesto = await db.Productos
                        .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                && p.Número.Trim() == productoId)
                        .FirstOrDefaultAsync();

                    // Detectar cambios
                    var cambios = _changeDetector.DetectarCambios(productoNesto, message);

                    if (!cambios.Any())
                    {
                        Console.WriteLine($"⚪ Producto {productoId}: Sin cambios detectados, NO SE ACTUALIZA");
                        return true;
                    }

                    Console.WriteLine($"🔄 Producto {productoId}: Cambios detectados:");
                    foreach (var cambio in cambios)
                    {
                        Console.WriteLine($"   - {cambio}");
                    }

                    if (productoNesto == null)
                    {
                        Console.WriteLine($"⚠️ Producto {productoId} no existe en Nesto. No se puede crear desde sistemas externos.");
                        return false;
                    }

                    // Actualizar el producto
                    ActualizarProductoDesdeExterno(productoNesto, message);
                    _ = await db.SaveChangesAsync();

                    Console.WriteLine($"✅ Producto {productoId} actualizado exitosamente");

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error procesando producto: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Actualiza los campos del producto de Nesto con los datos del sistema externo
        /// Solo actualiza campos que vengan informados en el mensaje externo
        /// </summary>
        private void ActualizarProductoDesdeExterno(Producto productoNesto, ProductoSyncMessage productoExterno)
        {
            // Nombre del producto
            if (!string.IsNullOrWhiteSpace(productoExterno.Nombre))
            {
                productoNesto.Nombre = productoExterno.Nombre;
            }

            // PVP (Precio Profesional)
            if (productoExterno.PrecioProfesional.HasValue)
            {
                productoNesto.PVP = productoExterno.PrecioProfesional.Value;
            }

            // Estado del producto
            if (productoExterno.Estado.HasValue)
            {
                productoNesto.Estado = productoExterno.Estado.Value;
            }

            // Rotura de stock de proveedor
            if (productoExterno.RoturaStockProveedor.HasValue)
            {
                productoNesto.RoturaStockProveedor = productoExterno.RoturaStockProveedor.Value;
            }

            // Código de barras
            if (!string.IsNullOrWhiteSpace(productoExterno.CodigoBarras))
            {
                productoNesto.CodBarras = productoExterno.CodigoBarras;
            }

            // Actualizar campos de auditoría
            productoNesto.Fecha_Modificación = DateTime.Now;
            productoNesto.Usuario = string.IsNullOrWhiteSpace(productoExterno.Usuario)
                ? "EXTERNAL_SYNC"
                : productoExterno.Usuario;
        }
    }
}
