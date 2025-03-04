﻿using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http.Formatting;
using System.Web.Http;


namespace NestoAPI
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
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
