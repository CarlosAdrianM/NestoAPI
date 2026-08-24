using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    ///
    /// 24\08: el filtro pasó de LISTA NEGRA de rutas de bot (seis barridos en dos meses,
    /// siempre por detrás de los escáneres) a LISTA BLANCA por código HTTP. Los tests
    /// cambian en consecuencia: ya no se enumeran firmas de escáner una a una, sino que se
    /// comprueba el contrato nuevo — todo 404/403 se filtra SALVO las rutas nuestras.
    /// </summary>
    [TestClass]
    public class ElmahErrorFilterConfigTests
    {
        // Contexto mínimo para evaluar la aserción: ELMAH usa DataBinder.Eval sobre las
        // propiedades del contexto (BaseException, HttpStatusCode, Context...). Context null
        // reproduce los errores señalados fuera de una petición HTTP (jobs de Hangfire).
        // Los nombres DEBEN coincidir con los de Elmah.Assertions.AssertionHelperContext:
        // lo verifica BindingsDelFiltro_ExistenEnElContextoRealDeElmah.
        private class ContextoPrueba
        {
            public Exception BaseException { get; set; }
            public object Context { get; set; }
            public int HttpStatusCode { get; set; }
            public bool HasHttpStatusCode => HttpStatusCode != 0;
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
            // ELMAH deriva HttpStatusCode de la propia excepción; se replica aquí para que el
            // contexto de prueba se comporte como el de producción.
            int codigo = (excepcion as System.Web.HttpException)?.GetHttpCode() ?? 0;
            return assertion.Test(new ContextoPrueba
            {
                BaseException = excepcion,
                HttpStatusCode = codigo
            });
        }

        private static System.Web.HttpException Error404DeRuta(string ruta)
        {
            return new System.Web.HttpException(404,
                $"No se encuentra el controlador de la ruta de acceso '{ruta}' o no implementa IController.");
        }

        /// <summary>
        /// Guarda directa del modo de fallo de #183: si un binding del filtro no existe como
        /// propiedad del contexto real de ELMAH, DataBinder.Eval lanza y ELMAH deja de
        /// registrar TODO en silencio. Este test lo detecta en compilación en vez de en
        /// producción tres semanas después.
        /// </summary>
        [TestMethod]
        public void BindingsDelFiltro_ExistenEnElContextoRealDeElmah()
        {
            Assembly elmah = typeof(IAssertion).Assembly;
            Type[] tipos;
            try
            {
                tipos = elmah.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                tipos = ex.Types.Where(t => t != null).ToArray();
            }

            Type contexto = tipos.FirstOrDefault(t => t.Name == "AssertionHelperContext");
            Assert.IsNotNull(contexto,
                "No se encuentra Elmah.Assertions.AssertionHelperContext: ¿ha cambiado la versión de ELMAH?");

            // Los bindings que usa el errorFilter del Web.config.
            foreach (string binding in new[] { "BaseException", "HttpStatusCode", "Context" })
            {
                PropertyInfo propiedad = contexto.GetProperty(binding,
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(propiedad,
                    $"El binding '{binding}' del errorFilter NO existe como propiedad pública de " +
                    $"{contexto.FullName}. En producción DataBinder.Eval lanzaría y ELMAH dejaría " +
                    "de registrar errores EN SILENCIO (incidente #183).");
            }

            // El contexto de prueba tiene que exponer lo mismo que el real, o los tests de
            // abajo estarían validando algo que no ocurre en producción.
            foreach (string binding in new[] { "BaseException", "HttpStatusCode", "Context" })
            {
                Assert.IsNotNull(typeof(ContextoPrueba).GetProperty(binding),
                    $"ContextoPrueba no expone '{binding}' y el contexto real de ELMAH sí.");
            }
        }

        [TestMethod]
        public void ErrorFilter_EscaneosDeBots_SeFiltran()
        {
            // NestoAPI#336: rutas reales del log. Con la lista blanca da igual la firma
            // concreta: cualquier ruta que no sea nuestra se filtra.
            Assert.IsTrue(SeFiltra(Error404DeRuta("/wp-admin/")), "/wp-admin/");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.git/config")), "/.git/config");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.aws/credentials")), "/.aws/credentials");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/actuator/env")), "/actuator/env");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/xmlrpc.php")), "ruta acabada en .php");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/id_rsa")), "/id_rsa");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.well-known/openid-configuration")), "OpenID discovery");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/.well-known/oauth-authorization-server")), "OAuth discovery");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/static")), "/static");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/login")), "/login");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/fetch")), "/fetch");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/api")), "/api PELADO es una sonda, no una llamada real");
        }

        /// <summary>
        /// La razón de ser del cambio a lista blanca: hasta el 19\08 cada ruta nueva de los
        /// escáneres exigía un barrido y un despliegue. Estas rutas NO estaban en ninguna
        /// lista negra y aun así tienen que filtrarse.
        /// </summary>
        [TestMethod]
        public void ErrorFilter_RutasDeEscaneoNuncaVistas_SeFiltranSinTocarElConfig()
        {
            Assert.IsTrue(SeFiltra(Error404DeRuta("/una-ruta-que-nadie-ha-visto-jamas")));
            Assert.IsTrue(SeFiltra(Error404DeRuta("/2027/el/exploit/de/moda")));
            Assert.IsTrue(SeFiltra(Error404DeRuta("/logs-api")), "typo del dashboard de ELMAH, no es ruta nuestra");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/apiv2/pedidos")), "'api' pegado a otras letras NO es nuestra ruta");
            Assert.IsTrue(SeFiltra(Error404DeRuta("/pedidos")), "'pedidos' no es el deep link '/pedido/'");
        }

        [TestMethod]
        public void ErrorFilter_Un404Legitimo_NoSeFiltra()
        {
            // Un cliente llamando a una ruta NUESTRA que ya no existe tras un refactor es
            // justo lo que queremos ver en el log (advertencia explícita de la issue #336).
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/Clientes/MetodoViejoEliminado")));
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/PedidosVenta")));
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/auth/prestashop-login")), "ruta real de AuthController");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/api/sync/poisonpills/changestatus")), "ruta real de SyncWebhookController");
        }

        /// <summary>
        /// Contrapartida de la lista blanca: TODAS las rutas nuestras fuera de /api tienen que
        /// estar contempladas, o sus 404 se ocultarían en silencio. Si se añade un controlador
        /// MVC nuevo, hay que añadirlo al filtro Y aquí.
        /// </summary>
        [TestMethod]
        public void ErrorFilter_RutasNuestrasFueraDeApi_NoSeFiltran()
        {
            Assert.IsFalse(SeFiltra(Error404DeRuta("/oauth/token")), "token de NestoApp (Startup.cs)");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/pago/redirect")), "PagoRedirectController (#121)");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/pedido/1/924625")), "deep link de pedido (#107)");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/Home/ResetPassword")), "HomeController");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/home/resetpassword")), "el enrutado MVC no distingue mayúsculas");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/")), "la raíz la sirve HomeController.Index");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/logs-nestoapi")), "dashboard de ELMAH (elmah.mvc.route)");
            Assert.IsFalse(SeFiltra(Error404DeRuta("/hangfire")), "dashboard de Hangfire");
            // .well-known/assetlinks.json es legítimo (deep linking #107): aunque filtramos las
            // rutas de discovery OAuth bajo .well-known, assetlinks debe seguir registrándose.
            Assert.IsFalse(SeFiltra(Error404DeRuta("/.well-known/assetlinks.json")), "assetlinks.json (deep linking #107)");
        }

        /// <summary>
        /// Los 403 de infraestructura (trace.axd) eran 13 errores/mes que la lista negra no
        /// cazaba porque su mensaje no lleva ruta.
        /// </summary>
        [TestMethod]
        public void ErrorFilter_403DeInfraestructura_SeFiltra()
        {
            var traceAxd = new System.Web.HttpException(403,
                "Se produjo una excepción de tipo 'System.Web.HttpException'.");
            Assert.IsTrue(SeFiltra(traceAxd), "el 403 de trace.axd no aporta nada al log");
        }

        [TestMethod]
        public void ErrorFilter_ErroresRealesDeNegocio_NoSeFiltran()
        {
            // El tipo (HttpException) forma parte del filtro: otro error cuyo mensaje
            // casualmente contenga un patrón no se filtra.
            Assert.IsFalse(SeFiltra(new InvalidOperationException("La secuencia contiene más de un elemento")));
            Assert.IsFalse(SeFiltra(new Exception("Error al procesar el producto wp-classic de la tarifa")));
            // Un HttpException que NO sea 404/403 (un 500 de verdad) tiene que registrarse.
            Assert.IsFalse(SeFiltra(new System.Web.HttpException(500, "Error interno del servidor")));
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
