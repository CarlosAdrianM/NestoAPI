using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
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

        [TestMethod]
        public async Task Enviar_SinDestinatario_DevuelveBadRequest()
        {
            var dto = new EnviarNotificacionDTO
            {
                Destinatario = null,
                Notificacion = new NotificacionPushDTO { Titulo = "Test" }
            };

            var resultado = await _controller.Enviar(dto);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Enviar_SinTitulo_DevuelveBadRequest()
        {
            var dto = new EnviarNotificacionDTO
            {
                Destinatario = "NV",
                TipoDestinatario = "vendedor",
                Notificacion = new NotificacionPushDTO { Titulo = null }
            };

            var resultado = await _controller.Enviar(dto);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Enviar_AVendedor_LlamaAlServicioCorrectamente()
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = "Pedido enviado",
                Cuerpo = "Tu pedido 12345 ha sido entregado a la agencia"
            };
            var dto = new EnviarNotificacionDTO
            {
                Destinatario = "NV ",
                TipoDestinatario = "vendedor",
                Empresa = "1  ",
                Notificacion = notificacion
            };

            A.CallTo(() => _servicio.EnviarAVendedor("1  ", "NV ", notificacion)).Returns(2);

            var resultado = await _controller.Enviar(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
            A.CallTo(() => _servicio.EnviarAVendedor("1  ", "NV ", notificacion))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Enviar_ACliente_LlamaAlServicioCorrectamente()
        {
            var notificacion = new NotificacionPushDTO
            {
                Titulo = "Factura emitida",
                Cuerpo = "Nueva factura disponible"
            };
            var dto = new EnviarNotificacionDTO
            {
                Destinatario = "15191     ",
                TipoDestinatario = "cliente",
                Empresa = "1  ",
                Notificacion = notificacion
            };

            A.CallTo(() => _servicio.EnviarACliente("1  ", "15191     ", notificacion)).Returns(1);

            var resultado = await _controller.Enviar(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
            A.CallTo(() => _servicio.EnviarACliente("1  ", "15191     ", notificacion))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Enviar_SinTipoDestinatario_EnviaPorUsuario()
        {
            var notificacion = new NotificacionPushDTO { Titulo = "Test" };
            var dto = new EnviarNotificacionDTO
            {
                Destinatario = "testuser",
                Notificacion = notificacion
            };

            A.CallTo(() => _servicio.EnviarAUsuario("testuser", "NestoApp", notificacion)).Returns(1);

            var resultado = await _controller.Enviar(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
            A.CallTo(() => _servicio.EnviarAUsuario("testuser", "NestoApp", notificacion))
                .MustHaveHappenedOnceExactly();
        }

        #region NotificarNuevoProtocolo

        private NotificacionesController CrearControllerConApiKey(string apiKey)
        {
            var controller = new NotificacionesController(_servicio);
            var config = new HttpConfiguration();
            var request = new HttpRequestMessage();
            request.SetConfiguration(config);

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            controller.Request = request;
            return controller;
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_SinApiKey_DevuelveUnauthorized()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey(null);
            var dto = new NuevoProtocoloDTO { Titulo = "Test" };

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_ConApiKeyIncorrecta_DevuelveUnauthorized()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-incorrecta");
            var dto = new NuevoProtocoloDTO { Titulo = "Test" };

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_ConBodyNull_DevuelveBadRequest()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");

            var resultado = await controller.NotificarNuevoProtocolo(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_SinTitulo_DevuelveBadRequest()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");
            var dto = new NuevoProtocoloDTO { Titulo = "" };

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_ConDatosValidos_EnviaATodosDeAplicacion()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");
            var dto = new NuevoProtocoloDTO
            {
                Titulo = "Nuevo protocolo de tinte",
                Descripcion = "Como aplicar el tinte correctamente",
                ImagenUrl = "https://img.youtube.com/vi/abc123/0.jpg",
                VideoId = 42
            };

            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                "TiendasNuevaVision", A<NotificacionPushDTO>.Ignored)).Returns(5);

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                "TiendasNuevaVision",
                A<NotificacionPushDTO>.That.Matches(n =>
                    n.Titulo == "Nuevo protocolo disponible" &&
                    n.Cuerpo == "Nuevo protocolo de tinte" &&
                    n.Datos["tipo"] == "protocolo" &&
                    n.Datos["videoId"] == "42" &&
                    n.Datos["imagenUrl"] == "https://img.youtube.com/vi/abc123/0.jpg"
                ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_SinVideoId_NoIncluyeVideoIdEnDatos()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");
            var dto = new NuevoProtocoloDTO { Titulo = "Protocolo sin video" };

            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                A<string>.Ignored, A<NotificacionPushDTO>.Ignored)).Returns(0);

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                "TiendasNuevaVision",
                A<NotificacionPushDTO>.That.Matches(n =>
                    !n.Datos.ContainsKey("videoId") &&
                    !n.Datos.ContainsKey("imagenUrl")
                ))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_SinDispositivosRegistrados_DevuelveOkConCeroEnviados()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");
            var dto = new NuevoProtocoloDTO { Titulo = "Protocolo nuevo" };

            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                A<string>.Ignored, A<NotificacionPushDTO>.Ignored)).Returns(0);

            var resultado = await controller.NotificarNuevoProtocolo(dto);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<int>));
        }

        [TestMethod]
        public async Task NotificarNuevoProtocolo_NoEnviaANestoApp()
        {
            ConfigurationManager.AppSettings["NotificacionesApiKey"] = "clave-secreta";
            var controller = CrearControllerConApiKey("clave-secreta");
            var dto = new NuevoProtocoloDTO { Titulo = "Solo tiendas" };

            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                A<string>.Ignored, A<NotificacionPushDTO>.Ignored)).Returns(3);

            await controller.NotificarNuevoProtocolo(dto);

            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                "TiendasNuevaVision", A<NotificacionPushDTO>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => _servicio.EnviarATodosDeAplicacion(
                "NestoApp", A<NotificacionPushDTO>.Ignored))
                .MustNotHaveHappened();
        }

        #endregion
    }
}
