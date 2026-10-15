using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    /// <summary>
    /// NestoAPI#627: <see cref="GestorFestivos.PrimerLaborableDesde(DateTime, string, Func{DateTime, string, bool})"/>, el
    /// día en que cuenta una recepción prevista de proveedor. Octubre de 2026: viernes 16, sábado 17, domingo 18, lunes 19.
    /// </summary>
    [TestClass]
    public class GestorFestivosPrimerLaborableTests
    {
        private const string ALGETE = Constantes.Almacenes.ALGETE;

        private static Func<DateTime, string, bool> SinFestivos()
        {
            Func<DateTime, string, bool> esFestivo = A.Fake<Func<DateTime, string, bool>>();
            A.CallTo(() => esFestivo.Invoke(A<DateTime>._, A<string>._)).Returns(false);
            return esFestivo;
        }

        private static Func<DateTime, string, bool> ConFestivo(DateTime festivo)
        {
            Func<DateTime, string, bool> esFestivo = A.Fake<Func<DateTime, string, bool>>();
            A.CallTo(() => esFestivo.Invoke(A<DateTime>._, A<string>._)).Returns(false);
            A.CallTo(() => esFestivo.Invoke(festivo, ALGETE)).Returns(true);
            return esFestivo;
        }

        [TestMethod]
        public void PrimerLaborableDesde_Sabado_DevuelveElLunes()
        {
            Assert.AreEqual(new DateTime(2026, 10, 19), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 17), ALGETE, SinFestivos()));
        }

        [TestMethod]
        public void PrimerLaborableDesde_Domingo_DevuelveElLunes()
        {
            Assert.AreEqual(new DateTime(2026, 10, 19), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 18), ALGETE, SinFestivos()));
        }

        [TestMethod]
        public void PrimerLaborableDesde_FestivoEntreSemana_DevuelveElSiguienteLaborable()
        {
            // Miércoles 14 festivo → jueves 15.
            Func<DateTime, string, bool> esFestivo = ConFestivo(new DateTime(2026, 10, 14));

            Assert.AreEqual(new DateTime(2026, 10, 15), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 14), ALGETE, esFestivo));
        }

        [TestMethod]
        public void PrimerLaborableDesde_PuenteViernesFestivoYFinDeSemana_DevuelveElLunes()
        {
            Func<DateTime, string, bool> esFestivo = ConFestivo(new DateTime(2026, 10, 16));

            Assert.AreEqual(new DateTime(2026, 10, 19), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 16), ALGETE, esFestivo));
        }

        [TestMethod]
        public void PrimerLaborableDesde_Laborable_NoCambiaYQuitaLaHora()
        {
            Assert.AreEqual(new DateTime(2026, 10, 15), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 15, 17, 30, 0), ALGETE, SinFestivos()));
        }

        [TestMethod]
        public void PrimerLaborableDesde_SabadoAunqueLaFuncionNoLoDigaFestivo_NoEsLaborable()
        {
            // La función de festivos de los tests puede no incluir los fines de semana: se comprueban siempre.
            Assert.IsFalse(GestorFestivos.EsLaborable(new DateTime(2026, 10, 17), ALGETE, SinFestivos()));
        }

        [TestMethod]
        public void PrimerLaborableDesde_ConElCalendarioReal_SabadoEsLunes()
        {
            Assert.AreEqual(new DateTime(2026, 10, 19), GestorFestivos.PrimerLaborableDesde(new DateTime(2026, 10, 17), ALGETE));
        }
    }
}
