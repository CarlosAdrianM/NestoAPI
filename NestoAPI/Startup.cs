using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.Owin.Security.Jwt;
using Microsoft.Owin.Security.OAuth;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Alquileres;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Comisiones;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PlanesVentajas;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Providers;
using Newtonsoft.Json.Serialization;
using Owin;
using System;
using System.Net;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Http.Formatting;
using System.Web.Http;
using Hangfire;
using Hangfire.SqlServer;

namespace NestoAPI
{
    public class Startup
    {

        public void Configuration(IAppBuilder app)
        {
            try
            {
                ConfigurarTlsDelProceso();

                _ = app.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

                HttpConfiguration httpConfig = new HttpConfiguration();

                // Configurar el contenedor de dependencias
                IServiceProvider serviceProvider = ConfigureServices();
                // Configurar el DependencyResolver
                httpConfig.DependencyResolver = new DependencyResolver(serviceProvider);

                // Configurar WebApi y pasarle el contenedor de dependencias
                WebApiConfig.Register(httpConfig);

                //// Configurar OWIN para usar el contenedor de dependencias
                //app.Use((context, next) =>
                //{
                //    // Establecer el proveedor de servicios en el contexto de OWIN
                //    context.Set<IServiceProvider>(serviceProvider);
                //    return next();
                //});

                ConfigureOAuthTokenGeneration(app);
                ConfigureOAuthTokenConsumption(app);

                // Configurar Hangfire para jobs programados
                ConfigureHangfire(app);

                ConfigureWebApi(httpConfig);

                _ = app.UseWebApi(httpConfig);
            }
            catch (Exception ex)
            {
                // Escribir el error en el log de eventos de Windows
                System.Diagnostics.EventLog.WriteEntry("Application",
                    $"Error en NestoAPI Startup: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message}",
                    System.Diagnostics.EventLogEntryType.Error);

                // Re-lanzar para que se vea en Visual Studio
                throw;
            }
        }

        private void ConfigureOAuthTokenGeneration(IAppBuilder app)
        {
            // Configure the db context and user manager to use a single instance per request
            _ = app.CreatePerOwinContext(ApplicationDbContext.Create);
            _ = app.CreatePerOwinContext<ApplicationUserManager>(ApplicationUserManager.Create);
            _ = app.CreatePerOwinContext<ApplicationRoleManager>(ApplicationRoleManager.Create);

            OAuthAuthorizationServerOptions OAuthServerOptions = new OAuthAuthorizationServerOptions()
            {
                //For Dev enviroment only (on production should be AllowInsecureHttp = false)
                AllowInsecureHttp = true,
                TokenEndpointPath = new PathString("/oauth/token"),
                AccessTokenExpireTimeSpan = TimeSpan.FromDays(30),
                // NestoAPI#188: refresh_token OAuth2 para el flow de NestoApp (grant password).
                // El access_token sigue durando 30 días — no acortar hasta que NestoApp#117
                // (refresh transparente) esté desplegado, o los usuarios con token de más
                // de 30 min recibirán 401 sin saber refrescar.
                RefreshTokenProvider = new SimpleRefreshTokenProvider(TimeSpan.FromDays(90)),
                Provider = new CustomOAuthProvider(),
                //AccessTokenFormat = new CustomJwtFormat("http://localhost:53364")
                AccessTokenFormat = new CustomJwtFormat("carlos")
            };

            // OAuth 2.0 Bearer Access Token Generation
            _ = app.UseOAuthAuthorizationServer(OAuthServerOptions);
        }

        private void ConfigureOAuthTokenConsumption(IAppBuilder app)
        {

            //var issuer = "http://localhost:53364";
            string issuer = "carlos";
            string audienceId = ConfigurationManager.AppSettings["as:AudienceId"];
            byte[] audienceSecret = TextEncodings.Base64Url.Decode(ConfigurationManager.AppSettings["as:AudienceSecret"]);

            // Api controllers with an [Authorize] attribute will be validated with JWT
            _ = app.UseJwtBearerAuthentication(
                new JwtBearerAuthenticationOptions
                {
                    AuthenticationMode = AuthenticationMode.Active,
                    AllowedAudiences = new[] { audienceId },
                    IssuerSecurityKeyProviders = new IIssuerSecurityKeyProvider[] {
                        new SymmetricKeyIssuerSecurityKeyProvider(issuer, audienceSecret)
                    }
                    // IMPORTANTE: NO usar TokenValidationParameters aquí.
                    // Si se proporciona TokenValidationParameters, OWIN ignora AllowedAudiences
                    // e IssuerSecurityKeyProviders, causando que todos los tokens se rechacen.
                    // Ver: StartupJwtConfigurationTests.cs para más detalles.
                    // El mapeo de usuario para ELMAH se hace en UserSyncHandler.
                });
        }

