using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#336 (fase 2): el bloqueo real de bots se hace con Request Filtering de IIS
    /// (system.webServer/security/requestFiltering/denyUrlSequences en el Web.config). IIS
    /// deniega la petición si la URL contiene CUALQUIERA de las secuencias, en cualquier
    /// posición — un patrón demasiado genérico bloquearía rutas legítimas de la API para los
    /// tres clientes (Nesto, NestoApp, TiendasNuevaVision). Estos tests cargan el XML real y
    /// comprueban que ninguna secuencia pisa una ruta nuestra conocida.
    /// </summary>
    [TestClass]
    public class RequestFilteringConfigTests
    {
        private static List<string> CargarSecuenciasDenegadas()
        {
            string ruta = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");

            var doc = new XmlDocument();
            doc.Load(ruta);
            var nodos = doc.SelectNodes(
                "/configuration/system.webServer/security/requestFiltering/denyUrlSequences/add");
            Assert.IsNotNull(nodos);
            return nodos.Cast<XmlElement>().Select(n => n.GetAttribute("sequence")).ToList();
        }

        [TestMethod]
        public void DenyUrlSequences_ExistenYBloqueanLosPatronesDeEscaneoConocidos()
        {
            List<string> secuencias = CargarSecuenciasDenegadas();

            Assert.IsTrue(secuencias.Count > 0, "El Web.config debe denegar las URLs de escaneo de bots");
            foreach (string urlDeBot in new[]
            {
                "/wp-admin/setup-config.php",
                "/wordpress/wp-login.php",
                "/.git/config",
                "/.env",
                "/.aws/credentials",
                "/phpmyadmin/index.php",
                "/actuator/health",
                "/index.php",
                "/cgi-bin/luci",
                "/debug/pprof/",
                "/debug/vars",
            })
            {
                Assert.IsTrue(
                    secuencias.Any(s => urlDeBot.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0),
                    $"La URL de escaneo '{urlDeBot}' no la bloquea ninguna secuencia denegada");
            }
        }

        [TestMethod]
        public void DenyUrlSequences_NoBloqueanNingunaRutaLegitimaDeLaApi()
        {
            List<string> secuencias = CargarSecuenciasDenegadas();

            // Rutas reales que usan los tres clientes y los flujos especiales del Web.config
            // (handlers de assetlinks, Redsys y ELMAH). Si alguien añade una secuencia que case
            // con una de estas, IIS empezaría a devolver 404.5 a peticiones legítimas.
            foreach (string rutaLegitima in new[]
            {
                "/api/PedidosVenta",
                "/api/Clientes/CorregirNif",
                "/api/PlanesVentajas?incluirCancelados=false",
                "/api/EnviosAgencias/12345/RestarReembolso",
                "/api/ExtractosCliente/Liquidar",
                "/api/Remesas/EfectosCandidatos",
                "/api/Facturas/CrearFactura",
                "/oauth/token",
                "/api/auth/windows-token",
                "/api/auth/token",
                "/.well-known/assetlinks.json",
                "/pago/ok.html",
                "/pago/ko.html",
                "/elmah.axd",
                "/api/Novedades",
                "/api/Errores",
            })
            {
                string secuenciaQueCasa = secuencias.FirstOrDefault(
                    s => rutaLegitima.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsNull(secuenciaQueCasa,
                    $"La secuencia denegada '{secuenciaQueCasa}' bloquearía la ruta legítima '{rutaLegitima}'");
            }
        }
    }
}
