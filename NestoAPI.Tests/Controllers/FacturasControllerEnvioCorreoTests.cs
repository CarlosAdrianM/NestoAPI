using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#259 (05/10/26): mandar facturas por correo a una dirección cualquiera es solo para empleados (Nesto).
    /// Sin esto, una clienta de TNV o un vendedor de NestoApp podía mandarse las facturas de otro cliente.
    /// </summary>
    [TestClass]
    public class FacturasControllerEnvioCorreoTests
    {
        private IServicioFacturas servicio;
        private IGestorFacturas gestor;

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioFacturas>();
            gestor = A.Fake<IGestorFacturas>();
        }

        private FacturasController Controlador(params Claim[] claims)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "Usuario"));
            identity.AddClaims(claims);
            return new FacturasController(servicio, gestor) { User = new ClaimsPrincipal(identity) };
        }

        private static EnvioFacturasCorreoDTO Envio() => new EnvioFacturasCorreoDTO
        {
            Empresa = "1",
            Facturas = new List<string> { "NV2616199" },
            Correos = new List<string> { "alguien@correo.es" }
        };

        [TestMethod]
        public async Task EnviarPorCorreo_UnaClientaDeLaTienda_NoPuedeMandarFacturas()
        {
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            var resultado = await controlador.EnviarPorCorreo(Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnVendedorDeNestoApp_NoPuedeMandarFacturas()
        {
            FacturasController controlador = Controlador(new Claim("IsVendedor", "true"));

            var resultado = await controlador.EnviarPorCorreo(Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnEmpleado_LasManda()
        {
            A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoEnvioFacturasCorreoDTO { Enviado = true }));
            FacturasController controlador = Controlador(new Claim("IsEmployee", "true"));

            var resultado = await controlador.EnviarPorCorreo(Envio());

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoEnvioFacturasCorreoDTO>));
        }

        [TestMethod]
        public void CorreoFacturas_QuienNoEsEmpleado_NoVeElCorreoDelCliente()
        {
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            var resultado = controlador.GetCorreoFacturas("1", "NV2616199") as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicio.LeerCorreoFacturas(A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