        private IServiceProvider ConfigureServices()
        {
            ServiceCollection services = new ServiceCollection();

            // Añadir logging
            _ = services.AddLogging(configure =>
            {
                _ = configure.AddEventLog(); // Opcionalmente, o AddEventLog en servidor
                _ = configure.AddDebug();   // Para ver en salida de VS
            });

            // Registrar tus dependencias aquí
            _ = services.AddScoped<IGestorClientes, GestorClientes>();
            _ = services.AddScoped<IServicioGestorClientes, ServicioGestorClientes>();
            _ = services.AddScoped<IServicioAgencias, ServicioAgencias>();
            _ = services.AddScoped<ISincronizacionEventPublisher, GooglePubSubEventPublisher>();
            _ = services.AddScoped<SincronizacionEventWrapper>();
            _ = services.AddScoped<IServicioVendedores, ServicioVendedores>();
            _ = services.AddScoped<ClientesController>();
            _ = services.AddScoped<IGestorAlbaranesVenta, GestorAlbaranesVenta>();
            _ = services.AddScoped<IServicioAlbaranesVenta, ServicioAlbaranesVenta>();
            _ = services.AddScoped<IServicioVideos, ServicioVideos>();
            _ = services.AddScoped<IServicioCorreoElectronico, ServicioCorreoElectronico>();
            _ = services.AddScoped<IServicioPlantillaVenta, ServicioPlantillaVenta>();

            // Servicios de Facturación de Rutas
            _ = services.AddScoped<IServicioPedidosParaFacturacion, ServicioPedidosParaFacturacion>();
            _ = services.AddScoped<IGestorFacturacionRutas, GestorFacturacionRutas>();
            _ = services.AddScoped<IServicioTraspasoEmpresa, ServicioTraspasoEmpresa>();
            _ = services.AddScoped<IServicioNotasEntrega, ServicioNotasEntrega>();
            _ = services.AddScoped<IServicioExtractoRuta, ServicioExtractoRuta>();

            // Servicios de Pagos y Redsys (Issues #93, #92, #59)
            _ = services.AddScoped<IRedsysService, RedsysService>();
            _ = services.AddScoped<IServicioReclamacionDeuda, ServicioReclamacionDeuda>();
            _ = services.AddScoped<IServicioPagos, ServicioPagos>();
            _ = services.AddScoped<IContabilidadService, ContabilidadService>();
            _ = services.AddScoped<ILectorParametrosUsuario, LectorParametrosUsuario>();

            // Registro centralizado de errores de clientes en ELMAH (ErroresController)
            _ = services.AddScoped<ILogService, ElmahLogService>();

            // Servicios de Notificaciones Push (Issue #108)
            _ = services.AddScoped<IServicioNotificacionesPush, ServicioNotificacionesPush>();

            // Servicios de Informes (Nesto#340 Fase 1A)
            _ = services.AddScoped<IInformesService, InformesService>();

            // Lecturas del panel de Comisiones del cliente (Nesto#340 Fase 1B)
            _ = services.AddScoped<IComisionesLecturaService, ComisionesLecturaService>();

            // Planes de Ventajas - CRUD para el cliente (Nesto#340 Fase 1B)
            _ = services.AddScoped<IPlanesVentajasService, PlanesVentajasService>();

            // Lista de productos en alquiler para el cliente (Nesto#340 Fase 1C.1)
            _ = services.AddScoped<IProductosAlquilerService, ProductosAlquilerService>();

            // Validación de "Servir junto" (NestoAPI#161)
            _ = services.AddScoped<IServicioValidarServirJunto, ServicioValidarServirJunto>();

            // Servicios de sincronización bidireccional (External Systems <-> Nesto)
            // Push Subscription: usa SyncWebhookController
            _ = services.AddSingleton<ISyncTableHandlerBase, ClientesSyncHandler>();
            _ = services.AddSingleton<ISyncTableHandlerBase, ProductosSyncHandler>();
            _ = services.AddSingleton<ISyncTableHandlerBase, PrestashopProductosSyncHandler>();
            _ = services.AddSingleton<SyncTableRouter>(sp =>
            {
                var handlers = sp.GetServices<ISyncTableHandlerBase>();
                return new SyncTableRouter(handlers);
            });
            _ = services.AddScoped<MessageRetryManager>(sp =>
            {
                var db = new NVEntities();
                return new MessageRetryManager(db);
            });
            _ = services.AddScoped<SyncWebhookController>();

            // Registrar el contexto de la base de datos
            _ = services.AddScoped<DbContext>(_ => new ApplicationDbContext());

            // Registrar los controladores de Web API
            _ = services.AddControllersAsServices(typeof(Startup).Assembly);

            // Construir el proveedor de servicios
            return services.BuildServiceProvider();
        }

