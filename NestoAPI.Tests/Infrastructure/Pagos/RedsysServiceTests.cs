using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    [TestClass]
    public class RedsysServiceTests
    {
        /// <summary>
        /// NestoAPI#181: los pagos EMV 3DS 2 se firman con FirmadorRedsys, que deriva la clave
        /// cifrando el pedido con 3DES, así que la clave tiene que ser base64 de 24 bytes de
        /// verdad (la "claveTestTPV" del resto de tests no lo es y no sirve aquí).
        /// </summary>
        private static readonly string CLAVE_3DES =
            Convert.ToBase64String(Enumerable.Range(1, 24).Select(i => (byte)i).ToArray());

        [TestMethod]
        public void GenerarNumeroPedido_Devuelve12Caracteres()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido();

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
        }

        [TestMethod]
        public void GenerarNumeroPedido_ConSufijoCliente_TerminaConCliente()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string numeroPedido = service.GenerarNumeroPedido("C15191");

            // Assert
            Assert.AreEqual(12, numeroPedido.Length);
            Assert.IsTrue(numeroPedido.EndsWith("C15191"));
        }

        private static string DecodificarParametros(string base64)
        {
            string normal = base64.Replace('-', '+').Replace('_', '/');
            while (normal.Length % 4 != 0)
            {
                normal += "=";
            }
            return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(normal));
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_ConReferencia_PagoPorRedireccionSinExencionMIT()
        {
            // Plan B (NestoAPI#178, SIS0883 del 02/09/26): la referencia va en el formulario y el
            // titular se autentica en la pasarela. Ni DIRECTPAYMENT ni EXCEP_SCA: no es MIT.
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(3.84m, "Pago pedido 925300", "a@b.com", "15191",
                "https://api/notif", "nestotiendas://pago/ok", "nestotiendas://pago/ko", "C",
                tokenTarjeta: "a26a5b0359c693cb3849bcb84d50f6bbf607aab0", cofTxnId: "232026245295044");

            string json = DecodificarParametros(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_IDENTIFIER\":\"a26a5b0359c693cb3849bcb84d50f6bbf607aab0\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_INI\":\"N\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_TXNID\":\"232026245295044\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_AMOUNT\":\"384\"");
            Assert.IsFalse(json.Contains("DS_MERCHANT_EXCEP_SCA"), "sin exención MIT: el cliente está presente");
            Assert.IsFalse(json.Contains("DS_MERCHANT_DIRECTPAYMENT"), "no es un pago directo");
            Assert.IsFalse(json.Contains("REQUIRED"), "ya está tokenizada: no se pide otra referencia");
        }

        [TestMethod]
        public void CrearParametrosTPVVirtual_SinReferenciaYConToken_PideTokenizar()
        {
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            var parametros = service.CrearParametrosTPVVirtual(3.84m, "Pago pedido 925300", "a@b.com", "15191",
                "https://api/notif", "ok", "ko", "C", solicitarToken: true);

            string json = DecodificarParametros(parametros.Ds_MerchantParameters);
            StringAssert.Contains(json, "\"DS_MERCHANT_IDENTIFIER\":\"REQUIRED\"");
            StringAssert.Contains(json, "\"DS_MERCHANT_COF_INI\":\"S\"");
        }

        [TestMethod]
        public void GenerarNumeroPedido_DosLlamadas_DevuelvenValoresDiferentes()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string pedido1 = service.GenerarNumeroPedido();
            System.Threading.Thread.Sleep(1); // Asegurar que el tick cambia
            string pedido2 = service.GenerarNumeroPedido();

            // Assert
            Assert.AreNotEqual(pedido1, pedido2);
        }

        [TestMethod]
        public void UrlFormularioRedsys_ModoPruebas_DevuelveUrlTest()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", true);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis-t.redsys.es"));
        }

        [TestMethod]
        public void UrlFormularioRedsys_ModoProduccion_DevuelveUrlProduccion()
        {
            // Arrange
            var service = new RedsysService("claveTestP2F", "claveTestTPV", "329515704", false);

            // Act
            string url = service.UrlFormularioRedsys;

            // Assert
            Assert.IsTrue(url.Contains("sis.redsys.es"));
            Assert.IsFalse(url.Contains("sis-t.redsys.es"));
        }

        #region EMV 3DS 2 (NestoAPI#181)

        [TestMethod]
        public void CrearParametrosInicio3DS_PideCardDataYNuncaMarcaLaExencionMIT()
        {
            // El test que protege lo importante: un pedido que hace el propio cliente es un CIT
            // sobre credencial en fichero. Si algún día vuelve a colarse EXCEP_SCA o
            // DIRECTPAYMENT aquí, estaríamos declarándolo MIT y comiéndonos los contracargos.
            var service = new RedsysService("claveTestP2F", CLAVE_3DES, "329515704", true);

            var parametros = service.CrearParametrosInicio3DS(3.84m, "Pago pedido 925300", "15191",
                tokenTarjeta: "a26a5b0359c693cb3849bcb84d50f6bbf607aab0", cofTxnId: "232026245295044");

            JObject json = JObject.Parse(DecodificarParametros(parametros.Ds_MerchantParameters));
            Assert.AreEqual("CardData", json["DS_MERCHANT_EMV3DS"]["threeDSInfo"].ToString());
            Assert.AreEqual("a26a5b0359c693cb3849bcb84d50f6bbf607aab0", json["DS_MERCHANT_IDENTIFIER"].ToString());
            Assert.AreEqual("N", json["DS_MERCHANT_COF_INI"].ToString());
            Assert.AreEqual("232026245295044", json["DS_MERCHANT_COF_TXNID"].ToString());
            Assert.AreEqual("384", json["DS_MERCHANT_AMOUNT"].ToString());
            Assert.IsNull(json["DS_MERCHANT_EXCEP_SCA"], "el cliente está presente: no es MIT");
            Assert.IsNull(json["DS_MERCHANT_DIRECTPAYMENT"], "hay intención de autenticar");
        }

        [TestMethod]
        public void CrearParametrosInicio3DS_VaAIniciaPeticionYLosDemasPasosATrataPeticion()
        {
            var service = new RedsysService("claveTestP2F", CLAVE_3DES, "329515704", true);

            var inicio = service.CrearParametrosInicio3DS(3.84m, "Pago", "15191", "token", null);
            var autenticacion = service.CrearParametrosAutenticacion3DS(3.84m, inicio.NumeroOrden,
                "Pago", "token", null, new PeticionAutenticacion3DS { ProtocolVersion = "2.2.0" });

            StringAssert.Contains(inicio.UrlRedsys.ToString(), "iniciaPeticionREST");
            StringAssert.Contains(autenticacion.UrlRedsys.ToString(), "trataPeticionREST");
        }

        [TestMethod]
        public void CrearParametrosAutenticacion3DS_LlevaNotificationUrlYDatosDelNavegador()
        {
            // Los datos del navegador son los que hacen que el emisor se atreva a no preguntar:
            // si no viajan, casi todo acaba en desafío y el cliente ve pantalla igualmente
            var service = new RedsysService("claveTestP2F", CLAVE_3DES, "329515704", true);

            var peticion = new PeticionAutenticacion3DS
            {
                ProtocolVersion = "2.2.0",
                ThreeDSServerTransID = "8a8a8a8a-1111-2222-3333-444444444444",
                NotificationURL = "https://api.nuevavision.es/pago3ds/abc/reto",
                ThreeDSCompInd = "Y",
                Navegador = new DatosNavegador3DS
                {
                    UserAgent = "Mozilla/5.0 (Linux; Android 14)",
                    AcceptHeader = "text/html",
                    Idioma = "es-ES",
                    ProfundidadColor = 24,
                    AltoPantalla = 2340,
                    AnchoPantalla = 1080,
                    DiferenciaHorariaMinutos = -120,
                    JavaScriptActivado = true
                }
            };

            var parametros = service.CrearParametrosAutenticacion3DS(3.84m, "250904C15191",
                "Pago pedido 925300", "token", "232026245295044", peticion);

            JObject emv3ds = (JObject)JObject.Parse(
                DecodificarParametros(parametros.Ds_MerchantParameters))["DS_MERCHANT_EMV3DS"];
            Assert.AreEqual("AuthenticationData", emv3ds["threeDSInfo"].ToString());
            Assert.AreEqual("2.2.0", emv3ds["protocolVersion"].ToString());
            Assert.AreEqual("8a8a8a8a-1111-2222-3333-444444444444", emv3ds["threeDSServerTransID"].ToString());
            Assert.AreEqual("https://api.nuevavision.es/pago3ds/abc/reto", emv3ds["notificationURL"].ToString());
            Assert.AreEqual("Y", emv3ds["threeDSCompInd"].ToString());
            Assert.AreEqual("es-ES", emv3ds["browserLanguage"].ToString());
            Assert.AreEqual("1080", emv3ds["browserScreenWidth"].ToString());
            Assert.AreEqual("-120", emv3ds["browserTZ"].ToString());
        }

        [TestMethod]
        public void CrearParametrosAutenticacion3DS_SinDatosDelNavegador_MarcaQueNoHubo3dsMethod()
        {
            var service = new RedsysService("claveTestP2F", CLAVE_3DES, "329515704", true);

            var parametros = service.CrearParametrosAutenticacion3DS(3.84m, "250904C15191", "Pago",
                "token", null, new PeticionAutenticacion3DS { ProtocolVersion = "2.2.0" });

            JObject emv3ds = (JObject)JObject.Parse(
                DecodificarParametros(parametros.Ds_MerchantParameters))["DS_MERCHANT_EMV3DS"];
            Assert.AreEqual("N", emv3ds["threeDSCompInd"].ToString());
            Assert.IsNull(emv3ds["browserUserAgent"]);
        }

        [TestMethod]
        public void CrearParametrosRespuestaReto3DS_LlevaElCresDelEmisor()
        {
            var service = new RedsysService("claveTestP2F", CLAVE_3DES, "329515704", true);

            var parametros = service.CrearParametrosRespuestaReto3DS(3.84m, "250904C15191", "Pago",
                "token", null, "2.2.0", "eyJhY3NUcmFuc0lEIjoiMTIzIn0=");

            JObject emv3ds = (JObject)JObject.Parse(
                DecodificarParametros(parametros.Ds_MerchantParameters))["DS_MERCHANT_EMV3DS"];
            Assert.AreEqual("ChallengeResponse", emv3ds["threeDSInfo"].ToString());
            Assert.AreEqual("eyJhY3NUcmFuc0lEIjoiMTIzIn0=", emv3ds["cres"].ToString());
        }

        [TestMethod]
        public void Emv3DSDe_AceptaTantoObjetoAnidadoComoCadenaConJsonDentro()
        {
            var comoObjeto = new RespuestaRedsys
            {
                Ds_EMV3DS = new JObject { ["protocolVersion"] = "2.2.0" }
            };
            var comoCadena = new RespuestaRedsys
            {
                Ds_EMV3DS = "%7B%22protocolVersion%22%3A%222.1.0%22%7D"
            };

            Assert.AreEqual("2.2.0", RedsysService.Emv3DSDe(comoObjeto)["protocolVersion"].ToString());
            Assert.AreEqual("2.1.0", RedsysService.Emv3DSDe(comoCadena)["protocolVersion"].ToString());
            Assert.IsNull(RedsysService.Emv3DSDe(new RespuestaRedsys()));
        }

        #endregion
    }
}
