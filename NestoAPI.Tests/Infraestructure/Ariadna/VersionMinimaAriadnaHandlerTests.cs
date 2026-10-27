using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ariadna;
using NestoAPI.Infraestructure.Notificaciones;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Ariadna
{
    /// <summary>
    /// NestoAPI#657: versión mínima de Ariadna comprobada por la API. El 16/10/26 había PDA con la 2.6 estando
    /// publicada la 2.11 y una reposición se terminó sin bultos ni etiquetas. 426 solo en Activo; en Sombra (por
    /// defecto) ELMAH una vez por dispositivo y día; sin cabecera (Nesto, NestoApp, Ariadna viejas) nada.
    /// </summary>
    [TestClass]
    public class VersionMinimaAriadnaHandlerTests
    {
        private List<Exception> logueados;
        private DateTime hoy;
        private int llegaronAlControlador;

        /// <summary>El «controlador»: cuenta las peticiones que le llegan y contesta 200.</summary>
        private sealed class ControladorDePrueba : DelegatingHandler
        {
            private readonly Action alLlegar;
            public ControladorDePrueba(Action alLlegar) { this.alLlegar = alLlegar; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                alLlegar();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
            }
        }

        [TestInitialize]
        public void Setup()
        {
            logueados = new List<Exception>();
            hoy = new DateTime(2026, 10, 27);
            llegaronAlControlador = 0;
        }

        private HttpMessageInvoker Api(string versionMinima = "2.18", string modo = "Sombra")
        {
            var handler = new VersionMinimaAriadnaHandler(PoliticaVersionMinimaAriadna.Crear(versionMinima, modo), logueados.Add, () => hoy)
            {
                InnerHandler = new ControladorDePrueba(() => llegaronAlControlador++)
            };
            return new HttpMessageInvoker(handler);
        }

        private static HttpRequestMessage Peticion(string ruta, string versionAriadna = null, string dispositivo = null)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Get, "https://api.nuevavision.es/" + ruta);
            if (versionAriadna != null)
            {
                peticion.Headers.TryAddWithoutValidation(PoliticaVersionMinimaAriadna.CABECERA_VERSION, versionAriadna);
            }
            if (dispositivo != null)
            {
                peticion.Headers.TryAddWithoutValidation(AvisosAlmacen.CABECERA_DISPOSITIVO, dispositivo);
            }
            return peticion;
        }

        private static Task<HttpResponseMessage> Enviar(HttpMessageInvoker api, HttpRequestMessage peticion) =>
            api.SendAsync(peticion, CancellationToken.None);

        #region Activo: 426

        [TestMethod]
        public async Task Activo_VersionPorDebajoDeLaMinima_426ConMensajeDeUsted()
        {
            HttpResponseMessage respuesta = await Enviar(Api("2.18", "Activo"), Peticion("api/Reposiciones/80932", "2.6", "PDA-03"));

            Assert.AreEqual(426, (int)respuesta.StatusCode);
            Assert.AreEqual(0, llegaronAlControlador, "No llega al controlador");
            JObject cuerpo = JObject.Parse(await respuesta.Content.ReadAsStringAsync());
            Assert.AreEqual("ARIADNA_DESACTUALIZADA", (string)cuerpo["error"]["code"]);
            Assert.AreEqual("Su Ariadna (2.6) está desactualizada: actualícela desde Play Store para seguir.", (string)cuerpo["error"]["message"]);
            Assert.AreEqual("2.18", (string)cuerpo["error"]["versionMinima"]);
            Assert.AreEqual("application/json", respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual(1, logueados.Count, "También en Activo se apunta a quién se ha bloqueado");
            StringAssert.Contains(logueados.Single().Message, "PDA-03");
        }

        [TestMethod]
        public async Task Activo_VersionIgualOSuperior_Pasa()
        {
            HttpMessageInvoker api = Api("2.18", "Activo");

            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Almacen/Salidas", "2.18"))).StatusCode, "Igual que la mínima vale");
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Almacen/Salidas", "2.19"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Almacen/Salidas", "2.18.1"))).StatusCode);
            Assert.AreEqual(3, llegaronAlControlador);
            Assert.AreEqual(0, logueados.Count);
        }

        [TestMethod]
        public async Task Activo_ComparaComoVersionNoComoTexto()
        {
            // «2.9» < «2.18» aunque alfabéticamente «2.9» > «2.18»
            Assert.AreEqual(426, (int)(await Enviar(Api("2.18", "Activo"), Peticion("api/Recepciones", "2.9"))).StatusCode);
        }

        #endregion

        #region Sombra: pasa y apunta una vez por dispositivo y día

        [TestMethod]
        public async Task Sombra_VersionPorDebajo_200YUnSoloApunteEnElmahPorDispositivoYDia()
        {
            HttpMessageInvoker api = Api("2.18", "Sombra");

            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Reposiciones/80932", "2.6", "PDA-03"))).StatusCode);
            }

            Assert.AreEqual(5, llegaronAlControlador, "En sombra no se bloquea a nadie");
            Assert.AreEqual(1, logueados.Count, "Una vez por dispositivo y día, no en cada llamada");
            StringAssert.Contains(logueados.Single().Message, "Ariadna 2.6 por debajo de la mínima 2.18 (Sombra)");
            StringAssert.Contains(logueados.Single().Message, "PDA-03");
        }

        [TestMethod]
        public async Task Sombra_OtroDispositivoUOtroDia_SeApuntaAparte()
        {
            HttpMessageInvoker api = Api("2.18", "Sombra");

            await Enviar(api, Peticion("api/Reposiciones", "2.6", "PDA-03"));
            await Enviar(api, Peticion("api/Reposiciones", "2.6", "PDA-07"));
            await Enviar(api, Peticion("api/Reposiciones", "2.6", "PDA-03"));
            Assert.AreEqual(2, logueados.Count, "Dos PDA, dos apuntes");

            hoy = hoy.AddDays(1);
            await Enviar(api, Peticion("api/Reposiciones", "2.6", "PDA-03"));
            Assert.AreEqual(3, logueados.Count, "Al día siguiente se vuelve a apuntar");
        }

        [TestMethod]
        public async Task Sombra_SinDispositivo_SeApuntaPorUsuario()
        {
            HttpMessageInvoker api = Api("2.18", "Sombra");

            await Enviar(api, Peticion("api/Reposiciones", "2.6"));
            await Enviar(api, Peticion("api/Reposiciones", "2.6"));

            Assert.AreEqual(1, logueados.Count);
            StringAssert.Contains(logueados.Single().Message, "Dispositivo: desconocido");
        }

        [TestMethod]
        public async Task Sombra_EsElModoPorDefecto_SinClaveOConValorRaro()
        {
            Assert.AreEqual(ModoVersionMinima.Sombra, PoliticaVersionMinimaAriadna.LeerModo(null));
            Assert.AreEqual(ModoVersionMinima.Sombra, PoliticaVersionMinimaAriadna.LeerModo(""));
            Assert.AreEqual(ModoVersionMinima.Sombra, PoliticaVersionMinimaAriadna.LeerModo("Bloquear"));
            Assert.AreEqual(ModoVersionMinima.Activo, PoliticaVersionMinimaAriadna.LeerModo(" activo "));

            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(Api("2.18", null), Peticion("api/Reposiciones", "2.6"))).StatusCode);
        }

        #endregion

        #region Sin cabecera: nadie se ve afectado

        [TestMethod]
        public async Task SinCabecera_PasaSinApuntarNada_InclusoEnActivo()
        {
            HttpMessageInvoker api = Api("2.18", "Activo");

            // Nesto (escritorio), NestoApp y una Ariadna anterior a la issue gemela: ninguna manda la cabecera
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/PedidosVenta?empresa=1"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Reposiciones/80932"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Novedades?ambito=NestoApp"))).StatusCode);

            Assert.AreEqual(3, llegaronAlControlador);
            Assert.AreEqual(0, logueados.Count);
        }

        [TestMethod]
        public async Task CabeceraQueNoSeEntiende_Pasa()
        {
            HttpMessageInvoker api = Api("2.18", "Activo");

            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Reposiciones", "beta"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Reposiciones", ""))).StatusCode);
            Assert.AreEqual(0, logueados.Count);
        }

        [TestMethod]
        public async Task SinMinimaConfigurada_Pasa()
        {
            HttpMessageInvoker api = Api(null, "Activo");

            Assert.AreEqual(HttpStatusCode.OK, (await Enviar(api, Peticion("api/Reposiciones", "2.6"))).StatusCode);
            Assert.AreEqual(0, logueados.Count);
        }

        #endregion

        #region La política

        [TestMethod]
        public void Parsear_VersionesDeAriadna()
        {
            Assert.AreEqual(new Version(2, 6), PoliticaVersionMinimaAriadna.Parsear("2.6"));
            Assert.AreEqual(new Version(2, 18, 1), PoliticaVersionMinimaAriadna.Parsear(" 2.18.1 "));
            Assert.AreEqual(new Version(3, 0), PoliticaVersionMinimaAriadna.Parsear("3"), "Una sola cifra también");
            Assert.IsNull(PoliticaVersionMinimaAriadna.Parsear(null));
            Assert.IsNull(PoliticaVersionMinimaAriadna.Parsear("2.x"));
        }

        [TestMethod]
        public void EstaDesactualizada_SoloConMinimaYVersionQueSeEntiende()
        {
            PoliticaVersionMinimaAriadna politica = PoliticaVersionMinimaAriadna.Crear("2.18", "Activo");

            Assert.IsTrue(politica.EstaDesactualizada("2.6", out Version v));
            Assert.AreEqual(new Version(2, 6), v);
            Assert.IsFalse(politica.EstaDesactualizada("2.18", out _));
            Assert.IsFalse(politica.EstaDesactualizada(null, out _));
            Assert.IsFalse(politica.EstaDesactualizada("rara", out _));
            Assert.IsFalse(PoliticaVersionMinimaAriadna.Crear("", "Activo").EstaDesactualizada("1.0", out _), "Sin mínima no hay desactualizadas");
        }

        [TestMethod]
        public void Mensaje_DeUsted()
        {
            Assert.AreEqual("Su Ariadna (2.6) está desactualizada: actualícela desde Play Store para seguir.",
                PoliticaVersionMinimaAriadna.Mensaje(new Version(2, 6)));
        }

        #endregion
    }
}
