using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#719: GET api/Informes/FacturacionTienda (y /Excel) con tipo Acumulado y Comparativo.</summary>
    [TestClass]
    public class InformesControllerFacturacionTiendaAnualTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12, 10, 0, 0);
        private IInformesService servicio;

        private readonly Dictionary<string, string> almacenes = new Dictionary<string, string>
        {
            ["Paloma"] = "REI",
            ["Alcobendas"] = "ALC"
        };

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync(A<string>._, A<DateTime>._, A<int>._))
                .ReturnsLazily((string t, DateTime h, int a) => Task.FromResult(GestorFacturacionTienda.ConstruirAnual(null, t, h, a)));
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string t, DateTime d, DateTime h) => Task.FromResult(GestorFacturacionTienda.Construir(null, t, d, h)));
        }

        private InformesController Controller(string usuario, params string[] grupos)
        {
            return new InformesController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Ahora = () => HOY,
                AlmacenDelUsuario = u => almacenes.TryGetValue(u, out string a) ? a : null,
                User = new GenericPrincipal(new GenericIdentity(usuario), grupos)
            };
        }

        private InformesController Direccion() => Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección");

        [TestMethod]
        public async Task Acumulado_SinHasta_HastaHoyYSinAniosAnteriores()
        {
            IHttpActionResult resultado = await Direccion().GetFacturacionTienda(tipo: "Acumulado", almacen: "REI");

            FacturacionTiendaAnualDTO informe = ((OkNegotiatedContentResult<FacturacionTiendaAnualDTO>)resultado).Content;
            Assert.AreEqual("Acumulado", informe.Tipo);
            Assert.AreEqual(11, informe.Meses.Count);
            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync("REI", new DateTime(2026, 11, 12), 0)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comparativo_SinAnios_DosAniosYLaFechaPedida()
        {
            await Direccion().GetFacturacionTienda("alc", null, new DateTime(2026, 6, 30), "comparativo");

            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync("ALC", new DateTime(2026, 6, 30), 2)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Comparativo_AniosFueraDeRango_BadRequestYNoConsulta()
        {
            IHttpActionResult resultado = await Direccion().GetFacturacionTienda("REI", tipo: "Comparativo", anios: 11);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync(A<string>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task TipoDesconocido_BadRequest()
        {
            IHttpActionResult resultado = await Direccion().GetFacturacionTienda("REI", tipo: "Mensual");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Semana_ExplicitoEsLoDeSiempre()
        {
            IHttpActionResult resultado = await Direccion().GetFacturacionTienda(tipo: "Semana", almacen: "REI");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturacionTiendaDTO>));
            A.CallTo(() => servicio.LeerFacturacionTiendaAsync("REI", new DateTime(2026, 11, 2), new DateTime(2026, 11, 8))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Acumulado_TiendasOtraTienda_ForbiddenYNoConsulta()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Alcobendas", "NUEVAVISION\\Tiendas")
                .GetFacturacionTienda(tipo: "Acumulado", almacen: "REI");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync(A<string>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Acumulado_TiendasSinDecirTienda_LaSuya()
        {
            await Controller("NUEVAVISION\\Paloma", "NUEVAVISION\\Tiendas").GetFacturacionTienda(tipo: "Acumulado");

            A.CallTo(() => servicio.LeerFacturacionTiendaAnualAsync("REI", A<DateTime>._, 0)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Acumulado_UnVendedor_Forbidden()
        {
            IHttpActionResult resultado = await Controller("NUEVAVISION\\Vendedor", "NUEVAVISION\\Comerciales")
                .GetFacturacionTienda("REI", tipo: "Acumulado");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task ExcelComparativo_DevuelveElXlsxConSuNombre()
        {
            HttpResponseMessage respuesta = await Direccion().GetFacturacionTiendaExcel("REI", hasta: new DateTime(2026, 11, 12), tipo: "Comparativo");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual(GeneradorExcelFacturacionTienda.CONTENT_TYPE, respuesta.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("FacturacionTienda_REI_Comparativo_20261112.xlsx", respuesta.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task ExcelTipoDesconocido_BadRequest()
        {
            HttpResponseMessage respuesta = await Direccion().GetFacturacionTiendaExcel("REI", tipo: "Mensual");

            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }
    }
}
