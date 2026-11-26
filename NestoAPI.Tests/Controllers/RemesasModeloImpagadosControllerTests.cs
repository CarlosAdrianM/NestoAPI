using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#896: api/Remesas/ModeloImpagados (Comparacion y Entrenar), solo Dirección e Informática.</summary>
    [TestClass]
    public class RemesasModeloImpagadosControllerTests
    {
        private IRepositorioModeloImpagos repositorio;
        private IProgramadorModeloImpagos programador;
        private DateTime ahora;
        private string carpeta;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioModeloImpagos>();
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerCuentaSombra()).Returns(new CuentaSombraImpagos { Predicciones = 10, Pendientes = 10 });
            A.CallTo(() => repositorio.LeerResueltas(A<DateTime>._)).Returns(new List<PrediccionResueltaImpago>());
            programador = A.Fake<IProgramadorModeloImpagos>();
            A.CallTo(() => programador.Encolar(A<string>._)).Returns("81");
            A.CallTo(() => programador.Programar(A<DateTime>._, A<string>._)).Returns("82");
            // Miércoles 25/11/2026 a las 10:00: horario de trabajo.
            ahora = new DateTime(2026, 11, 25, 10, 0, 0);
            carpeta = Path.Combine(Path.GetTempPath(), "impagos896c_" + Guid.NewGuid().ToString("N"));
        }

        private RemesasModeloImpagadosController Controller(params string[] roles) =>
            new RemesasModeloImpagadosController(() => repositorio, programador, () => ahora, () => new AlmacenModeloImpagos(carpeta), () => false)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [TestMethod]
        public void Comparacion_SinGrupo_403DeUsted()
        {
            var resultado = (NegotiatedContentResult<string>)Controller("Administración").GetComparacion();

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(RemesasModeloImpagadosController.MENSAJE_SIN_PERMISO, resultado.Content);
            A.CallTo(() => repositorio.LeerResueltas(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Comparacion_SinElScript_503()
        {
            A.CallTo(() => repositorio.TablaPrediccionesExiste()).Returns(false);

            var resultado = (NegotiatedContentResult<string>)Controller("Dirección").GetComparacion();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, ModeloImpagosRemesasJobsService.SCRIPT);
        }

        [TestMethod]
        public void Comparacion_Informatica_200ConElUltimoEntrenamientoYLaSombra()
        {
            A.CallTo(() => repositorio.UltimoEntrenamiento()).Returns(new EntrenamientoModeloImpagos { Id = 3, AucModelo = 0.87, AucRegla = 0.85 });

            var resultado = Controller("Informática").GetComparacion(8) as OkNegotiatedContentResult<ComparacionModeloImpagosDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(8, resultado.Content.Semanas);
            Assert.AreEqual(3, resultado.Content.UltimoEntrenamiento.Id);
            Assert.AreEqual(10, resultado.Content.Sombra.Pendientes);
            Assert.IsFalse(resultado.Content.Fiable);
            Assert.IsFalse(resultado.Content.ModeloGuardado);
            Assert.IsFalse(resultado.Content.Ensenar);
            Assert.IsFalse(string.IsNullOrEmpty(resultado.Content.Veredicto));
        }

        [TestMethod]
        public void Entrenar_SinGrupo_403YNoEncola()
        {
            var resultado = (NegotiatedContentResult<string>)Controller("Administración", "Almacén").PostEntrenar();

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(RemesasModeloImpagadosController.MENSAJE_SIN_PERMISO_ENTRENAR, resultado.Content);
            A.CallTo(() => programador.Encolar(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => programador.Programar(A<DateTime>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Entrenar_SinElScript_503()
        {
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(false);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)Controller("Dirección").PostEntrenar()).StatusCode);
            A.CallTo(() => programador.Encolar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Entrenar_EnHorario_SeProgramaParaLasNueveDeLaNoche()
        {
            var resultado = Controller("Dirección").PostEntrenar() as NegotiatedContentResult<LanzamientoModeloImpagosDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.StatusCode);
            Assert.IsTrue(resultado.Content.Programado);
            Assert.AreEqual("82", resultado.Content.JobId);
            StringAssert.Contains(resultado.Content.Mensaje, "21:00");
            A.CallTo(() => programador.Programar(new DateTime(2026, 11, 25, 21, 0, 0), @"NUEVAVISION\Prueba")).MustHaveHappenedOnceExactly();
            A.CallTo(() => programador.Encolar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Entrenar_EnHorarioConAhora_SeEncola()
        {
            var resultado = Controller("Informática").PostEntrenar(ahora: true) as NegotiatedContentResult<LanzamientoModeloImpagosDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.StatusCode);
            Assert.IsFalse(resultado.Content.Programado);
            Assert.AreEqual("81", resultado.Content.JobId);
        }

        [TestMethod]
        public void Entrenar_FueraDeHorario_SeEncola()
        {
            ahora = new DateTime(2026, 11, 25, 22, 30, 0);

            var resultado = Controller("Dirección").PostEntrenar() as NegotiatedContentResult<LanzamientoModeloImpagosDTO>;

            Assert.IsFalse(resultado.Content.Programado);
            A.CallTo(() => programador.Encolar(@"NUEVAVISION\Prueba")).MustHaveHappenedOnceExactly();
        }
    }
}
