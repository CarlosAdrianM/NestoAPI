using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#259 (05/10/26): mandar facturas por correo a una dirección cualquiera es solo para empleados (Nesto).
    /// Sin esto, una clienta de TNV o un vendedor de NestoApp podía mandarse las facturas de otro cliente.
    /// TNV (07/10/26): el cliente de la tienda sí puede, pero solo las suyas (y con un tope de envíos por hora).
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
            LimitadorEnviosFacturasCliente.Reiniciar();
        }

        [TestCleanup]
        public void Limpiar() => LimitadorEnviosFacturasCliente.Reiniciar();

        private void FacturaDe(string numero, string cliente)
            => A.CallTo(() => servicio.CargarCabFactura("1", numero)).Returns(new CabFacturaVta { Nº_Cliente = cliente });

        private void GestorEnvia()
            => A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoEnvioFacturasCorreoDTO { Enviado = true, Mensaje = "Enviada NV2616199 a gestoria@correo.es." }));

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
        public void EnviarPorCorreo_PideToken()
        {
            // Sin token → 401 lo da el [Authorize] (el filtro no corre en un test unitario: se comprueba que está)
            System.Reflection.MethodInfo metodo = typeof(FacturasController).GetMethod(nameof(FacturasController.EnviarPorCorreo));

            Assert.IsTrue(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnClienteDeLaTienda_MandaSusFacturasADondeQuiera()
        {
            // TNV (07/10/26): el cliente se manda sus facturas a su gestoría
            FacturaDe("NV2616199", "15191  ");
            FacturaDe("NV2616200", "15191");
            GestorEnvia();
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));
            EnvioFacturasCorreoDTO envio = Envio();
            envio.Facturas = new List<string> { "NV2616199", " NV2616200 " };

            IHttpActionResult resultado = await controlador.EnviarPorCorreo(envio);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoEnvioFacturasCorreoDTO>));
            A.CallTo(() => gestor.EnviarFacturasACorreo("1", envio.Facturas, envio.Correos, "Usuario")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnClienteDeLaTienda_NoPuedeMandarFacturasDeOtroCliente()
        {
            FacturaDe("NV2616199", "15191");
            FacturaDe("NV2699999", "20000");
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));
            EnvioFacturasCorreoDTO envio = Envio();
            envio.Facturas = new List<string> { "NV2616199", "NV2699999" };

            var resultado = await controlador.EnviarPorCorreo(envio) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            // Sin decir cuál no es suya
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS, resultado.Content);
            A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnClienteDeLaTienda_FacturaQueNoExiste_MismoRechazoQueSiFueraDeOtro()
        {
            // Para no desvelar qué facturas existen: inexistente y ajena dan el mismo 403
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).Returns(null);
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));

            var resultado = await controlador.EnviarPorCorreo(Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_SOLO_FACTURAS_PROPIAS, resultado.Content);
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnClienteDeLaTienda_DemasiadasFacturas_LoRechazaElGestorSinLeerlas()
        {
            A.CallTo(() => gestor.EnviarFacturasACorreo(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._, A<string>._))
                .Throws(new NestoBusinessException("Se pueden mandar como mucho 20 facturas en un correo (has marcado 21)."));
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));
            EnvioFacturasCorreoDTO envio = Envio();
            envio.Facturas = new List<string>();
            for (int i = 0; i <= GestorFacturas.MAXIMO_FACTURAS_POR_CORREO; i++)
            {
                envio.Facturas.Add("NV26" + i.ToString("00000"));
            }

            IHttpActionResult resultado = await controlador.EnviarPorCorreo(envio);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnClienteDeLaTienda_TopeDeEnviosPorHora()
        {
            FacturaDe("NV2616199", "15191");
            FacturaDe("NV2620000", "20000");
            GestorEnvia();
            FacturasController controlador = Controlador(new Claim("cliente", "15191"));
            for (int i = 0; i < LimitadorEnviosFacturasCliente.MAXIMO_ENVIOS_POR_HORA; i++)
            {
                Assert.IsInstanceOfType(await controlador.EnviarPorCorreo(Envio()), typeof(OkNegotiatedContentResult<ResultadoEnvioFacturasCorreoDTO>));
            }

            var resultado = await controlador.EnviarPorCorreo(Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(429, (int)resultado.StatusCode);
            Assert.AreEqual(FacturasController.MOTIVO_DEMASIADOS_ENVIOS, resultado.Content);

            // Otro cliente no se ve afectado...
            EnvioFacturasCorreoDTO envioOtro = Envio();
            envioOtro.Facturas = new List<string> { "NV2620000" };
            Assert.IsInstanceOfType(await Controlador(new Claim("cliente", "20000")).EnviarPorCorreo(envioOtro),
                typeof(OkNegotiatedContentResult<ResultadoEnvioFacturasCorreoDTO>));

            // ...y pasada la hora, vuelve a poder
            DateTime dentroDeUnaHora = DateTime.UtcNow.AddHours(1).AddMinutes(1);
            LimitadorEnviosFacturasCliente.Ahora = () => dentroDeUnaHora;
            Assert.IsInstanceOfType(await controlador.EnviarPorCorreo(Envio()), typeof(OkNegotiatedContentResult<ResultadoEnvioFacturasCorreoDTO>));
        }

        [TestMethod]
        public async Task EnviarPorCorreo_UnEmpleado_NoPasaPorLaComprobacionDeCliente()
        {
            GestorEnvia();
            FacturasController controlador = Controlador(new Claim("IsEmployee", "true"));

            _ = await controlador.EnviarPorCorreo(Envio());

            A.CallTo(() => servicio.CargarCabFactura(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => gestor.EnviarFacturasACorreo("1", A<IEnumerable<string>>._, A<IEnumerable<string>>._, "Usuario")).MustHaveHappenedOnceExactly();
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
