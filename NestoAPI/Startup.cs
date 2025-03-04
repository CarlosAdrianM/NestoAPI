using Microsoft.AspNet.Identity.Owin;
using Microsoft.Extensions.DependencyInjection;
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
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Mvc;

namespace NestoAPI
{
    public class Startup
    {

        public void Configuration(IAppBuilder app)
        {
            app.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

            HttpConfiguration httpConfig = new HttpConfiguration();

            // Configurar el contenedor de dependencias
            var serviceProvider = ConfigureServices();
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

            app.UseWebApi(httpConfig);
        }

        private void ConfigureOAuthTokenGeneration(IAppBuilder app)
        {
            // Configure the db context and user manager to use a single instance per request
            app.CreatePerOwinContext(ApplicationDbContext.Create);
            app.CreatePerOwinContext<ApplicationUserManager>(ApplicationUserManager.Create);
            app.CreatePerOwinContext<ApplicationRoleManager>(ApplicationRoleManager.Create);

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
            app.UseOAuthAuthorizationServer(OAuthServerOptions);
        }

        private void ConfigureOAuthTokenConsumption(IAppBuilder app)
        {

            //var issuer = "http://localhost:53364";
            var issuer = "carlos";
            string audienceId = ConfigurationManager.AppSettings["as:AudienceId"];
            byte[] audienceSecret = TextEncodings.Base64Url.Decode(ConfigurationManager.AppSettings["as:AudienceSecret"]);

            // Api controllers with an [Authorize] attribute will be validated with JWT
            app.UseJwtBearerAuthentication(
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
            var services = new ServiceCollection();
        
            // Registrar tus dependencias aquí
            services.AddScoped<IGestorClientes, GestorClientes>();
            services.AddScoped<IServicioGestorClientes, ServicioGestorClientes>();
            services.AddScoped<IServicioAgencias, ServicioAgencias>();
            services.AddScoped<ISincronizacionEventPublisher, GooglePubSubEventPublisher>();
            services.AddScoped<SincronizacionEventWrapper>();
            services.AddScoped<IServicioVendedores, ServicioVendedores>();
            services.AddScoped<ClientesController>();
            services.AddScoped<IGestorAlbaranesVenta, GestorAlbaranesVenta>();
            services.AddScoped<IServicioAlbaranesVenta, ServicioAlbaranesVenta>();

            // Registrar el contexto de la base de datos
            services.AddScoped<DbContext>(_ => new ApplicationDbContext());

            // Registrar los controladores de Web API
            services.AddControllersAsServices(typeof(Startup).Assembly);

            // Construir el proveedor de servicios
            return services.BuildServiceProvider();
        }

        private void ConfigureWebApi(HttpConfiguration config)
        {
            //config.MapHttpAttributeRoutes();

            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
            
            var jsonFormatter = config.Formatters.OfType<JsonMediaTypeFormatter>().First();
            jsonFormatter.SerializerSettings.ContractResolver = new DefaultContractResolver();

            //Elimino que el sistema devuelva en XML, sólo trabajaremos con JSON
            config.Formatters.Remove(GlobalConfiguration.Configuration.Formatters.XmlFormatter);
        }
    }
}
