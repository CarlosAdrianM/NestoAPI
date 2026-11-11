using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Feria online 12-16/11/26: «Aplicar Dto» = 1 en las fichas de la campaña desde las 00:00 del 12 y de vuelta a lo que tenían
    /// desde las 00:00 del 17 (job recurrente, idempotente, con el estado en la tabla).
    /// </summary>
    [TestClass]
    public class CampanasAplicarDtoTests
    {
        private static FilaCampanaAplicarDto Fila(DateTime? activado = null, DateTime? restaurado = null) => new FilaCampanaAplicarDto
        {
            Campana = "FERIA-ONLINE-1126",
            Empresa = "1",
            Producto = "45383",
            FechaDesde = new DateTime(2026, 11, 12),
            FechaHasta = new DateTime(2026, 11, 16),
            FechaActivado = activado,
            AplicarDtoAnterior = activado.HasValue ? false : (bool?)null,
            FechaRestaurado = restaurado
        };

        [TestMethod]
        public void Decidir_AntesDeEmpezar_Nada()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Nada, CampanasAplicarDto.Decidir(Fila(), new DateTime(2026, 11, 11, 23, 0, 0)));
        }

        [TestMethod]
        public void Decidir_PrimerDiaALas0_Activa()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Activar, CampanasAplicarDto.Decidir(Fila(), new DateTime(2026, 11, 12, 0, 0, 0)));
        }

        [TestMethod]
        public void Decidir_UltimoDiaSinActivar_Activa()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Activar, CampanasAplicarDto.Decidir(Fila(), new DateTime(2026, 11, 16, 23, 0, 0)));
        }

        [TestMethod]
        public void Decidir_YaActivadaDuranteLaCampana_Nada()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Nada, CampanasAplicarDto.Decidir(Fila(new DateTime(2026, 11, 12)), new DateTime(2026, 11, 16)));
        }

        [TestMethod]
        public void Decidir_DiaSiguienteAlUltimo_Restaura()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Restaurar, CampanasAplicarDto.Decidir(Fila(new DateTime(2026, 11, 12)), new DateTime(2026, 11, 17)));
        }

        [TestMethod]
        public void Decidir_YaRestaurada_Nada()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Nada,
                CampanasAplicarDto.Decidir(Fila(new DateTime(2026, 11, 12), new DateTime(2026, 11, 17)), new DateTime(2026, 11, 18)));
        }

        [TestMethod]
        public void Decidir_NuncaActivadaYYaPasada_Nada()
        {
            Assert.AreEqual(AccionCampanaAplicarDto.Nada, CampanasAplicarDto.Decidir(Fila(), new DateTime(2026, 11, 17)));
        }

        [TestMethod]
        public async Task Job_ActivaLasQueTocanYNoLasDemas()
        {
            IRepositorioCampanasAplicarDto repositorio = A.Fake<IRepositorioCampanasAplicarDto>();
            FilaCampanaAplicarDto pendiente = Fila();
            FilaCampanaAplicarDto activada = Fila(new DateTime(2026, 11, 12));
            activada.Producto = "45670";
            _ = A.CallTo(() => repositorio.LeerPendientes()).Returns(new List<FilaCampanaAplicarDto> { pendiente, activada });
            _ = A.CallTo(() => repositorio.Activar(pendiente)).Returns(ResultadoCampanaAplicarDto.Activada);

            Dictionary<string, ResultadoCampanaAplicarDto> hecho = await new CampanasAplicarDtoJobsService(repositorio, null)
                .EjecutarAsync(new DateTime(2026, 11, 12, 0, 0, 0));

            Assert.AreEqual(ResultadoCampanaAplicarDto.Activada, hecho["FERIA-ONLINE-1126/1/45383"]);
            Assert.AreEqual(1, hecho.Count);
            A.CallTo(() => repositorio.Restaurar(A<FilaCampanaAplicarDto>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Activar(activada)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_AlRestaurarSiAlguienLaCambio_NoPisaYLoApunta()
        {
            IRepositorioCampanasAplicarDto repositorio = A.Fake<IRepositorioCampanasAplicarDto>();
            FilaCampanaAplicarDto activada = Fila(new DateTime(2026, 11, 12));
            _ = A.CallTo(() => repositorio.LeerPendientes()).Returns(new List<FilaCampanaAplicarDto> { activada });
            _ = A.CallTo(() => repositorio.Restaurar(activada)).Returns(ResultadoCampanaAplicarDto.NoPisada);
            var registrados = new List<Exception>();

            Dictionary<string, ResultadoCampanaAplicarDto> hecho = await new CampanasAplicarDtoJobsService(repositorio, registrados.Add)
                .EjecutarAsync(new DateTime(2026, 11, 17, 0, 0, 0));

            Assert.AreEqual(ResultadoCampanaAplicarDto.NoPisada, hecho["FERIA-ONLINE-1126/1/45383"]);
            Assert.IsTrue(registrados.Exists(e => e.Message.Contains("se han dejado como estaban") && e.Message.Contains("45383")));
        }

        [TestMethod]
        public async Task Job_SiFallaUnaFicha_SigueConLasDemas()
        {
            IRepositorioCampanasAplicarDto repositorio = A.Fake<IRepositorioCampanasAplicarDto>();
            FilaCampanaAplicarDto mala = Fila();
            FilaCampanaAplicarDto buena = Fila();
            buena.Producto = "45670";
            _ = A.CallTo(() => repositorio.LeerPendientes()).Returns(new List<FilaCampanaAplicarDto> { mala, buena });
            _ = A.CallTo(() => repositorio.Activar(mala)).Throws(new InvalidOperationException("bloqueo"));
            _ = A.CallTo(() => repositorio.Activar(buena)).Returns(ResultadoCampanaAplicarDto.Activada);
            var registrados = new List<Exception>();

            Dictionary<string, ResultadoCampanaAplicarDto> hecho = await new CampanasAplicarDtoJobsService(repositorio, registrados.Add)
                .EjecutarAsync(new DateTime(2026, 11, 12, 1, 0, 0));

            Assert.AreEqual(ResultadoCampanaAplicarDto.Activada, hecho["FERIA-ONLINE-1126/1/45670"]);
            Assert.IsTrue(registrados.Exists(e => e.Message.Contains("No se ha podido activar")));
        }

        [TestMethod]
        public async Task Job_SinTabla_NoHaceNada()
        {
            IRepositorioCampanasAplicarDto repositorio = A.Fake<IRepositorioCampanasAplicarDto>();
            _ = A.CallTo(() => repositorio.LeerPendientes()).Returns(new List<FilaCampanaAplicarDto>());

            Dictionary<string, ResultadoCampanaAplicarDto> hecho = await new CampanasAplicarDtoJobsService(repositorio, null)
                .EjecutarAsync(new DateTime(2026, 11, 12));

            Assert.AreEqual(0, hecho.Count);
        }
    }
}
