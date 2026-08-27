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

                    // El precio público del mensaje se traduce a la INTENCIÓN que guarda Nesto en
                    // PrestashopProductos.PVP_IVA_Incluido. Va ANTES de la detección de cambios:
                    // un cambio solo de precio público no toca ningún campo de Productos y se
                    // perdería en el early-return de "sin cambios".
                    await ActualizarModoPrecioPublico(db, productoNesto, message);

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
        /// Cutover de precios con NestoSync 1.4.0 (26/08/2026): por el bus solo viajan los dos
        /// precios absolutos (profesional y público). Los modos NULL / -1 / fijo son internos de
        /// Nesto, así que cuando un sistema externo publica su precio público, aquí se deduce la
        /// intención (<see cref="ProductoDTO.InferirModoPrecioPublico"/>) y se guarda en
        /// PrestashopProductos.PVP_IVA_Incluido. Guarda con su propio SaveChanges porque tiene que
        /// ejecutarse aunque el resto del mensaje no cambie nada en Productos.
        /// </summary>
        private static async Task ActualizarModoPrecioPublico(NVEntities db, Producto productoNesto, ProductoSyncMessage message)
        {
            // Los mensajes de Nesto ("Nesto", "Nesto viejo") no enseñan nada: la intención ya está
            // en la tabla, que es de donde salió el precio del propio mensaje.
            string source = message.Source?.Trim() ?? string.Empty;
            if (source.StartsWith("Nesto", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (productoNesto == null || !message.PrecioPublicoFinal.HasValue || message.PrecioPublicoFinal.Value <= 0)
            {
                return;
            }
            decimal publico = message.PrecioPublicoFinal.Value;

            // El PVP del propio mensaje, que es la foto coherente con su público; el de la ficha
            // solo si el mensaje no lo trae.
            decimal pvp = message.PrecioProfesional ?? productoNesto.PVP ?? 0;
            if (pvp <= 0)
            {
                return;
            }

            decimal porcentajeIva = await ProductoDTO.LeerPorcentajeIvaProducto(db, productoNesto.IVA_Repercutido);
            decimal? modo = ProductoDTO.InferirModoPrecioPublico(publico, pvp, porcentajeIva);

            var fila = await db.PrestashopProductos
                .FirstOrDefaultAsync(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && pp.Número == productoNesto.Número);

            if (fila == null)
            {
                // Solo merece ficha si hay algo que recordar: el modo por defecto (NULL) sin más
                // datos es exactamente lo mismo que no tener fila.
                if (modo == null)
                {
                    return;
                }

                db.PrestashopProductos.Add(new PrestashopProducto
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Número = productoNesto.Número,
                    PVP_IVA_Incluido = modo,
                    // NestoAPI#411: un precio fijo que llega de la tienda ya está PUBLICADO allí;
                    // guardarlo sin visto bueno haría que la puerta lo derivara al 30 % en la
                    // siguiente publicación y cada ciclo de sync cambiaría el precio.
                    VistoBueno = modo > 0 ? true : (bool?)null,
                    Usuario = string.IsNullOrWhiteSpace(message.Usuario) ? "EXTERNAL_SYNC" : message.Usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }
            else
            {
                // #411: si el modo inferido es un precio fijo, además de guardarlo hay que dejarlo
                // con visto bueno (ver el comentario del alta); por eso el "sin cambios" exige
                // también que el visto bueno ya esté puesto.
                bool leFaltaVistoBueno = modo > 0 && fila.VistoBueno != true;
                if (fila.PVP_IVA_Incluido == modo && !leFaltaVistoBueno)
                {
                    return; // sin cambio de intención: no se ensucia ni auditoría ni fecha
                }

                fila.PVP_IVA_Incluido = modo;
                if (modo > 0)
                {
                    fila.VistoBueno = true;
                }
                fila.Usuario = string.IsNullOrWhiteSpace(message.Usuario) ? "EXTERNAL_SYNC" : message.Usuario;
                fila.Fecha_Modificación = DateTime.Now;
            }

            _ = await db.SaveChangesAsync();
            Console.WriteLine($"💶 Producto {productoNesto.Número?.Trim()}: modo de precio público ← " +
                $"{(modo == null ? "NULL (30 %)" : modo == Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL ? "-1 (mismo que profesional)" : $"fijo {modo}")}" +
                $" (público={publico}, PVP={pvp}, Source={source})");
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
