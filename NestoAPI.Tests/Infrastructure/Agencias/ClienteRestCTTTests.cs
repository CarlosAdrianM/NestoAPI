using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#493: el cliente HTTP de CTT. Token OAuth2 (client_credentials) cacheado 24 h y
    /// compartido, Bearer en cada llamada, 401 = token nuevo y una repetición, 5xx/red = excepción
    /// transitoria (la política de reintentos la coge), 4xx = respuesta con el error (decide la agencia).
    /// </summary>
    [TestClass]
    public class ClienteRestCTTTests
    {
        private const string RESP_TOKEN = @"{""access_token"":""TOKEN-1"",""expires_in"":86400,""token_type"":""Bearer""}";

        // Cada test usa un ClientId distinto: el token se cachea por proceso y por credencial.
        private static ConfiguracionCTT Config(string clientId) => new ConfiguracionCTT(
            "https://api-test.cttexpress.com/integrations/", clientId, "secreto", "8090300001", new RemitenteCTT());

        [TestMethod]
        public async Task PrimeraLlamada_PideElToken_YLoUsaComoBearer()
        {
            var handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.OK, RESP_TOKEN);
            handler.Responder("manifest/v2.0/shippings", HttpStatusCode.Created, @"{""shipping_data"":{""shipping_code"":""X""}}");
            var registro = new RegistroIntercambiosRemotos();
            var cliente = new ClienteRestCTT(Config("t1"), new HttpClient(handler), registro);

            RespuestaCTT r = await cliente.EnviarAsync(HttpMethod.Post, "manifest/v2.0/shippings", new { a = 1 }, "Manifiesto");

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(201, r.Codigo);
            Assert.AreEqual("X", (string)r.Json["shipping_data"]["shipping_code"]);

            HttpRequestMessage token = handler.Peticiones[0];
            Assert.AreEqual("https://api-test.cttexpress.com/integrations/oauth2/token", token.RequestUri.ToString());
            StringAssert.Contains(handler.Cuerpos[0], "grant_type=client_credentials");
            StringAssert.Contains(handler.Cuerpos[0], "client_id=t1");
            StringAssert.Contains(handler.Cuerpos[0], "scope=urn%3Acom%3Actt-express%3Aintegration-clients%3Ascopes%3Acommon%2FALL");

            HttpRequestMessage manifiesto = handler.Peticiones[1];
            Assert.AreEqual("Bearer", manifiesto.Headers.Authorization.Scheme);
            Assert.AreEqual("TOKEN-1", manifiesto.Headers.Authorization.Parameter);
            Assert.AreEqual("application/json", manifiesto.Content.Headers.ContentType.MediaType);
            Assert.AreEqual(@"{""a"":1}", handler.Cuerpos[1]);

            // Auditoría: el token no se registra en claro; el manifiesto sí (petición y respuesta crudas).
            Assert.AreEqual("Token", registro.Intercambios[0].Operacion);
            Assert.IsFalse(registro.Intercambios[0].Respuesta.Contains("TOKEN-1"));
            Assert.AreEqual("Manifiesto", registro.Intercambios[1].Operacion);
            StringAssert.Contains(registro.Intercambios[1].Peticion, @"{""a"":1}");
            StringAssert.Contains(registro.Intercambios[1].Respuesta, "HTTP 201");
        }

        [TestMethod]
        public async Task SegundaLlamada_ReutilizaElToken_SinVolverAPedirlo()
        {
            var handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.OK, RESP_TOKEN);
            handler.Responder("trf/item-history-api/history/1", HttpStatusCode.OK, @"{""data"":{}}");
            var cliente = new ClienteRestCTT(Config("t2"), new HttpClient(handler));

            await cliente.EnviarAsync(HttpMethod.Get, "trf/item-history-api/history/1", null, "Seguimiento");
            await cliente.EnviarAsync(HttpMethod.Get, "trf/item-history-api/history/1", null, "Seguimiento");

            Assert.AreEqual(1, handler.Peticiones.Count(p => p.RequestUri.AbsolutePath.EndsWith("oauth2/token")), "CTT pide UN token al día");
            Assert.AreEqual(2, handler.Peticiones.Count(p => p.RequestUri.AbsolutePath.Contains("history")));
        }

        [TestMethod]
        public async Task Un401_InvalidaElToken_YRepiteUnaVezConTokenNuevo()
        {
            var handler = new HandlerFalso();
            handler.ResponderSecuencia("oauth2/token",
                (HttpStatusCode.OK, RESP_TOKEN),
                (HttpStatusCode.OK, RESP_TOKEN.Replace("TOKEN-1", "TOKEN-2")));
            handler.ResponderSecuencia("manifest/v1.0/rpc-cancel-shipping-by-shipping-code/1",
                (HttpStatusCode.Unauthorized, @"{""message"":""Unauthorized""}"),
                (HttpStatusCode.NoContent, string.Empty));
            var cliente = new ClienteRestCTT(Config("t3"), new HttpClient(handler));

            RespuestaCTT r = await cliente.EnviarAsync(HttpMethod.Post, "manifest/v1.0/rpc-cancel-shipping-by-shipping-code/1", new { }, "Anular");

            Assert.AreEqual(204, r.Codigo);
            Assert.AreEqual(2, handler.Peticiones.Count(p => p.RequestUri.AbsolutePath.EndsWith("oauth2/token")));
            Assert.AreEqual("TOKEN-2", handler.Peticiones.Last().Headers.Authorization.Parameter);
        }

        [TestMethod]
        public async Task Un4xx_NoLanza_LlegaComoRespuestaConElMotivo()
        {
            var handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.OK, RESP_TOKEN);
            handler.Responder("manifest/v1.0/rpc-cancel-shipping-by-shipping-code/1", HttpStatusCode.Forbidden,
                @"{""error"":{""error_code"":403,""error_description"":""The operation cannot be processed"",""error_extended_info"":{""message"":""Already cancelled shipping""},""translation_key"":""ALREADY_NULLED_SHIPPING""}}");
            var cliente = new ClienteRestCTT(Config("t4"), new HttpClient(handler));

            RespuestaCTT r = await cliente.EnviarAsync(HttpMethod.Post, "manifest/v1.0/rpc-cancel-shipping-by-shipping-code/1", new { }, "Anular");

            Assert.IsFalse(r.Exito);
            Assert.AreEqual("ALREADY_NULLED_SHIPPING", r.ClaveError);
            StringAssert.Contains(r.Error, "403");
            StringAssert.Contains(r.Error, "Already cancelled shipping");
        }

        [TestMethod]
        public async Task Un5xx_LanzaTransitoria_ParaQueLaPoliticaReintente()
        {
            var handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.OK, RESP_TOKEN);
            handler.Responder("manifest/v2.0/shippings", HttpStatusCode.BadGateway, "<html>502</html>");
            var cliente = new ClienteRestCTT(Config("t5"), new HttpClient(handler));

            try
            {
                await cliente.EnviarAsync(HttpMethod.Post, "manifest/v2.0/shippings", new { }, "Manifiesto");
                Assert.Fail("Debía lanzar");
            }
            catch (AgenciaRemotaException ex)
            {
                Assert.IsTrue(ex.EsTransitoria);
                StringAssert.Contains(ex.Message, "502");
            }
        }

        [TestMethod]
        public async Task SinCredenciales_LanzaConMensajeClaro_SinLlamarACTT()
        {
            var handler = new HandlerFalso();
            var cliente = new ClienteRestCTT(new ConfiguracionCTT("https://api-test.cttexpress.com/integrations/", "", "", "8090300001", new RemitenteCTT()), new HttpClient(handler));

            try
            {
                await cliente.EnviarAsync(HttpMethod.Get, "x", null, "X");
                Assert.Fail("Debía lanzar");
            }
            catch (AgenciaRemotaException ex)
            {
                Assert.IsFalse(ex.EsTransitoria);
                StringAssert.Contains(ex.Message, "CTTClientId");
            }
            Assert.AreEqual(0, handler.Peticiones.Count);
        }

        [TestMethod]
        public async Task TokenRechazado_LanzaNoTransitoria()
        {
            var handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.BadRequest, @"{""error"":""invalid_client""}");
            var cliente = new ClienteRestCTT(Config("t6"), new HttpClient(handler));

            try
            {
                await cliente.EnviarAsync(HttpMethod.Get, "x", null, "X");
                Assert.Fail("Debía lanzar");
            }
            catch (AgenciaRemotaException ex)
            {
                Assert.IsFalse(ex.EsTransitoria, "Credenciales malas: reintentar no arregla nada");
                StringAssert.Contains(ex.Message, "invalid_client");
            }
        }

        /// <summary>HttpMessageHandler de prueba: responde según el final de la ruta y captura peticiones y cuerpos.</summary>
        private class HandlerFalso : HttpMessageHandler
        {
            private readonly Dictionary<string, Queue<(HttpStatusCode codigo, string cuerpo)>> _respuestas = new Dictionary<string, Queue<(HttpStatusCode, string)>>();
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public readonly List<string> Cuerpos = new List<string>();

            public void Responder(string sufijoRuta, HttpStatusCode codigo, string cuerpo)
                => ResponderSecuencia(sufijoRuta, (codigo, cuerpo));

            public void ResponderSecuencia(string sufijoRuta, params (HttpStatusCode codigo, string cuerpo)[] respuestas)
            {
                var cola = new Queue<(HttpStatusCode, string)>();
                foreach (var r in respuestas) cola.Enqueue(r);
                _respuestas[sufijoRuta] = cola;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                Cuerpos.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());

                string ruta = request.RequestUri.AbsolutePath;
                KeyValuePair<string, Queue<(HttpStatusCode codigo, string cuerpo)>> entrada = _respuestas.FirstOrDefault(kv => ruta.EndsWith(kv.Key));
                if (entrada.Key == null)
                {
                    throw new InvalidOperationException("El test no preparó respuesta para " + ruta);
                }
                (HttpStatusCode codigo, string cuerpo) r = entrada.Value.Count > 1 ? entrada.Value.Dequeue() : entrada.Value.Peek();
                return new HttpResponseMessage(r.codigo) { Content = new StringContent(r.cuerpo ?? string.Empty) };
            }
        }
    }
}
