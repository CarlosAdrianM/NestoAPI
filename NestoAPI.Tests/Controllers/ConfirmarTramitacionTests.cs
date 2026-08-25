using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// POST api/EnviosAgencias/{id}/ConfirmarTramitacion (Nesto#340 slice A4.1): el paso que cierra
    /// el envío en NUESTRA base de datos y contabiliza el reembolso, después de que la agencia lo
    /// haya registrado. El servicio va fakeado: aquí solo se comprueba el contrato del endpoint.
    /// </summary>
    [TestClass]
    public class ConfirmarTramitacionTests
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

        [TestMethod]
        public async Task ConfirmarTramitacion_Exito_DevuelveElAsientoYElMensaje()
        {
            ConUsuario("carlos");
            A.CallTo(() => fakeServicio.TramitarAsync(247975, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionEnvio
                {
                    Numero = 247975,
                    Asiento = 88131,
                    Mensaje = "Envío del pedido 922175 tramitado correctamente."
                }));

            var resultado = await controller.ConfirmarTramitacion(247975)
                as OkNegotiatedContentResult<ResultadoTramitacionEnvio>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(88131, resultado.Content.Asiento);
            Assert.AreEqual("Envío del pedido 922175 tramitado correctamente.", resultado.Content.Mensaje);
        }

        [TestMethod]
        public async Task ConfirmarTramitacion_UsaElUsuarioDelJwtParaElAsiento()
        {
            // reference_prdcontabilizar_usuario_api: el usuario del asiento lo pone el servidor
            // desde el token, NUNCA el cliente. Si no, la auditoría contable acaba diciendo
            // RDS2016$ en vez de quién lo hizo.
            ConUsuario("NUEVAVISION\\laura");
            A.CallTo(() => fakeServicio.TramitarAsync(A<int>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionEnvio()));

            _ = await controller.ConfirmarTramitacion(1);

            A.CallTo(() => fakeServicio.TramitarAsync(1, "NUEVAVISION\\laura")).MustHaveHappened();
        }
    }
}