        private void ConfigureWebApi(HttpConfiguration config)
        {
            //config.MapHttpAttributeRoutes();

            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            JsonMediaTypeFormatter jsonFormatter = config.Formatters.OfType<JsonMediaTypeFormatter>().First();
            jsonFormatter.SerializerSettings.ContractResolver = new DefaultContractResolver();

            //Elimino que el sistema devuelva en XML, sólo trabajaremos con JSON
            _ = config.Formatters.Remove(System.Web.Http.GlobalConfiguration.Configuration.Formatters.XmlFormatter);
        }

        private void ConfigureHangfire(IAppBuilder app)
        {
#if DEBUG
            Console.WriteLine("⚠️ Hangfire deshabilitado en modo DEBUG");
            System.Diagnostics.EventLog.WriteEntry("Application",
                "Hangfire deshabilitado en modo DEBUG (desarrollo)",
                System.Diagnostics.EventLogEntryType.Information);
            return;
#endif

            try
            {
                // Obtener connection string de Web.config
                string connectionString = ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString;

                // Configurar Hangfire para usar SQL Server
                Hangfire.GlobalConfiguration.Configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.Zero,
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    });

                // Configurar el dashboard de Hangfire en /hangfire
                // IMPORTANTE: Restringir acceso en producción con DashboardAuthorizationFilter
                app.UseHangfireDashboard("/hangfire", new DashboardOptions
                {
                    Authorization = new[] { new HangfireAuthorizationFilter() }
                });

                // Iniciar el servidor de Hangfire
                app.UseHangfireServer(new BackgroundJobServerOptions
                {
                    WorkerCount = 1 // Solo un worker para evitar procesamiento duplicado
                });

                // Configurar jobs recurrentes
                ConfigurarJobsRecurrentes();

