using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#914 (encuestas, E1): api/Calidad/Encuestas/Simulacion y /Sombra, solo Dirección e Informática.</summary>
    [TestClass]
    public class CalidadEncuestasControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27);
        private IServicioEncuestas servicio;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioEncuestas>();
            A.CallTo(() => servicio.Simular(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._)).Returns(new SimulacionEncuestasDTO());
            A.CallTo(() => servicio.Comparar(A<string>._, A<DateTime>._, A<DateTime>._, A<IReadOnlyList<int>>._)).Returns(new ComparacionSimulacionEncuestasDTO());
            A.CallTo(() => servicio.Sombra(A<string>._, A<int>._)).Returns(new SimulacionEncuestasDTO());
        }

        private CalidadEncuestasController Controller(params string[] roles) =>
            new CalidadEncuestasController(() => servicio, () => HOY)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [DataTestMethod]
        [DataRow("Administración")]
        [DataRow("Vendedores")]
        public void Simulacion_SinGrupo_403(string rol)
        {
            var r = (NegotiatedContentResult<string>)Controller(rol).GetSimulacion();

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            A.CallTo(() => servicio.Simular(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sombra_SinGrupo_403()
        {
            var r = (NegotiatedContentResult<string>)Controller("Almacén").GetSombra();

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [TestMethod]
        public void Simulacion_PorDefecto_UltimosNoventaDias_ConElIntervaloDelParametro()
        {
            Assert.IsInstanceOfType(Controller("Dirección").GetSimulacion(), typeof(OkNegotiatedContentResult<SimulacionEncuestasDTO>));

            A.CallTo(() => servicio.Simular("1", HOY.AddDays(-90), HOY, null)).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow(29)]
        [DataRow(10)]
        [DataRow(181)]
        public void Simulacion_DiasEntreFueraDeRango_400(int dias)
        {
            var r = Controller("Informática").GetSimulacion(diasEntre: dias) as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
            StringAssert.Contains(r.Message, "entre 30 y 180");
        }

        [TestMethod]
        public void Simulacion_DiasEntre30_Vale()
        {
            _ = Controller("Informática").GetSimulacion(diasEntre: 30);

            A.CallTo(() => servicio.Simular("1", A<DateTime>._, A<DateTime>._, 30)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Simulacion_Comparar_LlamaAComparaConLaListaOrdenada()
        {
            var r = Controller("Dirección").GetSimulacion(comparar: "60, 30,45,30");

            Assert.IsInstanceOfType(r, typeof(OkNegotiatedContentResult<ComparacionSimulacionEncuestasDTO>));
            A.CallTo(() => servicio.Comparar("1", A<DateTime>._, A<DateTime>._, A<IReadOnlyList<int>>.That.Matches(l => l.SequenceEqual(new[] { 30, 45, 60 }))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicio.Simular(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow("30,20")]
        [DataRow("30,uno")]
        [DataRow("30,40,50,60,70,80")]
        public void Simulacion_CompararMal_400(string comparar)
        {
            Assert.IsInstanceOfType(Controller("Dirección").GetSimulacion(comparar: comparar), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Simulacion_RangoDeMasDe180Dias_400()
        {
            Assert.IsInstanceOfType(Controller("Dirección").GetSimulacion(desde: HOY.AddDays(-181), hasta: HOY), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Simulacion_HastaEnElFuturo_400()
        {
            Assert.IsInstanceOfType(Controller("Dirección").GetSimulacion(desde: HOY.AddDays(-10), hasta: HOY.AddDays(1)), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Simulacion_FallaLaLectura_503()
        {
            A.CallTo(() => servicio.Simular(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._)).Throws(new InvalidOperationException("BD"));

            var r = (NegotiatedContentResult<string>)Controller("Dirección").GetSimulacion();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }

        [TestMethod]
        public void Sombra_SinTablas_503ConElMensaje()
        {
            A.CallTo(() => servicio.Sombra(A<string>._, A<int>._)).Throws(new EncuestasSinTablasException());

            var r = (NegotiatedContentResult<string>)Controller("Dirección").GetSombra();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.AreEqual(ServicioEncuestas.MENSAJE_SIN_TABLAS, r.Content);
        }

        [TestMethod]
        public void Sombra_PorDefecto30Dias()
        {
            Assert.IsInstanceOfType(Controller("Informática").GetSombra(), typeof(OkNegotiatedContentResult<SimulacionEncuestasDTO>));

            A.CallTo(() => servicio.Sombra("1", 30)).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(366)]
        public void Sombra_DiasFueraDeRango_400(int dias)
        {
            Assert.IsInstanceOfType(Controller("Informática").GetSombra(dias), typeof(BadRequestErrorMessageResult));
        }
    }
}
