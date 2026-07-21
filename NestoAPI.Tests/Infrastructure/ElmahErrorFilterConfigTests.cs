using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using Elmah.Assertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#336: el errorFilter del Web.config filtra el ruido de bots (38% del log) y
    /// NestoAPI#183: las cancelaciones de cliente. Estos tests cargan el XML REAL del
    /// Web.config y evalúan la aserción con la misma factory que usa ELMAH en producción,
    /// así que cubren dos regresiones a la vez:
    /// 1. Que el filtro filtra lo que debe y NO filtra errores reales (un 404 legítimo de
    ///    una ruta nuestra eliminada tiene que seguir registrándose).
    /// 2. Que el XML parsea: una sintaxis inválida (p. ej. un elemento &lt;is&gt;, que no
    ///    existe) rompería AQUÍ en vez de tirar IIS al arrancar; y un binding que lanza
    ///    (p. ej. Context.Request.Path con Context null) abortaría en silencio TODA la
    ///    cadena de logging de ELMAH (el incidente de #183).
    /// </summary>
    [TestClass]
    public class ElmahErrorFilterConfigTests
    {
        // Contexto mínimo para evaluar la aserción: ELMAH usa DataBinder.Eval sobre las
        // propiedades del contexto (BaseException, Context...). Context null reproduce los
        // errores señalados fuera de una petición HTTP (jobs de Hangfire).
        private class ContextoPrueba
        {
            public Exception BaseException { get; set; }
            public object Context { get; set; }
        }

        private static IAssertion CargarAssertionDelWebConfig()
        {
            // bin\Debug -> NestoAPI.Tests -> raíz de la solución -> NestoAPI\Web.config
            string ruta = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            Assert.IsTrue(File.Exists(ruta), $"No se encuentra el Web.config en {ruta}");

            var doc = new XmlDocument();
            doc.Load(ruta);
            var nodoTest = doc.SelectSingleNode("/configuration/elmah/errorFilter/test/*") as XmlElement;
            Assert.IsNotNull(nodoTest, "El Web.config no tiene errorFilter/test");

            return AssertionFactory.Create(nodoTest);
        }

        private static bool SeFiltra(Exception excepcion)
        {
            IAssertion assertion = CargarAssertionDelWebConfig();
            return assertion.Test(new ContextoPrueba { BaseException = excepcion });
        }

        private static System.Web.HttpException Error404DeRuta(string ruta)
        {
            return new System.Web.HttpException(404,
                $"No se encuentra el controlador de la ruta de acceso '{ruta}' o no implementa IController.");
        }

        [TestMethod]
        public void ErrorFilter_EscaneosDeBots_SeFiltran()
        {
            // NestoAPI#336: las rutas más repetidas del análisis (807 errores en 30 días)
            Assert.IsTrue(SeFiltra(Error404DeRuta("/wp-admin/")), "/wp-admin/");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/wp-includes/wlwmanifest.xml")), "/wp-includes/");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.git/config")), "/.git/config");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.aws/credentials")), "/.aws/credentials");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/actuator/env")), "/actuator/env");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/public/_ignition/execute-solution")), "_ignition");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/v1/graphql")), "graphql");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/xmlrpc.php")), "ruta acabada en .php");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/index.php/_ignition/execute-solution")), "index.php");
        }

        [TestMethod]
        public void ErrorFilter_Un404Legitimo_NoSeFiltra()
        {
            // Un cliente llamando a una ruta NUESTRA que ya no existe tras un refactor es
            // justo lo que queremos ver en el log (advertencia explícita de la issue #336).
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/Clientes/MetodoViejoEliminado")));
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/PedidosVenta")));
        }

        [TestMethod]
        public void ErrorFilter_ErroresRealesDeNegocio_NoSeFiltran()
        {
            // El tipo (HttpException) forma parte del filtro: otro error cuyo mensaje
            // casualmente contenga un patrón no se filtra.
            Assert.IsFalse(SeFiltra(new InvalidOperationException("La secuencia contiene más de un elemento")));
            Assert.IsFalse(SeFiltra(new Exception("Error al procesar el producto wp-classic de la tarifa")));
        }

        [TestMethod]
        public void ErrorFilter_CancelacionesDeCliente_SiguenFiltrandose()
        {
            // Regresión de NestoAPI#183: al añadir el filtro de bots (#336) no se puede
            // perder el de cancelaciones (TaskCanceledException hereda de OperationCanceled).
            Assert.IsTrue(SeFiltra(new OperationCanceledException()));
            Assert.IsTrue(SeFiltra(new TaskCanceledException()));
        }
    }
}
