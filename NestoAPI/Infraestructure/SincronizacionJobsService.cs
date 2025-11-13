using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Servicio con métodos estáticos para jobs de Hangfire de sincronización
    /// </summary>
    public class SincronizacionJobsService
    {
        /// <summary>
        /// Job para sincronizar productos pendientes desde nesto_sync
        /// Ejecutado por Hangfire cada 5 minutos
        /// </summary>
        public static async Task SincronizarProductos()
        {
            Console.WriteLine("🚀 [Hangfire] Iniciando sincronización de productos...");

            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;

                    var gestorSincronizacion = new GestorSincronizacion(db);
                    var sincronizacionEventWrapper = new SincronizacionEventWrapper(new GooglePubSubEventPublisher());
                    var gestorProductos = new GestorProductos(sincronizacionEventWrapper);
                    var productoService = new ProductoService();

                    bool resultado = await gestorSincronizacion.ProcesarTabla(
                        tabla: "Productos",
                        obtenerEntidades: async (registro) =>
                        {
                            // Buscar el producto en la base de datos
                            Producto producto = await db.Productos
                                .Include(p => p.Kits)
                                .Include(p => p.Familia1)
                                .Include(p => p.SubGruposProducto)
                                .SingleOrDefaultAsync(p => p.Número == registro.ModificadoId && p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO);

                            if (producto == null)
                            {
                                return new System.Collections.Generic.List<ProductoDTO>();
                            }

                            string productoId = registro.ModificadoId;

                            // Construir el ProductoDTO completo
                            ProductoDTO productoDTO = new ProductoDTO()
                            {
                                UrlFoto = await ProductoDTO.RutaImagen(productoId).ConfigureAwait(false),
                                PrecioPublicoFinal = await ProductoDTO.LeerPrecioPublicoFinal(productoId).ConfigureAwait(false),
                                UrlEnlace = await ProductoDTO.RutaEnlace(productoId).ConfigureAwait(false),
                                Producto = producto.Número?.Trim(),
                                Nombre = producto.Nombre?.Trim(),
                                Tamanno = producto.Tamaño,
                                UnidadMedida = producto.UnidadMedida?.Trim(),
                                Familia = producto.Familia1?.Descripción?.Trim(),
                                PrecioProfesional = (decimal)producto.PVP,
                                Estado = (short)producto.Estado,
                                Grupo = producto.Grupo,
                                Subgrupo = producto.SubGruposProducto?.Descripción?.Trim(),
                                RoturaStockProveedor = producto.RoturaStockProveedor,
                                CodigoBarras = producto.CodBarras?.Trim()
                            };

                            // Agregar kits si existen
                            foreach (var kit in producto.Kits)
                            {
                                productoDTO.ProductosKit.Add(new ProductoKit
                                {
                                    ProductoId = kit.NúmeroAsociado.Trim(),
                                    Cantidad = kit.Cantidad
                                });
                            }

                            // Agregar stocks si no es ficticio
                            if (!producto.Ficticio)
                            {
                                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Productos.ALMACEN_POR_DEFECTO));
                                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Productos.ALMACEN_TIENDA));
                                productoDTO.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Almacenes.ALCOBENDAS));
                            }

                            return new System.Collections.Generic.List<ProductoDTO> { productoDTO };
                        },
                        publicarEntidad: async (productoDTO, usuario) =>
                        {
                            await gestorProductos.PublicarProductoSincronizar(productoDTO, "Nesto viejo", usuario);
                        }
                    );

                    if (resultado)
                    {
                        Console.WriteLine("✅ [Hangfire] Sincronización de productos completada exitosamente");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ [Hangfire] Sincronización de productos completada con errores");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error en sincronización de productos: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// Job para sincronizar clientes pendientes desde nesto_sync
        /// (DESHABILITADO - Se usa Task Scheduler por ahora)
        /// </summary>
        public static async Task SincronizarClientes()
        {
            Console.WriteLine("🚀 [Hangfire] Iniciando sincronización de clientes...");

            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;

                    var gestorSincronizacion = new GestorSincronizacion(db);
                    var sincronizacionEventWrapper = new SincronizacionEventWrapper(new GooglePubSubEventPublisher());
                    var gestorClientes = new GestorClientes(
                        new ServicioGestorClientes(),
                        new ServicioAgencias(),
                        sincronizacionEventWrapper
                    );

                    bool resultado = await gestorSincronizacion.ProcesarTabla(
                        tabla: "Clientes",
                        obtenerEntidades: async (registro) =>
                        {
                            // Buscar todos los contactos del cliente en la base de datos
                            return await db.Clientes
                                .Where(c => c.Nº_Cliente == registro.ModificadoId && c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                                .OrderBy(c => c.Nº_Cliente)
                                .ThenByDescending(c => c.ClientePrincipal)
                                .ThenBy(c => c.Contacto)
                                .Include(c => c.PersonasContactoClientes1)
                                .ToListAsync();
                        },
                        publicarEntidad: async (cliente, usuario) =>
                        {
                            await gestorClientes.PublicarClienteSincronizar(cliente, "Nesto viejo", usuario);
                        }
                    );

                    if (resultado)
                    {
                        Console.WriteLine("✅ [Hangfire] Sincronización de clientes completada exitosamente");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ [Hangfire] Sincronización de clientes completada con errores");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error en sincronización de clientes: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }
    }
}
