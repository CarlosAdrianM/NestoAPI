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
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#726: la Management API de CTT (parar en vuelo, retorno, fecha y dirección de entrega) con el cliente HTTP
    /// real sobre un HttpMessageHandler falso: rutas, cuerpos y respuestas tal cual salieron en el sandbox el 05/11/26.
    /// </summary>
    [TestClass]
    public class GestionEnTransitoCTTTests
    {
        private const string ALBARAN = "0082800082809800827793";
        private const string BASE = "https://api-test.cttexpress.com/integrations/";
        private const string RUTA = "/integrations/trf/v1.0/management/v1/shippings/8090300001/" + ALBARAN + "/";
        private const string RESP_TOKEN = @"{""access_token"":""TOKEN-726"",""expires_in"":86400,""token_type"":""Bearer""}";
        private const string RESP_403 = @"{""error"":{""error_code"":403,""error_type"":""FORBIDDEN_DUE_RESOURCE_STATUS"",""error_description"":""The operation cannot be processed considering the current status of the resource"",""error_extended_info"":{""message"":""Can not perform action STOP_ON_THE_FLY over shipping " + ALBARAN + @""",""details"":[]},""translation_key"":null}}";

        private HandlerFalso handler;
        private AgenciaRemotaCTT agencia;

        [TestInitialize]
        public void Setup()
        {
            handler = new HandlerFalso();
            handler.Responder("oauth2/token", HttpStatusCode.OK, RESP_TOKEN);
            // Cada test, su credencial: el token se cachea por proceso y por credencial.
            var config = new ConfiguracionCTT(BASE, "gestion726-" + Guid.NewGuid().ToString("N"), "secreto", "8090300001", new RemitenteCTT());
            agencia = new AgenciaRemotaCTT(new ClienteRestCTT(config, new HttpClient(handler)), config);
        }

        private HttpRequestMessage PeticionGestion() => handler.Peticiones.Single(p => !p.RequestUri.AbsolutePath.EndsWith("oauth2/token"));
        private string CuerpoGestion() => handler.Cuerpos[handler.Peticiones.IndexOf(PeticionGestion())];

        [TestMethod]
        public async Task PararEnVuelo_PostSinCuerpo_ALaRutaDelCentro()
        {
            handler.Responder("rpc-stop-on-the-fly", HttpStatusCode.NoContent, string.Empty);

            ResultadoOperacionRemota r = await agencia.PararEnVueloAsync(ALBARAN + " ");

            Assert.IsTrue(r.Exito);
            HttpRequestMessage p = PeticionGestion();
            Assert.AreEqual(HttpMethod.Post, p.Method);
            Assert.AreEqual(RUTA + "rpc-stop-on-the-fly", p.RequestUri.AbsolutePath);
            Assert.IsNull(p.Content, "Sin cuerpo");
            Assert.AreEqual("TOKEN-726", p.Headers.Authorization.Parameter);
        }

        [TestMethod]
        public async Task CrearRetorno_SinCuerpoNiContentType_DevuelveElReverseShippingCode()
        {
            handler.Responder("rpc-create-reverse-shipping", HttpStatusCode.OK, @"{""reverse_shipping_code"":""0082800031029800000086""}");

            string codigo = await agencia.CrearRetornoAsync(ALBARAN);

            Assert.AreEqual("0082800031029800000086", codigo);
            HttpRequestMessage p = PeticionGestion();
            Assert.AreEqual(RUTA + "rpc-create-reverse-shipping", p.RequestUri.AbsolutePath);
            Assert.IsNull(p.Content, "CTT lo quiere sin cuerpo y sin Content-Type");
        }

        [TestMethod]
        public async Task CrearRetorno_RespuestaSinCodigo_LanzaExcepcion()
        {
            handler.Responder("rpc-create-reverse-shipping", HttpStatusCode.OK, "{}");

            AgenciaRemotaException ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => agencia.CrearRetornoAsync(ALBARAN));

            StringAssert.Contains(ex.Message, "reverse_shipping_code");
        }

        [TestMethod]
        public async Task CambiarFecha_MandaNewDeliveryDate()
        {
            handler.Responder("rpc-change-delivery-options", HttpStatusCode.Accepted, string.Empty);

            ResultadoOperacionRemota r = await agencia.CambiarFechaEntregaAsync(ALBARAN, new DateTime(2026, 11, 6, 15, 30, 0));

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(RUTA + "rpc-change-delivery-options", PeticionGestion().RequestUri.AbsolutePath);
            Assert.AreEqual(@"{""new_delivery_date"":""2026-11-06""}", CuerpoGestion());
            Assert.AreEqual("application/json", PeticionGestion().Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task CambiarDireccion_MandaElObjetoNewDeliveryAddressCompleto()
        {
            handler.Responder("rpc-change-delivery-address", HttpStatusCode.NoContent, string.Empty);

            ResultadoOperacionRemota r = await agencia.CambiarDireccionEntregaAsync(ALBARAN, new DireccionEntregaRemota
            {
                Nombre = "TEST GRABACION ENVIOS",
                Direccion = "Calle Estafeta 1",
                CodigoPostal = "31001",
                Poblacion = "Pamplona",
                Telefono = "+34999999999",
                Correo = "pruebas@example.com"
            });

            Assert.IsTrue(r.Exito);
            Assert.AreEqual(RUTA + "rpc-change-delivery-address", PeticionGestion().RequestUri.AbsolutePath);
            JObject esperado = JObject.Parse(@"{""new_delivery_address"":{""recipient_name"":""TEST GRABACION ENVIOS"",""recipient_address"":""Calle Estafeta 1"",""recipient_postal_code"":""31001"",""recipient_town"":""Pamplona"",""recipient_country_code"":""ES"",""recipient_phones"":[""+34999999999""],""recipient_email"":""pruebas@example.com""}}");
            Assert.IsTrue(JToken.DeepEquals(esperado, JObject.Parse(CuerpoGestion())), CuerpoGestion());
        }

        [TestMethod]
        public async Task CambiarDireccion_SinTelefonoNiCorreo_ArgumentExceptionSinLlamarACtt()
        {
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => agencia.CambiarDireccionEntregaAsync(ALBARAN, new DireccionEntregaRemota
            {
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodigoPostal = "28001",
                Poblacion = "Madrid"
            }));

            StringAssert.Contains(ex.Message, "el teléfono y el correo electrónico");
            Assert.AreEqual(0, handler.Peticiones.Count, "No se llega a pedir ni el token");
        }

        [TestMethod]
        public async Task ErrorDeCtt_AgenciaRemotaExceptionConElMensajeDeCtt()
        {
            handler.Responder("rpc-stop-on-the-fly", HttpStatusCode.Forbidden, RESP_403);

            AgenciaRemotaException ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => agencia.PararEnVueloAsync(ALBARAN));

            StringAssert.Contains(ex.Message, "Can not perform action STOP_ON_THE_FLY");
            StringAssert.Contains(ex.Message, "no lo permite en el estado actual");
            Assert.IsFalse(ex.EsTransitoria);
        }

        [TestMethod]
        public async Task ErrorDeEsquema_400_LlevaElMotivo()
        {
            handler.Responder("rpc-change-delivery-options", HttpStatusCode.BadRequest, @"{""error"":{""error_code"":400,""error_extended_info"":{""message"":""/new_delivery_date bad format""}}}");

            AgenciaRemotaException ex = await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => agencia.CambiarFechaEntregaAsync(ALBARAN, new DateTime(2026, 11, 6)));

            StringAssert.Contains(ex.Message, "/new_delivery_date bad format");
            StringAssert.Contains(ex.Message, "CTT respondió 400");
        }

        // ---------- NestoAPI#726 (06/11/26): el camino de producción ----------
        // En producción la fábrica no da AgenciaRemotaCTT sino su decorador de reintentos (PerfilAgenciaCTT.Componer). El
        // decorador de antes solo exponía IAgenciaRemota e ISeguimientoPorLotes, el «as IGestionEnTransitoAgenciaRemota»
        // daba null y Gestiones contestaba «La agencia CTT no tiene gestión en tránsito» con los envíos reales.

        [TestMethod]
        public void ComposicionDeProduccion_ConservaLaGestionEnTransitoYLaEtiquetaPdf()
        {
            IAgenciaRemota produccion = NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.Componer(
                new ConfiguracionCTT(BASE, "gestion726-" + Guid.NewGuid().ToString("N"), "secreto", "8090300001", new RemitenteCTT()));

            Assert.IsInstanceOfType(produccion, typeof(IGestionEnTransitoAgenciaRemota), "Gestiones lo comprueba con «as»");
            Assert.IsInstanceOfType(produccion, typeof(IEtiquetaPdfRemota), "La etiqueta PDF de las recogidas (#494) también");
            Assert.IsInstanceOfType(produccion, typeof(ISeguimientoPorLotes), "El poll sigue por lotes");
        }

        [TestMethod]
        public void ComposicionDeProduccion_GestionesDeUnEnvioTramitadoDeCtt_AdmiteCambiarLaFecha()
        {
            IAgenciaRemota produccion = NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.Componer(
                new ConfiguracionCTT(BASE, "gestion726-" + Guid.NewGuid().ToString("N"), "secreto", "8090300001", new RemitenteCTT()));
            var envio = new NestoAPI.Models.EnviosAgencia
            {
                Numero = 250655,
                Agencia = NestoAPI.Models.Constantes.Agencias.AGENCIA_CTT,
                Estado = NestoAPI.Models.Constantes.Agencias.ESTADO_TRAMITADO,
                CodigoBarras = ALBARAN
            };

            GestionesEnvioDTO gestiones = GestionEnTransitoEnvio.Calcular(envio, produccion as IGestionEnTransitoAgenciaRemota, "CTT");

            Assert.IsTrue(gestiones.CambiarFecha);
            Assert.IsTrue(gestiones.PararEnVuelo);
            Assert.IsTrue(gestiones.CambiarDireccion);
        }

        [TestMethod]
        public async Task Decorador_ReenviaLaGestionACtt()
        {
            handler.Responder("rpc-stop-on-the-fly", HttpStatusCode.NoContent, string.Empty);
            handler.Responder("rpc-change-delivery-options", HttpStatusCode.Accepted, string.Empty);
            IGestionEnTransitoAgenciaRemota decorada = new AgenciaRemotaCTTConReintentos(agencia);

            Assert.IsTrue((await decorada.PararEnVueloAsync(ALBARAN)).Exito);
            Assert.IsTrue((await decorada.CambiarFechaEntregaAsync(ALBARAN, new DateTime(2026, 11, 9))).Exito);

            Assert.IsTrue(handler.Peticiones.Any(p => p.RequestUri.AbsolutePath == RUTA + "rpc-stop-on-the-fly"));
            Assert.IsTrue(handler.Peticiones.Any(p => p.RequestUri.AbsolutePath == RUTA + "rpc-change-delivery-options"));
        }

        [TestMethod]
        public async Task Decorador_ParadaEnVueloQueFallaPorTransitorio_NoSeReintenta()
        {
            handler.Responder("rpc-stop-on-the-fly", HttpStatusCode.ServiceUnavailable, "{}");
            IGestionEnTransitoAgenciaRemota decorada = new AgenciaRemotaCTTConReintentos(agencia);

            await Assert.ThrowsExceptionAsync<AgenciaRemotaException>(() => decorada.PararEnVueloAsync(ALBARAN));

            Assert.AreEqual(1, handler.Peticiones.Count(p => p.RequestUri.AbsolutePath.EndsWith("rpc-stop-on-the-fly")),
                "No idempotente: tras un fallo CTT puede haberlo hecho ya");
        }

        private class HandlerFalso : HttpMessageHandler
        {
            private readonly Dictionary<string, (HttpStatusCode codigo, string cuerpo)> _respuestas = new Dictionary<string, (HttpStatusCode, string)>();
            public readonly List<HttpRequestMessage> Peticiones = new List<HttpRequestMessage>();
            public readonly List<string> Cuerpos = new List<string>();

            public void Responder(string sufijoRuta, HttpStatusCode codigo, string cuerpo) => _respuestas[sufijoRuta] = (codigo, cuerpo);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Peticiones.Add(request);
                Cuerpos.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync());
                string ruta = request.RequestUri.AbsolutePath;
                KeyValuePair<string, (HttpStatusCode codigo, string cuerpo)> r = _respuestas.FirstOrDefault(kv => ruta.EndsWith(kv.Key));
                if (r.Key == null)
                {
                    throw new InvalidOperationException("El test no preparó respuesta para " + ruta);
                }
                return new HttpResponseMessage(r.Value.codigo) { Content = new StringContent(r.Value.cuerpo ?? string.Empty) };
            }
        }
    }
}
