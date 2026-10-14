using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#630: POST api/PropuestasStock/Generar y GET api/PropuestasStock (solo Dirección).</summary>
    [TestClass]
    public class PropuestasStockControllerTests
    {
        private List<(string mes, string usuario)> encolados;
        private List<(string mes, string usuario, DateTimeOffset cuando)> programados;
        private IRepositorioTendencias repositorio;

        private PropuestasStockController Controller(DateTime ahora, bool direccion = true)
        {
            encolados = new List<(string, string)>();
            programados = new List<(string, string, DateTimeOffset)>();
            repositorio = A.Fake<IRepositorioTendencias>();
            PropuestasStockController controller = new PropuestasStockController(
                (m, u) => { encolados.Add((m, u)); return "1"; },
                (m, u, c) => { programados.Add((m, u, c)); return "2"; },
                () => repositorio,
                () => ahora)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.User = direccion
                ? new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
                : new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Vendedor"), new[] { "NUEVAVISION\\Vendedores" });
            return controller;
        }

        private static readonly DateTime Noche = new DateTime(2026, 10, 14, 23, 0, 0);
        private static readonly DateTime Horario = new DateTime(2026, 10, 14, 11, 0, 0);

        [TestMethod]
        public void Generar_SinSerDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            IHttpActionResult resultado = controller.Generar("2026-09");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual(0, encolados.Count + programados.Count);
        }

        [TestMethod]
        public void Generar_DeNoche_EncolaYa()
        {
            IHttpActionResult resultado = Controller(Noche).Generar("2026-09");

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual(("2026-09", "NUEVAVISION\\Carlos"), encolados[0]);
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void Generar_EnHorario_SeProgramaParaLaMadrugada()
        {
            IHttpActionResult resultado = Controller(Horario).Generar();

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual(0, encolados.Count);
            Assert.AreEqual("2026-09", programados[0].mes); // sin mes: el último cerrado
            Assert.AreEqual(new DateTime(2026, 10, 15, 4, 30, 0), programados[0].cuando.DateTime);
        }

        [TestMethod]
        public void Generar_EnHorarioConAhora_EncolaYa()
        {
            _ = Controller(Horario).Generar("2026-09", ahora: true);

            Assert.AreEqual(1, encolados.Count);
        }

        [TestMethod]
        public void Generar_MesMalEscritoOFuturo_BadRequest()
        {
            Assert.IsInstanceOfType(Controller(Noche).Generar("09/2026"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller(Noche).Generar("2026-11"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Generar_SinHangfire_503()
        {
            PropuestasStockController controller = new PropuestasStockController((m, u) => throw new InvalidOperationException("JobStorage no inicializado"),
                null, null, () => Noche)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, controller.Generar("2026-09").ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Get_SinSerDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)controller.Get("2026-09")).StatusCode);
            A.CallTo(() => repositorio.LeerPropuestas(A<DateTime?>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Get_DevuelveLasFilasDelMes()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerPropuestas(new DateTime(2026, 9, 1), 10))
                .Returns(new List<PropuestaStockGuardada> { new PropuestaStockGuardada { Producto = "17404" } });

            IHttpActionResult resultado = controller.Get("2026-09", 10);

            var ok = (OkNegotiatedContentResult<List<PropuestaStockGuardada>>)resultado;
            Assert.AreEqual("17404", ok.Content[0].Producto);
        }

        [TestMethod]
        public void Get_SinTabla_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Get().ExecuteAsync(default).Result.StatusCode);
        }
    }
}
