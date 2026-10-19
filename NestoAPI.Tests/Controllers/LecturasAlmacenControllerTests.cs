using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>Ariadna#43: api/Almacen/Lecturas.</summary>
    [TestClass]
    public class LecturasAlmacenControllerTests
    {
        private IServicioLecturasAlmacen servicio;
        private LecturasAlmacenController controller;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioLecturasAlmacen>();
            controller = Controlador("Andre", "Almacén");
        }

        private LecturasAlmacenController Controlador(string usuario, params string[] roles) => new LecturasAlmacenController(servicio)
        {
            User = new GenericPrincipal(new GenericIdentity(usuario, "Bearer"), roles),
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration()
        };

        [TestMethod]
        public async Task PostNoCasadas_UnMozo_LasGuardaASuNombre()
        {
            var lecturas = new List<LecturaNoCasadaDTO> { new LecturaNoCasadaDTO { Codigo = "8412345678905" } };
            A.CallTo(() => servicio.RegistrarNoCasadas("1", lecturas, "Andre")).Returns(new ResultadoLecturasNoCasadasDTO { Guardadas = 1 });

            var resultado = (OkNegotiatedContentResult<ResultadoLecturasNoCasadasDTO>)await controller.PostNoCasadas(lecturas);

            Assert.AreEqual(1, resultado.Content.Guardadas);
        }

        [TestMethod]
        public async Task PostNoCasadas_UnaTienda_TambienPuede()
        {
            controller = Controlador("Reina", "Tiendas");
            A.CallTo(() => servicio.RegistrarNoCasadas(A<string>._, A<IList<LecturaNoCasadaDTO>>._, A<string>._)).Returns(new ResultadoLecturasNoCasadasDTO());

            IHttpActionResult resultado = await controller.PostNoCasadas(new List<LecturaNoCasadaDTO>());

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoLecturasNoCasadasDTO>));
        }

        [TestMethod]
        public async Task PostNoCasadas_SinRol_403()
        {
            controller = Controlador("Cliente");

            var resultado = (NegotiatedContentResult<string>)await controller.PostNoCasadas(new List<LecturaNoCasadaDTO>());

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicio.RegistrarNoCasadas(A<string>._, A<IList<LecturaNoCasadaDTO>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetMotivoTecleo_DiceSiYaSePregunto()
        {
            A.CallTo(() => servicio.LeerMotivo("1", "17404")).Returns(new EstadoMotivoTecleoDTO { Producto = "17404", Preguntado = true, Motivo = "OTRO" });

            var resultado = (OkNegotiatedContentResult<EstadoMotivoTecleoDTO>)await controller.GetMotivoTecleo("17404");

            Assert.IsTrue(resultado.Content.Preguntado);
        }

        [TestMethod]
        public async Task PostMotivoTecleo_Guardado_200()
        {
            var motivo = new MotivoTecleoDTO { Producto = "17404", Motivo = MotivosTecleo.NO_SE_LEE };
            A.CallTo(() => servicio.GuardarMotivo("1", motivo, "Andre")).Returns(new EstadoMotivoTecleoDTO { Preguntado = true, Mensaje = "Apuntado." });

            var resultado = (OkNegotiatedContentResult<EstadoMotivoTecleoDTO>)await controller.PostMotivoTecleo(motivo);

            Assert.AreEqual("Apuntado.", resultado.Content.Mensaje);
        }

        [TestMethod]
        public async Task PostMotivoTecleo_MotivoQueNoVale_400()
        {
            A.CallTo(() => servicio.GuardarMotivo(A<string>._, A<MotivoTecleoDTO>._, A<string>._)).Throws(new ArgumentException("El motivo tiene que ser…"));

            IHttpActionResult resultado = await controller.PostMotivoTecleo(new MotivoTecleoDTO { Producto = "17404", Motivo = "X" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PostMotivoTecleo_SinRol_403()
        {
            controller = Controlador("Cliente");

            var resultado = (NegotiatedContentResult<string>)await controller.PostMotivoTecleo(new MotivoTecleoDTO { Producto = "17404", Motivo = "OTRO" });

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }
    }
}
