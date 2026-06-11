using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Models.CanalesExternos;
using System;
using System.Net;
using System.Security.Claims;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#225: el endpoint de credencial Amazon solo puede servir el secreto a los grupos
    /// AD que ejecutan la integración (TiendaOnline y Administración). Los grupos llegan en el
    /// JWT de windows-token como "DOMINIO\Grupo".
    /// </summary>
    [TestClass]
    public class CredencialesAmazonControllerTests
    {
        private IAmazonCredencialStore _store;
        private CredencialesAmazonController _controller;

        [TestInitialize]
        public void Setup()
        {
            _store = A.Fake<IAmazonCredencialStore>();
            A.CallTo(() => _store.Obtener()).Returns(new AmazonSpApiCredencial
            {
                ClientId = "amzn1.application-oa2-client.test",
                ClientSecret = "secreto-vigente",
                RefreshToken = "refresh-token",
                SecretExpiry = new DateTime(2026, 12, 7)
            });
            _controller = new CredencialesAmazonController(_store);
        }

        private void AutenticarConRoles(params string[] roles)
        {
            var claims = new System.Collections.Generic.List<Claim>
            {
                new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos"),
                new Claim("IsEmployee", "true")
            };
            foreach (string rol in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, rol));
            }
            var identity = new ClaimsIdentity(claims, "JWT");
            _controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identity) };
        }

        [TestMethod]
        public void GetCredencialAmazon_GrupoTiendaOnline_DevuelveLaCredencial()
        {
            AutenticarConRoles("NUEVAVISION\\Usuarios del dominio", "NUEVAVISION\\TiendaOnline");

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<CredencialAmazonDTO>));
            var ok = (OkNegotiatedContentResult<CredencialAmazonDTO>)resultado;
            Assert.AreEqual("secreto-vigente", ok.Content.ClientSecret);
            Assert.AreEqual("refresh-token", ok.Content.RefreshToken);
            Assert.AreEqual(new DateTime(2026, 12, 7), ok.Content.SecretExpiry);
        }

        [TestMethod]
        public void GetCredencialAmazon_GrupoAdministracionConAcento_DevuelveLaCredencial()
        {
            AutenticarConRoles("NUEVAVISION\\Administración");

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<CredencialAmazonDTO>));
        }

        [TestMethod]
        public void GetCredencialAmazon_GrupoSinDominio_TambienVale()
        {
            // Por si el claim llegara sin el prefijo del dominio.
            AutenticarConRoles("TiendaOnline");

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<CredencialAmazonDTO>));
        }

        [TestMethod]
        public void GetCredencialAmazon_EmpleadoSinGrupoAutorizado_Forbidden()
        {
            // Una comercial autenticada (como MariaJose) NO debe poder leer el secreto.
            AutenticarConRoles("NUEVAVISION\\Usuarios del dominio", "NUEVAVISION\\Comerciales", "Todos");

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => _store.Obtener()).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetCredencialAmazon_SinRoles_Forbidden()
        {
            // Tokens de NestoApp/TiendasNuevaVision no llevan grupos AD: quedan excluidos.
            AutenticarConRoles();

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public void GetCredencialAmazon_SinCredencialEnTabla_NotFound()
        {
            A.CallTo(() => _store.Obtener()).Returns(null);
            AutenticarConRoles("NUEVAVISION\\TiendaOnline");

            var resultado = _controller.GetCredencialAmazon();

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }
    }
}
