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
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    // NestoAPI#672: el ajuste de las impresoras de etiquetas y la etiqueta de prueba, solo para Dirección e Informática.
    [TestClass]
    public class EtiquetasImpresorasControllerTests
    {
        private IServicioAjustesImpresoras servicio;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private EtiquetasImpresorasController Controlador(params string[] grupos) => new EtiquetasImpresorasController(servicio)
        {
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = Usuario("NUEVAVISION\\Carlos", grupos)
        };

        [TestInitialize]
        public void Inicializar()
        {
            servicio = A.Fake<IServicioAjustesImpresoras>();
        }

        [TestMethod]
        public void PideEstarIdentificado()
        {
            Assert.IsTrue(typeof(EtiquetasImpresorasController).GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any());
        }

        [TestMethod]
        public void SinSerDeDireccionNiInformatica_403YNoTocaNada()
        {
            EtiquetasImpresorasController controller = Controlador("Almacén", "Tiendas");

            foreach (IHttpActionResult respuesta in new[]
            {
                controller.GetAjustes(),
                controller.GetAjuste(@"\\RDS2016\etiquetasreina"),
                controller.PutAjuste(new AjusteImpresoraEtiquetasDTO { Impresora = @"\\RDS2016\etiquetasreina", DesplazamientoX = 24 }),
                controller.PostPrueba("1", @"\\RDS2016\etiquetasreina")
            })
            {
                var contenido = respuesta as NegotiatedContentResult<string>;
                Assert.IsNotNull(contenido);
                Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
            }
            A.CallTo(servicio).MustNotHaveHappened();
        }

        [TestMethod]
        public void PutAjuste_Informatica_GuardaConSuUsuario()
        {
            var ajuste = new AjusteImpresoraEtiquetasDTO { Impresora = @"\\RDS2016\etiquetasreina", DesplazamientoX = 24 };
            A.CallTo(() => servicio.Guardar(ajuste, A<string>.That.Contains("Carlos"))).Returns(ajuste);

            IHttpActionResult respuesta = Controlador("Informática").PutAjuste(ajuste);

            Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<AjusteImpresoraEtiquetasDTO>));
        }

        [TestMethod]
        public void PutAjuste_SinLaTabla_503()
        {
            A.CallTo(() => servicio.Guardar(A<AjusteImpresoraEtiquetasDTO>._, A<string>._)).Throws(new InvalidOperationException("Falta la tabla"));

            var respuesta = Controlador("Dirección").PutAjuste(new AjusteImpresoraEtiquetasDTO { Impresora = "x" }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        }

        [TestMethod]
        public void PostPrueba_Direccion_ImprimeConLosValoresPedidos()
        {
            A.CallTo(() => servicio.ImprimirPrueba("1", @"\\RDS2016\etiquetasreina", 24, 0, A<string>._))
                .Returns(new ResultadoEtiquetaPruebaDTO { DesplazamientoX = 24 });

            IHttpActionResult respuesta = Controlador("Dirección").PostPrueba("1", @"\\RDS2016\etiquetasreina", 24, 0);

            Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<ResultadoEtiquetaPruebaDTO>));
        }

        [TestMethod]
        public void PostPrueba_ValoresQueNoValen_400()
        {
            A.CallTo(() => servicio.ImprimirPrueba(A<string>._, A<string>._, A<int?>._, A<int?>._, A<string>._)).Throws(new ArgumentException("No vale"));

            IHttpActionResult respuesta = Controlador("Informática").PostPrueba("1", null, 999, 0);

            Assert.IsInstanceOfType(respuesta, typeof(BadRequestErrorMessageResult));
        }
    }
}
