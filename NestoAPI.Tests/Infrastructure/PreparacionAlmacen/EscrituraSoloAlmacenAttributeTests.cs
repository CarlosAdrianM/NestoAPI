using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna: en api/Almacen leer basta con estar identificado; escribir (lo que no es GET) pide
    /// Almacén o Dirección. Así el usuario de revisión de Google Play no escribe datos reales.
    /// </summary>
    [TestClass]
    public class EscrituraSoloAlmacenAttributeTests
    {
        private static HttpActionContext Contexto(HttpMethod metodo, string accion, IPrincipal usuario)
        {
            var configuracion = new HttpConfiguration();
            var request = new HttpRequestMessage(metodo, "https://api.nuevavision.es/api/Almacen/x");
            request.SetConfiguration(configuracion);
            var descriptorControlador = new HttpControllerDescriptor(configuracion, "Almacen", typeof(AlmacenController));
            var contextoControlador = new HttpControllerContext
            {
                Configuration = configuracion,
                Request = request,
                RequestContext = new HttpRequestContext { Principal = usuario },
                ControllerDescriptor = descriptorControlador
            };
            var descriptorAccion = new ReflectedHttpActionDescriptor(descriptorControlador,
                typeof(AlmacenController).GetMethod(accion));
            return new HttpActionContext(contextoControlador, descriptorAccion);
        }

        private static IPrincipal Usuario(string nombre, params string[] roles)
        {
            return new GenericPrincipal(new GenericIdentity(nombre, "Bearer"), roles);
        }

        private static HttpResponseMessage Autorizar(HttpMethod metodo, string accion, IPrincipal usuario)
        {
            HttpActionContext contexto = Contexto(metodo, accion, usuario);
            new EscrituraSoloAlmacenAttribute().OnAuthorization(contexto);
            return contexto.Response; // null = puede pasar
        }

        [TestMethod]
        public void ElControlador_LlevaElAtributo()
        {
            Assert.IsTrue(typeof(AlmacenController).GetCustomAttributes(typeof(EscrituraSoloAlmacenAttribute), true).Any());
        }

        [TestMethod]
        public void Leer_SinRoles_Puede()
        {
            Assert.IsNull(Autorizar(HttpMethod.Get, nameof(AlmacenController.GetRecogidas), Usuario("RevisionGoogle")));
        }

        [TestMethod]
        public void Escribir_SinRoles_403ConMotivo()
        {
            HttpResponseMessage respuesta = Autorizar(HttpMethod.Post, nameof(AlmacenController.PostEscaneos), Usuario("RevisionGoogle"));

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content.ReadAsStringAsync().Result, "permiso de Almacén");
        }

        [TestMethod]
        public void Escribir_RolDeIdentityAlmacen_Puede()
        {
            Assert.IsNull(Autorizar(HttpMethod.Post, nameof(AlmacenController.PostUbicar), Usuario("Andre", "Almacén")));
        }

        [TestMethod]
        public void Escribir_GrupoDeWindowsConDominio_Puede()
        {
            Assert.IsNull(Autorizar(HttpMethod.Post, nameof(AlmacenController.PostEscaneos), Usuario(@"NUEVAVISION\Andre", @"NUEVAVISION\Almacén")));
        }

        [TestMethod]
        public void Escribir_Direccion_Puede()
        {
            Assert.IsNull(Autorizar(HttpMethod.Post, nameof(AlmacenController.PostFotoBulto), Usuario(@"NUEVAVISION\Carlos", @"NUEVAVISION\Dirección")));
        }

        [TestMethod]
        public void Escribir_OtroGrupo_403()
        {
            HttpResponseMessage respuesta = Autorizar(HttpMethod.Post, nameof(AlmacenController.PostEscaneos), Usuario("Laura", "Tiendas"));

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta?.StatusCode);
        }

        [TestMethod]
        public void SinIdentificar_401ComoAntes()
        {
            var anonimo = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);

            Assert.AreEqual(HttpStatusCode.Unauthorized, Autorizar(HttpMethod.Get, nameof(AlmacenController.GetRecogidas), anonimo)?.StatusCode);
            Assert.AreEqual(HttpStatusCode.Unauthorized, Autorizar(HttpMethod.Post, nameof(AlmacenController.PostEscaneos), anonimo)?.StatusCode);
        }

        [TestMethod]
        public void FotoPublica_SigueSinPedirIdentificacion()
        {
            var anonimo = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);

            Assert.IsNull(Autorizar(HttpMethod.Get, nameof(AlmacenController.GetFotoPublica), anonimo));
        }
    }
}
