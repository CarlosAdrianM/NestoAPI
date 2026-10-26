using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Sugerencia 648: POST api/Informes/FacturacionTienda/Correo manda el Excel ADJUNTO al correo del usuario (Nesto corre en el
    /// escritorio remoto, donde Office no tiene licencia). Mismos permisos y misma semana por defecto que GET /Excel.
    /// </summary>
    [TestClass]
    public class InformesControllerCorreoFacturacionTiendaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 27, 10, 0, 0); // martes: semana anterior 19-25/10
        private IInformesService servicio;
        private IServicioCorreoElectronico servicioCorreo;
        private CorreoCapturado enviado;
        private bool resultadoEnvio;

        private sealed class CorreoCapturado
        {
            public string De { get; set; }
            public List<string> Para { get; set; }
            public string Asunto { get; set; }
            public string Cuerpo { get; set; }
            public string NombreAdjunto { get; set; }
            public string TipoAdjunto { get; set; }
            public byte[] Adjunto { get; set; }
            public int Adjuntos { get; set; }
        }

        private readonly Dictionary<string, string> almacenes = new Dictionary<string, string>
        {
            ["paloma"] = "ALC",
            ["Pilar"] = "REI",
            ["Carlos"] = "ALG",
            ["SinCorreo"] = "ALC"
        };

        private readonly Dictionary<string, string> correos = new Dictionary<string, string>
        {
            ["paloma"] = "paloma@nuevavision.es ",
            ["Pilar"] = "pilaralvarez@nuevavision.es",
            ["Carlos"] = "carlosadrian@nuevavision.es"
        };

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string t, DateTime d, DateTime h) => Task.FromResult(GestorFacturacionTienda.Construir(null, t, d, h)));
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            resultadoEnvio = true;
            enviado = null;
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
            {
                // El controlador libera el MailMessage al acabar: se copia aquí lo que se manda
                Attachment adjunto = m.Attachments.FirstOrDefault();
                byte[] bytes = null;
                if (adjunto != null)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        adjunto.ContentStream.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }
                enviado = new CorreoCapturado
                {
                    De = m.From?.Address,
                    Para = m.To.Select(t => t.Address).ToList(),
                    Asunto = m.Subject,
                    Cuerpo = m.Body,
                    Adjuntos = m.Attachments.Count,
                    NombreAdjunto = adjunto?.Name,
                    TipoAdjunto = adjunto?.ContentType.MediaType,
                    Adjunto = bytes
                };
                return resultadoEnvio;
            });
        }

        private InformesController Controller(string usuario, params string[] grupos)
        {
            InformesController controller = new InformesController(servicio, servicioCorreo)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Ahora = () => HOY,
                AlmacenDelUsuario = u => almacenes.TryGetValue(u, out string a) ? a : null,
                CorreoDelUsuario = u => correos.TryGetValue(u, out string c) ? c : null
            };
            if (usuario != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity(usuario), grupos);
            }
            return controller;
        }

        private static PeticionCorreoFacturacionTiendaDTO Peticion(string tienda = null, string correo = null)
            => new PeticionCorreoFacturacionTiendaDTO { Tienda = tienda, Correo = correo };

        [TestMethod]
        public void Correo_SinIniciarSesion_401PorElAuthorizeDeLaClase()
        {
            Assert.IsTrue(typeof(InformesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
            foreach (string nombre in new[] { nameof(InformesController.PostCorreoFacturacionTienda), nameof(InformesController.GetCorreoFacturacionTienda) })
            {
                MethodInfo metodo = typeof(InformesController).GetMethod(nombre);
                Assert.AreEqual(0, metodo.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length, nombre);
            }
        }

        [TestMethod]
        public async Task Correo_Tiendas_MandaElExcelAdjuntoASuCorreo()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion());

            CorreoFacturacionTiendaDTO respuesta = ((OkNegotiatedContentResult<CorreoFacturacionTiendaDTO>)resultado).Content;
            Assert.AreEqual("paloma@nuevavision.es", respuesta.Correo);
            Assert.AreEqual("ALC", respuesta.Tienda);
            Assert.AreEqual(new DateTime(2026, 10, 19), respuesta.Desde);
            Assert.AreEqual(new DateTime(2026, 10, 25), respuesta.Hasta);
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("ALC", new DateTime(2026, 10, 19), new DateTime(2026, 10, 25))).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "paloma@nuevavision.es" }, enviado.Para);
            Assert.AreEqual("nesto@nuevavision.es", enviado.De);
            Assert.AreEqual("Facturado en la tienda ALC del 19/10/2026 al 25/10/2026", enviado.Asunto);
            Assert.AreEqual(1, enviado.Adjuntos);
            Assert.AreEqual("FacturacionTienda_ALC_20261019_20261025.xlsx", enviado.NombreAdjunto);
            Assert.AreEqual(GeneradorExcelFacturacionTienda.CONTENT_TYPE, enviado.TipoAdjunto);
        }

        [TestMethod]
        public async Task Correo_ElAdjuntoEsElExcelDelGenerador_ConSusDosHojas()
        {
            await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion());

            using (SpreadsheetDocument libro = SpreadsheetDocument.Open(new MemoryStream(enviado.Adjunto), false))
            {
                CollectionAssert.AreEqual(new[] { "Facturas", "Por cliente" },
                    libro.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(h => h.Name.Value).ToArray());
            }
        }

        [TestMethod]
        public async Task Correo_ElCuerpoLlevaElTotalYElNumeroDeFacturas()
        {
            FacturacionTiendaDTO informe = new FacturacionTiendaDTO
            {
                Tienda = "REI",
                Desde = new DateTime(2026, 10, 19),
                Hasta = new DateTime(2026, 10, 25),
                NumeroFacturas = 12,
                TotalBaseImponible = 1000m,
                Total = 1234.56m
            };
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("REI", A<DateTime>._, A<DateTime>._)).Returns(Task.FromResult(informe));

            IHttpActionResult resultado = await Controller("NUEVAVISION\\Pilar", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion());

            CorreoFacturacionTiendaDTO respuesta = ((OkNegotiatedContentResult<CorreoFacturacionTiendaDTO>)resultado).Content;
            Assert.AreEqual(12, respuesta.NumeroFacturas);
            Assert.AreEqual(1234.56m, respuesta.Total);
            StringAssert.Contains(enviado.Cuerpo, "<b>12</b>");
            StringAssert.Contains(enviado.Cuerpo, "1.234,56 €");
            StringAssert.Contains(enviado.Cuerpo, "1.000,00 €");
        }

        [TestMethod]
        public async Task Correo_UnVendedor_ForbiddenYNoManda()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Comerciales").PostCorreoFacturacionTienda(Peticion("REI"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_TiendasOtraTienda_ForbiddenYNoManda()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion("REI"));

            NegotiatedContentResult<string> prohibido = (NegotiatedContentResult<string>)resultado;
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            Assert.AreEqual(GestorFacturacionTienda.MENSAJE_SOLO_SU_TIENDA, prohibido.Content);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_DireccionCualquierTienda_ASuCorreo()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").PostCorreoFacturacionTienda(Peticion("ALC"));

            Assert.AreEqual("carlosadrian@nuevavision.es", ((OkNegotiatedContentResult<CorreoFacturacionTiendaDTO>)resultado).Content.Correo);
            Assert.AreEqual("Facturado en la tienda ALC del 19/10/2026 al 25/10/2026", enviado.Asunto);
        }

        [TestMethod]
        public async Task Correo_SinCorreoDelUsuario_BadRequestClaroYNoManda()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\SinCorreo", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion());

            Assert.AreEqual(CorreoFacturacionTienda.MENSAJE_SIN_CORREO, ((BadRequestErrorMessageResult)resultado).Message);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_SinCorreoDelUsuarioPeroDiceUnoDeLaEmpresa_SeMandaAEse()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\SinCorreo", "NUEVAVISION\\Tiendas")
                .PostCorreoFacturacionTienda(Peticion(correo: " tiendaalcobendas@nuevavision.es "));

            Assert.AreEqual("tiendaalcobendas@nuevavision.es", ((OkNegotiatedContentResult<CorreoFacturacionTiendaDTO>)resultado).Content.Correo);
            CollectionAssert.AreEqual(new[] { "tiendaalcobendas@nuevavision.es" }, enviado.Para);
        }

        [TestMethod]
        public async Task Correo_UnCorreoDeFuera_BadRequestPorProteccionDeDatos()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas")
                .PostCorreoFacturacionTienda(Peticion(correo: "paloma@gmail.com"));

            Assert.AreEqual(CorreoFacturacionTienda.MENSAJE_SOLO_CORREOS_EMPRESA, ((BadRequestErrorMessageResult)resultado).Message);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_UnCorreoMalEscrito_BadRequest()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas")
                .PostCorreoFacturacionTienda(Peticion(correo: "paloma@nuevavision.es; otro@nuevavision.es"));

            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "no es un correo válido");
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_FallaElEnvio_500ConMensajeDeUsted()
        {
            resultadoEnvio = false;

            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(Peticion());

            NegotiatedContentResult<string> error = (NegotiatedContentResult<string>)resultado;
            Assert.AreEqual(HttpStatusCode.InternalServerError, error.StatusCode);
            Assert.AreEqual("No se ha podido mandar el correo a paloma@nuevavision.es. Inténtelo de nuevo dentro de un rato.", error.Content);
        }

        [TestMethod]
        public async Task Correo_SinCuerpo_SuTiendaYLaSemanaAnterior()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Pilar", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(null);

            Assert.AreEqual("REI", ((OkNegotiatedContentResult<CorreoFacturacionTiendaDTO>)resultado).Content.Tienda);
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("REI", new DateTime(2026, 10, 19), new DateTime(2026, 10, 25))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Correo_FechasAlReves_BadRequestYNoManda()
        {
            PeticionCorreoFacturacionTiendaDTO peticion = Peticion();
            peticion.Desde = new DateTime(2026, 10, 25);
            peticion.Hasta = new DateTime(2026, 10, 19);

            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoFacturacionTienda(peticion);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- GET: el correo que enseña Nesto

        [TestMethod]
        public void GetCorreo_Tiendas_ElSuyo()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").GetCorreoFacturacionTienda();

            Assert.AreEqual("paloma@nuevavision.es", ((OkNegotiatedContentResult<CorreoUsuarioFacturacionTiendaDTO>)resultado).Content.Correo);
        }

        [TestMethod]
        public void GetCorreo_SinCorreo_Null()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\SinCorreo", "NUEVAVISION\\Tiendas").GetCorreoFacturacionTienda();

            Assert.IsNull(((OkNegotiatedContentResult<CorreoUsuarioFacturacionTiendaDTO>)resultado).Content.Correo);
        }

        [TestMethod]
        public void GetCorreo_UnVendedor_Forbidden()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Comerciales").GetCorreoFacturacionTienda();

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        // ---------------------------------------------------------------- el destinatario

        [TestMethod]
        public void ResolverDestinatario_ElSuyoAunqueNoSeaDeLaEmpresa_Vale()
        {
            Assert.AreEqual("Paloma@Gmail.com", CorreoFacturacionTienda.ResolverDestinatario("Paloma@Gmail.com", "paloma@gmail.com", out string error));
            Assert.IsNull(error);
        }

        [TestMethod]
        public void ResolverDestinatario_ElDelUsuarioMalPuesto_SinCorreo()
        {
            Assert.IsNull(CorreoFacturacionTienda.ResolverDestinatario(null, "paloma", out string error));
            Assert.AreEqual(CorreoFacturacionTienda.MENSAJE_SIN_CORREO, error);
        }
    }
}
