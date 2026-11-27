using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Calidad.Almacen;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#914 corte 2: api/Calidad/Almacen (Resumen y Detalle), solo Dirección e Informática.</summary>
    [TestClass]
    public class CalidadAlmacenControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27);
        private IServicioCalidadAlmacen servicio;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioCalidadAlmacen>();
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new ResumenCalidadAlmacenDTO());
            A.CallTo(() => servicio.Detalle(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<IndicadorAlmacen?>._, A<int>._, A<int>._))
                .Returns(new DetalleCalidadAlmacenDTO());
        }

        private CalidadAlmacenController Controller(params string[] roles) =>
            new CalidadAlmacenController(() => servicio, () => HOY)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [DataTestMethod]
        [DataRow("Almacén")]
        [DataRow("Administración")]
        public void Resumen_SinGrupo_403DeUsted(string rol)
        {
            var resultado = (NegotiatedContentResult<string>)Controller(rol).GetResumen();

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(CalidadAlmacenController.MENSAJE_SIN_PERMISO, resultado.Content);
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Detalle_SinGrupo_403()
        {
            var resultado = (NegotiatedContentResult<string>)Controller("Almacén").GetDetalle(preparador: "Alfredo");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public void Resumen_SinFechas_UltimosTresMesesHastaHoy()
        {
            var resultado = Controller("Dirección").GetResumen() as OkNegotiatedContentResult<ResumenCalidadAlmacenDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.Resumen("1", new DateTime(2026, 8, 27), new DateTime(2026, 11, 27))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Resumen_PeriodoMalo_400()
        {
            Assert.IsInstanceOfType(Controller("Informática").GetResumen("1", new DateTime(2026, 11, 1), new DateTime(2026, 10, 1)), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller("Informática").GetResumen("1", new DateTime(2025, 1, 1), new DateTime(2026, 11, 1)), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller("Informática").GetResumen("1", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Detalle_IndicadorQueNoExiste_400ConLaLista()
        {
            var resultado = Controller("Dirección").GetDetalle(indicador: "Inventado") as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "FaltaEnPacking");
            Assert.IsInstanceOfType(Controller("Dirección").GetDetalle(indicador: "1"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Detalle_IndicadorSinDistinguirMayusculas_YPreparadorRecortado()
        {
            var resultado = Controller("Dirección").GetDetalle(preparador: " Alfredo ", indicador: "faltaenpacking", pagina: 2, tamano: 50)
                as OkNegotiatedContentResult<DetalleCalidadAlmacenDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.Detalle("1", new DateTime(2026, 8, 27), HOY, "Alfredo", IndicadorAlmacen.FaltaEnPacking, 2, 50)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Detalle_PaginaOTamanoMalos_400()
        {
            Assert.IsInstanceOfType(Controller("Dirección").GetDetalle(pagina: 0), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller("Dirección").GetDetalle(tamano: 5000), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Resumen_FallaLaLectura_503DeUsted()
        {
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Throws(new InvalidOperationException("timeout"));

            var resultado = (NegotiatedContentResult<string>)Controller("Dirección").GetResumen();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            Assert.AreEqual(CalidadAlmacenController.MENSAJE_NO_SE_PUEDE_LEER, resultado.Content);
        }
    }
}
