using Elmah;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Xml;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Seguridad (25/11/26): ELMAH no guarda credenciales (el JWT de Authorization, cookies, X-API-KEY, contraseñas…) y deja intacto
    /// todo lo demás. LA REGLA es <see cref="ReglaElmahSinCredenciales"/>; el punto único, <see cref="ErrorLogSinCredenciales"/> en el Web.config.
    /// </summary>
    [TestClass]
    public class ElmahSinCredencialesTests
    {
        private const string JWT = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJjYXJsb3MifQ.abc123";

        [TestMethod]
        public void NombresSensibles()
        {
            foreach (string nombre in new[] { "HTTP_AUTHORIZATION", "Authorization", "HTTP_COOKIE", "Cookie", "HTTP_X_API_KEY", "X-API-KEY", "x-apikey",
                "access_token", "refresh_token", "HTTP_X_CLIENT_SECRET", "password", "AUTH_PASSWORD", "Proxy-Authorization" })
            {
                Assert.IsTrue(ReglaElmahSinCredenciales.EsNombreSensible(nombre), nombre);
            }
            foreach (string nombre in new[] { "AUTH_USER", "LOGON_USER", "AUTH_TYPE", "HTTP_USER_AGENT", "REMOTE_ADDR", "HTTP_X_ARIADNA_VERSION",
                "HTTP_HOST", "URL", "QUERY_STRING", "", null })
            {
                Assert.IsFalse(ReglaElmahSinCredenciales.EsNombreSensible(nombre), nombre ?? "null");
            }
        }

        [TestMethod]
        public void Authorization_DejaElEsquema_YQuitaLaCredencial()
        {
            Assert.AreEqual("Bearer (oculto)", ReglaElmahSinCredenciales.Ocultar("HTTP_AUTHORIZATION", "Bearer " + JWT));
            Assert.AreEqual("Basic (oculto)", ReglaElmahSinCredenciales.Ocultar("Authorization", "Basic dXN1YXJpbzpjbGF2ZQ=="));
            Assert.AreEqual("(oculto)", ReglaElmahSinCredenciales.Ocultar("HTTP_AUTHORIZATION", JWT));
            Assert.AreEqual("(oculto)", ReglaElmahSinCredenciales.Ocultar("HTTP_X_API_KEY", "clave-secreta"));
            Assert.AreEqual("", ReglaElmahSinCredenciales.Ocultar("HTTP_COOKIE", ""));
        }

        [TestMethod]
        public void AllHttp_SoloLasLineasSensibles()
        {
            string allHttp = "HTTP_CONNECTION:keep-alive\r\nHTTP_AUTHORIZATION:Bearer " + JWT + "\r\nHTTP_COOKIE:.AspNet.Cookies=xyz; otra=1\r\n" +
                "HTTP_HOST:api.nuevavision.es\r\nHTTP_X_API_KEY:k-123\r\nHTTP_USER_AGENT:Nesto/1.0\r\n";

            string limpio = ReglaElmahSinCredenciales.OcultarEnBloque(allHttp);

            Assert.AreEqual("HTTP_CONNECTION:keep-alive\r\nHTTP_AUTHORIZATION:Bearer (oculto)\r\nHTTP_COOKIE:(oculto)\r\n" +
                "HTTP_HOST:api.nuevavision.es\r\nHTTP_X_API_KEY:(oculto)\r\nHTTP_USER_AGENT:Nesto/1.0\r\n", limpio);
        }

        [TestMethod]
        public void AllRaw_ConEspacioTrasLosDosPuntos()
        {
            string allRaw = "Host: api.nuevavision.es:443\r\nAuthorization: Bearer " + JWT + "\r\nX-Ariadna-Version: 1.2.3\r\nPassword: x";

            Assert.AreEqual("Host: api.nuevavision.es:443\r\nAuthorization: Bearer (oculto)\r\nX-Ariadna-Version: 1.2.3\r\nPassword: (oculto)",
                ReglaElmahSinCredenciales.OcultarEnBloque(allRaw));
        }

        [TestMethod]
        public void Parametros_DeLaQueryString_YElReferer()
        {
            Assert.AreEqual("empresa=1&access_token=(oculto)&pedido=930942",
                ReglaElmahSinCredenciales.OcultarParametros("empresa=1&access_token=" + JWT + "&pedido=930942"));
            Assert.AreEqual("https://x/y?apiKey=(oculto)#a", ReglaElmahSinCredenciales.OcultarParametros("https://x/y?apiKey=abc#a"));
            Assert.AreEqual("empresa=1&cliente=15191", ReglaElmahSinCredenciales.OcultarParametros("empresa=1&cliente=15191"));
        }

        [TestMethod]
        public void Error_SinCredenciales_YElRestoIntacto_SinTocarElOriginal()
        {
            var error = new Error(new InvalidOperationException("fallo"));
            error.User = "NUEVAVISION\\Carlos";
            error.ServerVariables["HTTP_AUTHORIZATION"] = "Bearer " + JWT;
            error.ServerVariables["HTTP_COOKIE"] = "a=b";
            error.ServerVariables["HTTP_X_API_KEY"] = "k-123";
            error.ServerVariables["ALL_HTTP"] = "HTTP_AUTHORIZATION:Bearer " + JWT + "\r\nHTTP_HOST:h\r\n";
            error.ServerVariables["ALL_RAW"] = "Authorization: Bearer " + JWT + "\r\nHost: h\r\n";
            error.ServerVariables["QUERY_STRING"] = "access_token=" + JWT;
            error.ServerVariables["AUTH_USER"] = "NUEVAVISION\\Carlos";
            error.ServerVariables["REMOTE_ADDR"] = "10.0.0.1";
            error.Form["grant_type"] = "password";
            error.Form["username"] = "carlos";
            error.Form["password"] = "secreta";
            error.QueryString["access_token"] = JWT;
            error.QueryString["empresa"] = "1";
            error.Cookies[".AspNet.Cookies"] = "sesion";

            Error limpio = ReglaElmahSinCredenciales.Limpiar(error);

            Assert.AreNotSame(error, limpio);
            Assert.AreEqual("Bearer (oculto)", limpio.ServerVariables["HTTP_AUTHORIZATION"]);
            Assert.AreEqual("(oculto)", limpio.ServerVariables["HTTP_COOKIE"]);
            Assert.AreEqual("(oculto)", limpio.ServerVariables["HTTP_X_API_KEY"]);
            Assert.AreEqual("HTTP_AUTHORIZATION:Bearer (oculto)\r\nHTTP_HOST:h\r\n", limpio.ServerVariables["ALL_HTTP"]);
            Assert.AreEqual("Authorization: Bearer (oculto)\r\nHost: h\r\n", limpio.ServerVariables["ALL_RAW"]);
            Assert.AreEqual("access_token=(oculto)", limpio.ServerVariables["QUERY_STRING"]);
            Assert.AreEqual("NUEVAVISION\\Carlos", limpio.ServerVariables["AUTH_USER"]);
            Assert.AreEqual("10.0.0.1", limpio.ServerVariables["REMOTE_ADDR"]);
            Assert.AreEqual("password", limpio.Form["grant_type"], "El valor «password» de otro campo no se toca");
            Assert.AreEqual("carlos", limpio.Form["username"]);
            Assert.AreEqual("(oculto)", limpio.Form["password"]);
            Assert.AreEqual("(oculto)", limpio.QueryString["access_token"]);
            Assert.AreEqual("1", limpio.QueryString["empresa"]);
            Assert.AreEqual("(oculto)", limpio.Cookies[".AspNet.Cookies"]);
            Assert.AreEqual("fallo", limpio.Message);
            Assert.AreEqual("NUEVAVISION\\Carlos", limpio.User);

            // El original, como estaba (lo pueden usar otros)
            Assert.AreEqual("Bearer " + JWT, error.ServerVariables["HTTP_AUTHORIZATION"]);
            Assert.IsFalse(ErrorXml.EncodeString(limpio).Contains(JWT), "En el XML que se guarda no queda el token");
        }

        [TestMethod]
        public void NuncaLanza()
        {
            Assert.IsNull(ReglaElmahSinCredenciales.Limpiar((Error)null));
            ReglaElmahSinCredenciales.Limpiar((NameValueCollection)null);
            Assert.IsNull(ReglaElmahSinCredenciales.OcultarEnBloque(null));
            Assert.AreEqual("", ReglaElmahSinCredenciales.OcultarParametros(""));
            Error sinContexto = ReglaElmahSinCredenciales.Limpiar(new Error(new Exception("x")));
            Assert.AreEqual("x", sinContexto.Message);
        }

        [TestMethod]
        public void WebConfig_UsaElErrorLogSinCredenciales_YElmahLoPuedeCrear()
        {
            string ruta = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\NestoAPI\Web.config"));
            var doc = new XmlDocument();
            doc.Load(ruta);
            var nodo = (XmlElement)doc.SelectSingleNode("/configuration/elmah/errorLog");

            string tipo = nodo.GetAttribute("type");
            Assert.AreEqual("NestoAPI.Infraestructure.ErrorLogSinCredenciales, NestoAPI", tipo);
            Type resuelto = Type.GetType(tipo, throwOnError: true);
            Assert.IsTrue(typeof(SqlErrorLog).IsAssignableFrom(resuelto));
            // ELMAH lo crea con el constructor (IDictionary) y la configuración del nodo
            var config = new Hashtable { ["connectionString"] = "Data Source=.;Initial Catalog=x;Integrated Security=True", ["applicationName"] = "NestoAPI" };
            var log = (ErrorLog)Activator.CreateInstance(resuelto, config);
            Assert.AreEqual("NestoAPI", log.ApplicationName);
        }
    }
}
