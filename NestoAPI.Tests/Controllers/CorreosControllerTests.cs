using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models.Correos;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class CorreosControllerTests
    {
        private IServicioCorreoElectronico _servicio;
        private CorreosController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            _controller = new CorreosController(_servicio);
            _controller.User = new GenericPrincipal(new GenericIdentity("usuarioTest"), null);
        }

        [TestMethod]
        public void CorreosController_TieneAuthorizeAttribute()
        {
            var atributos = typeof(CorreosController)
                .GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), true);
            Assert.AreEqual(1, atributos.Length);
        }

        [TestMethod]
        public void Enviar_DtoNulo_DevuelveBadRequest()
        {
            var resultado = _controller.Enviar(null);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Enviar_SinDestinatarios_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO { Asunto = "x" };
            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Enviar_DestinatariosTodosVacios_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "  ", "" },
                Asunto = "x"
            };
            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Enviar_SinAsunto_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" }
            };
            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Enviar_OK_LlamaAlServicioYDevuelveOk()
        {
            MailMessage capturado = null;
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => capturado = m)
                .Returns(true);

            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                CopiaOculta = new List<string> { "bcc@example.com" },
                Asunto = "Solicitud de recogida",
                Cuerpo = "<p>Hola</p>",
                EsHtml = true
            };

            var resultado = _controller.Enviar(dto);

            var ok = resultado as OkNegotiatedContentResult<EnvioCorreoRespuestaDTO>;
            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Enviado);
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
            Assert.IsNotNull(capturado);
            Assert.AreEqual("destino@example.com", capturado.To[0].Address);
            Assert.AreEqual("bcc@example.com", capturado.Bcc[0].Address);
            Assert.AreEqual("Solicitud de recogida", capturado.Subject);
            Assert.IsTrue(capturado.IsBodyHtml);
            Assert.AreEqual("nesto@nuevavision.es", capturado.From.Address);
            Assert.AreEqual("Nueva Visión", capturado.From.DisplayName);
        }

        [TestMethod]
        public void Enviar_ConRemitenteCustom_LoUsa()
        {
            MailMessage capturado = null;
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => capturado = m)
                .Returns(true);

            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                Asunto = "x",
                Remitente = "envios@nuevavision.es",
                NombreRemitente = "Envíos NV"
            };

            _controller.Enviar(dto);

            Assert.AreEqual("envios@nuevavision.es", capturado.From.Address);
            Assert.AreEqual("Envíos NV", capturado.From.DisplayName);
        }

        [TestMethod]
        public void Enviar_ConAdjuntoBase64Valido_LoAdjunta()
        {
            int totalAdjuntos = -1;
            string nombreAdjunto = null;
            string mediaType = null;
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) =>
                {
                    totalAdjuntos = m.Attachments.Count;
                    if (totalAdjuntos > 0)
                    {
                        nombreAdjunto = m.Attachments[0].Name;
                        mediaType = m.Attachments[0].ContentType.MediaType;
                    }
                })
                .Returns(true);

            byte[] bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                Asunto = "x",
                Adjuntos = new List<AdjuntoCorreoDTO>
                {
                    new AdjuntoCorreoDTO
                    {
                        Nombre = "factura.pdf",
                        ContenidoBase64 = Convert.ToBase64String(bytes),
                        TipoMime = "application/pdf"
                    }
                }
            };

            _controller.Enviar(dto);

            Assert.AreEqual(1, totalAdjuntos);
            Assert.AreEqual("factura.pdf", nombreAdjunto);
            Assert.AreEqual("application/pdf", mediaType);
        }

        [TestMethod]
        public void Enviar_ConAdjuntoBase64Invalido_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                Asunto = "x",
                Adjuntos = new List<AdjuntoCorreoDTO>
                {
                    new AdjuntoCorreoDTO { Nombre = "x.pdf", ContenidoBase64 = "no es base64 !!!" }
                }
            };

            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Enviar_ConAdjuntoSinContenido_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                Asunto = "x",
                Adjuntos = new List<AdjuntoCorreoDTO>
                {
                    new AdjuntoCorreoDTO { Nombre = "x.pdf", ContenidoBase64 = "" }
                }
            };

            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Enviar_DestinatarioInvalido_DevuelveBadRequest()
        {
            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "no-es-un-correo" },
                Asunto = "x"
            };

            var resultado = _controller.Enviar(dto);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Enviar_ServicioFalla_DevuelveError500()
        {
            A.CallTo(() => _servicio.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);

            var dto = new EnvioCorreoDTO
            {
                Destinatarios = new List<string> { "destino@example.com" },
                Asunto = "x"
            };

            var resultado = _controller.Enviar(dto);

            var negotiated = resultado as NegotiatedContentResult<EnvioCorreoRespuestaDTO>;
            Assert.IsNotNull(negotiated);
            Assert.AreEqual(HttpStatusCode.InternalServerError, negotiated.StatusCode);
            Assert.IsFalse(negotiated.Content.Enviado);
        }
    }
}
