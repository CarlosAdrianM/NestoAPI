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
    /// <summary>
    /// NestoAPI#630: POST api/PropuestasStock/Generar (solo Dirección) y GET api/PropuestasStock.
    /// NestoAPI#631: GET con estado, GET Resumen (todos consultan) y POST {id}/Decision (Compras y Dirección).
    /// </summary>
    [TestClass]
    public class PropuestasStockControllerTests
    {
        private List<(string mes, string usuario)> encolados;
        private List<(string mes, string usuario, DateTimeOffset cuando)> programados;
        private IRepositorioTendencias repositorio;
        private IRepositorioDecisionesPropuestasStock decisiones;

        private PropuestasStockController Controller(DateTime ahora, bool direccion = true, string[] grupos = null)
        {
            encolados = new List<(string, string)>();
            programados = new List<(string, string, DateTimeOffset)>();
            repositorio = A.Fake<IRepositorioTendencias>();
            decisiones = A.Fake<IRepositorioDecisionesPropuestasStock>();
            PropuestasStockController controller = new PropuestasStockController(
                (m, u) => { encolados.Add((m, u)); return "1"; },
                (m, u, c) => { programados.Add((m, u, c)); return "2"; },
                () => repositorio,
                () => ahora,
                () => decisiones)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.User = grupos != null
                ? new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Manuel"), grupos)
                : direccion
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
        public void Get_SinSerDireccion_TambienConsulta()
        {
            // NestoAPI#631: «Compras y Dirección deciden; el resto consulta».
            PropuestasStockController controller = Controller(Noche, direccion: false);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuestas(new DateTime(2026, 9, 1), null, 50))
                .Returns(new List<PropuestaStockConDecision> { new PropuestaStockConDecision { Producto = "17404", Estado = EstadosPropuesta.PENDIENTE } });

            var ok = (OkNegotiatedContentResult<List<PropuestaStockConDecision>>)controller.Get("2026-09");

            Assert.AreEqual(EstadosPropuesta.PENDIENTE, ok.Content[0].Estado);
        }

        [TestMethod]
        public void Get_ConEstado_FiltraPorElEstadoNormalizado()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);

            _ = controller.Get("2026-09", 10, "pendiente");

            A.CallTo(() => decisiones.LeerPropuestas(new DateTime(2026, 9, 1), EstadosPropuesta.PENDIENTE, 10)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Get_EstadoDesconocido_BadRequest()
        {
            Assert.IsInstanceOfType(Controller(Noche).Get("2026-09", 10, "Borrada"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Get_ConEstadoSinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Get("2026-09", 10, "Pendiente").ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Resumen_DevuelveLosEstadosDelMes()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.Resumen(new DateTime(2026, 9, 1)))
                .Returns(new List<ResumenEstadoPropuestas> { new ResumenEstadoPropuestas { Estado = EstadosPropuesta.PENDIENTE, Propuestas = 42 } });

            var ok = (OkNegotiatedContentResult<List<ResumenEstadoPropuestas>>)controller.Resumen("2026-09");

            Assert.AreEqual(42, ok.Content[0].Propuestas);
        }

        [TestMethod]
        public void Resumen_SinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Resumen().ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Decision_SinSerComprasNiDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            Assert.AreEqual(HttpStatusCode.Forbidden, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Decision_Compras_Guarda()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(new PropuestaStockConDecision
            {
                Id = 7, Mes = new DateTime(2026, 9, 1), Producto = "17404", Almacen = "ALG", Accion = AccionesPropuesta.PARAR_COMPRAS, Estado = EstadosPropuesta.PENDIENTE
            });

            IHttpActionResult resultado = controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" });

            var ok = (OkNegotiatedContentResult<ResultadoDecisionPropuestaStock>)resultado;
            Assert.AreEqual(EstadosPropuesta.POSPUESTA, ok.Content.Estado);
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>.That.Matches(r => r.Usuario == "NUEVAVISION\\Manuel" && r.Fecha == Noche)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Decision_ErrorDeNegocio_DevuelveSuCodigoYElTexto()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);

            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(new PropuestaStockConDecision
            {
                Id = 7, Mes = new DateTime(2026, 9, 1), Producto = "17404", Almacen = "ALG", Accion = AccionesPropuesta.AJUSTAR, Decision = DecisionesPropuesta.APLICAR, Estado = EstadosPropuesta.APLICADA
            });
            Assert.AreEqual(HttpStatusCode.Conflict, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Decision_SinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
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
