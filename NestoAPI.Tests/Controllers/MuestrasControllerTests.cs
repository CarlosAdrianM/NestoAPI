using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#884: los tres endpoints de las muestras sin código de fabricante. Dar código en bloque: Almacén, Compras,
    /// Dirección e Informática. Etiquetas de cajón y hoja: los de las etiquetas de producto (también Tiendas). Ensayo por defecto.
    /// </summary>
    [TestClass]
    public class MuestrasControllerTests
    {
        private IServicioCodigoInternoMuestras codigoInterno;
        private IServicioEtiquetasMuestras etiquetas;
        private IServicioHojaMuestras hoja;
        private MuestrasController controller;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestInitialize]
        public void Inicializar()
        {
            codigoInterno = A.Fake<IServicioCodigoInternoMuestras>();
            etiquetas = A.Fake<IServicioEtiquetasMuestras>();
            hoja = A.Fake<IServicioHojaMuestras>();
            controller = new MuestrasController(codigoInterno, etiquetas, hoja)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Alfredo", "Almacén")
            };
        }

        [TestMethod]
        public void PideEstarIdentificado_YNoLlevaElFiltroDeEscrituraDeAlmacen()
        {
            object[] atributos = typeof(MuestrasController).GetCustomAttributes(true);

            Assert.IsTrue(atributos.OfType<AuthorizeAttribute>().Any());
            Assert.IsFalse(atributos.OfType<EscrituraSoloAlmacenAttribute>().Any(), "Si no, una tienda no podría imprimir sus etiquetas");
        }

        [TestMethod]
        public async Task CodigoInterno_PorDefectoEsEnsayo()
        {
            var esperado = new ResultadoCodigoInternoMuestrasDTO { Ensayo = true, Total = 287 };
            A.CallTo(() => codigoInterno.Dar("1", A<string>.That.Contains("Alfredo"), true)).Returns(esperado);

            IHttpActionResult respuesta = await controller.PostCodigoInterno();

            Assert.AreSame(esperado, ((OkNegotiatedContentResult<ResultadoCodigoInternoMuestrasDTO>)respuesta).Content);
        }

        [TestMethod]
        public async Task CodigoInterno_DeVerdad_ConEnsayoFalse()
        {
            _ = await controller.PostCodigoInterno("1", false);

            A.CallTo(() => codigoInterno.Dar("1", A<string>._, false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CodigoInterno_UnaTienda_403()
        {
            controller.User = Usuario("NUEVAVISION\\Paloma", "Tiendas");

            IHttpActionResult respuesta = await controller.PostCodigoInterno("1", false);

            var contenido = (NegotiatedContentResult<HttpError>)respuesta;
            Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
            Assert.AreEqual(PermisosMuestras.MENSAJE_SIN_PERMISO_BLOQUE, contenido.Content.Message);
            A.CallTo(() => codigoInterno.Dar(A<string>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CodigoInterno_ComprasDireccionEInformatica_Pueden()
        {
            foreach (string grupo in new[] { "Compras", "Dirección", "Informática" })
            {
                controller.User = Usuario("NUEVAVISION\\X", grupo);
                Assert.IsInstanceOfType(await controller.PostCodigoInterno(), typeof(OkNegotiatedContentResult<ResultadoCodigoInternoMuestrasDTO>), grupo);
            }
        }

        [TestMethod]
        public async Task Etiquetas_UnaTienda_Puede_YPorDefectoEsEnsayo()
        {
            controller.User = Usuario("NUEVAVISION\\Paloma", "Tiendas");
            var peticion = new PeticionEtiquetasMuestrasDTO { Almacen = "REI" };

            IHttpActionResult respuesta = await controller.PostEtiquetas(peticion);

            Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<ResultadoEtiquetasMuestrasDTO>));
            A.CallTo(() => etiquetas.Imprimir("1", peticion, A<string>.That.Contains("Paloma"), true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Etiquetas_SinGrupo_403()
        {
            controller.User = Usuario("RevisionGoogle");

            IHttpActionResult respuesta = await controller.PostEtiquetas(new PeticionEtiquetasMuestrasDTO { Almacen = "REI" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<HttpError>)respuesta).StatusCode);
            A.CallTo(() => etiquetas.Imprimir(A<string>._, A<PeticionEtiquetasMuestrasDTO>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Etiquetas_ArgumentException_400_YLaImpresoraFalla_502()
        {
            A.CallTo(() => etiquetas.Imprimir(A<string>._, A<PeticionEtiquetasMuestrasDTO>._, A<string>._, false)).Throws(new ArgumentException("Falta el almacén (ALG, REI o ALC)."));
            A.CallTo(() => etiquetas.Imprimir(A<string>._, A<PeticionEtiquetasMuestrasDTO>._, A<string>._, true)).Throws(new ImpresionEtiquetasException("sin papel"));

            var malo = (BadRequestErrorMessageResult)await controller.PostEtiquetas(new PeticionEtiquetasMuestrasDTO(), "1", false);
            StringAssert.Contains(malo.Message, "almacén");
            var impresora = (NegotiatedContentResult<HttpError>)await controller.PostEtiquetas(new PeticionEtiquetasMuestrasDTO(), "1", true);
            Assert.AreEqual(HttpStatusCode.BadGateway, impresora.StatusCode);
        }

        [TestMethod]
        public async Task Hoja_DevuelveElPdf()
        {
            var datos = new HojaMuestras { Familia = "Lisap", Almacen = "REI", Muestras = new List<FichaHojaMuestra> { new FichaHojaMuestra { Producto = "1" } } };
            A.CallTo(() => hoja.Preparar("1", "REI", "Lisap", false, true)).Returns(datos);
            A.CallTo(() => hoja.GenerarPdf(datos)).Returns(new byte[] { 37, 80, 68, 70 });

            IHttpActionResult respuesta = await controller.GetHoja("REI", "Lisap");

            HttpResponseMessage mensaje = await respuesta.ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, mensaje.StatusCode);
            Assert.AreEqual("application/pdf", mensaje.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("Muestras Lisap REI.pdf", mensaje.Content.Headers.ContentDisposition.FileName.Trim('"'));
        }

        [TestMethod]
        public async Task Hoja_SinMuestras_404ConMensaje()
        {
            A.CallTo(() => hoja.Preparar(A<string>._, A<string>._, A<string>._, A<bool>._, A<bool>._)).Returns(new HojaMuestras());

            var respuesta = (NegotiatedContentResult<HttpError>)await controller.GetHoja(familia: "Nada");

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            A.CallTo(() => hoja.GenerarPdf(A<HojaMuestras>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Hoja_FiltroQueNoVale_400()
        {
            A.CallTo(() => hoja.Preparar(A<string>._, A<string>._, A<string>._, true, A<bool>._)).Throws(new ArgumentException("Para sacar solo las muestras con stock, diga de qué almacén."));

            Assert.IsInstanceOfType(await controller.GetHoja(soloConStock: true), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Hoja_UnaTiendaPuede_SinGrupo403()
        {
            controller.User = Usuario("NUEVAVISION\\Paloma", "Tiendas");
            A.CallTo(() => hoja.Preparar(A<string>._, A<string>._, A<string>._, A<bool>._, A<bool>._)).Returns(new HojaMuestras());
            Assert.AreEqual(HttpStatusCode.NotFound, ((NegotiatedContentResult<HttpError>)await controller.GetHoja()).StatusCode);

            controller.User = Usuario("RevisionGoogle");
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<HttpError>)await controller.GetHoja()).StatusCode);
        }

        [TestMethod]
        public void NombreFichero_SinCaracteresRaros()
        {
            Assert.AreEqual("Muestras.pdf", MuestrasController.NombreFichero(new HojaMuestras()));
            Assert.AreEqual("Muestras ab ALG.pdf", MuestrasController.NombreFichero(new HojaMuestras { Familia = "a/b", Almacen = "ALG" }));
        }
    }
}
