using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using Newtonsoft.Json.Linq;
using RedsysAPIPrj;
using System;
using System.Linq;
using System.Text;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#181: EMV 3DS 2 obliga a mandar DS_MERCHANT_EMV3DS como objeto JSON anidado, y el
    /// helper oficial (RedsysAPI.dll) solo admite cadenas, así que montamos y firmamos nosotros
    /// los parámetros. El test importante es el primero: demuestra que nuestra firma es
    /// EXACTAMENTE la del helper oficial. Sin eso no habría forma de fiarse en un cobro real.
    /// </summary>
    [TestClass]
    public class FirmadorRedsysTests
    {
        // 24 bytes (3DES) con los tres tercios distintos, para que no sea una clave débil
        private static readonly string CLAVE_COMERCIO =
            Convert.ToBase64String(Enumerable.Range(1, 24).Select(i => (byte)i).ToArray());

        private const string NUMERO_ORDEN = "250904C15191";

        [TestMethod]
        public void Firmar_ProduceLaMismaFirmaQueElHelperOficialDeRedsys()
        {
            // Arrange: los parámetros los genera el helper oficial, así que la única diferencia
            // posible entre las dos firmas está en la criptografía, que es lo que se comprueba
            RedsysAPI oficial = new RedsysAPI();
            oficial.SetParameter("DS_MERCHANT_AMOUNT", "1234");
            oficial.SetParameter("DS_MERCHANT_ORDER", NUMERO_ORDEN);
            oficial.SetParameter("DS_MERCHANT_MERCHANTCODE", "329515704");
            oficial.SetParameter("DS_MERCHANT_CURRENCY", "978");
            oficial.SetParameter("DS_MERCHANT_TRANSACTIONTYPE", "0");
            oficial.SetParameter("DS_MERCHANT_TERMINAL", "1");

            string parametrosBase64 = oficial.createMerchantParameters();
            string firmaOficial = oficial.createMerchantSignature(CLAVE_COMERCIO);

            // Act
            string firmaNuestra = FirmadorRedsys.Firmar(parametrosBase64, NUMERO_ORDEN, CLAVE_COMERCIO);

            // Assert
            Assert.AreEqual(firmaOficial, firmaNuestra);
        }

        [TestMethod]
        public void Firmar_ConOtroNumeroDeOrden_TambienCoincideConElHelperOficial()
        {
            // La clave de firma se deriva del número de pedido: hay que comprobar más de uno,
            // y uno cuya longitud no sea múltiplo de 8 (el relleno de ceros)
            const string otraOrden = "250904ABC";

            RedsysAPI oficial = new RedsysAPI();
            oficial.SetParameter("DS_MERCHANT_AMOUNT", "9900");
            oficial.SetParameter("DS_MERCHANT_ORDER", otraOrden);
            oficial.SetParameter("DS_MERCHANT_MERCHANTCODE", "329515704");
            oficial.SetParameter("DS_MERCHANT_TERMINAL", "1");

            string parametrosBase64 = oficial.createMerchantParameters();
            string firmaOficial = oficial.createMerchantSignature(CLAVE_COMERCIO);

            string firmaNuestra = FirmadorRedsys.Firmar(parametrosBase64, otraOrden, CLAVE_COMERCIO);

            Assert.AreEqual(firmaOficial, firmaNuestra);
        }

        [TestMethod]
        public void ParametrosBase64_DejaElEmv3dsComoObjetoAnidadoNoComoCadena()
        {
            // Es la razón de ser de esta clase: por REST, "DS_MERCHANT_EMV3DS" tiene que ser un
            // objeto. Si acaba siendo una cadena con JSON dentro (lo que haría SetParameter),
            // Redsys no lo interpreta como EMV3DS
            JObject parametros = new JObject
            {
                ["DS_MERCHANT_ORDER"] = NUMERO_ORDEN,
                ["DS_MERCHANT_EMV3DS"] = new JObject { ["threeDSInfo"] = "CardData" }
            };

            string base64 = FirmadorRedsys.ParametrosBase64(parametros);

            JObject decodificado = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            Assert.AreEqual(JTokenType.Object, decodificado["DS_MERCHANT_EMV3DS"].Type);
            Assert.AreEqual("CardData", decodificado["DS_MERCHANT_EMV3DS"]["threeDSInfo"].ToString());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Firmar_SinNumeroDeOrden_Falla()
        {
            FirmadorRedsys.Firmar("cualquierCosa", null, CLAVE_COMERCIO);
        }
    }
}