                Console.WriteLine("✅ Hangfire configurado correctamente");
                System.Diagnostics.EventLog.WriteEntry("Application",
                    "Hangfire configurado correctamente en NestoAPI. Dashboard disponible en /hangfire",
                    System.Diagnostics.EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al configurar Hangfire: {ex.Message}");
                System.Diagnostics.EventLog.WriteEntry("Application",
                    $"Error al configurar Hangfire: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
        }

        /// <summary>
        /// NestoAPI#404: fija el protocolo TLS del proceso UNA VEZ, al arrancar.
        ///
        /// El Web.config declara <c>&lt;httpRuntime targetFramework="4.5"&gt;</c>, lo que activa el modo
        /// de compatibilidad de .NET 4.5: ahí el valor por defecto de
        /// <c>ServicePointManager.SecurityProtocol</c> es <b>SSL 3.0 + TLS 1.0</b> (a partir de 4.6
        /// pasó a ser moderno). Y <c>ServicePointManager</c> es GLOBAL al proceso.
        ///
        /// Hasta ahora cada llamada saliente lo fijaba por su cuenta justo antes de usarlo (12
        /// sitios repartidos), menos AmazonFeedsGateway, que no lo fija nunca. Resultado: si tras
        /// un reinicio del app pool la PRIMERA llamada HTTPS saliente era la de Amazon, salía con
        /// TLS 1.0 y la SP-API la rechazaba con "No se puede crear un canal seguro SSL/TLS". En
        /// cuanto cualquier otro camino (Verifacti, Redsys, AEAT, agencias...) fijaba Tls12,
        /// quedaba arreglado para todo el proceso y Amazon volvía a funcionar solo.
        ///
        /// Caso real: despliegue del 24/08/26. El app pool arrancó a las 13:28:20 y a las 13:30:06
        /// fallaron a la vez las DOS rutas de Amazon (el botón "Subir factura" de Enrique y el job
        /// AmazonFacturasJobs); ni una sola vez antes en 45 días ni después. Es una carrera que
        /// reaparece en CADA reinicio, y en esa ventana la facturación de Amazon queda rota.
        ///
        /// Se deja Tls12 porque es exactamente lo que ya fijan los otros 12 sitios y lo que
        /// funciona hoy en producción. Los 12 pasan a ser redundantes y se pueden ir quitando.
        /// El arreglo de fondo sería subir el targetFramework a 4.8, pero eso cambia muchos
        /// comportamientos de ASP.NET y merece su propio cambio.
        /// </summary>
        internal static void ConfigurarTlsDelProceso()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private void ConfigurarJobsRecurrentes()
        {
            // Sincronización de Productos cada 5 minutos
            RecurringJob.AddOrUpdate(
                "sincronizar-productos",
                () => SincronizacionJobsService.SincronizarProductos(),
                "*/5 * * * *", // Cron: cada 5 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'sincronizar-productos' configurado (cada 5 minutos)");

            // NestoAPI#410: a las 2:00, encolar en Nesto_sync los productos con movimientos de
            // stock de los últimos 2 días, para que los stocks de Odoo y PrestaShop no se desvíen.
            // Solo encola: la publicación la hace 'sincronizar-productos' en sus pasadas.
            // Si se pasa a cadencia horaria, cambiar el cron Y la ventana a la vez (ver el
            // comentario de HORAS_VENTANA_NOCTURNA).
            RecurringJob.AddOrUpdate(
                "sincronizar-stocks-nocturno",
                () => Infraestructure.Sincronizacion.SincronizacionStocksJobsService.EncolarProductosConMovimientos(
                    Infraestructure.Sincronizacion.SincronizacionStocksJobsService.HORAS_VENTANA_NOCTURNA),
                "0 2 * * *", // Cron: todos los días a las 2:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sincronizar-stocks-nocturno' configurado (diario a las 2:00)");

            // Job de correos post-compra: se ejecuta los miércoles a las 20:30
            // Issue #74: Sistema de correos automáticos con videos personalizados post-compra
            RecurringJob.RemoveIfExists("correos-postcompra-procesar-albaranes"); // Eliminar job viejo (diario)
            RecurringJob.AddOrUpdate(
                "correos-postcompra-semanal",
                () => CorreosPostCompraJobsService.ProcesarCorreosSemanales(),
                "30 20 * * 3", // Cron: miércoles a las 20:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'correos-postcompra-semanal' configurado (miércoles a las 20:30)");

            // Issue #137: Informe semanal de clientes nuevos por vendedor
            RecurringJob.AddOrUpdate(
                "informe-clientes-nuevos-semanal",
                () => InformeClientesNuevosJobsService.ProcesarInformeSemanal(),
                "0 9 * * 5", // Cron: viernes a las 9:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'informe-clientes-nuevos-semanal' configurado (viernes a las 9:00)");

            // NestoAPI#225: rotación automática del client_secret LWA de Amazon SP-API.
            // Diario a las 7:00: sondea la cola SQS (persiste secretos nuevos) y rota si el
            // secreto almacenado está cerca de caducar. No hace nada hasta ~15 días antes de caducar.
            RecurringJob.AddOrUpdate(
                "amazon-rotacion-credenciales",
                () => AmazonCredencialRotacionJobsService.ProcesarRotacionCredenciales(),
                "0 7 * * *", // Cron: todos los días a las 7:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'amazon-rotacion-credenciales' configurado (diario a las 7:00)");

            // Diario a las 6:30: registra en ComparativaAgenciaSombra qué envíos reales habría ganado
            // cada agencia SOMBRA (p.ej. CTT) y a qué coste, para evaluar si negociar con ella. No
            // hace nada si no hay agencias sombra (AgenciasTransporte.EsSombra = 1).
            RecurringJob.AddOrUpdate(
                "comparativa-agencia-sombra",
                () => ComparativaAgenciaSombraJobsService.ProcesarComparativaDiaria(),
                "30 6 * * *", // Cron: todos los días a las 6:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'comparativa-agencia-sombra' configurado (diario a las 6:30)");

            // Cada 2 horas: poll de seguimiento de envíos (#248). Actualiza Estado (Entregado/Incidentado)
            // y FechaEntrega real consultando a cada agencia con gestión remota (hoy Innovatrans). Acotado
            // a los envíos desde una fecha de corte fija (SeguimientoEnviosJobsService.FECHA_CORTE), así
            // que no recorre el histórico antiguo de GLS.
            RecurringJob.AddOrUpdate(
                "seguimiento-envios",
                () => SeguimientoEnviosJobsService.ProcesarSeguimientosAsync(),
                "0 */2 * * *", // Cron: cada 2 horas
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'seguimiento-envios' configurado (cada 2 horas)");

            // NestoAPI#329: cada hora (a y cuarto, para no coincidir con seguimiento-envios),
            // consulta estados Verifactu pendientes, reintenta las facturas sin declarar (series
            // que tramitan, desde la fecha de arranque de la sombra) y marca fichas con NIF
            // rechazado. No-op si Verifacti:Habilitado está apagado.
            // NestoAPI#346: cadencia HORARIA porque el art. 16.4 de la Orden HAC/1177/2024 exige
            // reintentar la remisión pendiente "al menos una vez cada hora". El ruido de
            // reintentos repetidos lo corta DeduplicadorErroresVerifactu.
            RecurringJob.AddOrUpdate(
                "verifactu-estados",
                () => Infraestructure.Verifactu.VerifactuJobsService.Procesar(),
                "15 * * * *", // Cron: cada hora, a y cuarto
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'verifactu-estados' configurado (cada hora)");

            // NestoAPI#366: cada 30 minutos, cierra el bucle de las facturas subidas a Amazon
            // (feed UPLOAD_VAT_INVOICE): consulta getFeed de las filas ENVIADA y guarda el
            // resultado (DONE/FATAL) con su informe. No-op si no hay filas pendientes.
            RecurringJob.AddOrUpdate(
                "amazon-facturas-resultados",
                () => Infraestructure.CanalesExternos.Amazon.AmazonFacturasJobsService.ComprobarResultadosFeeds(),
                "*/30 * * * *", // Cron: cada 30 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'amazon-facturas-resultados' configurado (cada 30 minutos)");

            // NestoAPI#361: picking de CIERRE del día. Venía de una tarea del Task Scheduler que
            // llamaba a la API por HTTP y que estaba programada a las 10:59:40 para no pasarse de
            // las 11h, porque el horizonte de entrega se deducía del reloj y arrancar un segundo
            // tarde hacía que el picking sacara TAMBIÉN las entregas de mañana. Resuelto eso (el
            // horizonte se declara, no se deduce), llegar unos segundos tarde dejó de costar nada
            // y el job se puede programar a las 11:00 en punto.
            //
            // De hecho el retraso natural de Hangfire (unos segundos) ahora JUEGA A FAVOR: da
            // margen a que commiteen los pedidos metidos justo antes del corte. El último pedido
            // que entra es el de las 10:59:59; a las 11:00:00 ya se le asigna la siguiente ruta.
            // NestoAPI#416: se registra el punto de entrada VOID. El que devuelve la lista es
            // solo para el endpoint manual: Hangfire serializa el retorno dentro del commit del
            // Succeeded y la lista entera de pedidos lo reventaba (job en Failed con el picking
            // ya sacado).
            RecurringJob.AddOrUpdate(
                "picking-cierre-diario",
                () => PickingJobsService.SacarPickingDeCierreJob(),
                "0 11 * * 1-5", // Cron: de lunes a viernes a las 11:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'picking-cierre-diario' configurado (L-V a las 11:00)");

            // NOTA: El job de clientes está deshabilitado porque aún se usa Task Scheduler
            // Para habilitarlo en el futuro, cambia '#if false' por '#if true':
#if false
            RecurringJob.AddOrUpdate(
                "sincronizar-clientes",
                () => SincronizacionJobsService.SincronizarClientes(),
                "*/5 * * * *", // Cron: cada 5 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sincronizar-clientes' configurado (cada 5 minutos)");
#endif
        }
    }

    /// <summary>
    /// Filtro de autorización simple para el dashboard de Hangfire
    /// En producción, deberías implementar autenticación real
    /// </summary>
    public class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
    {
        public bool Authorize(Hangfire.Dashboard.DashboardContext context)
        {
            // TODO: Implementar autenticación real en producción
            // Por ahora permite acceso a todos (solo para desarrollo/testing)
            // En producción podrías verificar:
            // - Usuario autenticado
            // - Rol específico (ej: Admin)
            // - IP permitida
            return true;
        }
    }
}
