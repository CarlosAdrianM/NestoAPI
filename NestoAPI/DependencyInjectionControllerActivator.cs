using System;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using System.Web.Http.Dispatcher;

namespace NestoAPI
{
    public class DependencyInjectionControllerActivator : IHttpControllerActivator
    {
        private readonly IDependencyResolver _resolver;

        public DependencyInjectionControllerActivator(IDependencyResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IHttpController Create(HttpRequestMessage request, HttpControllerDescriptor controllerDescriptor, Type controllerType)
        {
            return (IHttpController)_resolver.GetService(controllerType);
        }
    }
}
