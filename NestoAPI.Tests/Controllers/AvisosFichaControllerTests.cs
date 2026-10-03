using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>Ariadna#8: informar de un dato mal en la ficha de un producto y cerrar el aviso.</summary>
    [TestClass]
    public class AvisosFichaControllerTests
    {
        private IServicioAvisosFicha servicio;
        private AvisosFichaController controller;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioAvisosFicha>();
            controller = new AvisosFichaController(servicio)
            {
                User = new GenericPrincipal(new GenericIdentity("Pedro", "Bearer"), new[] { "Almacén" }),
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public async Task PostInformar_UnMozo_LoGuardaASuNombre()
        {
            var peticion = new InformarDatoMalDTO { Producto = "22624" };
            A.CallTo(() => servicio.Informar("1", peticion, "Pedro"))
                .Returns(new ResultadoInformarDatoMal { Estado = EstadoInformarDatoMal.Guardado, Mensaje = "Avisado a Tienda online." });

            var resultado = (OkNegotiatedContentResult<ResultadoInformarDatoMalDTO>)await controller.PostInformar(peticion);

            Assert.AreEqual("Avisado a Tienda online.", resultado.Content.Mensaje);
        }

        [TestMethod]
        public async Task PostInformar_SinRolDeAlmacen_403()
        {
            controller.User = new GenericPrincipal(new GenericIdentity("Cliente", "Bearer"), new string[0]);

            var resultado = (NegotiatedContentResult<string>)await controller.PostInformar(new InformarDatoMalDTO { Producto = "22624" });

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicio.Informar(A<string>._, A<InformarDatoMalDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostInformar_NoValido_400()
        {
            A.CallTo(() => servicio.Informar(A<string>._, A<InformarDatoMalDTO>._, A<string>._))
                .Returns(new ResultadoInformarDatoMal { Estado = EstadoInformarDatoMal.NoValido, Mensaje = "Marca qué está mal." });

            Assert.IsInstanceOfType(await controller.PostInformar(new InformarDatoMalDTO()), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostCerrar_DevuelveSegunElResultado()
        {
            A.CallTo(() => servicio.Cerrar(7, "Cambiado", controller.User)).Returns(new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.Cerrado, Mensaje = "ok" });
            A.CallTo(() => servicio.Cerrar(8, "Cambiado", controller.User)).Returns(new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.SinPermiso, Mensaje = "no" });
            A.CallTo(() => servicio.Cerrar(9, "Cambiado", controller.User)).Returns(new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.YaCerrado, Mensaje = "ya" });
            A.CallTo(() => servicio.Cerrar(10, "Cambiado", controller.User)).Returns(new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.NoExiste });

            Assert.IsInstanceOfType(await controller.PostCerrar(7, new CerrarAvisoFichaDTO { Resultado = "Cambiado" }), typeof(OkNegotiatedContentResult<string>));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)await controller.PostCerrar(8, new CerrarAvisoFichaDTO { Resultado = "Cambiado" })).StatusCode);
            Assert.AreEqual(HttpStatusCode.Conflict, ((NegotiatedContentResult<string>)await controller.PostCerrar(9, new CerrarAvisoFichaDTO { Resultado = "Cambiado" })).StatusCode);
            Assert.IsInstanceOfType(await controller.PostCerrar(10, new CerrarAvisoFichaDTO { Resultado = "Cambiado" }), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetCerrarConEnlace_ContestaUnaPaginaParaElNavegador()
        {
            var clave = Guid.NewGuid();
            A.CallTo(() => servicio.CerrarConEnlace(clave, "EstabaBien"))
                .Returns(new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.Cerrado, Mensaje = "Aviso del 22624 cerrado: estaba bien." });

            HttpResponseMessage respuesta = await (await controller.GetCerrarConEnlace(clave, "EstabaBien")).ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("text/html", respuesta.Content.Headers.ContentType.MediaType);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "estaba bien");
        }
    }
}
