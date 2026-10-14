using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Tests.Infrastructure.IndicadoresStock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#629: GET api/IndicadoresStock (Dirección e Informática) y POST .../Reconstruir (Dirección).</summary>
    [TestClass]
    public class IndicadoresStockControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 14, 10, 0, 0);
        private RepositorioIndicadoresStockEnMemoria repo;
        private List<(int meses, DateTime desde)> encolados;

        [TestInitialize]
        public void Inicializar()
        {
            repo = new RepositorioIndicadoresStockEnMemoria();
            encolados = new List<(int, DateTime)>();
        }

        private IndicadoresStockController Controller(string usuario, params string[] grupos)
        {
            var controller = new IndicadoresStockController(() => repo, (meses, desde) =>
            {
                encolados.Add((meses, desde));
                return "42";
            }, () => HOY)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            if (usuario != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity(usuario), grupos);
            }
            return controller;
        }

        private void Filas(string almacen, DateTime desde, int meses)
        {
            for (int i = 0; i < meses; i++)
            {
                repo.Tabla.Add(new FilaIndicadoresStock
                {
                    Mes = desde.AddMonths(i), Almacen = almacen, ValorStock = 1000m + i, UnidadesCompradasMes = 10, UnidadesCompradasCoberturaAlta = 3
                });
            }
        }

        [TestMethod]
        public void Get_SinSerDireccionNiInformatica_Forbidden()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Compras", "NUEVAVISION\\Compras").GetIndicadoresStock().Result;

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public void Get_SinUsuario_Forbidden()
        {
            Assert.IsInstanceOfType(Controller(null).GetIndicadoresStock().Result, typeof(StatusCodeResult));
        }

        [TestMethod]
        public void Get_Informatica_DevuelveLasFilasDelPeriodoConElPorcentajeDe12Meses()
        {
            Filas("TOTAL", new DateTime(2025, 1, 1), 21);   // ene-25 a sep-26
            Filas("ALG", new DateTime(2026, 1, 1), 9);

            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica")
                .GetIndicadoresStock("2026-01", "2026-09").Result;

            List<IndicadoresStockDTO> filas = ((OkNegotiatedContentResult<List<IndicadoresStockDTO>>)resultado).Content;
            Assert.AreEqual(18, filas.Count, "9 meses × (TOTAL y ALG); no los 11 de antes que se leen para el %");
            Assert.AreEqual("2026-01", filas[0].Periodo);
            Assert.AreEqual("TOTAL", filas[0].Almacen, "La TOTAL primero en cada mes");
            Assert.AreEqual(30m, filas[0].PorcentajeComprasCoberturaAlta12Meses);
            Assert.IsNull(filas[1].PorcentajeComprasCoberturaAlta12Meses, "Algete no tiene los 12 meses");
        }

        [TestMethod]
        public void Get_Direccion_FiltraPorAlmacenYPorDefectoLos36MesesHastaElAnterior()
        {
            Filas("TOTAL", new DateTime(2023, 1, 1), 46);   // ene-23 a oct-26
            Filas("ALG", new DateTime(2023, 1, 1), 46);

            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetIndicadoresStock(almacen: "TOTAL").Result;

            List<IndicadoresStockDTO> filas = ((OkNegotiatedContentResult<List<IndicadoresStockDTO>>)resultado).Content;
            Assert.AreEqual(36, filas.Count);
            Assert.AreEqual("2023-10", filas.First().Periodo);
            Assert.AreEqual("2026-09", filas.Last().Periodo);
            Assert.IsTrue(filas.All(f => f.Almacen == "TOTAL"));
        }

        [TestMethod]
        public void Get_MesMalEscrito_BadRequest()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetIndicadoresStock("2026/01").Result;

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Get_DesdePosteriorAHasta_BadRequest()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetIndicadoresStock("2026-05", "2026-04").Result;

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Get_SinTabla_503()
        {
            repo.TablaExiste = false;

            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").GetIndicadoresStock().Result;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Reconstruir_Informatica_Forbidden()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica").Reconstruir();

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void Reconstruir_Direccion_Encola36MesesDesdeElAnteriorY202()
        {
            IHttpActionResult resultado = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").Reconstruir();

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual((36, new DateTime(2026, 9, 1)), encolados.Single());
        }

        [TestMethod]
        public void Reconstruir_UnMesConcreto()
        {
            _ = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección").Reconstruir(1, "2025-03");

            Assert.AreEqual((1, new DateTime(2025, 3, 1)), encolados.Single());
        }

        [TestMethod]
        public void Reconstruir_MesSinCerrarOMesesFueraDeRango_BadRequest()
        {
            IndicadoresStockController controller = Controller("NUEVAVISION\\Carlos", "NUEVAVISION\\Dirección");

            Assert.IsInstanceOfType(controller.Reconstruir(1, "2026-10"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(controller.Reconstruir(0), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(controller.Reconstruir(61), typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(0, encolados.Count);
        }

        [TestMethod]
        public void Reconstruir_SinHangfire_503()
        {
            var controller = new IndicadoresStockController(() => repo, (m, d) => throw new InvalidOperationException("JobStorage.Current"), () => HOY)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, controller.Reconstruir().ExecuteAsync(default).Result.StatusCode);
        }
    }
}
