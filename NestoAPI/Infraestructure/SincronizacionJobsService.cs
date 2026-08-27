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

                            // Referencias reservadas ("no usar", altas a medio hacer): existen para que
                            // nadie ocupe el número, pero no tienen PVP ni Estado. Publicarlas no se
                            // puede —el DTO exige ambos— y hasta ahora reventaban aquí cada 5 minutos,
                            // reintentándose para siempre y llamando en balde a la API de PrestaShop.
                            // Se saltan: cuando se rellene la ficha, el trigger las vuelve a encolar.
                            if (!TieneDatosMinimosParaSincronizar(producto))
                            {
                                Console.WriteLine($"⚠️ Producto {registro.ModificadoId} sin PVP o sin Estado (referencia reservada): no se sincroniza");
                                return new System.Collections.Generic.List<ProductoDTO>();
                            }

                            string productoId = registro.ModificadoId;

                            // Construir el ProductoDTO completo
                            ProductoDTO productoDTO = new ProductoDTO()
                            {
                                UrlFoto = await ProductoDTO.RutaImagen(productoId).ConfigureAwait(false),
                                PrecioPublicoFinal = await ProductoDTO.LeerPrecioPublicoFinal(productoId, db).ConfigureAwait(false),
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

                            await ProductoDTO.CargarTextosTienda(productoDTO, db).ConfigureAwait(false);
                            await ProductoDTO.CargarTipoIva(productoDTO, db, producto.IVA_Repercutido).ConfigureAwait(false);
                            await ProductoDTO.CargarCategoriasSecundarias(productoDTO, db).ConfigureAwait(false);
                            await ProductoDTO.CargarDescuentosWeb(productoDTO, db, producto.PVP).ConfigureAwait(false);

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
        /// ¿Tiene la ficha lo mínimo para armar el ProductoDTO? PVP y Estado se copian al mensaje sin
        /// poder ser nulos ((decimal)PVP, (short)Estado), así que sin ellos la publicación lanza
        /// InvalidOperationException y el registro de Nesto_sync se queda pendiente para siempre.
        /// </summary>
        internal static bool TieneDatosMinimosParaSincronizar(Producto producto)
        {
            return producto != null && producto.PVP.HasValue && producto.Estado.HasValue;
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
