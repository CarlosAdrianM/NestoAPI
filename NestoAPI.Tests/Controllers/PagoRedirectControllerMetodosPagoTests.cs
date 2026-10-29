using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#728: la página de pago (/pago/{token}) con Apple Pay y Google Pay además de
    /// tarjeta y Bizum. Apple Pay solo en dispositivos de Apple; con el método fijado, solo ese.
    /// </summary>
    [TestClass]
    public class PagoRedirectControllerMetodosPagoTests
    {
        private static readonly Guid TOKEN = Guid.Parse("99999999-2222-3333-4444-555555555555");
        private const string UA_IPHONE = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
        private const string UA_MAC = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";
        private const string UA_ANDROID = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";
        private const string UA_WINDOWS = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private NVEntities _db;
        private IRedsysService _redsys;
        private PagoRedirectController _controller;
        private PagoTPV _pago;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            var fakePagos = A.Fake<DbSet<PagoTPV>>(o => o.Implements<IQueryable<PagoTPV>>().Implements<IDbAsyncEnumerable<PagoTPV>>());
            var fakePersonas = A.Fake<DbSet<PersonaContactoCliente>>(o => o.Implements<IQueryable<PersonaContactoCliente>>().Implements<IDbAsyncEnumerable<PersonaContactoCliente>>());
            A.CallTo(() => _db.PagosTPV).Returns(fakePagos);
            A.CallTo(() => _db.PersonasContactoClientes).Returns(fakePersonas);
            A.CallTo(() => fakePagos.Include(A<string>.Ignored)).Returns(fakePagos);
            A.CallTo(() => _db.SaveChangesAsync()).Returns(Task.FromResult(1));

            _pago = new PagoTPV
            {
                Id = 1,
                TokenAcceso = TOKEN,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Importe = 100m,
                Descripcion = "PAGO PEDIDO 123456",
                Correo = "cliente@test.com",
                NumeroOrden = "TEST12345678",
                Estado = Constantes.EstadosPagoTPV.PENDIENTE
            };
            ConfigurarFakeDbSet(fakePagos, new List<PagoTPV> { _pago }.AsQueryable());
            ConfigurarFakeDbSet(fakePersonas, new List<PersonaContactoCliente>().AsQueryable());

            // Cada formulario lleva en sus parámetros el método con el que se firmó, para poder
            // comprobar que cada botón va con el suyo.
            _redsys = A.Fake<IRedsysService>();
            A.CallTo(() => _redsys.CrearParametrosTPVVirtual(A<decimal>._, A<string>._, A<string>._, A<string>._,
                    A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .ReturnsLazily(call => new ParametrosRedsysFirmados
                {
                    Ds_SignatureVersion = "HMAC_SHA256_V1",
                    Ds_MerchantParameters = "PARAMS-" + call.GetArgument<string>(7),
                    Ds_Signature = "FIRMA-" + call.GetArgument<string>(7),
                    UrlRedsys = new Uri("https://sis-t.redsys.es:25443/sis/realizarPago"),
                    NumeroOrden = call.GetArgument<string>(8)
                });

            _controller = new PagoRedirectController(_redsys, _db);
        }

        private async Task<string> Pagina(string userAgent)
        {
            _controller.Request = new HttpRequestMessage(HttpMethod.Get, $"https://api.nuevavision.es/pago/{TOKEN}");
            if (userAgent != null)
            {
                _controller.Request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }
            _controller.Configuration = new HttpConfiguration();
            HttpResponseMessage respuesta = await _controller.PaginaPago(TOKEN);
            return await respuesta.Content.ReadAsStringAsync();
        }

        private static int Contar(string html, string texto)
        {
            int n = 0;
            int i = 0;
            while ((i = html.IndexOf(texto, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += texto.Length;
            }
            return n;
        }

        [TestMethod]
        public async Task PaginaPago_SinMetodoEnIPhoneYMac_CuatroBotones()
        {
            foreach (string ua in new[] { UA_IPHONE, UA_MAC })
            {
                string html = await Pagina(ua);

                Assert.AreEqual(4, Contar(html, "<form action="), ua);
                StringAssert.Contains(html, "btn-tarjeta");
                StringAssert.Contains(html, "btn-bizum");
                StringAssert.Contains(html, "btn-applepay");
                StringAssert.Contains(html, "btn-googlepay");
                StringAssert.Contains(html, "Pagar con Apple&nbsp;Pay");
                StringAssert.Contains(html, "Pagar con Google&nbsp;Pay");
                // Cada formulario firmado con su DS_MERCHANT_PAYMETHODS
                StringAssert.Contains(html, @"data-metodo=""xpay""");
                StringAssert.Contains(html, "PARAMS-xpay");
                StringAssert.Contains(html, "FIRMA-google");
                Assert.IsFalse(html.Contains(@"id=""enviar-al-cargar"""), "sin método fijado no se envía sola");
            }
        }

        [TestMethod]
        public async Task PaginaPago_SinMetodoFueraDeApple_TresBotonesSinApplePay()
        {
            foreach (string ua in new[] { UA_ANDROID, UA_WINDOWS, null })
            {
                string html = await Pagina(ua);

                Assert.AreEqual(3, Contar(html, "<form action="), ua ?? "(sin user-agent)");
                StringAssert.Contains(html, "btn-tarjeta");
                StringAssert.Contains(html, "btn-bizum");
                StringAssert.Contains(html, "btn-googlepay");
                Assert.IsFalse(html.Contains(@"data-metodo=""xpay"""), "Apple Pay solo en dispositivos de Apple");
            }
            A.CallTo(() => _redsys.CrearParametrosTPVVirtual(A<decimal>._, A<string>._, A<string>._, A<string>._,
                    A<string>._, A<string>._, A<string>._, "xpay", A<string>._, A<bool>._, A<string>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PaginaPago_MetodoFijado_SoloEseBoton()
        {
            var casos = new Dictionary<string, string>
            {
                ["C"] = "btn-tarjeta",
                ["z"] = "btn-bizum",
                ["google"] = "btn-googlepay",
                ["xpay"] = "btn-applepay"
            };
            foreach (var caso in casos)
            {
                _pago.MetodoPago = caso.Key;

                string html = await Pagina(UA_IPHONE);

                Assert.AreEqual(1, Contar(html, "<form action="), caso.Key);
                StringAssert.Contains(html, caso.Value);
                StringAssert.Contains(html, $@"data-metodo=""{caso.Key}""");
                foreach (string otra in casos.Values.Where(c => c != caso.Value))
                {
                    Assert.IsFalse(html.Contains(@"btn-pagar " + otra), $"{caso.Key}: no debe salir {otra}");
                }
            }
        }

        [TestMethod]
        public async Task PaginaPago_ApplePayOGooglePayFijados_SeEnviaSolaAlCargar()
        {
            foreach (string metodo in new[] { "xpay", "google" })
            {
                _pago.MetodoPago = metodo;

                string html = await Pagina(UA_IPHONE);

                StringAssert.Contains(html, @"id=""enviar-al-cargar""", metodo);
                StringAssert.Contains(html, "form.submit()", metodo);
            }

            // Tarjeta o Bizum fijados: como antes, sin enviarse sola
            foreach (string metodo in new[] { "C", "z" })
            {
                _pago.MetodoPago = metodo;

                string html = await Pagina(UA_IPHONE);

                Assert.IsFalse(html.Contains(@"id=""enviar-al-cargar"""), metodo);
            }
        }

        [TestMethod]
        public async Task PaginaPago_GooglePayFijadoSinCorreo_NoSeEnviaSolaParaPoderDarElCorreo()
        {
            _pago.MetodoPago = "google";
            _pago.Correo = null;

            string html = await Pagina(UA_ANDROID);

            StringAssert.Contains(html, "bloque-correo");
            Assert.IsFalse(html.Contains(@"id=""enviar-al-cargar"""));
        }

        [TestMethod]
        public async Task PaginaPago_ApplePayFijadoEnAndroid_OfreceLosDemasYLoExplica()
        {
            _pago.MetodoPago = "xpay";

            string html = await Pagina(UA_ANDROID);

            Assert.AreEqual(3, Contar(html, "<form action="));
            Assert.IsFalse(html.Contains(@"data-metodo=""xpay"""));
            StringAssert.Contains(html, "Apple Pay no está disponible en este dispositivo");
            Assert.IsFalse(html.Contains(@"id=""enviar-al-cargar"""));
        }

        [TestMethod]
        public async Task PaginaPago_LosFormulariosVuelvenAOkYKoQueRedirigenALaApp()
        {
            // Si la app abre esta página en el navegador del sistema (Apple Pay / Google Pay no
            // van dentro de un WebView), al terminar Redsys vuelve a ok.html/ko.html, que
            // redirigen a nestotiendas://pago/ok|ko: la app recupera el control.
            _pago.MetodoPago = "google";

            await Pagina(UA_ANDROID);

            A.CallTo(() => _redsys.CrearParametrosTPVVirtual(A<decimal>._, A<string>._, A<string>._, A<string>._,
                    A<string>._, "https://api.nuevavision.es/pago/ok.html", "https://api.nuevavision.es/pago/ko.html",
                    "google", "TEST12345678", A<bool>._, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PaginaPago_TextosDeUsted()
        {
            _pago.Correo = null;

            string html = await Pagina(UA_IPHONE);

            StringAssert.Contains(html, "&iquest;Quiere el justificante por correo?");
            StringAssert.Contains(html, "Introduzca un correo válido.");
            StringAssert.Contains(html, "copie este enlace");
            Assert.IsFalse(html.Contains("Quieres"));
            Assert.IsFalse(html.Contains("Puedes"));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
