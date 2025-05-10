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
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Providers;
using Newtonsoft.Json.Serialization;
using Owin;
using System;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Http.Formatting;
using System.Web.Http;

namespace NestoAPI
{
    public class Startup
    {

        public void Configuration(IAppBuilder app)
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

            ConfigureWebApi(httpConfig);

            _ = app.UseWebApi(httpConfig);
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
            _ = config.Formatters.Remove(GlobalConfiguration.Configuration.Formatters.XmlFormatter);
        }
    }
}
