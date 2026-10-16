using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    // NestoAPI#654: la etiqueta de producto la piden también las tiendas (Paloma es de Tiendas, no de Almacén): no lleva el
    // filtro de escritura de Almacén; pueden los mismos que gestionan los códigos de barras.
    [TestClass]
    public class EtiquetasProductoControllerTests
    {
        private IServicioEtiquetasProducto servicio;
        private EtiquetasProductoController controller;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioEtiquetasProducto>();
            controller = new EtiquetasProductoController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Paloma", "Tiendas", "Comerciales")
            };
        }

        [TestMethod]
        public void NoLlevaElFiltroDeEscrituraDeAlmacen_PeroSiPideEstarIdentificado()
        {
            object[] atributos = typeof(EtiquetasProductoController).GetCustomAttributes(true);

            Assert.IsFalse(atributos.OfType<EscrituraSoloAlmacenAttribute>().Any(), "Si no, una tienda no podría imprimir la etiqueta");
            Assert.IsTrue(atributos.OfType<AuthorizeAttribute>().Any());
        }

        [TestMethod]
        public async Task Imprimir_UnaTienda_LlamaAlServicioConSuUsuario()
        {
            var peticion = new EtiquetasProductoDTO { Producto = "17404", Copias = 1 };
            var esperado = new ResultadoEtiquetasProductoDTO { Producto = "17404", Impresas = 1 };
            A.CallTo(() => servicio.Imprimir("1", peticion, A<string>.That.Contains("Paloma"), false)).Returns(esperado);

            IHttpActionResult respuesta = await controller.PostImprimir(peticion);

            Assert.AreSame(esperado, ((OkNegotiatedContentResult<ResultadoEtiquetasProductoDTO>)respuesta).Content);
        }

        [TestMethod]
        public async Task Imprimir_Ensayo_SePasaAlServicio()
        {
            var peticion = new EtiquetasProductoDTO { Producto = "17404" };

            _ = await controller.PostImprimir(peticion, "1", true);

            A.CallTo(() => servicio.Imprimir("1", peticion, A<string>._, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Imprimir_SinGrupoDeLosQuePueden_403SinLlamarAlServicio()
        {
            controller.User = Usuario("RevisionGoogle");

            IHttpActionResult respuesta = await controller.PostImprimir(new EtiquetasProductoDTO { Producto = "17404" });

            var contenido = (NegotiatedContentResult<string>)respuesta;
            Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
            Assert.AreEqual(EtiquetasProductoController.MENSAJE_SIN_PERMISO, contenido.Content);
            A.CallTo(() => servicio.Imprimir(A<string>._, A<EtiquetasProductoDTO>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_ProductoQueNoExiste_400ConElMotivo()
        {
            A.CallTo(() => servicio.Imprimir(A<string>._, A<EtiquetasProductoDTO>._, A<string>._, A<bool>._))
                .ThrowsAsync(new ArgumentException("No existe el producto 99999."));

            IHttpActionResult respuesta = await controller.PostImprimir(new EtiquetasProductoDTO { Producto = "99999" });

            Assert.AreEqual("No existe el producto 99999.", ((BadRequestErrorMessageResult)respuesta).Message);
        }

        [TestMethod]
        public async Task Imprimir_WindowsNoDejaImprimir_502()
        {
            A.CallTo(() => servicio.Imprimir(A<string>._, A<EtiquetasProductoDTO>._, A<string>._, A<bool>._))
                .ThrowsAsync(new ImpresionEtiquetasException("No se puede abrir la impresora X (error de Windows 5)."));

            IHttpActionResult respuesta = await controller.PostImprimir(new EtiquetasProductoDTO { Producto = "17404" });

            Assert.AreEqual(HttpStatusCode.BadGateway, ((NegotiatedContentResult<string>)respuesta).StatusCode);
        }
    }
}
