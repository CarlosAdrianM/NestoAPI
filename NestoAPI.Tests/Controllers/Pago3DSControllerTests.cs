using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#181: la página de pago con EMV 3DS 2 y sus tres pasos.
    /// </summary>
    [TestClass]
    public class Pago3DSControllerTests
    {
        private static readonly Guid TOKEN = new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        private const int TARJETA = 7;

        private static Pago3DSController CrearController(IServicioPagos servicio, string url)
        {
            return new Pago3DSController(servicio)
            {
                Request = new HttpRequestMessage(HttpMethod.Post, url),
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public async Task Pagina_LlevaElTokenYLaTarjetaAlJavaScript()
        {
            var controller = new Pago3DSController(A.Fake<IServicioPagos>());

            HttpResponseMessage respuesta = controller.Pagina(TOKEN, TARJETA);
            string html = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert.AreEqual("text/html", respuesta.Content.Headers.ContentType.MediaType);
            StringAssert.Contains(html, $"/pago3ds/{TOKEN}/{TARJETA}");
            // Sin el 3DSMethod el porcentaje de frictionless se hunde: tiene que seguir ahí
            StringAssert.Contains(html, "threeDSMethodData");
        }

        [TestMethod]
        public async Task Autenticar_Frictionless_DevuelveAutorizadoComoTexto()
        {
            IServicioPagos servicio = A.Fake<IServicioPagos>();
            A.CallTo(() => servicio.Autenticar3DS(TOKEN, TARJETA, A<PeticionAutenticacion3DS>._))
                .Returns(Task.FromResult(new ResultadoAutenticacion3DS
                {
                    Estado = EstadoAutenticacion3DS.Autorizado,
                    CodigoRespuesta = "0000"
                }));

            var controller = CrearController(servicio, "https://api.nuevavision.es/pago3ds/x/7/autenticar");

            IHttpActionResult resultado = await controller
                .Autenticar(TOKEN, TARJETA, new PeticionAutenticacion3DS()).ConfigureAwait(false);

            JObject json = JObject.FromObject(((OkNegotiatedContentResult<object>)resultado).Content);
            // Como texto a propósito: un enum por número se rompe solo con reordenar los valores
            Assert.AreEqual("Autorizado", json["Estado"].ToString());
        }

        [TestMethod]
        public async Task Autenticar_ConDesafio_DevuelveLaUrlDelBancoYElCreq()
        {
            IServicioPagos servicio = A.Fake<IServicioPagos>();
            A.CallTo(() => servicio.Autenticar3DS(TOKEN, TARJETA, A<PeticionAutenticacion3DS>._))
                .Returns(Task.FromResult(new ResultadoAutenticacion3DS
                {
                    Estado = EstadoAutenticacion3DS.RetoRequerido,
                    AcsUrl = "https://acs.miemisor.es/challenge",
                    Creq = "eyJhY3NUcmFuc0lEIjoiMSJ9",
                    ProtocolVersion = "2.2.0"
                }));

            var controller = CrearController(servicio, "https://api.nuevavision.es/pago3ds/x/7/autenticar");

            IHttpActionResult resultado = await controller
                .Autenticar(TOKEN, TARJETA, new PeticionAutenticacion3DS()).ConfigureAwait(false);

            JObject json = JObject.FromObject(((OkNegotiatedContentResult<object>)resultado).Content);
            Assert.AreEqual("RetoRequerido", json["Estado"].ToString());
            Assert.AreEqual("https://acs.miemisor.es/challenge", json["AcsUrl"].ToString());
            Assert.AreEqual("eyJhY3NUcmFuc0lEIjoiMSJ9", json["Creq"].ToString());
        }

        [TestMethod]
        public async Task Reto_ConCresAutorizado_SacaAlClienteDelIframeYLoLlevaAOk()
        {
            IServicioPagos servicio = A.Fake<IServicioPagos>();
            A.CallTo(() => servicio.ResolverReto3DS(TOKEN, TARJETA, "2.2.0", "elCres"))
                .Returns(Task.FromResult(new ResultadoAutenticacion3DS
                {
                    Estado = EstadoAutenticacion3DS.Autorizado
                }));

            var controller = CrearController(servicio,
                $"https://api.nuevavision.es/pago3ds/{TOKEN}/{TARJETA}/reto?pv=2.2.0");
            controller.Request.Content = new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("cres", "elCres") });

            HttpResponseMessage respuesta = await controller.Reto(TOKEN, TARJETA).ConfigureAwait(false);
            string html = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);

            // window.top y no window.location: el cres llega dentro del iframe del desafío
            StringAssert.Contains(html, "window.top.location.href='/pago/ok.html'");
        }

        [TestMethod]
        public async Task Reto_DenegadoPorElBanco_LlevaAKo()
        {
            IServicioPagos servicio = A.Fake<IServicioPagos>();
            A.CallTo(() => servicio.ResolverReto3DS(TOKEN, TARJETA, A<string>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoAutenticacion3DS
                {
                    Estado = EstadoAutenticacion3DS.Denegado
                }));

            var controller = CrearController(servicio,
                $"https://api.nuevavision.es/pago3ds/{TOKEN}/{TARJETA}/reto?pv=2.2.0");
            controller.Request.Content = new StringContent("cres=loQueSea",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            HttpResponseMessage respuesta = await controller.Reto(TOKEN, TARJETA).ConfigureAwait(false);
            string html = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);

            StringAssert.Contains(html, "window.top.location.href='/pago/ko.html'");
        }

        [TestMethod]
        public async Task Metodo_AvisaALaPaginaDeQueYaPuedeSeguir()
        {
            var controller = new Pago3DSController(A.Fake<IServicioPagos>());

            HttpResponseMessage respuesta = controller.Metodo(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);

            StringAssert.Contains(html, "3dsMethodDone");
        }
    }

    /// <summary>
    /// NestoAPI#181: las decisiones sueltas del flujo 3DS que conviene tener ancladas.
    /// </summary>
    [TestClass]
    public class Emv3DSDecisionesTests
    {
        [TestMethod]
        public void SoportaEmv3DS2_SoloAceptaLasVersionesDos()
        {
            Assert.IsTrue(ServicioPagos.SoportaEmv3DS2("2.1.0"));
            Assert.IsTrue(ServicioPagos.SoportaEmv3DS2("2.2.0"));
            // Lo que Redsys responde cuando la tarjeta no está en el protocolo
            Assert.IsFalse(ServicioPagos.SoportaEmv3DS2("NO_3DS_v2"));
            Assert.IsFalse(ServicioPagos.SoportaEmv3DS2("1.0.2"));
            Assert.IsFalse(ServicioPagos.SoportaEmv3DS2(null));
            Assert.IsFalse(ServicioPagos.SoportaEmv3DS2("  "));
        }

        [TestMethod]
        public void ConstruirMethodData_VaEnBase64UrlSinRelleno()
        {
            string metodo = ServicioPagos.ConstruirMethodData("8a8a-1111",
                "https://api.nuevavision.es/pago3ds/abc/metodo");

            // La especificación pide base64 URL-safe: ni '+', ni '/', ni '=' al final
            Assert.IsFalse(metodo.Contains("+"));
            Assert.IsFalse(metodo.Contains("/"));
            Assert.IsFalse(metodo.EndsWith("="));

            string relleno = metodo.Replace('-', '+').Replace('_', '/');
            while (relleno.Length % 4 != 0)
            {
                relleno += "=";
            }
            JObject datos = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(relleno)));
            Assert.AreEqual("8a8a-1111", datos["threeDSServerTransID"].ToString());
            Assert.AreEqual("https://api.nuevavision.es/pago3ds/abc/metodo",
                datos["threeDSMethodNotificationURL"].ToString());
        }
    }
}
