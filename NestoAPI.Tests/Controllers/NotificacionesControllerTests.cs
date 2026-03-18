using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class NotificacionesControllerTests
    {
        private IServicioNotificacionesPush _servicio;
        private NotificacionesController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioNotificacionesPush>();
            _controller = new NotificacionesController(_servicio);
            _controller.User = new GenericPrincipal(new GenericIdentity("testuser"), null);
        }

        [TestMethod]
        public async Task RegistrarDispositivo_ConBodyNull_DevuelveBadRequest()
        {
            var resultado = await _controller.RegistrarDispositivo(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task RegistrarDispositivo_ConDatosValidos_DevuelveOk()
        {
            var registro = new RegistrarDispositivoDTO
            {
                Token = "token123",
                Plataforma = "Android",
                Aplicacion = "NestoApp",
                Empresa = "1  ",
                Vendedor = "NV "
            };

            var dispositivo = new DispositivoNotificacion
            {
                Id = 1,
                Usuario = "testuser",
                Token = "token123",
                Plataforma = "Android",
                Aplicacion = "NestoApp",
                Empresa = "1  ",
                Vendedor = "NV ",
                FechaRegistro = DateTime.Now,
                FechaUltimaActividad = DateTime.Now,
                Activo = true
            };

            A.CallTo(() => _servicio.RegistrarDispositivo(registro, "testuser"))
                .Returns(dispositivo);

            var resultado = await _controller.RegistrarDispositivo(registro);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<DispositivoNotificacion>));
            var okResult = (OkNegotiatedContentResult<DispositivoNotificacion>)resultado;
            Assert.AreEqual("token123", okResult.Content.Token);
            Assert.AreEqual("NestoApp", okResult.Content.Aplicacion);
        }

        [TestMethod]
        public async Task RegistrarDispositivo_CuandoServicioLanzaArgumentException_DevuelveBadRequest()
        {
            var registro = new RegistrarDispositivoDTO
            {
                Token = "",
                Plataforma = "Android",
                Aplicacion = "NestoApp"
            };

            A.CallTo(() => _servicio.RegistrarDispositivo(registro, A<string>.Ignored))
                .Throws(new ArgumentException("El token del dispositivo es obligatorio"));

            var resultado = await _controller.RegistrarDispositivo(registro);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task DesregistrarDispositivo_ConTokenNull_DevuelveBadRequest()
        {
            var resultado = await _controller.DesregistrarDispositivo(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task DesregistrarDispositivo_CuandoTokenNoExiste_DevuelveNotFound()
        {
            A.CallTo(() => _servicio.DesregistrarDispositivo("tokenInexistente"))
                .Returns(false);

            var resultado = await _controller.DesregistrarDispositivo("tokenInexistente");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task DesregistrarDispositivo_CuandoTokenExiste_DevuelveOk()
        {
            A.CallTo(() => _servicio.DesregistrarDispositivo("tokenExistente"))
                .Returns(true);

            var resultado = await _controller.DesregistrarDispositivo("tokenExistente");

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
        }

        [TestMethod]
        public async Task RegistrarDispositivo_TiendasNuevaVision_IncluyeEmpresaClienteContacto()
        {
            var registro = new RegistrarDispositivoDTO
            {
                Token = "tokenTienda456",
                Plataforma = "Android",
                Aplicacion = "TiendasNuevaVision",
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  "
            };

            var dispositivo = new DispositivoNotificacion
            {
                Id = 2,
                Usuario = "testuser",
                Token = "tokenTienda456",
                Plataforma = "Android",
                Aplicacion = "TiendasNuevaVision",
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                FechaRegistro = DateTime.Now,
                FechaUltimaActividad = DateTime.Now,
                Activo = true
            };

            A.CallTo(() => _servicio.RegistrarDispositivo(registro, "testuser"))
                .Returns(dispositivo);

            var resultado = await _controller.RegistrarDispositivo(registro);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<DispositivoNotificacion>));
            var okResult = (OkNegotiatedContentResult<DispositivoNotificacion>)resultado;
            Assert.AreEqual("TiendasNuevaVision", okResult.Content.Aplicacion);
            Assert.AreEqual("15191     ", okResult.Content.Cliente);
            Assert.AreEqual("0  ", okResult.Content.Contacto);
        }
    }
}
