using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#601 (07/10/26): GET api/Facturas (PDF) y api/Facturas/FacturaJson eran anónimos y no miraban de quién era
    /// la factura: cualquiera con la URL descargaba cualquier factura cambiando el número (correlativo), y un cliente de
    /// la tienda veía las de otros. Ahora piden token y el cliente de la tienda solo ve las suyas.
    /// </summary>
    [TestClass]
    public class FacturasControllerDescargaTests
    {
        private const string FACTURA = "NV2616199";
        private IServicioFacturas servicio;
        private IGestorFacturas gestor;

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioFacturas>();
            gestor = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestor.LeerFacturas(A<List<FacturaLookup>>._)).Returns(new List<Factura> { new Factura() });
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).Returns(new Factura());
        }

        private void FacturaDe(string numero, string cliente)
            => A.CallTo(() => servicio.CargarCabFactura("1", numero)).Returns(new CabFacturaVta { Nº_Cliente = cliente });

        private FacturasController Controlador(params Claim[] claims)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "Usuario"));
            identity.AddClaims(claims);
            return new FacturasController(servicio, gestor) { User = new ClaimsPrincipal(identity) };
        }

        [TestMethod]
        public void GetFactura_PideToken()
        {
            // Sin token → 401 lo da el [Authorize] (el filtro no corre en un test unitario: se comprueba que está)
            System.Reflection.MethodInfo metodo = typeof(FacturasController).GetMethod(nameof(FacturasController.GetFactura));

            Assert.IsTrue(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public void GetFacturaJson_PideToken()
        {
            System.Reflection.MethodInfo metodo = typeof(FacturasController).GetMethod(nameof(FacturasController.GetFacturaJson));

            Assert.IsTrue(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task GetFactura_UnClienteDeLaTienda_DescargaSuFactura()
        {
            FacturaDe(FACTURA, "15191  ");
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            HttpResponseMessage respuesta = await controlador.GetFactura("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/pdf", respuesta.Content.Headers.ContentType.MediaType);
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetFactura_UnClienteDeLaTienda_NoPuedeDescargarLaFacturaDeOtroCliente()
        {
            FacturaDe(FACTURA, "20000");
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            HttpResponseMessage respuesta = await controlador.GetFactura("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS_VER, await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => gestor.LeerFacturas(A<List<FacturaLookup>>._)).MustNotHaveHappened();
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFactura_UnClienteDeLaTienda_FacturaQueNoExiste_MismoRechazoQueSiFueraDeOtro()
        {
            // Para no desvelar qué facturas existen: inexistente y ajena dan el mismo 403
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).Returns(null);
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            HttpResponseMessage respuesta = await controlador.GetFactura("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS_VER, await respuesta.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task GetFactura_UnEmpleado_NoPasaPorLaComprobacionDeCliente()
        {
            FacturasController controlador = Controlador(new Claim("IsEmployee", "true"));

            HttpResponseMessage respuesta = await controlador.GetFactura("1", FACTURA);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFacturaJson_UnClienteDeLaTienda_VeSuFactura()
        {
            FacturaDe(FACTURA, "15191");
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            IHttpActionResult resultado = await controlador.GetFacturaJson("1", FACTURA);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<Factura>));
        }

        [TestMethod]
        public async Task GetFacturaJson_UnClienteDeLaTienda_NoPuedeVerLaFacturaDeOtroCliente()
        {
            FacturaDe(FACTURA, "20000");
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            var resultado = await controlador.GetFacturaJson("1", FACTURA) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS_VER, resultado.Content);
            A.CallTo(() => gestor.LeerFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetFacturaJson_UnClienteDeLaTienda_FacturaQueNoExiste_MismoRechazoQueSiFueraDeOtro()
        {
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).Returns(null);
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            var resultado = await controlador.GetFacturaJson("1", FACTURA) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS_VER, resultado.Content);
        }

        [TestMethod]
        public async Task GetFacturaJson_UnEmpleado_NoPasaPorLaComprobacionDeCliente()
        {
            FacturasController controlador = Controlador(new Claim("IsEmployee", "true"));

            IHttpActionResult resultado = await controlador.GetFacturaJson("1", FACTURA);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<Factura>));
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
