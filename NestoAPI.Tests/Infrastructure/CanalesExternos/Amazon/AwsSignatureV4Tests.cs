using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;

namespace NestoAPI.Tests.Infrastructure.CanalesExternos.Amazon
{
    /// <summary>
    /// El algoritmo SigV4 ya se validó EN VIVO contra Amazon SQS el 10/06/2026 (autenticó
    /// correctamente). Estos tests fijan el formato y el determinismo para evitar regresiones.
    /// </summary>
    [TestClass]
    public class AwsSignatureV4Tests
    {
        private const string ACCESS = "AKIAEXAMPLE0000000000";
        private const string SECRET = "secretKeyExample/abcDEF1234567890";
        private static readonly DateTime FECHA = new DateTime(2026, 6, 10, 10, 2, 48, DateTimeKind.Utc);

        private static AwsSignatureV4.FirmaResultado Firmar(DateTime fecha)
        {
            return AwsSignatureV4.FirmarPost(
                ACCESS, SECRET, "eu-west-1", "sqs",
                "sqs.eu-west-1.amazonaws.com", "/190854240256/sp-api-credential-rotation",
                "Action=ReceiveMessage&Version=2012-11-05", fecha);
        }

        [TestMethod]
        public void FirmarPost_AmzDate_TieneFormatoIso8601Basico()
        {
            AwsSignatureV4.FirmaResultado f = Firmar(FECHA);
            Assert.AreEqual("20260610T100248Z", f.AmzDate);
        }

        [TestMethod]
        public void FirmarPost_Authorization_TieneEstructuraSigV4()
        {
            AwsSignatureV4.FirmaResultado f = Firmar(FECHA);

            StringAssert.StartsWith(f.Authorization, "AWS4-HMAC-SHA256 ");
            StringAssert.Contains(f.Authorization, "Credential=" + ACCESS + "/20260610/eu-west-1/sqs/aws4_request");
            StringAssert.Contains(f.Authorization, "SignedHeaders=host;x-amz-date");
            Assert.IsTrue(Regex.IsMatch(f.Authorization, "Signature=[0-9a-f]{64}$"),
                "La firma debe ser 64 caracteres hexadecimales al final del header.");
        }

        [TestMethod]
        public void FirmarPost_MismasEntradas_ProduceMismaFirma()
        {
            Assert.AreEqual(Firmar(FECHA).Authorization, Firmar(FECHA).Authorization);
        }

        [TestMethod]
        public void FirmarPost_DistintaFecha_ProduceDistintaFirma()
        {
            Assert.AreNotEqual(Firmar(FECHA).Authorization, Firmar(FECHA.AddSeconds(1)).Authorization);
        }
    }
}
