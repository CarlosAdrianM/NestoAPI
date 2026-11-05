using Hangfire;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Concurrent;
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
        // 23/09/26 (carga inicial NestoAPI#498): los jobs de Nesto_sync corren cada 5 minutos y cada
        // pasada se lleva TODA la cola pendiente (lotes de 50 con 5 s de pausa). Si una pasada dura
        // más de 5 minutos (una carga masiva), Hangfire arrancaba otra que volvía a leer las mismas
        // filas aún sin marcar y las publicaba otra vez: mensajes duplicados a Odoo y carga
        // multiplicada. Si la pasada anterior sigue viva, la nueva no hace nada; los pendientes los
        // recoge la que está en marcha o la siguiente.
        //
        // NestoAPI#820 (incidente 04-05/11/26): se guarda CUÁNDO empezó la pasada. El candado se suelta siempre en el finally,
        // pero si la pasada se queda colgada sin terminar (una llamada externa que no vuelve, un hilo que Hangfire abandona al
        // parar) el nombre se quedaría ocupado hasta reciclar el pool. Pasados MinutosCandadoCaducado se da por muerta, se deja
        // entrar a la nueva y se avisa en ELMAH. Con el plazo de Pub/Sub (60 s) una pasada viva no se queda quieta tanto rato.
        private static readonly ConcurrentDictionary<string, DateTime> jobsEnMarcha = new ConcurrentDictionary<string, DateTime>();

        /// <summary>
        /// NestoAPI#820: minutos tras los que una pasada que sigue «en marcha» se da por muerta. appSettings
        /// <c>Sincronizacion:MinutosCandadoCaducado</c>; sin la clave, 30.
        /// </summary>
        internal const string CLAVE_MINUTOS_CANDADO = "Sincronizacion:MinutosCandadoCaducado";
        internal const int MINUTOS_CANDADO_POR_DEFECTO = 30;

        /// <summary>NestoAPI#820: cola de Hangfire de las sincronizaciones e integraciones externas (su propio servidor en Startup).</summary>
        public const string COLA = "sincronizacion";

        internal static bool IntentarEmpezar(string job) => IntentarEmpezar(job, DateTime.UtcNow, TimeSpan.FromMinutes(MinutosCandado()), out _);

        /// <param name="caducado">True si había otra pasada marcada en marcha desde hace más de <paramref name="caducidad"/> y se ha
        /// dado por muerta (la nueva entra).</param>
        /// <returns>True si esta pasada puede trabajar.</returns>
        internal static bool IntentarEmpezar(string job, DateTime ahora, TimeSpan caducidad, out TimeSpan? caducado)
        {
            caducado = null;
            if (jobsEnMarcha.TryAdd(job, ahora))
            {
                return true;
            }
            if (!jobsEnMarcha.TryGetValue(job, out DateTime desde) || ahora - desde <= caducidad)
            {
                return false;
            }
            // Solo uno se queda el candado caducado: el que lo cambia sobre el valor que ha leído.
            if (!jobsEnMarcha.TryUpdate(job, ahora, desde))
            {
                return false;
            }
            caducado = ahora - desde;
            return true;
        }

        internal static void Terminar(string job) => jobsEnMarcha.TryRemove(job, out _);

        /// <summary>
        /// NestoAPI#820: suelta el candado solo si sigue siendo el de esta pasada. Una pasada dada por muerta que al final vuelva
        /// no debe soltar el de la nueva.
        /// </summary>
        internal static bool Terminar(string job, DateTime empezo) =>
            ((System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, DateTime>>)jobsEnMarcha)
                .Remove(new System.Collections.Generic.KeyValuePair<string, DateTime>(job, empezo));

        private static int MinutosCandado()
        {
            return int.TryParse(System.Configuration.ConfigurationManager.AppSettings[CLAVE_MINUTOS_CANDADO], out int minutos) && minutos > 0
                ? minutos
                : MINUTOS_CANDADO_POR_DEFECTO;
        }

        private static async Task EjecutarSinSolapar(string job, Func<Task> trabajo)
        {
            DateTime empezo = DateTime.UtcNow;
            if (!IntentarEmpezar(job, empezo, TimeSpan.FromMinutes(MinutosCandado()), out TimeSpan? caducado))
            {
                Console.WriteLine($"⏭️ [Hangfire] {job}: sigue en marcha la pasada anterior; esta no hace nada.");
                return;
            }
            if (caducado.HasValue)
            {
                ElmahHelper.Log(new Exception($"[Hangfire #820] {job}: la pasada anterior llevaba {Math.Round(caducado.Value.TotalMinutes)} minutos " +
                    "marcada en marcha sin terminar; se da por muerta y empieza una nueva."), USUARIO_SISTEMA);
            }
            try
            {
                await trabajo().ConfigureAwait(false);
            }
            catch (ServicioExternoSinRespuestaException ex)
            {
                // NestoAPI#820: sin esto el fallo solo quedaba en Hangfire; el 04/11 no había nada en ELMAH.
                ElmahHelper.Log(new Exception($"[Hangfire #820] {job}: {ex.Message}", ex), USUARIO_SISTEMA);
                throw;
            }
            finally
            {
                _ = Terminar(job, empezo);
            }
        }

        internal const string USUARIO_SISTEMA = "Sistema (sincronización)";

        [Queue(COLA)]
        public static Task SincronizarProductos() => EjecutarSinSolapar("sincronizar-productos", SincronizarProductosSinCandado);

        [Queue(COLA)]
        public static Task SincronizarClientes() => EjecutarSinSolapar("sincronizar-clientes", SincronizarClientesSinCandado);

        /// <summary>
        /// Job para sincronizar productos pendientes desde nesto_sync
        /// Ejecutado por Hangfire cada 5 minutos
        /// </summary>
        private static async Task SincronizarProductosSinCandado()
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

                            // NestoAPI#432: la puerta de publicación decide si este producto debe
                            // viajar a la tienda (dentro va también la comprobación de referencia
                            // reservada sin PVP/Estado de siempre). Si no pasa, el registro se da
                            // por procesado: cuando algo cambie, el trigger lo vuelve a encolar y
                            // la puerta lo reevalúa con los datos de ese momento.
                            ResultadoPuertaPublicacion puerta = await PuertaPublicacionTienda
                                .ConstruirParaPublicarSiPasa(producto, db, productoService).ConfigureAwait(false);
                            if (!puerta.Publicable)
                            {
                                Console.WriteLine($"⛔ Producto {registro.ModificadoId} no publicable: {puerta.Motivo}");
                                return new System.Collections.Generic.List<ProductoDTO>();
                            }

                            return new System.Collections.Generic.List<ProductoDTO> { puerta.Dto };
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
        private static async Task SincronizarClientesSinCandado()
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

                    // NestoAPI#498: las fechas de compras son del cliente, no del contacto: se
                    // calculan una vez por registro y valen para todos sus contactos
                    // (ProcesarTabla recorre los registros de uno en uno).
                    FechasComprasCliente fechasCompras = null;

                    bool resultado = await gestorSincronizacion.ProcesarTabla(
                        tabla: "Clientes",
                        obtenerEntidades: async (registro) =>
                        {
                            fechasCompras = CalculoFechasComprasCliente.Buscar(
                                await gestorClientes.LeerFechasCompras(new[] { registro.ModificadoId }),
                                registro.ModificadoId);

                            // Buscar todos los contactos del cliente en la base de datos
                            return await db.Clientes
                                .Where(c => c.Nº_Cliente == registro.ModificadoId && c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                                .OrderBy(c => c.Nº_Cliente)
                                .ThenByDescending(c => c.ClientePrincipal)
                                .ThenBy(c => c.Contacto)
                                // NestoAPI#504: PublicarClienteSincronizar lee PersonasContactoClientes (sin el 1;
                                // las dos navegaciones cuelgan de la misma FK duplicada) y el Mail del vendedor:
                                // con el lazy loading apagado, lo que no se incluye aquí viaja vacío.
                                .Include(c => c.PersonasContactoClientes)
                                .Include(c => c.Vendedore)
                                .ToListAsync();
                        },
                        publicarEntidad: async (cliente, usuario) =>
                        {
                            await gestorClientes.PublicarClienteSincronizar(cliente, fechasCompras, "Nesto viejo", usuario);
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
