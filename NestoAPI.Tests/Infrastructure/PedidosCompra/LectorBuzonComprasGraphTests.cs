using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857: el lector de Graph con un HttpMessageHandler falso (nada sale a Internet).</summary>
    [TestClass]
    public class LectorBuzonComprasGraphTests
    {
        private const string TENANT = "00000000-0000-0000-0000-000000000001";
        private const string BUZON = "buzon-prueba@ejemplo.test";

        private class HandlerFalso : HttpMessageHandler
        {
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public readonly List<string> CuerposPeticion = new List<string>();
            public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                CuerposPeticion.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                return Responder(request);
            }
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode estado = HttpStatusCode.OK)
            => new HttpResponseMessage(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Token() => Json("{\"token_type\":\"Bearer\",\"expires_in\":3599,\"access_token\":\"tok-123\"}");

        private static string Mensaje(string id, string asunto) =>
            "{\"id\":\"" + id + "\",\"internetMessageId\":\"<" + id + "@ejemplo.test>\",\"receivedDateTime\":\"2026-11-12T09:30:00Z\"," +
            "\"subject\":\"" + asunto + "\",\"from\":{\"emailAddress\":{\"name\":\"Avisos\",\"address\":\"avisos@ejemplo-agencia.test\"}}," +
            "\"body\":{\"contentType\":\"html\",\"content\":\"<a href='https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=61770000001'>x</a>\"}}";

        private static (LectorBuzonComprasGraph Lector, HandlerFalso Handler) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var handler = new HandlerFalso { Responder = responder };
            var lector = new LectorBuzonComprasGraph(new ConfiguracionBuzonCompras
            {
                Tenant = TENANT, ClientId = "cliente-prueba", Secreto = "secreto-de-prueba", Buzon = BUZON
            }, new HttpClient(handler), () => new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc));
            return (lector, handler);
        }

        private static bool EsToken(HttpRequestMessage r) => r.RequestUri.Host == "login.microsoftonline.com";

        [TestMethod]
        public void Configuracion_SinAlgunaClave_EsNull()
        {
            var claves = new Dictionary<string, string>
            {
                [ConfiguracionBuzonCompras.CLAVE_TENANT] = TENANT,
                [ConfiguracionBuzonCompras.CLAVE_CLIENT_ID] = "c",
                [ConfiguracionBuzonCompras.CLAVE_SECRETO] = "s",
                [ConfiguracionBuzonCompras.CLAVE_BUZON] = " "
            };
            Assert.IsNull(ConfiguracionBuzonCompras.Leer(k => claves.TryGetValue(k, out string v) ? v : null));
            claves[ConfiguracionBuzonCompras.CLAVE_BUZON] = BUZON;
            Assert.AreEqual(BUZON, ConfiguracionBuzonCompras.Leer(k => claves[k]).Buzon);
            Assert.IsNull(ConfiguracionBuzonCompras.Leer(k => null));
        }

        [TestMethod]
        public async Task LeerNuevos_PrimeraVez_TokenDeAplicacionYDeltaDeLaBandejaConPaginacion()
        {
            const string siguiente = "https://graph.microsoft.com/v1.0/users/x/mailFolders/inbox/messages/delta?$skiptoken=PAG2";
            const string delta = "https://graph.microsoft.com/v1.0/users/x/mailFolders/inbox/messages/delta?$deltatoken=FIN";
            var (lector, handler) = Crear(r =>
            {
                if (EsToken(r))
                {
                    return Token();
                }
                return r.RequestUri.AbsoluteUri.Contains("PAG2")
                    ? Json("{\"value\":[" + Mensaje("m2", "Dos") + ",{\"id\":\"m3\",\"@removed\":{\"reason\":\"deleted\"}}],\"@odata.deltaLink\":\"" + delta + "\"}")
                    : Json("{\"value\":[" + Mensaje("m1", "Uno") + "],\"@odata.nextLink\":\"" + siguiente + "\"}");
            });

            LecturaBuzonCompras lectura = await lector.LeerNuevos(null, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

            Assert.IsNull(lectura.Error);
            Assert.IsTrue(lectura.Completa);
            Assert.AreEqual(delta, lectura.SiguienteEnlace);
            CollectionAssert.AreEqual(new[] { "Uno", "Dos" }, lectura.Correos.Select(c => c.Asunto).ToArray(), "El @removed no cuenta");
            CorreoCompras primero = lectura.Correos[0];
            Assert.AreEqual("<m1@ejemplo.test>", primero.MensajeId);
            Assert.AreEqual("avisos@ejemplo-agencia.test", primero.RemitenteDireccion);
            Assert.IsTrue(primero.CuerpoEsHtml);
            Assert.AreEqual(new DateTime(2026, 11, 12, 9, 30, 0, DateTimeKind.Utc).ToLocalTime(), primero.Recibido);

            HttpRequestMessage token = handler.Peticiones[0];
            Assert.AreEqual(HttpMethod.Post, token.Method);
            Assert.AreEqual($"/{TENANT}/oauth2/v2.0/token", token.RequestUri.AbsolutePath);
            StringAssert.Contains(handler.CuerposPeticion[0], "grant_type=client_credentials");
            StringAssert.Contains(handler.CuerposPeticion[0], "scope=" + Uri.EscapeDataString("https://graph.microsoft.com/.default"));

            HttpRequestMessage primera = handler.Peticiones[1];
            Assert.AreEqual(HttpMethod.Get, primera.Method, "Solo lectura: nunca PATCH ni POST a los mensajes");
            Assert.AreEqual("Bearer tok-123", primera.Headers.Authorization.ToString());
            StringAssert.Contains(Uri.UnescapeDataString(primera.RequestUri.AbsolutePath), "/users/" + BUZON + "/mailFolders/inbox/messages/delta");
            string consulta = Uri.UnescapeDataString(primera.RequestUri.Query);
            StringAssert.Contains(consulta, "$select=id,internetMessageId,receivedDateTime,from,subject,body");
            StringAssert.Contains(consulta, "receivedDateTime ge 2026-11-05T00:00:00Z");
            Assert.AreEqual(3, handler.Peticiones.Count, "Un token para las dos páginas");
            Assert.IsTrue(handler.Peticiones.Skip(1).All(p => p.Method == HttpMethod.Get));
        }

        [TestMethod]
        public async Task LeerNuevos_ConEnlaceGuardado_EmpiezaPorEl()
        {
            const string guardado = "https://graph.microsoft.com/v1.0/users/x/mailFolders/inbox/messages/delta?$deltatoken=ANTES";
            var (lector, handler) = Crear(r => EsToken(r) ? Token() : Json("{\"value\":[],\"@odata.deltaLink\":\"" + guardado + "2\"}"));

            LecturaBuzonCompras lectura = await lector.LeerNuevos(guardado, DateTime.UtcNow);

            Assert.AreEqual(guardado, handler.Peticiones[1].RequestUri.AbsoluteUri);
            Assert.AreEqual(guardado + "2", lectura.SiguienteEnlace);
            Assert.AreEqual(0, lectura.Correos.Count);
        }

        [TestMethod]
        public async Task LeerNuevos_Graph401_NoRompeYNoCambiaLoGuardado()
        {
            var (lector, _) = Crear(r => EsToken(r) ? Token() : Json("{\"error\":{\"code\":\"InvalidAuthenticationToken\"}}", HttpStatusCode.Unauthorized));

            LecturaBuzonCompras lectura = await lector.LeerNuevos("https://graph.microsoft.com/v1.0/delta?$deltatoken=A", DateTime.UtcNow);

            Assert.AreEqual(HttpStatusCode.Unauthorized, lectura.Estado);
            StringAssert.Contains(lectura.Error, "401");
            Assert.IsNull(lectura.SiguienteEnlace, "Se reintenta desde lo guardado");
            Assert.IsFalse(lectura.Reiniciar);
        }

        [TestMethod]
        public async Task LeerNuevos_429EnLaSegundaPagina_DevuelveLoLeidoYSigueDesdeEsaPagina()
        {
            const string siguiente = "https://graph.microsoft.com/v1.0/users/x/mailFolders/inbox/messages/delta?$skiptoken=PAG2";
            var (lector, _) = Crear(r =>
            {
                if (EsToken(r))
                {
                    return Token();
                }
                return r.RequestUri.AbsoluteUri.Contains("PAG2")
                    ? Json("{\"error\":{\"code\":\"TooManyRequests\"}}", (HttpStatusCode)429)
                    : Json("{\"value\":[" + Mensaje("m1", "Uno") + "],\"@odata.nextLink\":\"" + siguiente + "\"}");
            });

            LecturaBuzonCompras lectura = await lector.LeerNuevos(null, DateTime.UtcNow.AddDays(-7));

            Assert.AreEqual(1, lectura.Correos.Count);
            Assert.AreEqual(siguiente, lectura.SiguienteEnlace);
            StringAssert.Contains(lectura.Error, "429");
        }

        [TestMethod]
        public async Task LeerNuevos_410_PideReiniciar()
        {
            var (lector, _) = Crear(r => EsToken(r) ? Token() : Json("{\"error\":{\"code\":\"SyncStateNotFound\"}}", HttpStatusCode.Gone));

            LecturaBuzonCompras lectura = await lector.LeerNuevos("https://graph.microsoft.com/v1.0/delta?$deltatoken=VIEJO", DateTime.UtcNow);

            Assert.IsTrue(lectura.Reiniciar);
        }

        [TestMethod]
        public async Task LeerNuevos_FallaElToken_NoPideNadaAGraph()
        {
            var (lector, handler) = Crear(r => EsToken(r)
                ? Json("{\"error\":\"invalid_client\",\"error_description\":\"AADSTS7000215: Invalid client secret provided.\"}", HttpStatusCode.Unauthorized)
                : throw new AssertFailedException("No debería llamar a Graph"));

            LecturaBuzonCompras lectura = await lector.LeerNuevos(null, DateTime.UtcNow);

            StringAssert.Contains(lectura.Error, "token");
            Assert.AreEqual(1, handler.Peticiones.Count);
            Assert.AreEqual(0, lectura.Correos.Count);
        }

        [TestMethod]
        public async Task LeerNuevos_ErrorDeRed_NoLanza()
        {
            var (lector, _) = Crear(r => EsToken(r) ? Token() : throw new HttpRequestException("sin red"));

            LecturaBuzonCompras lectura = await lector.LeerNuevos(null, DateTime.UtcNow);

            StringAssert.Contains(lectura.Error, "sin red");
        }

        [TestMethod]
        public async Task LeerNuevos_DemasiadasPaginas_SeCortaYSeGuardaElNextLink()
        {
            int pagina = 0;
            var (lector, handler) = Crear(r => EsToken(r)
                ? Token()
                : Json("{\"value\":[],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/delta?$skiptoken=P" + (++pagina) + "\"}"));

            LecturaBuzonCompras lectura = await lector.LeerNuevos(null, DateTime.UtcNow);

            Assert.AreEqual(LectorBuzonComprasGraph.MAXIMO_PAGINAS + 1, handler.Peticiones.Count);
            Assert.IsFalse(lectura.Completa);
            StringAssert.EndsWith(lectura.SiguienteEnlace, "P" + LectorBuzonComprasGraph.MAXIMO_PAGINAS);
        }
    }
}
