using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreciosMedios;

namespace NestoAPI.Tests.Infrastructure.PreciosMedios
{
    /// <summary>Issue #547, corte (b): endpoints de diagnóstico de la sombra (solo Dirección e Informática, sin escribir).</summary>
    [TestClass]
    public class PreciosMediosControllerTests
    {
        private IRepositorioPreciosMedios repositorio;
        private PreciosMediosController controller;
        private int encoladas;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPreciosMedios>();
            A.CallTo(() => repositorio.EmpresaEspejo("1")).Returns("3");
            A.CallTo(() => repositorio.UltimaEjecucionSP()).Returns(null);
            A.CallTo(() => repositorio.LeerDatos("1", "3", A<string>._, A<bool>._))
                .ReturnsLazily((string e, string esp, string p, bool v) => ServicioSombraPreciosMediosTests.ProductoIgual(p));
            encoladas = 0;
            controller = new PreciosMediosController(() => new ServicioSombraPreciosMedios(repositorio), () => { encoladas++; return "42"; })
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Informatica" })
            };
        }

        [TestMethod]
        public void Get_SinSerDireccionNiInformatica_Prohibido()
        {
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Almacen"), new[] { "NUEVAVISION\\Almacen" });

            var resultado = controller.GetSombraProducto("1", "41281") as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public void Get_Informatica_DevuelveElCalculoYLaDiferencia()
        {
            var resultado = controller.GetSombraProducto("1", " 41281 ", ventas: true) as OkNegotiatedContentResult<ResultadoSombraPrecioMedio>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(ClasificacionSombraPrecioMedio.Igual, resultado.Content.Clasificacion);
            A.CallTo(() => repositorio.LeerDatos("1", "3", "41281", true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostSombra_Direccion_VariosProductosSinRegistrar()
        {
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" });

            var resultado = controller.PostSombra("1", "41281, 44904;41281");

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado is BadRequestErrorMessageResult);
            A.CallTo(() => repositorio.LeerDatos("1", "3", "41281", false)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerDatos("1", "3", "44904", false)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarFila(A<FilaPreciosMediosSombra>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.BorrarPasada(A<System.DateTime>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, encoladas);
        }

        [TestMethod]
        public void PostSombra_SinProductos_BadRequest()
        {
            Assert.IsInstanceOfType(controller.PostSombra("1", null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PostSombra_DemasiadosProductos_BadRequest()
        {
            string productos = string.Join(",", Enumerable.Range(1, PreciosMediosController.MAXIMO_PRODUCTOS_PARCIAL + 1));
            Assert.IsInstanceOfType(controller.PostSombra("1", productos), typeof(BadRequestErrorMessageResult));
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostSombra_Completa_SoloEncolaEnHangfire()
        {
            var resultado = controller.PostSombra(completa: true);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, encoladas);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostSombra_SinPermiso_NoEncola()
        {
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Almacen"), new string[0]);

            var resultado = controller.PostSombra(completa: true) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            Assert.AreEqual(0, encoladas);
        }
    }
}
