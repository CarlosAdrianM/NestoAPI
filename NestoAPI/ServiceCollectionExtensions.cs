using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using System.Web.Http.Controllers;

namespace NestoAPI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddControllersAsServices(this IServiceCollection services, Assembly assembly)
        {
            // Obtener todos los tipos que son controladores de Web API
            var controllerTypes = assembly.GetExportedTypes()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                .Where(t => typeof(IHttpController).IsAssignableFrom(t) || t.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase));

            // Registrar cada controlador como un servicio
            foreach (var type in controllerTypes)
            {
                services.AddTransient(type);
            }

            return services;
        }
    }
}
