using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Tests.Models.Facturas
{
    [TestClass]
    public class RegistroSeriesVerifactuTests
    {
        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieNVExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("NV");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
            Assert.AreEqual("F1", serie.TipoFacturaVerifactuPorDefecto);
            Assert.IsFalse(serie.EsRectificativa);
            Assert.AreEqual("RV", serie.SerieRectificativaAsociada);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieCVExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("CV");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
            Assert.AreEqual("F1", serie.TipoFacturaVerifactuPorDefecto);
            Assert.IsFalse(serie.EsRectificativa);
            Assert.AreEqual("RC", serie.SerieRectificativaAsociada);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieGBNoExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("GB");

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieInexistenteDevuelveNull()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("XX");

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieNullDevuelveNull()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie(null);

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieConEspaciosSeNormaliza()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("  NV  ");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieMinusculasSeNormaliza()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("nv");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_TramitaVerifactu_SerieNVDevuelveTrue()
        {
            var tramita = RegistroSeriesVerifactu.TramitaVerifactu("NV");

            Assert.IsTrue(tramita);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_TramitaVerifactu_SerieGBDevuelveFalse()
        {
            var tramita = RegistroSeriesVerifactu.TramitaVerifactu("GB");

            Assert.IsFalse(tramita);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_TramitaVerifactu_SerieInexistenteDevuelveFalse()
        {
            var tramita = RegistroSeriesVerifactu.TramitaVerifactu("XX");

            Assert.IsFalse(tramita);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_EsSerieRectificativa_SerieNVNoEsRectificativa()
        {
            var esRectificativa = RegistroSeriesVerifactu.EsSerieRectificativa("NV");

            Assert.IsFalse(esRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerieRectificativa_SerieNVDevuelveRV()
        {
            var serieRectificativa = RegistroSeriesVerifactu.ObtenerSerieRectificativa("NV");

            Assert.AreEqual("RV", serieRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerieRectificativa_SerieCVDevuelveRC()
        {
            var serieRectificativa = RegistroSeriesVerifactu.ObtenerSerieRectificativa("CV");

            Assert.AreEqual("RC", serieRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerieRectificativa_SerieInexistenteDevuelveNull()
        {
            var serieRectificativa = RegistroSeriesVerifactu.ObtenerSerieRectificativa("GB");

            Assert.IsNull(serieRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_SerieNV_DescripcionVerifactuNoEsVacia()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("NV");

            Assert.IsFalse(string.IsNullOrWhiteSpace(serie.DescripcionVerifactu));
            Assert.IsTrue(serie.DescripcionVerifactu.Length <= 500);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_SerieCV_DescripcionVerifactuNoEsVacia()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("CV");

            Assert.IsFalse(string.IsNullOrWhiteSpace(serie.DescripcionVerifactu));
            Assert.IsTrue(serie.DescripcionVerifactu.Length <= 500);
        }

        // Tests para series rectificativas RV y RC

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieRVExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("RV");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
            Assert.AreEqual("R1", serie.TipoFacturaVerifactuPorDefecto);
            Assert.IsTrue(serie.EsRectificativa);
            Assert.IsNull(serie.SerieRectificativaAsociada);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieRCExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("RC");

            Assert.IsNotNull(serie);
            Assert.IsTrue(serie.TramitaVerifactu);
            Assert.AreEqual("R1", serie.TipoFacturaVerifactuPorDefecto);
            Assert.IsTrue(serie.EsRectificativa);
            Assert.IsNull(serie.SerieRectificativaAsociada);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_EsSerieRectificativa_SerieRVEsRectificativa()
        {
            var esRectificativa = RegistroSeriesVerifactu.EsSerieRectificativa("RV");

            Assert.IsTrue(esRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_EsSerieRectificativa_SerieRCEsRectificativa()
        {
            var esRectificativa = RegistroSeriesVerifactu.EsSerieRectificativa("RC");

            Assert.IsTrue(esRectificativa);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_TramitaVerifactu_SerieRVDevuelveTrue()
        {
            var tramita = RegistroSeriesVerifactu.TramitaVerifactu("RV");

            Assert.IsTrue(tramita);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_TramitaVerifactu_SerieRCDevuelveTrue()
        {
            var tramita = RegistroSeriesVerifactu.TramitaVerifactu("RC");

            Assert.IsTrue(tramita);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_SerieRV_DescripcionVerifactuNoEsVacia()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("RV");

            Assert.IsFalse(string.IsNullOrWhiteSpace(serie.DescripcionVerifactu));
            Assert.IsTrue(serie.DescripcionVerifactu.Length <= 500);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_SerieRC_DescripcionVerifactuNoEsVacia()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("RC");

            Assert.IsFalse(string.IsNullOrWhiteSpace(serie.DescripcionVerifactu));
            Assert.IsTrue(serie.DescripcionVerifactu.Length <= 500);
        }

        // Tests para series eliminadas (no deben existir en el diccionario)

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieEVNoExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("EV");

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieULNoExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("UL");

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieVCNoExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("VC");

            Assert.IsNull(serie);
        }

        [TestMethod]
        public void RegistroSeriesVerifactu_ObtenerSerie_SerieDVNoExiste()
        {
            var serie = RegistroSeriesVerifactu.ObtenerSerie("DV");

            Assert.IsNull(serie);
        }
    }
}
