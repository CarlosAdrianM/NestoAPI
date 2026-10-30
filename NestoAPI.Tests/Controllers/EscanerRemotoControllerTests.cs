using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models.EscanerRemoto;
using System;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#768: api/EscanerRemoto (Ariadna manda lo leído al Nesto del mismo usuario).</summary>
    [TestClass]
    public class EscanerRemotoControllerTests
    {
        private IEscanerRemoto servicio;

        [TestInitialize]
        public void Preparar() => servicio = A.Fake<IEscanerRemoto>();

        private EscanerRemotoController Controlador(IPrincipal usuario)
        {
            var controller = new EscanerRemotoController(servicio) { User = usuario };
            return controller;
        }

        private static IPrincipal Mozo(string nombre = "Alfredo")
            => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "Bearer"));

        [TestMethod]
        public void PostLectura_MandaConElUsuarioDelTokenYElCodigoRecortado()
        {
            var esperada = new DestinoEscanerRemotoDTO { HayNesto = true, Entregado = true, Mensaje = "Enviado a Nesto de Alfredo (RDS2016)." };
            _ = A.CallTo(() => servicio.Enviar("Alfredo", A<LecturaEscanerRemotoDTO>.That.Matches(l => l.Codigo == "8412345678901" && l.ClaveIdempotencia == "k1")))
                .Returns(esperada);

            IHttpActionResult resultado = Controlador(Mozo()).PostLectura(
                new LecturaEscanerRemotoDTO { Codigo = "  8412345678901 ", ClaveIdempotencia = "k1", FechaHora = DateTime.Now });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<DestinoEscanerRemotoDTO>));
            Assert.AreSame(esperada, ((OkNegotiatedContentResult<DestinoEscanerRemotoDTO>)resultado).Content);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void PostLectura_SinCodigo_400YNoManda(string codigo)
        {
            IHttpActionResult resultado = Controlador(Mozo()).PostLectura(new LecturaEscanerRemotoDTO { Codigo = codigo });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.Enviar(A<string>._, A<LecturaEscanerRemotoDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostLectura_SinCuerpo_400()
        {
            Assert.IsInstanceOfType(Controlador(Mozo()).PostLectura(null), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PostLectura_CodigoDemasiadoLargo_400()
        {
            IHttpActionResult resultado = Controlador(Mozo()).PostLectura(
                new LecturaEscanerRemotoDTO { Codigo = new string('1', EscanerRemotoLimites.LONGITUD_MAXIMA_CODIGO + 1) });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.Enviar(A<string>._, A<LecturaEscanerRemotoDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostLectura_ClaveDemasiadoLarga_400()
        {
            IHttpActionResult resultado = Controlador(Mozo()).PostLectura(
                new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = new string('k', EscanerRemotoLimites.LONGITUD_MAXIMA_CLAVE + 1) });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PostLectura_TokenDeCliente_403YNoManda()
        {
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "a@b.es"), new Claim("cliente", "15191") }, "Bearer"));

            IHttpActionResult resultado = Controlador(cliente).PostLectura(new LecturaEscanerRemotoDTO { Codigo = "123" });

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<HttpError>));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<HttpError>)resultado).StatusCode);
            A.CallTo(() => servicio.Enviar(A<string>._, A<LecturaEscanerRemotoDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PostLectura_SinUsuario_401()
        {
            var anonimo = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.IsInstanceOfType(Controlador(anonimo).PostLectura(new LecturaEscanerRemotoDTO { Codigo = "123" }), typeof(UnauthorizedResult));
            A.CallTo(() => servicio.Enviar(A<string>._, A<LecturaEscanerRemotoDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetDestino_DevuelveElDelUsuarioDelToken()
        {
            var esperado = new DestinoEscanerRemotoDTO { HayNesto = true, Mensaje = "Enviando a Nesto de Alfredo (RDS2016)." };
            _ = A.CallTo(() => servicio.Destino("NUEVAVISION\\Alfredo")).Returns(esperado);

            IHttpActionResult resultado = Controlador(Mozo("NUEVAVISION\\Alfredo")).GetDestino();

            Assert.AreSame(esperado, ((OkNegotiatedContentResult<DestinoEscanerRemotoDTO>)resultado).Content);
        }

        [TestMethod]
        public void GetDestino_TokenDeCliente_403()
        {
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "a@b.es"), new Claim("cliente", "15191") }, "Bearer"));

            IHttpActionResult resultado = Controlador(cliente).GetDestino();

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<HttpError>)resultado).StatusCode);
        }

        [TestMethod]
        public void ExtremoAExtremo_ElServicioRealSoloMandaAlNestoDelMismoUsuario()
        {
            var conexiones = new ConexionesEscanerRemoto();
            conexiones.Actividad("c-alfredo", "NUEVAVISION\\Alfredo", "RDS2016", true, DateTime.UtcNow);
            conexiones.Actividad("c-pedro", "NUEVAVISION\\Pedro", "RDS2016", true, DateTime.UtcNow.AddSeconds(1));
            string mandadaA = null;
            servicio = new ServicioEscanerRemoto(conexiones, (c, a) => mandadaA = c, () => DateTime.UtcNow);

            var resultado = (OkNegotiatedContentResult<DestinoEscanerRemotoDTO>)Controlador(Mozo("alfredo"))
                .PostLectura(new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k" });

            Assert.AreEqual("c-alfredo", mandadaA);
            Assert.IsTrue(resultado.Content.Entregado);
            Assert.AreEqual("Enviado a Nesto de alfredo (RDS2016).", resultado.Content.Mensaje);
        }
    }
}
