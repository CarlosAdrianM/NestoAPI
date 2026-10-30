using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.EnlacesDescarga;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#737 (sugerencias 866/867): el enlace de descarga del pedido o la factura para WhatsApp Web (Nesto#570). Crearlo es
    /// solo de empleados; descargarlo, de quien tenga el enlace (token aleatorio, 30 días). Caducado o inexistente → página de usted.
    /// </summary>
    [TestClass]
    public class DocumentosControllerTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 30, 10, 15, 0);
        private const string TOKEN = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde"; // 43 caracteres
        private static readonly Claim EMPLEADO = new Claim("IsEmployee", "true");

        private IRepositorioEnlacesDescarga repositorio;
        private IProveedorDocumentosEnlace proveedor;
        private GestorEnlacesDescarga gestor;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            ahora = AHORA;
            repositorio = A.Fake<IRepositorioEnlacesDescarga>();
            proveedor = A.Fake<IProveedorDocumentosEnlace>();
            gestor = new GestorEnlacesDescarga(repositorio, proveedor, () => ahora, () => "https://api.nuevavision.es/", () => TOKEN);
            A.CallTo(() => proveedor.LeerDatos(TiposDocumentoDescarga.PEDIDO, "1", "929498")).Returns(Task.FromResult(new DatosDocumentoEnlace
            {
                Cliente = "15191",
                Contacto = "0",
                Nombre = "Peluquería Ana",
                Pais = "ES",
                Importe = 1234.564M,
                Telefonos = new List<string> { "916281914 / 600 11 22 33", "600112233", "+34 711-22-33-44" }
            }));
            A.CallTo(() => proveedor.Pdf(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new byte[] { 37, 80, 68, 70 }));
        }

        private DocumentosController Controlador(params Claim[] claims)
        {
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma"));
            identity.AddClaims(claims);
            return new DocumentosController(gestor) { User = new ClaimsPrincipal(identity) };
        }

        private static PeticionEnlaceDescargaDTO Pedido() => new PeticionEnlaceDescargaDTO { Tipo = "pedido", Empresa = "1", Numero = "929498" };

        // ===================================================================== POST api/Documentos/EnlaceDescarga

        [TestMethod]
        public void CrearEnlace_PideToken()
        {
            Assert.IsTrue(typeof(DocumentosController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task CrearEnlace_SinSerEmpleado_403YNoGuardaNada()
        {
            var resultado = await Controlador().CrearEnlaceDescarga(Pedido()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<EnlaceDescargaDocumento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearEnlace_UnEmpleado_GuardaElEnlaceA30DiasYDevuelveUrlTextoYMoviles()
        {
            EnlaceDescargaDocumento guardado = null;
            A.CallTo(() => repositorio.Guardar(A<EnlaceDescargaDocumento>._)).Invokes((EnlaceDescargaDocumento e) => guardado = e).Returns(Task.CompletedTask);

            var resultado = await Controlador(EMPLEADO).CrearEnlaceDescarga(Pedido()) as OkNegotiatedContentResult<EnlaceDescargaDTO>;

            Assert.IsNotNull(resultado);
            EnlaceDescargaDTO enlace = resultado.Content;
            Assert.AreEqual("https://api.nuevavision.es/d/" + TOKEN, enlace.Url);
            Assert.AreEqual(AHORA.AddDays(30), enlace.Caduca);
            Assert.AreEqual(TiposDocumentoDescarga.PEDIDO, enlace.Tipo);
            Assert.AreEqual(1234.56M, enlace.Importe);
            Assert.AreEqual("Peluquería Ana", enlace.Nombre);
            CollectionAssert.AreEqual(new[] { "34600112233", "34711223344" }, enlace.Moviles, "sin el fijo y sin repetir");
            StringAssert.Contains(enlace.Texto, enlace.Url);

            Assert.IsNotNull(guardado);
            Assert.AreEqual(TOKEN, guardado.Token);
            Assert.AreEqual(TiposDocumentoDescarga.PEDIDO, guardado.Tipo);
            Assert.AreEqual("1", guardado.Empresa);
            Assert.AreEqual("929498", guardado.Numero);
            Assert.AreEqual(AHORA, guardado.Creado);
            Assert.AreEqual(AHORA.AddDays(30), guardado.Caduca);
            Assert.AreEqual("NUEVAVISION\\Paloma", guardado.Usuario, "queda quién lo creó");
        }

        [TestMethod]
        public async Task CrearEnlace_TipoQueNoEsPedidoNiFactura_400()
        {
            var resultado = await Controlador(EMPLEADO).CrearEnlaceDescarga(new PeticionEnlaceDescargaDTO { Tipo = "Albaran", Numero = "1" })
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(GestorEnlacesDescarga.MOTIVO_TIPO, resultado.Message);
        }

        [TestMethod]
        public async Task CrearEnlace_PedidoQueNoEsUnNumero_400()
        {
            var resultado = await Controlador(EMPLEADO).CrearEnlaceDescarga(new PeticionEnlaceDescargaDTO { Tipo = "Pedido", Numero = "NV26" })
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            A.CallTo(() => proveedor.LeerDatos(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearEnlace_DocumentoQueNoExiste_404YNoGuarda()
        {
            A.CallTo(() => proveedor.LeerDatos(TiposDocumentoDescarga.FACTURA, "1", "NV2699999")).Returns(Task.FromResult<DatosDocumentoEnlace>(null));

            var resultado = await Controlador(EMPLEADO).CrearEnlaceDescarga(new PeticionEnlaceDescargaDTO { Tipo = "Factura", Numero = "nv2699999" })
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "la factura NV2699999");
            A.CallTo(() => repositorio.Guardar(A<EnlaceDescargaDocumento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearEnlace_FacturaDeSerieQueNoSeDescarga_403()
        {
            A.CallTo(() => proveedor.LeerDatos(TiposDocumentoDescarga.FACTURA, "1", "XX2600001"))
                .ThrowsAsync(new NestoBusinessException("Las facturas de la serie XX no se pueden descargar.") { StatusCode = HttpStatusCode.Forbidden });

            var resultado = await Controlador(EMPLEADO).CrearEnlaceDescarga(new PeticionEnlaceDescargaDTO { Tipo = "Factura", Numero = "XX2600001" })
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        // ===================================================================== texto de WhatsApp

        [TestMethod]
        public void TextoWhatsApp_Pedido_DeUstedConImporteEnlaceYCaducidad()
        {
            string texto = GestorEnlacesDescarga.TextoWhatsApp(TiposDocumentoDescarga.PEDIDO, "929498", "Peluquería Ana", 1234.564M,
                "https://api.nuevavision.es/d/x", AHORA.AddDays(30), AHORA);

            StringAssert.StartsWith(texto, "Buenos días, Peluquería Ana:");
            StringAssert.Contains(texto, "Le enviamos su pedido 929498, de 1.234,56 €. Puede descargarlo en PDF desde este enlace:\nhttps://api.nuevavision.es/d/x");
            StringAssert.Contains(texto, "El enlace estará disponible hasta el 29/11/2026.");
            StringAssert.Contains(texto, "Puede descargar nuestra app para Android, con sus pedidos y facturas:\nhttps://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas&referrer=utm_source%3Dwhatsapp%26utm_medium%3Dmensaje%26utm_campaign%3Denvio_pedido");
            StringAssert.EndsWith(texto, "Un saludo,\nNueva Visión");
            Assert.IsFalse(texto.Contains(" tu ") || texto.Contains("Te "), "de usted, nunca de tú");
        }

        [TestMethod]
        public void TextoWhatsApp_FacturaPorLaTardeYSinNombre()
        {
            string texto = GestorEnlacesDescarga.TextoWhatsApp(TiposDocumentoDescarga.FACTURA, "NV2616199", null, 50M,
                "https://api.nuevavision.es/d/x", AHORA.AddDays(30), new DateTime(2026, 10, 30, 17, 0, 0));

            StringAssert.StartsWith(texto, "Buenas tardes:");
            StringAssert.Contains(texto, "Le enviamos su factura NV2616199, de 50,00 €. Puede descargarla en PDF");
            StringAssert.Contains(texto, "utm_campaign%3Denvio_factura");
        }

        // ===================================================================== móviles y token

        [TestMethod]
        public void Moviles_FormatoInternacionalSegunElPais()
        {
            Assert.AreEqual("34600112233", MovilesWhatsApp.Internacional("600 11 22 33", "ES"));
            Assert.AreEqual("34600112233", MovilesWhatsApp.Internacional("600112233", null), "sin país = España");
            Assert.AreEqual("34600112233", MovilesWhatsApp.Internacional("+34 600 112 233", "PT"), "con prefijo manda el prefijo");
            Assert.AreEqual("34600112233", MovilesWhatsApp.Internacional("0034600112233", "ES"));
            Assert.AreEqual("34600112233", MovilesWhatsApp.Internacional("34600112233", "ES"));
            Assert.AreEqual("351912345678", MovilesWhatsApp.Internacional("912 345 678", "PT"));
            Assert.AreEqual("33612345678", MovilesWhatsApp.Internacional("+33 6 12 34 56 78", "FR"));
            Assert.IsNull(MovilesWhatsApp.Internacional("916281914", "ES"), "un fijo no vale para WhatsApp");
            Assert.IsNull(MovilesWhatsApp.Internacional("+34 916 281 914", "ES"));
            Assert.IsNull(MovilesWhatsApp.Internacional("612345678", "FR"), "de otro país sin prefijo no sabemos formatearlo");
            Assert.IsNull(MovilesWhatsApp.Internacional("", "ES"));
        }

        [TestMethod]
        public void Moviles_VariosPorCampoSinRepetir()
        {
            List<string> moviles = MovilesWhatsApp.Internacionales(new[] { "916281914/600112233", null, "600112233; 655443322" }, "ES");

            CollectionAssert.AreEqual(new[] { "34600112233", "34655443322" }, moviles);
        }

        [TestMethod]
        public void Token_AleatorioDe43CaracteresBase64Url()
        {
            var tokens = Enumerable.Range(0, 50).Select(_ => TokenEnlaceDescarga.Nuevo()).ToList();

            Assert.IsTrue(tokens.All(TokenEnlaceDescarga.FormatoValido));
            Assert.IsTrue(tokens.All(t => t.Length == TokenEnlaceDescarga.LONGITUD));
            Assert.AreEqual(50, tokens.Distinct().Count(), "no se repiten");
            Assert.IsFalse(TokenEnlaceDescarga.FormatoValido("929498"));
            Assert.IsFalse(TokenEnlaceDescarga.FormatoValido(null));
            Assert.IsFalse(TokenEnlaceDescarga.FormatoValido(TOKEN.Substring(1) + "+"));
        }

        // ===================================================================== GET /d/{token}

        private DescargaDocumentoController Descarga() => new DescargaDocumentoController(gestor) { Request = new HttpRequestMessage(), Configuration = new HttpConfiguration() };

        private void HayEnlace(DateTime caduca, string tipo = TiposDocumentoDescarga.PEDIDO, string numero = "929498")
            => A.CallTo(() => repositorio.Leer(TOKEN)).Returns(Task.FromResult(new EnlaceDescargaDocumento
            {
                Token = TOKEN,
                Tipo = tipo,
                Empresa = "1",
                Numero = numero,
                Creado = caduca.AddDays(-30),
                Caduca = caduca
            }));

        [TestMethod]
        public void Descarga_EsAnonimaEnLaRutaD()
        {
            Assert.IsTrue(typeof(DescargaDocumentoController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);
            var prefijo = (RoutePrefixAttribute)typeof(DescargaDocumentoController).GetCustomAttributes(typeof(RoutePrefixAttribute), true).Single();
            Assert.AreEqual("d", prefijo.Prefix);
        }

        [TestMethod]
        public async Task Descarga_EnlaceVigente_DevuelveElPdfYCuentaLaDescarga()
        {
            HayEnlace(AHORA.AddDays(29));

            HttpResponseMessage respuesta = await Descarga().GetDocumento(TOKEN);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/pdf", respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("inline", respuesta.Content.Headers.ContentDisposition.DispositionType);
            Assert.AreEqual("Pedido_929498.pdf", respuesta.Content.Headers.ContentDisposition.FileName);
            CollectionAssert.AreEqual(new byte[] { 37, 80, 68, 70 }, await respuesta.Content.ReadAsByteArrayAsync());
            Assert.IsTrue(respuesta.Headers.CacheControl.NoStore);
            A.CallTo(() => proveedor.Pdf(TiposDocumentoDescarga.PEDIDO, "1", "929498")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.RegistrarDescarga(TOKEN, AHORA)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Descarga_Factura_NombreDeFicheroDeFactura()
        {
            HayEnlace(AHORA.AddDays(1), TiposDocumentoDescarga.FACTURA, "NV2616199");

            HttpResponseMessage respuesta = await Descarga().GetDocumento(TOKEN);

            Assert.AreEqual("Factura_NV2616199.pdf", respuesta.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task Descarga_EnlaceCaducado_PaginaDeUsted410SinPdf()
        {
            HayEnlace(AHORA.AddMinutes(-1));

            HttpResponseMessage respuesta = await Descarga().GetDocumento(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync();

            Assert.AreEqual((HttpStatusCode)410, respuesta.StatusCode);
            Assert.AreEqual("text/html", respuesta.Content.Headers.ContentType.MediaType);
            StringAssert.Contains(html, "Este enlace ha caducado");
            StringAssert.Contains(html, "Póngase en contacto con nosotros y le enviaremos de nuevo su pedido 929498.");
            A.CallTo(() => proveedor.Pdf(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.RegistrarDescarga(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Descarga_EnlaceQueNoExiste_PaginaDeUsted404()
        {
            A.CallTo(() => repositorio.Leer(TOKEN)).Returns(Task.FromResult<EnlaceDescargaDocumento>(null));

            HttpResponseMessage respuesta = await Descarga().GetDocumento(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync();

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            StringAssert.Contains(html, "No encontramos este documento");
            StringAssert.Contains(html, "Compruebe que lo ha copiado entero");
            StringAssert.Contains(html, "noindex");
        }

        [TestMethod]
        public async Task Descarga_TokenSinFormato_NiSeBuscaEnLaBD()
        {
            HttpResponseMessage respuesta = await Descarga().GetDocumento("929498");

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            A.CallTo(() => repositorio.Leer(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Descarga_DocumentoBorradoDespuesDeCrearElEnlace_404()
        {
            HayEnlace(AHORA.AddDays(10));
            A.CallTo(() => proveedor.Pdf(A<string>._, A<string>._, A<string>._))
                .ThrowsAsync(new NestoBusinessException("No existe") { StatusCode = HttpStatusCode.NotFound });

            HttpResponseMessage respuesta = await Descarga().GetDocumento(TOKEN);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            A.CallTo(() => repositorio.RegistrarDescarga(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }
    }
}
