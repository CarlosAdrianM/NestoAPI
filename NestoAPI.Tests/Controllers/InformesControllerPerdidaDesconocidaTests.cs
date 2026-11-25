using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
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
    /// <summary>NestoAPI#893: GET api/Informes/PerdidaDesconocida (+/Excel): solo Dirección e Informática, a coste.</summary>
    [TestClass]
    public class InformesControllerPerdidaDesconocidaTests
    {
        private static readonly DateTime HOY = new DateTime(2027, 5, 20, 10, 0, 0);
        private IInformesService servicio;
        private DateTime? hastaPedido;

        [TestInitialize]
        public void Setup()
        {
            hastaPedido = null;
            servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerPerdidaDesconocidaAsync(A<DateTime>._)).ReturnsLazily((DateTime h) =>
            {
                hastaPedido = h;
                return Task.FromResult(GestorPerdidaDesconocida.Construir(new List<FilaPerdidaDesconocidaDTO>
                {
                    new FilaPerdidaDesconocidaDTO { Almacen = "REI", Mes = 1, Producto = "100", Diario = "_RegInvent", Bajas = 1, PrecioMedio = 4m }
                }, h));
            });
        }

        private InformesController Controller(string usuario, params string[] grupos)
        {
            var controller = new InformesController(servicio, A.Fake<IServicioCorreoElectronico>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                Ahora = () => HOY
            };
            if (usuario != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity(usuario), grupos);
            }
            return controller;
        }

        [TestMethod]
        public async Task Get_Direccion_SinNada_EsteAnioHastaHoy()
        {
            IHttpActionResult r = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetPerdidaDesconocida();

            PerdidaDesconocidaDTO informe = ((OkNegotiatedContentResult<PerdidaDesconocidaDTO>)r).Content;
            Assert.AreEqual(new DateTime(2027, 5, 20), hastaPedido);
            Assert.AreEqual(4m, informe.Total.PerdidaRealValor);
        }

        [TestMethod]
        public async Task Get_AnioPasado_HastaEl31DeDiciembre()
        {
            _ = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetPerdidaDesconocida(anio: 2026);

            Assert.AreEqual(new DateTime(2026, 12, 31), hastaPedido);
        }

        [TestMethod]
        public async Task Get_Tienda_403SinLeerNada()
        {
            IHttpActionResult r = await Controller("NUEVAVISION\\paloma", "NUEVAVISION\\Tiendas").GetPerdidaDesconocida();

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)r).StatusCode);
            A.CallTo(() => servicio.LeerPerdidaDesconocidaAsync(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Get_AnioFuturo_400()
        {
            IHttpActionResult r = await Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetPerdidaDesconocida(anio: 2028);

            Assert.IsInstanceOfType(r, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Excel_Informatica_Xlsx()
        {
            HttpResponseMessage r = await Controller("NUEVAVISION\\Rafa", "NUEVAVISION\\Informática").GetPerdidaDesconocidaExcel();

            Assert.AreEqual(HttpStatusCode.OK, r.StatusCode);
            Assert.AreEqual(GeneradorExcelPerdidaDesconocida.CONTENT_TYPE, r.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("PerdidaDesconocida_20270101_20270520.xlsx", r.Content.Headers.ContentDisposition.FileName);
        }

        [TestMethod]
        public async Task Excel_SinPermiso_403()
        {
            HttpResponseMessage r = await Controller("NUEVAVISION\\Pepe", "NUEVAVISION\\Almacén").GetPerdidaDesconocidaExcel();

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }
    }
}
