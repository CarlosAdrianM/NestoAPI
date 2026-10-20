using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#658: GET api/Informes/FacturacionTienda (y /Excel): permisos, validación y semana por defecto.</summary>
    [TestClass]
    public class InformesControllerFacturacionTiendaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 20, 10, 0, 0); // martes
        private IInformesService servicio;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string t, DateTime d, DateTime h) => Task.FromResult(GestorFacturacionTienda.Construir(null, t, d, h)));
        }

        private InformesController Controller(string usuario, params string[] grupos)
        {
            InformesController controller = new InformesController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Ahora = () => HOY
            };
            if (usuario != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity(usuario), grupos);
            }
            return controller;
        }

        [TestMethod]
        public void FacturacionTienda_SinIniciarSesion_401PorElAuthorizeDeLaClase()
        {
            Assert.IsTrue(typeof(InformesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
            MethodInfo metodo = typeof(InformesController).GetMethod(nameof(InformesController.GetFacturacionTienda));
            Assert.AreEqual(0, metodo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length);
            MethodInfo excel = typeof(InformesController).GetMethod(nameof(InformesController.GetFacturacionTiendaExcel));
            Assert.AreEqual(0, excel.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length);
        }

        [TestMethod]
        public async Task FacturacionTienda_SinUsuario_Forbidden()
        {
            IHttpActionResult resultado = await Controller(null).GetFacturacionTienda("REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task FacturacionTienda_UnVendedor_ForbiddenYNoConsulta()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Comerciales").GetFacturacionTienda("REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturacionTiendaExcel_UnVendedor_Forbidden()
        {
            HttpResponseMessage respuesta = await Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Comerciales").GetFacturacionTiendaExcel("REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task FacturacionTienda_Tiendas_SinFechasPideLaUltimaSemanaCerrada()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Paloma", "NUEVAVISION\\Tiendas").GetFacturacionTienda("rei");

            FacturacionTiendaDTO informe = ((OkNegotiatedContentResult<FacturacionTiendaDTO>)resultado).Content;
            Assert.AreEqual("REI", informe.Tienda);
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("REI", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturacionTienda_Direccion_PasaElPeriodoPedido()
        {
            await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetFacturacionTienda("ALC", new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("ALC", new DateTime(2026, 9, 1), new DateTime(2026, 9, 30))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturacionTienda_Informatica_Puede()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Informatico", "NUEVAVISION\\Informatica").GetFacturacionTienda("ALC");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturacionTiendaDTO>));
        }

        [TestMethod]
        public async Task FacturacionTienda_AlmacenQueNoEsTienda_BadRequest()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetFacturacionTienda("ALG");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturacionTienda_DesdePosteriorAHasta_BadRequest()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección")
                .GetFacturacionTienda("REI", new DateTime(2026, 10, 18), new DateTime(2026, 10, 12));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task FacturacionTiendaExcel_Tiendas_DevuelveElXlsx()
        {
            HttpResponseMessage respuesta = await Controller("NUEVAVISION\\Paloma", "NUEVAVISION\\Tiendas").GetFacturacionTiendaExcel("REI");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual(GeneradorExcelFacturacionTienda.CONTENT_TYPE, respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("FacturacionTienda_REI_20261012_20261018.xlsx", respuesta.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task FacturacionTiendaExcel_TiendaMal_BadRequest()
        {
            HttpResponseMessage respuesta = await Controller("NUEVAVISION\\Paloma", "NUEVAVISION\\Tiendas").GetFacturacionTiendaExcel("XYZ");

            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }
    }
}
