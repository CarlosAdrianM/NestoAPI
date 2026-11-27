using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Infraestructure.Remesas.ModeloImpagados;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
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
        private IRemesasService remesas;
        private IPredictorImpagos predictor;

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
            remesas = A.Fake<IRemesasService>();
            A.CallTo(() => remesas.LeerEfectosCandidatosSepaAsync(A<string>._, A<DateTime?>._)).Returns(Task.FromResult(new List<EfectoCandidatoDTO>
            {
                new EfectoCandidatoDTO { Id = 7, Cliente = "15191 ", Contacto = "0", Documento = "NV26001", Efecto = "1", ImportePendiente = 120m, Preseleccionado = true }
            }));
            predictor = A.Fake<IPredictorImpagos>();
            A.CallTo(() => predictor.HayAlgo).Returns(false);
        }

        private RemesasModeloImpagadosController Controller(params string[] roles) =>
            new RemesasModeloImpagadosController(() => repositorio, programador, () => ahora, () => new AlmacenModeloImpagos(carpeta), () => false,
                () => remesas, a => predictor)
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
        public async Task Candidatos_SinGrupo_403DeUstedYNoLeeLosCandidatos()
        {
            var resultado = (NegotiatedContentResult<string>)await Controller("Administración", "Almacén").GetCandidatos("1");

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(RemesasModeloImpagadosController.MENSAJE_SIN_PERMISO, resultado.Content);
            A.CallTo(() => remesas.LeerEfectosCandidatosSepaAsync(A<string>._, A<DateTime?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Candidatos_SinElScript_503()
        {
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(false);

            var resultado = (NegotiatedContentResult<string>)await Controller("Dirección").GetCandidatos("1");

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            Assert.AreEqual(ModeloImpagosRemesasJobsService.MENSAJE_SIN_TABLA, resultado.Content);
            A.CallTo(() => remesas.LeerEfectosCandidatosSepaAsync(A<string>._, A<DateTime?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Candidatos_Direccion_MismosParametrosQueEfectosCandidatos_SinModelo200YNoEscribe()
        {
            var hasta = new DateTime(2026, 11, 30);

            var resultado = await Controller("Dirección").GetCandidatos("1", hasta) as OkNegotiatedContentResult<CandidatosRiesgoImpagosDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => remesas.LeerEfectosCandidatosSepaAsync("1", hasta)).MustHaveHappenedOnceExactly();
            Assert.IsFalse(resultado.Content.ConModelo);
            Assert.AreEqual(CandidatosRiesgoImpagos.MENSAJE_SIN_MODELO, resultado.Content.Mensaje);
            Assert.AreEqual(ahora, resultado.Content.Fecha);
            Assert.AreEqual("15191", resultado.Content.Efectos.Single().Cliente);
            Assert.IsNull(resultado.Content.Efectos.Single().Probabilidad);
            A.CallTo(() => repositorio.GuardarPredicciones(A<IReadOnlyCollection<FilaPrediccionImpago>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Candidatos_Informatica_ConElInterruptorDeEnsenarApagado_TambienLosVe()
        {
            A.CallTo(() => predictor.HayAlgo).Returns(true);
            A.CallTo(() => predictor.Predecir(A<IList<RasgosImpago>>._)).ReturnsLazily((IList<RasgosImpago> r) =>
                r.Select(_ => new PrediccionRiesgoImpago { Probabilidad = 0.2, ProbabilidadRegla = 0.1, VersionModelo = "impagos-1" }).ToList());
            A.CallTo(() => repositorio.LeerEfectosCartera("1", A<IReadOnlyCollection<int>>._)).Returns(new List<ReciboGirado>());
            A.CallTo(() => repositorio.LeerHistorialClientes("1", A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HistoricoImpagos());
            A.CallTo(() => repositorio.LeerNivelGeneral(A<DateTime>._)).Returns(new NivelGeneralImpagos(null, null));

            var resultado = await Controller("Informática").GetCandidatos("1") as OkNegotiatedContentResult<CandidatosRiesgoImpagosDTO>;

            Assert.IsTrue(resultado.Content.ConModelo);
            Assert.AreEqual("impagos-1", resultado.Content.VersionModelo);
            Assert.AreEqual("15-30 %", resultado.Content.Efectos.Single().Tramo);
            Assert.AreEqual(24m, resultado.Content.Total.ImporteEnRiesgo);
            A.CallTo(() => remesas.LeerEfectosCandidatosSepaAsync("1", null)).MustHaveHappenedOnceExactly();
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
