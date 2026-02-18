using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.Owin.Security.Jwt;
using Microsoft.Owin.Security.OAuth;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Providers;
using Newtonsoft.Json.Serialization;
using Owin;
using System;
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

            // Job de correos post-compra: se ejecuta cada día a las 20:30
            // Issue #74: Sistema de correos automáticos con videos personalizados post-compra
            RecurringJob.AddOrUpdate(
                "correos-postcompra-procesar-albaranes",
                () => CorreosPostCompraJobsService.ProcesarAlbaranesDiarios(),
                "30 20 * * *", // Cron: cada día a las 20:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'correos-postcompra-procesar-albaranes' configurado (cada día a las 20:30)");

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
