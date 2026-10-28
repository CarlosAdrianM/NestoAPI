using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.EnvioDocumentosCorreo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#724 (sugerencia 831): mandar el pedido o el mandato SEPA por correo sin descargarlo. Con el servicio
    /// de correo falso: no sale ningún correo real.
    /// </summary>
    [TestClass]
    public class GestorEnvioDocumentosCorreoTests
    {
        private IProveedorDocumentosCorreo proveedor;
        private IServicioCorreoElectronico servicioCorreo;
        private GestorEnvioDocumentosCorreo gestor;
        private byte[] adjuntoEnviado;
        private static readonly byte[] PDF = { 0x25, 0x50, 0x44, 0x46 };

        [TestInitialize]
        public void Inicializar()
        {
            proveedor = A.Fake<IProveedorDocumentosCorreo>();
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            adjuntoEnviado = null;
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
            {
                // Se copia aquí: el gestor libera el correo (y sus adjuntos) en cuanto sale
                using (var ms = new MemoryStream())
                {
                    m.Attachments.Single().ContentStream.CopyTo(ms);
                    adjuntoEnviado = ms.ToArray();
                }
                Destinatarios = m.To.Select(t => t.Address).ToList();
                Copias = m.CC.Select(t => t.Address).ToList();
                Remitente = m.From.Address;
                Asunto = m.Subject;
                Cuerpo = m.Body;
                NombreAdjunto = m.Attachments.Single().Name;
                return true;
            });
            A.CallTo(() => proveedor.PdfPedido("1", 929498, A<string>._)).Returns(Task.FromResult(new DocumentoParaCorreo
            {
                Pdf = PDF,
                NombreFichero = "Pedido_929498.pdf",
                Cliente = "15191",
                Contacto = "0",
                Remitente = new MailAddress("administracion@nuevavision.es"),
                Firma = "<p>Departamento de Administración</p>"
            }));
            A.CallTo(() => proveedor.PdfMandato("1", "15191", "0", A<string>._, A<string>._)).Returns(Task.FromResult(new DocumentoParaCorreo
            {
                Pdf = PDF,
                NombreFichero = "MandatoSEPA_15191_1.pdf",
                Cliente = "15191",
                Contacto = "0",
                Remitente = new MailAddress("administracion@nuevavision.es")
            }));
            A.CallTo(() => proveedor.CorreoUsuario("NUEVAVISION\\MariaJose")).Returns("mariajose@nuevavision.es");
            gestor = new GestorEnvioDocumentosCorreo(proveedor, servicioCorreo);
        }

        private List<string> Destinatarios;
        private List<string> Copias;
        private string Remitente;
        private string Asunto;
        private string Cuerpo;
        private string NombreAdjunto;

        private static EnvioDocumentoCorreoDTO Para(params string[] destinatarios)
            => new EnvioDocumentoCorreoDTO { Destinatarios = destinatarios.ToList() };

        [TestMethod]
        public async Task EnviarPedido_ConDestinatarios_MandaElPdfDelPedidoConElRemitenteDeLaEmpresaYCopiaAlUsuario()
        {
            ResultadoEnvioDocumentoCorreoDTO resultado = await gestor.EnviarPedido("1", 929498,
                Para("cliente@correo.es; otro@correo.es"), "NUEVAVISION\\MariaJose");

            CollectionAssert.AreEqual(new[] { "cliente@correo.es", "otro@correo.es" }, Destinatarios);
            CollectionAssert.AreEqual(new[] { "mariajose@nuevavision.es" }, Copias);
            Assert.AreEqual("administracion@nuevavision.es", Remitente);
            Assert.AreEqual("Pedido nº 929498", Asunto);
            StringAssert.Contains(Cuerpo, "Adjunto le enviamos el pedido nº 929498.");
            StringAssert.Contains(Cuerpo, "Departamento de Administración");
            Assert.AreEqual("Pedido_929498.pdf", NombreAdjunto);
            CollectionAssert.AreEqual(PDF, adjuntoEnviado);
            CollectionAssert.AreEqual(new[] { "cliente@correo.es", "otro@correo.es" }, resultado.Destinatarios);
            Assert.AreEqual("Pedido_929498.pdf", resultado.Adjunto);
            // Ni las personas de contacto: van a quien ha dicho el usuario
            A.CallTo(() => proveedor.CorreosContacto(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPedido_SinDestinatarios_VaALasPersonasDeContactoConCorreo()
        {
            A.CallTo(() => proveedor.CorreosContacto("1", "15191", "0"))
                .Returns(Task.FromResult(new List<string> { "compras@cliente.es", "jefa@cliente.es" }));

            ResultadoEnvioDocumentoCorreoDTO resultado = await gestor.EnviarPedido("1", 929498, new EnvioDocumentoCorreoDTO(), "NUEVAVISION\\MariaJose");

            CollectionAssert.AreEqual(new[] { "compras@cliente.es", "jefa@cliente.es" }, Destinatarios);
            CollectionAssert.AreEqual(new[] { "compras@cliente.es", "jefa@cliente.es" }, resultado.Destinatarios);
        }

        [TestMethod]
        public async Task EnviarPedido_SinDestinatariosNiPersonasDeContactoConCorreo_400ConElMotivoYNoManda()
        {
            A.CallTo(() => proveedor.CorreosContacto("1", "15191", "0")).Returns(Task.FromResult(new List<string>()));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarPedido("1", 929498, Para(" ", null), "NUEVAVISION\\MariaJose"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(GestorEnvioDocumentosCorreo.MOTIVO_SIN_DESTINATARIOS, ex.Message);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => proveedor.RegistrarEnvio(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPedido_CorreoMalEscrito_400DiciendoCual()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarPedido("1", 929498, Para("cliente@correo.es;esto no es un correo"), "NUEVAVISION\\MariaJose"));

            StringAssert.Contains(ex.Message, "esto no es un correo");
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarPedido_QueNoExiste_404()
        {
            A.CallTo(() => proveedor.PdfPedido("1", 1, A<string>._)).Returns(Task.FromResult<DocumentoParaCorreo>(null));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarPedido("1", 1, Para("cliente@correo.es"), "NUEVAVISION\\MariaJose"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task EnviarPedido_ConAsuntoYTexto_UsaLosDelUsuarioSinMeterSuHtml()
        {
            var envio = Para("cliente@correo.es");
            envio.Asunto = "Su pedido de octubre";
            envio.Texto = "Hola, Ana:\nle mando el pedido <b>revisado</b>.";

            _ = await gestor.EnviarPedido("1", 929498, envio, "NUEVAVISION\\MariaJose");

            Assert.AreEqual("Su pedido de octubre", Asunto);
            StringAssert.Contains(Cuerpo, "Hola, Ana:<br/>le mando el pedido &lt;b&gt;revisado&lt;/b&gt;.");
        }

        [TestMethod]
        public async Task EnviarPedido_UsuarioSinCorreo_SaleSinCopia()
        {
            _ = await gestor.EnviarPedido("1", 929498, Para("cliente@correo.es"), "NUEVAVISION\\Otro");

            Assert.AreEqual(0, Copias.Count);
        }

        [TestMethod]
        public async Task EnviarPedido_QuedaConstanciaEnModificaciones()
        {
            _ = await gestor.EnviarPedido("1", 929498, Para("cliente@correo.es"), "NUEVAVISION\\MariaJose");

            A.CallTo(() => proveedor.RegistrarEnvio("Pedidos", "Pedido 929498",
                A<string>.That.StartsWith("Pedido 929498 enviado por correo a cliente@correo.es"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarPedido_ElServidorDeCorreoNoLoAcepta_502YSinConstancia()
        {
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarPedido("1", 929498, Para("cliente@correo.es"), "NUEVAVISION\\MariaJose"));

            Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
            A.CallTo(() => proveedor.RegistrarEnvio(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarMandato_MandaElPdfDelMandatoDeEsaCuenta()
        {
            var envio = new EnvioMandatoCorreoDTO { Ccc = "1", Motor = "QuestPDF", Destinatarios = new List<string> { "cliente@correo.es" } };

            ResultadoEnvioDocumentoCorreoDTO resultado = await gestor.EnviarMandato("1", "15191", "0", envio, "NUEVAVISION\\MariaJose");

            A.CallTo(() => proveedor.PdfMandato("1", "15191", "0", "1", "QuestPDF")).MustHaveHappenedOnceExactly();
            Assert.AreEqual("MandatoSEPA_15191_1.pdf", NombreAdjunto);
            CollectionAssert.AreEqual(PDF, adjuntoEnviado);
            Assert.AreEqual("Orden de domiciliación SEPA", Asunto);
            StringAssert.Contains(Cuerpo, "devuelva firmada");
            Assert.AreEqual("MandatoSEPA_15191_1.pdf", resultado.Adjunto);
            A.CallTo(() => proveedor.RegistrarEnvio("Clientes", "Cliente 15191/0",
                A<string>.That.StartsWith("Mandato SEPA del cliente 15191/0 enviado por correo a cliente@correo.es"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarMandato_SinDestinatarios_VaALasPersonasDeContactoYSiNoHay400()
        {
            A.CallTo(() => proveedor.CorreosContacto("1", "15191", "0")).Returns(Task.FromResult(new List<string>()));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarMandato("1", "15191", "0", new EnvioMandatoCorreoDTO { Ccc = "1" }, "NUEVAVISION\\MariaJose"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarMandato_ClienteOCuentaQueNoExiste_404()
        {
            A.CallTo(() => proveedor.PdfMandato("1", "99999", "0", A<string>._, A<string>._)).Returns(Task.FromResult<DocumentoParaCorreo>(null));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => gestor.EnviarMandato("1", "99999", "0", new EnvioMandatoCorreoDTO { Ccc = "1" }, "NUEVAVISION\\MariaJose"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public void CorreosValidosSinRepetir_SaltaLosMalEscritosYLosRepetidos()
        {
            List<string> correos = GestorEnvioDocumentosCorreo.CorreosValidosSinRepetir(
                new[] { " ana@cliente.es ", "ANA@cliente.es", "mal escrito", null, "luis@cliente.es; pepe@cliente.es" });

            CollectionAssert.AreEqual(new[] { "ana@cliente.es", "luis@cliente.es", "pepe@cliente.es" }, correos);
        }
    }
}
