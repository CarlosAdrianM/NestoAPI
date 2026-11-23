using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#886: GET api/Informes/AltasBajasTienda (+/Excel) con los permisos de FacturacionTienda y el POST …/Correo.</summary>
    [TestClass]
    public class InformesControllerAltasBajasTiendaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 23, 10, 0, 0);
        private IInformesService servicio;
        private IServicioCorreoElectronico servicioCorreo;
        private List<FilaAltaBajaTiendaDTO> filas;
        private MailMessage capturado;

        private readonly Dictionary<string, string> almacenes = new Dictionary<string, string>
        {
            ["paloma"] = "ALC",
            ["Pilar"] = "REI"
        };

        [TestInitialize]
        public void Setup()
        {
            filas = new List<FilaAltaBajaTiendaDTO>
            {
                new FilaAltaBajaTiendaDTO { Producto = "100", Cantidad = -1, Fecha = new DateTime(2026, 10, 5), Diario = "_RegInvent", Pvp = 5m }
            };
            servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string a, DateTime d, DateTime h) => Task.FromResult(GestorAltasBajasTienda.Construir(filas, a, d, h)));
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            capturado = null;
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
            {
                capturado = new MailMessage { Subject = m.Subject };
                foreach (MailAddress t in m.To) { capturado.To.Add(t); }
                foreach (MailAddress c in m.CC) { capturado.CC.Add(c); }
                return true;
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
                CorreoDelUsuario = u => null
            };
            if (usuario != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity(usuario), grupos);
            }
            return controller;
        }

        [TestMethod]
        public async Task Get_TiendaSinFechas_SuAlmacenYElMesAnterior()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").GetAltasBajasTienda();

            AltasBajasTiendaDTO informe = ((OkNegotiatedContentResult<AltasBajasTiendaDTO>)resultado).Content;
            Assert.AreEqual("ALC", informe.Almacen);
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync("ALC", new DateTime(2026, 10, 1), new DateTime(2026, 10, 31))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Get_TiendaOtraTienda_Forbidden()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").GetAltasBajasTienda("REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Get_Vendedor_Forbidden()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Vendedores").GetAltasBajasTienda("REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task Get_DireccionCualquierTienda_ConFechas()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección")
                .GetAltasBajasTienda("rei", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<AltasBajasTiendaDTO>));
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync("REI", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Get_FechasAlReves_O_MasDe366Dias_400()
        {
            InformesController c = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección");
            Assert.IsInstanceOfType(await c.GetAltasBajasTienda("REI", new DateTime(2026, 11, 30), new DateTime(2026, 11, 1)), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await c.GetAltasBajasTienda("REI", new DateTime(2025, 1, 1), new DateTime(2026, 6, 1)), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Excel_DevuelveElFichero()
        {
            HttpResponseMessage respuesta = await Controller("NUEVAVISION\\Pilar", "NUEVAVISION\\Tiendas").GetAltasBajasTiendaExcel();

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("AltasBajasTienda_REI_20261001_20261031.xlsx", respuesta.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task Correo_LasTiendasNoPueden_403DeUsted()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").PostCorreoAltasBajasTienda("ALC");

            NegotiatedContentResult<string> r = (NegotiatedContentResult<string>)resultado;
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(InformesController.MENSAJE_SIN_PERMISO_CORREO_ALTAS_BAJAS, r.Content);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_AdministracionNoPuede()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Admon", "NUEVAVISION\\Administración").PostCorreoAltasBajasTienda("ALC");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task Correo_ComprasDireccionEInformatica_LoMandanALaTiendaConCopiaACompras()
        {
            foreach (string grupo in new[] { "NUEVAVISION\\Compras", "NUEVAVISION\\Dirección", "NUEVAVISION\\Informatica", "NUEVAVISION\\Informática" })
            {
                IHttpActionResult resultado = await Controller("NUEVAVISION\\Alguien", grupo).PostCorreoAltasBajasTienda("REI");

                CorreoAltasBajasTiendaDTO r = ((OkNegotiatedContentResult<CorreoAltasBajasTiendaDTO>)resultado).Content;
                Assert.IsTrue(r.Enviado, grupo);
                CollectionAssert.AreEqual(new[] { "tienda@nuevavision.es" }, capturado.To.Select(t => t.Address).ToArray());
                CollectionAssert.AreEqual(new[] { "compras@nuevavision.es" }, capturado.CC.Select(t => t.Address).ToArray());
                Assert.AreEqual("Altas y bajas de stock de Reina · octubre 2026", capturado.Subject);
            }
        }

        [TestMethod]
        public async Task Correo_SinMovimientos_NoSeManda()
        {
            filas.Clear();
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Alguien", "NUEVAVISION\\Compras").PostCorreoAltasBajasTienda("ALC");

            CorreoAltasBajasTiendaDTO r = ((OkNegotiatedContentResult<CorreoAltasBajasTiendaDTO>)resultado).Content;
            Assert.IsFalse(r.Enviado);
            Assert.AreEqual(EnviadorAltasBajasTienda.MENSAJE_SIN_MOVIMIENTOS, r.Mensaje);
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Correo_SinTiendaOFechasMal_400()
        {
            InformesController c = Controller("NUEVAVISION\\Alguien", "NUEVAVISION\\Compras");
            Assert.IsInstanceOfType(await c.PostCorreoAltasBajasTienda(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await c.PostCorreoAltasBajasTienda("ALG"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await c.PostCorreoAltasBajasTienda("REI", new DateTime(2026, 11, 30), new DateTime(2026, 11, 1)),
                typeof(BadRequestErrorMessageResult));
        }
    }
}
