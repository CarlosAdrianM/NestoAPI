using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#660 / #593: la miniatura del cheque de la campaña (GET api/ChequesRegalo/{campana}/Miniatura), que es
    /// la foto de la línea del cheque en el correo «Gracias por su pedido».
    /// </summary>
    [TestClass]
    public class MiniaturasChequeRegaloTests
    {
        private IAvisadorChequesRegalo avisador;
        private IRepositorioAvisoChequesRegalo repositorio;

        [TestInitialize]
        public void Preparar()
        {
            avisador = A.Fake<IAvisadorChequesRegalo>();
            repositorio = A.Fake<IRepositorioAvisoChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampana(A<string>._)).Returns(Task.FromResult<CampanaCorreoChequeRegalo>(null));
            A.CallTo(() => repositorio.LeerCampana("CHEQUE50_OCT_2026"))
                .Returns(Task.FromResult(PlantillaCorreoChequeRegaloTests.Campana()));
            A.CallTo(() => repositorio.LeerCampana("SIN_IMAGEN"))
                .Returns(Task.FromResult(PlantillaCorreoChequeRegaloTests.Campana(imagen: null)));
            A.CallTo(() => repositorio.LeerCampana("IMAGEN_QUE_NO_EXISTE"))
                .Returns(Task.FromResult(PlantillaCorreoChequeRegaloTests.Campana(imagen: "no-existe.jpg")));
        }

        private ChequesRegaloController Controlador(HttpRequestMessage peticion = null)
            => new ChequesRegaloController(avisador)
            {
                // Anónimo: los clientes de correo no mandan credenciales
                Request = peticion ?? new HttpRequestMessage(HttpMethod.Get, "https://api.nuevavision.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura"),
                Configuration = new HttpConfiguration(),
                CampanasCorreo = repositorio
            };

        private static HttpResponseMessage Respuesta(IHttpActionResult resultado)
        {
            Assert.IsInstanceOfType(resultado, typeof(ResponseMessageResult));
            return ((ResponseMessageResult)resultado).Response;
        }

        [TestMethod]
        public void ElEndpoint_EsAnonimoYEstaEnSuRuta()
        {
            var metodo = typeof(ChequesRegaloController).GetMethod(nameof(ChequesRegaloController.GetMiniatura));

            Assert.IsTrue(metodo.GetCustomAttributes(typeof(AllowAnonymousAttribute), false).Any(), "La pide el cliente de correo, sin credenciales");
            Assert.IsFalse(metodo.GetCustomAttributes(typeof(AuthorizeAttribute), false).Any());
            Assert.AreEqual("{campana}/Miniatura", ((RouteAttribute)metodo.GetCustomAttributes(typeof(RouteAttribute), false).Single()).Template);
            Assert.IsTrue(metodo.GetCustomAttributes(typeof(HttpGetAttribute), false).Any());
        }

        [TestMethod]
        public async Task GetMiniatura_DevuelveUnJpegDe128DeAncho()
        {
            HttpResponseMessage respuesta = Respuesta(await Controlador().GetMiniatura("CHEQUE50_OCT_2026"));

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("image/jpeg", respuesta.Content.Headers.ContentType.MediaType);
            byte[] bytes = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.AreEqual(bytes.Length, respuesta.Content.Headers.ContentLength);
            using (var ms = new MemoryStream(bytes))
            using (Image imagen = Image.FromStream(ms))
            {
                Assert.AreEqual(ImageFormat.Jpeg.Guid, imagen.RawFormat.Guid);
                Assert.AreEqual(128, imagen.Width);
                Assert.AreEqual(85, imagen.Height, "900x600 → 128x85: en proporción");
            }
            Assert.IsTrue(bytes.Length < 15 * 1024, $"Una miniatura ligera para el correo ({bytes.Length} bytes)");
        }

        [TestMethod]
        public async Task GetMiniatura_LlevaCacheControlPublicoLargoYETag()
        {
            HttpResponseMessage respuesta = Respuesta(await Controlador().GetMiniatura("CHEQUE50_OCT_2026"));

            Assert.IsTrue(respuesta.Headers.CacheControl.Public);
            Assert.AreEqual(TimeSpan.FromDays(7), respuesta.Headers.CacheControl.MaxAge);
            Assert.IsFalse(respuesta.Headers.CacheControl.Private);
            Assert.IsFalse(respuesta.Headers.CacheControl.NoCache);
            Assert.IsNotNull(respuesta.Headers.ETag);
            StringAssert.StartsWith(respuesta.Headers.ETag.Tag, "\"");
            Assert.IsFalse(respuesta.Headers.ETag.IsWeak);
        }

        [TestMethod]
        public async Task GetMiniatura_ConElMismoETag_304SinCuerpo()
        {
            HttpResponseMessage primera = Respuesta(await Controlador().GetMiniatura("CHEQUE50_OCT_2026"));
            var peticion = new HttpRequestMessage(HttpMethod.Get, "https://api.nuevavision.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura");
            peticion.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(primera.Headers.ETag.Tag));

            HttpResponseMessage segunda = Respuesta(await Controlador(peticion).GetMiniatura("CHEQUE50_OCT_2026"));

            Assert.AreEqual(HttpStatusCode.NotModified, segunda.StatusCode);
            Assert.IsNull(segunda.Content);
            Assert.AreEqual(primera.Headers.ETag.Tag, segunda.Headers.ETag.Tag);
            Assert.IsTrue(segunda.Headers.CacheControl.Public);
        }

        [TestMethod]
        public async Task GetMiniatura_ConOtroETag_DevuelveLaImagen()
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, "https://api.nuevavision.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura");
            peticion.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"otra\""));

            HttpResponseMessage respuesta = Respuesta(await Controlador(peticion).GetMiniatura("CHEQUE50_OCT_2026"));

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetMiniatura_CampanaQueNoExisteOSinImagen_404()
        {
            Assert.IsInstanceOfType(await Controlador().GetMiniatura("NO_EXISTE"), typeof(NotFoundResult));
            Assert.IsInstanceOfType(await Controlador().GetMiniatura("SIN_IMAGEN"), typeof(NotFoundResult));
            Assert.IsInstanceOfType(await Controlador().GetMiniatura("IMAGEN_QUE_NO_EXISTE"), typeof(NotFoundResult), "El recurso no está en el ensamblado");
            Assert.IsInstanceOfType(await Controlador().GetMiniatura(" "), typeof(NotFoundResult));
        }

        [TestMethod]
        public void DeImagen_SeGeneraUnaVezYSeGuardaEnMemoria()
        {
            int lecturas = 0;
            byte[] original = ImagenesChequeRegalo.Leer("cheque50.jpg");
            Func<string, byte[]> leer = n => { lecturas++; return original; };
            string nombre = "prueba-cache-" + Guid.NewGuid().ToString("N") + ".jpg";

            MiniaturaChequeRegalo primera = MiniaturasChequeRegalo.DeImagen(nombre, leer);
            MiniaturaChequeRegalo segunda = MiniaturasChequeRegalo.DeImagen(nombre, leer);

            Assert.AreEqual(1, lecturas);
            Assert.AreSame(primera, segunda);
            Assert.AreEqual(128, primera.Ancho);
        }

        [TestMethod]
        public void Url_EsAbsolutaHttpsDeLaApi()
        {
            Assert.AreEqual("https://api.nuevavision.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura", MiniaturasChequeRegalo.Url("CHEQUE50_OCT_2026"));
            Assert.AreEqual("https://otra.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura", MiniaturasChequeRegalo.Url(" CHEQUE50_OCT_2026 ", "https://otra.es/"));
        }
    }
}
