using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#780: el 02/11/2026 (festivo en la Comunidad de Madrid) el job picking-cierre-diario sacó el picking automático
    /// porque el cron solo excluye sábados y domingos. La puerta común de los festivos y el picking de cierre.
    /// </summary>
    [TestClass]
    public class JobsFestivosTests
    {
        private static readonly DateTime FESTIVO_02_11 = new DateTime(2026, 11, 2, 11, 0, 3);
        private static readonly DateTime LABORABLE_03_11 = new DateTime(2026, 11, 3, 11, 0, 3);
        private static bool SoloEl2DeNoviembre(DateTime dia) => dia == new DateTime(2026, 11, 2);

        private List<Tuple<Exception, string>> trazas;

        [TestInitialize]
        public void Inicializar()
        {
            trazas = new List<Tuple<Exception, string>>();
        }

        private void Trazar(Exception ex, string usuario) => trazas.Add(Tuple.Create(ex, usuario));

        [TestMethod]
        public void PickingCierre_EnFestivo_NoSacaPickingYDejaTraza()
        {
            int llamadas = 0;

            bool sacado = PickingJobsService.EjecutarJobCierre(SoloEl2DeNoviembre, () => FESTIVO_02_11, () => llamadas++, Trazar);

            Assert.IsFalse(sacado);
            Assert.AreEqual(0, llamadas);
            Assert.AreEqual(1, trazas.Count);
            Assert.AreEqual("[Picking] No se saca el picking automático de cierre: el día es festivo", trazas[0].Item1.Message);
            Assert.AreEqual(PickingJobsService.USUARIO_SISTEMA, trazas[0].Item2);
        }

        [TestMethod]
        public void PickingCierre_EnLaborable_SacaPickingComoSiempreYSinTraza()
        {
            int llamadas = 0;

            bool sacado = PickingJobsService.EjecutarJobCierre(SoloEl2DeNoviembre, () => LABORABLE_03_11, () => llamadas++, Trazar);

            Assert.IsTrue(sacado);
            Assert.AreEqual(1, llamadas);
            Assert.AreEqual(0, trazas.Count);
        }

        [TestMethod]
        public void PickingCierre_SeMiraElDiaSinLaHora()
        {
            DateTime? preguntado = null;

            _ = PickingJobsService.EjecutarJobCierre(d => { preguntado = d; return false; }, () => FESTIVO_02_11, () => { }, Trazar);

            Assert.AreEqual(new DateTime(2026, 11, 2), preguntado);
        }

        [TestMethod]
        public void SaltarSiFestivo_SiFallaElCalendario_ElJobSigueYSeTraza()
        {
            bool saltar = JobsFestivos.SaltarSiFestivo("mensaje", "Sistema", d => throw new InvalidOperationException("BD caída"),
                () => FESTIVO_02_11, Trazar);

            Assert.IsFalse(saltar);
            Assert.AreEqual(1, trazas.Count);
            StringAssert.Contains(trazas[0].Item1.Message, "No se ha podido comprobar");
        }

        [TestMethod]
        public void SaltarSiFestivo_SinFuncionDeFestivos_NoSalta()
        {
            Assert.IsFalse(JobsFestivos.SaltarSiFestivo("mensaje", "Sistema", null, () => FESTIVO_02_11, Trazar));
            Assert.AreEqual(0, trazas.Count);
        }
    }
}
