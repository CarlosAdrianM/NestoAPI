using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnvioDocumentosCorreo;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#724: el envío del pedido y del mandato SEPA por correo es solo para empleados (token de Nesto con
    /// IsEmployee=true), como el de facturas. Ni una clienta de TNV ni un vendedor de NestoApp.
    /// </summary>
    [TestClass]
    public class EnvioDocumentosCorreoControllerTests
    {
        private IProveedorDocumentosCorreo proveedor;
        private IServicioCorreoElectronico servicioCorreo;

        [TestInitialize]
        public void Inicializar()
        {
            proveedor = A.Fake<IProveedorDocumentosCorreo>();
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            A.CallTo(() => proveedor.PdfPedido(A<string>._, A<int>._, A<string>._)).Returns(Task.FromResult(new DocumentoParaCorreo
            {
                Pdf = new byte[] { 1 },
                NombreFichero = "Pedido_929498.pdf",
                Cliente = "15191",
                Contacto = "0",
                Remitente = new MailAddress("administracion@nuevavision.es")
            }));
            A.CallTo(() => proveedor.PdfMandato(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new DocumentoParaCorreo
            {
                Pdf = new byte[] { 1 },
                NombreFichero = "MandatoSEPA_15191_1.pdf",
                Cliente = "15191",
                Contacto = "0",
                Remitente = new MailAddress("administracion@nuevavision.es")
            }));
        }

        private EnvioDocumentosCorreoController Controlador(params Claim[] claims)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\MariaJose"));
            identity.AddClaims(claims);
            return new EnvioDocumentosCorreoController(proveedor, servicioCorreo) { User = new ClaimsPrincipal(identity) };
        }

        private static readonly Claim EMPLEADO = new Claim("IsEmployee", "true");

        private static EnvioDocumentoCorreoDTO Envio() => new EnvioDocumentoCorreoDTO { Destinatarios = new List<string> { "cliente@correo.es" } };

        [TestMethod]
        public void EnviarPorCorreo_PideToken()
        {
            Assert.IsTrue(typeof(EnvioDocumentosCorreoController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_UnEmpleado_LoMandaYDevuelveDestinatariosYAdjunto()
        {
            var resultado = await Controlador(EMPLEADO).EnviarPedidoPorCorreo("1", 929498, Envio())
                as OkNegotiatedContentResult<ResultadoEnvioDocumentoCorreoDTO>;

            Assert.IsNotNull(resultado);
            CollectionAssert.AreEqual(new[] { "cliente@correo.es" }, resultado.Content.Destinatarios);
            Assert.AreEqual("Pedido_929498.pdf", resultado.Content.Adjunto);
            A.CallTo(() => proveedor.PdfPedido("1", 929498, "NUEVAVISION\\MariaJose")).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_UnClienteDeLaTienda_403YNoManda()
        {
            var resultado = await Controlador(new Claim("cliente", "15191")).EnviarPedidoPorCorreo("1", 929498, Envio())
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => proveedor.PdfPedido(A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_UnVendedorDeNestoApp_403()
        {
            var resultado = await Controlador(new Claim("IsVendedor", "true")).EnviarPedidoPorCorreo("1", 929498, Envio())
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(EnvioDocumentosCorreoController.MOTIVO_SOLO_EMPLEADOS, resultado.Content);
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_SinDestinatariosNiPersonasDeContacto_400ConMotivo()
        {
            A.CallTo(() => proveedor.CorreosContacto(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new List<string>()));

            var resultado = await Controlador(EMPLEADO).EnviarPedidoPorCorreo("1", 929498, null) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(GestorEnvioDocumentosCorreo.MOTIVO_SIN_DESTINATARIOS, resultado.Message);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_PedidoQueNoExiste_404()
        {
            A.CallTo(() => proveedor.PdfPedido(A<string>._, A<int>._, A<string>._)).Returns(Task.FromResult<DocumentoParaCorreo>(null));

            var resultado = await Controlador(EMPLEADO).EnviarPedidoPorCorreo("1", 1, Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
        }

        [TestMethod]
        public async Task EnviarPedidoPorCorreo_ElCorreoNoSale_502()
        {
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);

            var resultado = await Controlador(EMPLEADO).EnviarPedidoPorCorreo("1", 929498, Envio()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
        }

        [TestMethod]
        public async Task EnviarMandatoPorCorreo_UnEmpleado_LoMandaConLaCuentaYElMotor()
        {
            var envio = new EnvioMandatoCorreoDTO { Ccc = "1", Motor = "RDLC", Destinatarios = new List<string> { "cliente@correo.es" } };

            var resultado = await Controlador(EMPLEADO).EnviarMandatoPorCorreo("1", "15191  ", "0  ", envio)
                as OkNegotiatedContentResult<ResultadoEnvioDocumentoCorreoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("MandatoSEPA_15191_1.pdf", resultado.Content.Adjunto);
            A.CallTo(() => proveedor.PdfMandato("1", "15191", "0", "1", "RDLC")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarMandatoPorCorreo_QuienNoEsEmpleado_403()
        {
            var resultado = await Controlador(new Claim("cliente", "15191")).EnviarMandatoPorCorreo("1", "15191", "0", new EnvioMandatoCorreoDTO())
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => proveedor.PdfMandato(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorreosContacto_UnEmpleado_LosDevuelve_YQuienNoLoEs403()
        {
            A.CallTo(() => proveedor.CorreosContacto("1", "15191", "0")).Returns(Task.FromResult(new List<string> { "ana@cliente.es" }));

            var ok = await Controlador(EMPLEADO).GetCorreosContacto("1", "15191 ", "0") as OkNegotiatedContentResult<List<string>>;
            var prohibido = await Controlador(new Claim("cliente", "15191")).GetCorreosContacto("1", "15191", "0") as NegotiatedContentResult<string>;

            Assert.IsNotNull(ok);
            CollectionAssert.AreEqual(new[] { "ana@cliente.es" }, ok.Content);
            Assert.IsNotNull(prohibido);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
        }
    }
}
