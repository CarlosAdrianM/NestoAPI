using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using RedsysAPIPrj;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#728: Apple Pay ("xpay") y Google Pay ("google") en los enlaces de pago, además de
    /// tarjeta ("C") y Bizum ("z").
    /// </summary>
    [TestClass]
    public class MetodosPagoRedsysTests
    {
        private const string UA_IPHONE = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
        private const string UA_MAC = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";
        private const string UA_ANDROID = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";
        private const string UA_WINDOWS = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private static readonly string CLAVE_3DES =
            Convert.ToBase64String(Enumerable.Range(1, 24).Select(i => (byte)i).ToArray());

        #region Qué botones salen

        [TestMethod]
        public void AMostrar_SinFijarEnDispositivoApple_LosCuatro()
        {
            foreach (string ua in new[] { UA_IPHONE, UA_MAC, "Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X)", "Mozilla/5.0 (iPod touch; CPU iPhone OS 12_0 like Mac OS X)" })
            {
                CollectionAssert.AreEqual(new[] { "C", "z", "xpay", "google" }, MetodosPagoRedsys.AMostrar(null, ua).ToArray(), ua);
            }
        }

        [TestMethod]
        public void AMostrar_SinFijarFueraDeApple_TresSinApplePay()
        {
            foreach (string ua in new[] { UA_ANDROID, UA_WINDOWS, "", null })
            {
                CollectionAssert.AreEqual(new[] { "C", "z", "google" }, MetodosPagoRedsys.AMostrar(null, ua).ToArray(), ua ?? "(null)");
            }
        }

        [TestMethod]
        public void AMostrar_MetodoFijado_SoloEse()
        {
            CollectionAssert.AreEqual(new[] { "C" }, MetodosPagoRedsys.AMostrar("C", UA_IPHONE).ToArray());
            CollectionAssert.AreEqual(new[] { "z" }, MetodosPagoRedsys.AMostrar("z", UA_ANDROID).ToArray());
            CollectionAssert.AreEqual(new[] { "google" }, MetodosPagoRedsys.AMostrar("google", UA_ANDROID).ToArray());
            CollectionAssert.AreEqual(new[] { "google" }, MetodosPagoRedsys.AMostrar("google", UA_IPHONE).ToArray());
            CollectionAssert.AreEqual(new[] { "xpay" }, MetodosPagoRedsys.AMostrar("xpay", UA_IPHONE).ToArray());
            CollectionAssert.AreEqual(new[] { "xpay" }, MetodosPagoRedsys.AMostrar("xpay", UA_MAC).ToArray());
        }

        [TestMethod]
        public void AMostrar_ApplePayFijadoFueraDeApple_OfreceLosDemas()
        {
            // No se puede completar en Android/Windows: mejor ofrecer los otros que dejarle sin salida
            CollectionAssert.AreEqual(new[] { "C", "z", "google" }, MetodosPagoRedsys.AMostrar("xpay", UA_ANDROID).ToArray());
        }

        [TestMethod]
        public void Normalizar_YEsValido()
        {
            Assert.IsNull(MetodosPagoRedsys.Normalizar(null));
            Assert.IsNull(MetodosPagoRedsys.Normalizar("  "));
            Assert.AreEqual("google", MetodosPagoRedsys.Normalizar(" Google "));
            Assert.AreEqual("xpay", MetodosPagoRedsys.Normalizar("XPAY"));
            Assert.AreEqual("C", MetodosPagoRedsys.Normalizar("C"));

            Assert.IsTrue(MetodosPagoRedsys.EsValido(null));
            Assert.IsTrue(MetodosPagoRedsys.EsValido("C"));
            Assert.IsTrue(MetodosPagoRedsys.EsValido("z"));
            Assert.IsTrue(MetodosPagoRedsys.EsValido("xpay"));
            Assert.IsTrue(MetodosPagoRedsys.EsValido("google"));
            Assert.IsFalse(MetodosPagoRedsys.EsValido("applepay"));
            Assert.IsFalse(MetodosPagoRedsys.EsValido("Z"), "los códigos de Redsys distinguen mayúsculas");
        }

        [TestMethod]
        public void PermiteTokenizar_SoloSinWallet()
        {
            Assert.IsTrue(MetodosPagoRedsys.PermiteTokenizar(null));
            Assert.IsTrue(MetodosPagoRedsys.PermiteTokenizar("C"));
            Assert.IsTrue(MetodosPagoRedsys.PermiteTokenizar("z"), "Bizum: sin cambios respecto a antes de #728");
            Assert.IsFalse(MetodosPagoRedsys.PermiteTokenizar("xpay"));
            Assert.IsFalse(MetodosPagoRedsys.PermiteTokenizar("google"));
        }

        #endregion

        #region Parámetros firmados

        private static string Decodificar(string base64)
        {
            string normal = base64.Replace('-', '+').Replace('_', '/');
            while (normal.Length % 4 != 0)
            {
                normal += "=";
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(normal));
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_ApplePay_PaymethodsXpay()
        {
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(12.5m, "Pago pedido 1", "a@b.com", "15191",
                "https://api/notif", "https://api.nuevavision.es/pago/ok.html", "https://api.nuevavision.es/pago/ko.html", "xpay", "ORDEN0000001");

            string json = Decodificar(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_PAYMETHODS\":\"xpay\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_ORDER\":\"ORDEN0000001\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_URLOK\":\"https://api.nuevavision.es/pago/ok.html\"");
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_GooglePay_PaymethodsGoogle()
        {
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(12.5m, "Pago pedido 1", "a@b.com", "15191",
                "https://api/notif", "nestotiendas://pago/ok", "nestotiendas://pago/ko", "google");

            string json = Decodificar(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_PAYMETHODS\":\"google\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_URLOK\":\"nestotiendas://pago/ok\"");
            Assert.IsFalse(json.Contains("REQUIRED"), "sin pedirlo no se tokeniza");
        }

        #endregion

        #region Notificación y contabilidad

        private static NotificacionRedsys NotificacionFirmada(string json, string clave)
        {
            string parametros = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            string firma = new RedsysAPI().createMerchantSignatureNotif(clave, parametros);
            return new NotificacionRedsys
            {
                Ds_SignatureVersion = "HMAC_SHA256_V1",
                Ds_MerchantParameters = parametros,
                Ds_Signature = firma
            };
        }

        [TestMethod]
        public void ValidarNotificacion_PagoConGooglePay_AutorizadoComoTarjetaYConSuMetodo()
        {
            var service = new RedsysService(CLAVE_3DES, CLAVE_3DES, "329515704", true);
            // Lo que manda Redsys en un pago con wallet: Ds_Card_Last4 en vez del número y el
            // método procesado. El código concreto se verá en los primeros pagos reales; aquí da
            // igual, que es justo lo que se comprueba.
            string json = "{\"Ds_Amount\":\"1250\",\"Ds_Currency\":\"978\",\"Ds_Order\":\"ORDEN0000001\"," +
                "\"Ds_MerchantCode\":\"329515704\",\"Ds_Terminal\":\"001\",\"Ds_Response\":\"0000\"," +
                "\"Ds_AuthorisationCode\":\"123456\",\"Ds_TransactionType\":\"0\",\"Ds_SecurePayment\":\"1\"," +
                "\"Ds_Card_Last4\":\"4242\",\"Ds_Card_Brand\":\"1\",\"Ds_ProcessedPayMethod\":\"78\"}";

            ResultadoValidacionNotificacion resultado = service.ValidarNotificacion(NotificacionFirmada(json, CLAVE_3DES));

            Assert.IsTrue(resultado.FirmaValida, resultado.MensajeError);
            Assert.IsTrue(resultado.PagoAutorizado);
            Assert.AreEqual("ORDEN0000001", resultado.NumeroOrden);
            Assert.AreEqual("123456", resultado.CodigoAutorizacion);
            Assert.AreEqual("78", resultado.MetodoPagoProcesado);
            Assert.AreEqual("4242", resultado.UltimosDigitosTarjeta);
            Assert.IsFalse(resultado.TieneTokenTarjeta);
        }

        [TestMethod]
        public void DestinoDelCobro_NoDependeDelMetodoDePago()
        {
            foreach (string metodo in new[] { null, "C", "z", "xpay", "google" })
            {
                var enlace = new PagoTPV { Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL, MetodoPago = metodo };
                var pedidoApp = new PagoTPV { Tipo = Constantes.TiposPagoTPV.PEDIDO_APP, Documento = "925300", MetodoPago = metodo };
                var carrito = new PagoTPV { Tipo = Constantes.TiposPagoTPV.CARRITO_APP, MetodoPago = metodo };

                Assert.AreEqual(ServicioPagos.DestinoCobro.ContabilizarCobro, ServicioPagos.DestinoDelCobro(enlace), metodo ?? "(null)");
                Assert.AreEqual(ServicioPagos.DestinoCobro.PrepagoPedido, ServicioPagos.DestinoDelCobro(pedidoApp), metodo ?? "(null)");
                Assert.AreEqual(ServicioPagos.DestinoCobro.PendienteDeCrearPedido, ServicioPagos.DestinoDelCobro(carrito), metodo ?? "(null)");
            }
        }

        [TestMethod]
        public void ConstruirPrepago_ConGooglePayOApplePay_MismaCuentaQueTarjeta()
        {
            Prepago conTarjeta = ServicioPagos.ConstruirPrepago(
                new PagoTPV { Tipo = Constantes.TiposPagoTPV.PEDIDO_APP, Documento = "925300", Importe = 10m, NumeroOrden = "ORD1", MetodoPago = "C" }, 925300, "1");

            foreach (string metodo in new[] { "xpay", "google" })
            {
                Prepago prepago = ServicioPagos.ConstruirPrepago(
                    new PagoTPV { Tipo = Constantes.TiposPagoTPV.PEDIDO_APP, Documento = "925300", Importe = 10m, NumeroOrden = "ORD1", MetodoPago = metodo }, 925300, "1");

                Assert.AreEqual(Constantes.Prepagos.CUENTA_REDSYS, prepago.CuentaContable, metodo);
                Assert.AreEqual(conTarjeta.CuentaContable, prepago.CuentaContable, metodo);
                Assert.AreEqual(conTarjeta.ConceptoAdicional, prepago.ConceptoAdicional, metodo);
                Assert.AreEqual(conTarjeta.Importe, prepago.Importe, metodo);
            }
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_PagoConWallet_NoGuardaElToken()
        {
            var store = A.Fake<ITarjetaClienteStore>();
            var servicio = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(),
                A.Fake<ILectorParametrosUsuario>(), A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>(), store);
            var resultado = new ResultadoValidacionNotificacion { TokenTarjeta = "token", UltimosDigitosTarjeta = "4242" };

            foreach (string metodo in new[] { "xpay", "google" })
            {
                servicio.GuardarTarjetaDeLaNotificacion(resultado,
                    new PagoTPV { Empresa = "1", Cliente = "15191", MetodoPago = metodo, NumeroOrden = "ORD1" });
            }
            A.CallTo(() => store.GuardarOActualizar(A<TarjetaCliente>._)).MustNotHaveHappened();

            servicio.GuardarTarjetaDeLaNotificacion(resultado,
                new PagoTPV { Empresa = "1", Cliente = "15191", MetodoPago = "C", NumeroOrden = "ORD2" });
            A.CallTo(() => store.GuardarOActualizar(A<TarjetaCliente>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task IniciarPago_MetodoDesconocido_LanzaArgumentException()
        {
            var servicio = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<IContabilidadService>(),
                A.Fake<ILectorParametrosUsuario>(), A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>());

            await servicio.IniciarPago(new SolicitudPagoTPV
            {
                Importe = 10m,
                Descripcion = "Pago pedido 123456",
                Cliente = "15191",
                MetodoPago = "applepay"
            }, "usuario");
        }

        [TestMethod]
        public void UrlPaginaPago_EsLaPaginaDelToken()
        {
            // La que devuelve RespuestaIniciarPago.UrlPaginaPago: la app la abre en el navegador
            // del sistema para Apple Pay y Google Pay
            Guid token = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Assert.AreEqual("https://api.nuevavision.es/pago/11111111-2222-3333-4444-555555555555", ServicioPagos.UrlPaginaPago(token));
        }

        #endregion

        #region Reintento tras denegado (#165)

        [TestMethod]
        public async Task RegenerarPagoDenegado_ConApplePayOGooglePay_ConservaElMetodo()
        {
            foreach (string metodo in new[] { "xpay", "google" })
            {
                var redsys = A.Fake<IRedsysService>();
                A.CallTo(() => redsys.CrearParametrosTPVVirtual(A<decimal>._, A<string>._, A<string>._, A<string>._,
                        A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                    .Returns(new ParametrosRedsysFirmados { NumeroOrden = "NUEVAORDEN01" });
                var servicio = new ServicioPagos(redsys, A.Fake<IContabilidadService>(),
                    A.Fake<ILectorParametrosUsuario>(), A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>());

                NVEntities db = A.Fake<NVEntities>();
                DbSet<PagoTPV> pagos = A.Fake<DbSet<PagoTPV>>(o => o.Implements<IQueryable<PagoTPV>>().Implements<IDbAsyncEnumerable<PagoTPV>>());
                ConfigurarFakeDbSet(pagos, new List<PagoTPV>().AsQueryable());
                A.CallTo(() => db.PagosTPV).Returns(pagos);
                var anadidos = new List<PagoTPV>();
                A.CallTo(() => pagos.Add(A<PagoTPV>._)).Invokes((PagoTPV p) => anadidos.Add(p)).ReturnsLazily((PagoTPV p) => p);
                A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

                var denegado = new PagoTPV
                {
                    Id = 7,
                    Tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL,
                    Empresa = "1",
                    Cliente = "15191",
                    Importe = 10m,
                    Descripcion = "PAGO PEDIDO 123456",
                    NumeroOrden = "ORDEN0000001",
                    MetodoPago = metodo,
                    PagosTPV_Efectos = null
                };

                await servicio.RegenerarPagoDenegado(denegado, db);

                A.CallTo(() => redsys.CrearParametrosTPVVirtual(A<decimal>._, A<string>._, A<string>._, A<string>._,
                        A<string>._, A<string>._, A<string>._, metodo, A<string>._, A<bool>._, A<string>._, A<string>._))
                    .MustHaveHappenedOnceExactly();
                Assert.AreEqual(1, anadidos.Count, metodo);
                Assert.AreEqual(metodo, anadidos[0].MetodoPago);
                Assert.AreEqual(7, anadidos[0].PagoOriginalId);
            }
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

        #endregion
    }
}
