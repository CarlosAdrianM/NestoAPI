using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Xml;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#404: el proceso tiene que salir a Internet con TLS 1.2 desde el primer segundo.
    ///
    /// El Web.config declara <c>httpRuntime targetFramework="4.5"</c>, lo que activa el modo de
    /// compatibilidad de .NET 4.5: ahí el defecto de <c>ServicePointManager.SecurityProtocol</c>
    /// es SSL 3.0 + TLS 1.0. Como <c>ServicePointManager</c> es global al proceso y
    /// AmazonFeedsGateway no lo fija nunca, la primera llamada saliente tras un reinicio podía
    /// salir con TLS 1.0 y la SP-API de Amazon la rechazaba.
    /// </summary>
    [TestClass]
    public class ConfiguracionTlsTests
    {
        [TestMethod]
        public void ConfigurarTlsDelProceso_DejaElProcesoEnTls12()
        {
            SecurityProtocolType original = ServicePointManager.SecurityProtocol;
            try
            {
                // Se simula el arranque en el modo 4.5: SSL 3.0 + TLS 1.0, que es lo que Amazon rechaza.
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;

                NestoAPI.Startup.ConfigurarTlsDelProceso();

                Assert.IsTrue(ServicePointManager.SecurityProtocol.HasFlag(SecurityProtocolType.Tls12),
                    "Sin TLS 1.2 la SP-API de Amazon rechaza la conexión");
                Assert.IsFalse(ServicePointManager.SecurityProtocol.HasFlag(SecurityProtocolType.Ssl3),
                    "SSL 3.0 no debe quedar habilitado");
            }
            finally
            {
                ServicePointManager.SecurityProtocol = original;
            }
        }

        /// <summary>
        /// Guarda del porqué: si algún día se sube el targetFramework a 4.8, el defecto del
        /// framework ya sería moderno y este test recuerda revisar (y quizá retirar) el apaño y
        /// los 12 sitios que fijan el protocolo a mano.
        /// </summary>
        [TestMethod]
        public void WebConfig_SigueEnModoDeCompatibilidad45_QueEsLoQueObligaAFijarElTls()
        {
            string ruta = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");

            var doc = new XmlDocument();
            doc.Load(ruta);
            var httpRuntime = doc.SelectSingleNode("/configuration/system.web/httpRuntime") as XmlElement;
            Assert.IsNotNull(httpRuntime, "El Web.config no tiene system.web/httpRuntime");

            Assert.AreEqual("4.5", httpRuntime.GetAttribute("targetFramework"),
                "Si esto ha cambiado, revisar si sigue haciendo falta fijar el TLS a mano (#404)");
        }
    }
}
