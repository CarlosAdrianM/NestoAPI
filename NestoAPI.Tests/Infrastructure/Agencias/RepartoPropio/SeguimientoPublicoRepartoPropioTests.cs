using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#744: la página pública de seguimiento del reparto propio (agencia 14): el token firmado (no enumerable), la
    /// estrategia que lo convierte en el enlace de todos los canales (correo, aviso de incidencia, TNV/NestoApp, Nesto) y la
    /// página (entregado, incidencia, en reparto, token malo → 404), que nunca enseña el DNI, la firma ni la ubicación.
    /// </summary>
    [TestClass]
    public class SeguimientoPublicoRepartoPropioTests
    {
        private const string CLAVE = "clave-de-pruebas-del-seguimiento-reparto-propio-744";
        private const int ENVIO = 123456;
        private const int PEDIDO = 928001;
        private const string DNI = "12345678Z";
        private Func<string> claveOriginal;
        private Func<string> urlOriginal;

        [TestInitialize]
        public void Preparar()
        {
            claveOriginal = EnlaceSeguimientoRepartoPropio.LeerClave;
            urlOriginal = EnlaceSeguimientoRepartoPropio.LeerUrlBase;
            EnlaceSeguimientoRepartoPropio.LeerClave = () => CLAVE;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = () => "https://api.nuevavision.es";
        }

        [TestCleanup]
        public void Restaurar()
        {
            EnlaceSeguimientoRepartoPropio.LeerClave = claveOriginal;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = urlOriginal;
        }

        // ===================================================================== token

        [TestMethod]
        public void Token_EsEstableYSeLeeDeVuelta()
        {
            string token = EnlaceSeguimientoRepartoPropio.Token(ENVIO);

            Assert.AreEqual(token, EnlaceSeguimientoRepartoPropio.Token(ENVIO), "el mismo envío, el mismo enlace (sin tabla)");
            Assert.IsTrue(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(token, out int envio));
            Assert.AreEqual(ENVIO, envio);
        }

        [TestMethod]
        public void Token_EsOpaco_NoLlevaElNumeroDeEnvioNiElPedidoAPelo()
        {
            string token = EnlaceSeguimientoRepartoPropio.Token(ENVIO);

            StringAssert.Matches(token, new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_-]{27}$"));
            Assert.IsFalse(token.Contains(ENVIO.ToString()));
            Assert.AreNotEqual(token, EnlaceSeguimientoRepartoPropio.Token(ENVIO + 1));
        }

        [TestMethod]
        public void Token_Manipulado_NoValida()
        {
            string token = EnlaceSeguimientoRepartoPropio.Token(ENVIO);
            // Cambiar un carácter de la firma
            char ultimo = token[20];
            string manipulado = token.Substring(0, 20) + (ultimo == 'A' ? 'B' : 'A') + token.Substring(21);

            Assert.IsFalse(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(manipulado, out int envio));
            Assert.AreEqual(0, envio);
        }

        [TestMethod]
        public void Token_DeOtroEnvioConLaFirmaDeEste_NoValida()
        {
            // Enumerar: poner el número de otro envío delante de una firma buena
            byte[] bytes = Convert.FromBase64String(EnlaceSeguimientoRepartoPropio.Token(ENVIO).Replace('-', '+').Replace('_', '/') + "=");
            bytes[3] ^= 0x01;
            string otro = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            Assert.IsFalse(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(otro, out _));
        }

        [TestMethod]
        public void Token_FirmadoConOtraClave_NoValida()
        {
            string ajeno = EnlaceSeguimientoRepartoPropio.Token("otra-clave-de-pruebas-que-no-es-la-buena-744", ENVIO);

            Assert.IsFalse(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(ajeno, out _));
        }

        [TestMethod]
        public void Token_Basura_NoValidaNiRevienta()
        {
            foreach (string basura in new[] { null, "", "   ", "123456", "RP123456", "%%%", "a", "ñññññññññññññññññññññññññññ", new string('A', 27) })
            {
                Assert.IsFalse(EnlaceSeguimientoRepartoPropio.TryLeerEnvio(basura, out _), basura ?? "(null)");
            }
        }

        [TestMethod]
        public void Token_SinClave_NoHayEnlace()
        {
            EnlaceSeguimientoRepartoPropio.LeerClave = () => null;

            Assert.IsNull(EnlaceSeguimientoRepartoPropio.Token(ENVIO));
            Assert.IsNull(EnlaceSeguimientoRepartoPropio.Url(ENVIO));
        }

        // ===================================================================== estrategia

        [TestMethod]
        public void Estrategia_RegistradaParaLaAgencia14ConSuNombre()
        {
            IEstrategiaSeguimientoAgencia estrategia = RegistroSeguimientoAgencias.Obtener("Reparto propio");

            Assert.IsNotNull(estrategia);
            CollectionAssert.AreEqual(new[] { Constantes.Agencias.AGENCIA_REPARTO_PROPIO }, new List<int>(estrategia.AgenciasId));
        }

        [TestMethod]
        public void Estrategia_ConstruyeLaUrlDeLaPaginaConElToken()
        {
            string url = RegistroSeguimientoAgencias.ConstruirUrl(new DatosSeguimientoEnvio { AgenciaNombre = "Reparto propio", CodigoSeguimiento = "RP123456 " });

            Assert.AreEqual("https://api.nuevavision.es/seguimiento/" + EnlaceSeguimientoRepartoPropio.Token(ENVIO), url);
        }

        [TestMethod]
        public void Estrategia_DesdeElCodigoDeUnBulto_ElMismoEnlace()
        {
            string url = RegistroSeguimientoAgencias.ConstruirUrl(new DatosSeguimientoEnvio { AgenciaNombre = "Reparto propio", CodigoSeguimiento = "RP123456-2" });

            Assert.AreEqual("https://api.nuevavision.es/seguimiento/" + EnlaceSeguimientoRepartoPropio.Token(ENVIO), url);
        }

        [TestMethod]
        public void Estrategia_SinAlbaranDelRepartoPropio_Null()
        {
            Assert.IsNull(RegistroSeguimientoAgencias.ConstruirUrl(new DatosSeguimientoEnvio { AgenciaNombre = "Reparto propio", CodigoSeguimiento = null }));
            Assert.IsNull(RegistroSeguimientoAgencias.ConstruirUrl(new DatosSeguimientoEnvio { AgenciaNombre = "Reparto propio", CodigoSeguimiento = "0082800082809772536836" }));
        }

        [TestMethod]
        public void EnvioAgenciaDTO_YaNoDaElTextoDeError_DaElEnlace()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Reparto propio", CodigoBarras = "RP123456", Pedido = PEDIDO };

            StringAssert.StartsWith(dto.EnlaceSeguimiento, "https://api.nuevavision.es/seguimiento/");
        }

        [TestMethod]
        public void EnvioAgenciaDTO_CanalesExternos_NullNuncaElTextoDeError()
        {
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "Reparto propio", CodigoBarras = "RP123456" };

            Assert.IsNull(dto.TransportistaPrestashop);
            Assert.IsNull(dto.TrackingPrestashop);
            Assert.IsNull(dto.CarrierCodeAmazon);
            Assert.IsNull(dto.CarrierNameAmazon);
            Assert.IsNull(dto.ShippingMethodAmazon);
        }

        [TestMethod]
        public void UltimoEnvioClienteDTO_TieneUrlDeSeguimiento()
        {
            var dto = new UltimoEnvioClienteDTO { AgenciaNombre = "Reparto propio", NumeroSeguimiento = "RP123456", Pedido = PEDIDO };

            Assert.AreEqual(EnlaceSeguimientoRepartoPropio.Url(ENVIO), dto.UrlSeguimiento);
        }

        [TestMethod]
        public void AvisoDeIncidencia_LlevaElEnlace()
        {
            var envio = new EnviosAgencia { Numero = ENVIO, CodigoBarras = "RP123456  ", Pedido = PEDIDO, Cliente = "15191" };

            Assert.AreEqual(EnlaceSeguimientoRepartoPropio.Url(ENVIO), AvisadorIncidenciasEnvio.EnlaceSeguimiento(envio, "Reparto propio", null));
        }

        [TestMethod]
        public void CorreoDeSalida_ParrafoConEnlaceANuestraPagina()
        {
            string parrafo = GestorEnviosAgencia.ParrafoSeguimientoRepartoPropio(EnlaceSeguimientoRepartoPropio.Url(ENVIO), "RP123456");

            StringAssert.Contains(parrafo, "href=\"https://api.nuevavision.es/seguimiento/");
            StringAssert.Contains(parrafo, "RP123456");
            Assert.IsFalse(parrafo.Contains("error"));
        }

        [TestMethod]
        public void CorreoDeSalida_SinEnlace_SoloElNumero()
        {
            string parrafo = GestorEnviosAgencia.ParrafoSeguimientoRepartoPropio(string.Empty, "RP123456");

            Assert.IsFalse(parrafo.Contains("href"));
            StringAssert.Contains(parrafo, "RP123456");
        }

        // ===================================================================== página

        private static EnviosAgencia Envio(short estado, int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO) => new EnviosAgencia
        {
            Numero = ENVIO,
            Agencia = agencia,
            Pedido = PEDIDO,
            Bultos = 3,
            Estado = estado,
            Fecha = new DateTime(2026, 10, 29),
            Nombre = "PELUQUERÍA SECRETA",
            Direccion = "C/ Escondida 1",
            Telefono = "600111222",
            Email = "cliente@ejemplo.com",
            Observaciones = "Dejar en la portería",
            CodigoBarras = "RP123456"
        };

        private static async Task<PaginaSeguimientoReparto> Pagina(EnviosAgencia envio, List<HitoEntregaPublico> hitos, string token = null)
        {
            var lector = A.Fake<ILectorSeguimientoPublicoReparto>();
            _ = A.CallTo(() => lector.LeerEnvio(ENVIO)).Returns(Task.FromResult(envio));
            _ = A.CallTo(() => lector.LeerHitos(ENVIO)).Returns(Task.FromResult(hitos ?? new List<HitoEntregaPublico>()));
            return await new ServicioSeguimientoPublicoReparto(lector).Pagina(token ?? EnlaceSeguimientoRepartoPropio.Token(ENVIO));
        }

        [TestMethod]
        public async Task Pagina_Entregado_FechaHoraYQuienLoRecogio()
        {
            var hitos = new List<HitoEntregaPublico>
            {
                new HitoEntregaPublico { Tipo = "Incidencia", FechaHora = new DateTime(2026, 10, 29, 10, 5, 0), Motivo = IncidenciaRepartoDTO.AUSENTE },
                new HitoEntregaPublico { Tipo = "Entrega", FechaHora = new DateTime(2026, 10, 29, 12, 45, 0), Receptor = "  María López " }
            };

            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_ENTREGADO), hitos);

            Assert.AreEqual(HttpStatusCode.OK, pagina.Estado);
            StringAssert.Contains(pagina.Html, "<h1>Entregado</h1>");
            StringAssert.Contains(pagina.Html, "jueves, 29 de octubre de 2026, 12:45");
            StringAssert.Contains(pagina.Html, "Lo recogió <strong>María López</strong>");
            StringAssert.Contains(pagina.Html, PEDIDO.ToString());
            StringAssert.Contains(pagina.Html, "<dd>3</dd>");
            StringAssert.Contains(pagina.Html, "No había nadie en la dirección de entrega.", "la historia lleva la incidencia anterior");
            Assert.IsTrue(pagina.Html.IndexOf("12:45") < pagina.Html.IndexOf("10:05"), "lo más reciente arriba");
        }

        [TestMethod]
        public async Task Pagina_Incidencia_MotivoLegibleYContacto()
        {
            var hitos = new List<HitoEntregaPublico>
            {
                new HitoEntregaPublico { Tipo = "Incidencia", FechaHora = new DateTime(2026, 10, 29, 11, 0, 0), Motivo = IncidenciaRepartoDTO.DIRECCION_INCORRECTA }
            };

            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_INCIDENTADO), hitos);

            Assert.AreEqual(HttpStatusCode.OK, pagina.Estado);
            StringAssert.Contains(pagina.Html, "Incidencia en la entrega");
            StringAssert.Contains(pagina.Html, "La dirección de entrega no es correcta o está incompleta.");
            Assert.IsFalse(pagina.Html.Contains("DireccionIncorrecta"), "nunca el código de Ariadna");
            StringAssert.Contains(pagina.Html, "contacto destacado");
            StringAssert.Contains(pagina.Html, "tel:+34916281914");
            StringAssert.Contains(pagina.Html, "mailto:" + Constantes.Correos.LOGISTICA);
        }

        [TestMethod]
        public async Task Pagina_EnReparto()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_TRAMITADO), null);

            Assert.AreEqual(HttpStatusCode.OK, pagina.Estado);
            StringAssert.Contains(pagina.Html, "<h1>En reparto</h1>");
            StringAssert.Contains(pagina.Html, "Preparado para el reparto");
            Assert.IsFalse(pagina.Html.Contains("Lo recogió"));
            Assert.IsFalse(pagina.Html.Contains("contacto destacado"));
        }

        [TestMethod]
        public async Task Pagina_Pendiente_SinHistoria()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio((short)Constantes.Agencias.ESTADO_EN_CURSO), null);

            StringAssert.Contains(pagina.Html, "Preparando su pedido");
            Assert.IsFalse(pagina.Html.Contains("Historia del envío"));
        }

        [TestMethod]
        public async Task Pagina_EsMovilYClaroOscuro()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_TRAMITADO), null);

            StringAssert.Contains(pagina.Html, "name=\"viewport\"");
            StringAssert.Contains(pagina.Html, "prefers-color-scheme:dark");
            StringAssert.Contains(pagina.Html, "noindex");
            StringAssert.Contains(pagina.Html, "Nueva Visión");
        }

        [TestMethod]
        public async Task Pagina_NuncaEnsenaDniFirmaUbicacionNiDatosDelCliente()
        {
            // Aunque el lector devolviera más de la cuenta, la clase del hito no tiene dónde llevarlos; el envío sí tiene
            // dirección, teléfono, correo y observaciones, y no deben salir.
            var hitos = new List<HitoEntregaPublico>
            {
                new HitoEntregaPublico { Tipo = "Entrega", FechaHora = new DateTime(2026, 10, 29, 12, 45, 0), Receptor = "María López" }
            };

            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_ENTREGADO), hitos);

            foreach (string prohibido in new[] { DNI, "Dni", "DNI", "Firma", "firma", "Latitud", "Longitud", "data:image",
                "C/ Escondida", "600111222", "cliente@ejemplo.com", "portería", "PELUQUERÍA SECRETA", "RP123456" })
            {
                Assert.IsFalse(pagina.Html.Contains(prohibido), prohibido);
            }
            foreach (string propiedad in new[] { "DniReceptor", "Firma", "TieneFirma", "Latitud", "Longitud", "Texto", "ImporteCobrado", "CobroReembolso" })
            {
                Assert.IsNull(typeof(HitoEntregaPublico).GetProperty(propiedad), "el hito público no puede llevar " + propiedad);
            }
        }

        [TestMethod]
        public async Task Pagina_TokenInvalido_404Amable_SinLeerLaBD()
        {
            var lector = A.Fake<ILectorSeguimientoPublicoReparto>();

            PaginaSeguimientoReparto pagina = await new ServicioSeguimientoPublicoReparto(lector).Pagina("esto-no-es-un-token");

            Assert.AreEqual(HttpStatusCode.NotFound, pagina.Estado);
            StringAssert.Contains(pagina.Html, "No encontramos este envío");
            A.CallTo(() => lector.LeerEnvio(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pagina_EnvioQueNoEsDelRepartoPropio_404()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(Constantes.Agencias.ESTADO_ENTREGADO, agencia: Constantes.Agencias.AGENCIA_CTT), null);

            Assert.AreEqual(HttpStatusCode.NotFound, pagina.Estado);
        }

        [TestMethod]
        public async Task Pagina_EnvioQueNoExiste_404()
        {
            PaginaSeguimientoReparto pagina = await Pagina(null, null);

            Assert.AreEqual(HttpStatusCode.NotFound, pagina.Estado);
        }

        [TestMethod]
        public async Task Controller_Devuelve404ConHtml_Y200ConCabecerasSinCache()
        {
            var lector = A.Fake<ILectorSeguimientoPublicoReparto>();
            _ = A.CallTo(() => lector.LeerEnvio(ENVIO)).Returns(Task.FromResult(Envio(Constantes.Agencias.ESTADO_TRAMITADO)));
            _ = A.CallTo(() => lector.LeerHitos(ENVIO)).Returns(Task.FromResult(new List<HitoEntregaPublico>()));
            var controller = new SeguimientoController(new ServicioSeguimientoPublicoReparto(lector));

            HttpResponseMessage malo = await controller.GetSeguimiento("xxx");
            HttpResponseMessage bueno = await controller.GetSeguimiento(EnlaceSeguimientoRepartoPropio.Token(ENVIO));

            Assert.AreEqual(HttpStatusCode.NotFound, malo.StatusCode);
            Assert.AreEqual("text/html", malo.Content.Headers.ContentType.MediaType);
            StringAssert.Contains(await malo.Content.ReadAsStringAsync(), "No encontramos este envío");
            Assert.AreEqual(HttpStatusCode.OK, bueno.StatusCode);
            Assert.IsTrue(bueno.Headers.CacheControl.NoStore);
            StringAssert.Contains(await bueno.Content.ReadAsStringAsync(), "En reparto");
        }
    }
}
