using Microsoft.Extensions.DependencyInjection;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Filters;
using System;
using System.Net.Http.Formatting;
using System.Web.Http;


namespace NestoAPI
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Sincronizar usuario autenticado (JWT/OWIN) con HttpContext.Current.User
            // Esto es necesario para que ELMAH y otros componentes legacy vean el usuario
            config.MessageHandlers.Add(new UserSyncHandler());

            // Registrar filtro global de excepciones para manejo centralizado de errores
            config.Filters.Add(new GlobalExceptionFilter());

            // Verificar la configuración global de Web API
            config.Formatters.Clear();  // Limpiar todos los formatters
            config.Formatters.Add(new JsonMediaTypeFormatter());  // Añadir solo el JSON formatter

            // Rutas de API web
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            //Evito las referencias circulares al trabajar con Entity FrameWork         
            config.Formatters.JsonFormatter.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Serialize;
            config.Formatters.JsonFormatter.SerializerSettings.PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.Objects;
                    
            //Elimino que el sistema devuelva en XML, sólo trabajaremos con JSON
            config.Formatters.Remove(GlobalConfiguration.Configuration.Formatters.XmlFormatter);

            // Carlos 24/07/15: activamos CORS para poder entrar desde cualquier dominio
            //var cors = new System.Web.Http.Cors.EnableCorsAttribute(
            //origins: "*",
            //headers: "*",
            //methods: "*");
            //config.EnableCors(cors);            

            GlobalConfiguration.Configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
        }
    }
}
