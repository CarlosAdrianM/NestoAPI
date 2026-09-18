using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// POST api/EnviosAgencias/PagarReembolsos (Nesto#415 / Nesto#340 slice A4.3): la agencia paga
    /// los reembolsos cobrados. El servicio va fakeado: aquí solo se comprueba el contrato del
    /// endpoint (usuario del JWT, 400 con el motivo de negocio tal cual).
    /// </summary>
    [TestClass]
    public class PagarReembolsosControllerTests
    {
        private NVEntities db;
        private ITramitacionEnviosService fakeServicio;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeServicio = A.Fake<ITramitacionEnviosService>();
            controller = new EnviosAgenciasController(db, fakeServicio);
        }

        private void ConUsuario(string nombre)
        {
            IPrincipal usuario = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, nombre) }, "Bearer"));
            controller.User = usuario;
        }

        private static PagoReembolsosDTO Pago() => new PagoReembolsosDTO
        {
            Empresa = "1",
            Cliente = "12345",
            Agencia = 12,
            NumerosEnvio = new List<int> { 248001, 248002 }
        };

        [TestMethod]
        public async Task PagarReembolsos_Exito_DevuelveElResultadoDelServicio()
        {
            ConUsuario("carlos");
            A.CallTo(() => fakeServicio.PagarReembolsosAsync(A<PagoReembolsosDTO>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoPagoReembolsos { Asiento = 88131, Envios = 2, Importe = 151.50M, Mensaje = "ok" }));

            var resultado = await controller.PagarReembolsos(Pago()) as OkNegotiatedContentResult<ResultadoPagoReembolsos>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(88131, resultado.Content.Asiento);
            Assert.AreEqual(2, resultado.Content.Envios);
            Assert.AreEqual(151.50M, resultado.Content.Importe);
        }

        [TestMethod]
        public async Task PagarReembolsos_UsaElUsuarioDelJwtYPasaLosDatosTalCual()
        {
            // reference_prdcontabilizar_usuario_api: el usuario del asiento lo pone el servidor
            // desde el token, NUNCA el cliente.
            ConUsuario(@"NUEVAVISION\laura");
            A.CallTo(() => fakeServicio.PagarReembolsosAsync(A<PagoReembolsosDTO>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoPagoReembolsos()));
            PagoReembolsosDTO datos = Pago();

            _ = await controller.PagarReembolsos(datos);

            A.CallTo(() => fakeServicio.PagarReembolsosAsync(datos, @"NUEVAVISION\laura")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagarReembolsos_MotivoDeNegocio_Devuelve400ConElTextoTalCual()
        {
            ConUsuario("carlos");
            A.CallTo(() => fakeServicio.PagarReembolsosAsync(A<PagoReembolsosDTO>.Ignored, A<string>.Ignored))
                .Throws(new NestoBusinessException("El reembolso del envío 248001 (pedido 925100) ya se pagó el 17/09/2026."));

            var resultado = await controller.PagarReembolsos(Pago()) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("El reembolso del envío 248001 (pedido 925100) ya se pagó el 17/09/2026.", resultado.Message);
        }

        [TestMethod]
        public async Task PagarReembolsos_SinCuerpo_Devuelve400SinLlamarAlServicio()
        {
            ConUsuario("carlos");

            var resultado = await controller.PagarReembolsos(null) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            A.CallTo(() => fakeServicio.PagarReembolsosAsync(A<PagoReembolsosDTO>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }
    }
}
