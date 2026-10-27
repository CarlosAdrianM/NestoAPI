using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Novedades;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>NestoAPI#711: la cabecera Range del vídeo de las novedades (saltar adelante y atrás).</summary>
    [TestClass]
    public class RangoBytesHttpTests
    {
        private const long LONGITUD = 1000;

        [TestMethod]
        public void SinRange_Completo()
        {
            ResultadoRango r = RangoBytesHttp.Calcular(null, LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Completo, r.Tipo);
            Assert.AreEqual(0, r.Desde);
            Assert.AreEqual(999, r.Hasta);
        }

        [TestMethod]
        public void DesdeElPrincipioSinFin_ParcialHastaElFinal()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=0-", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(0, r.Desde);
            Assert.AreEqual(999, r.Hasta);
            Assert.AreEqual(1000, r.Longitud);
            Assert.AreEqual("bytes 0-999/1000", r.ContentRange(LONGITUD));
        }

        [TestMethod]
        public void TramoCerrado_Parcial()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=100-199", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(100, r.Desde);
            Assert.AreEqual(199, r.Hasta);
            Assert.AreEqual(100, r.Longitud);
            Assert.AreEqual("bytes 100-199/1000", r.ContentRange(LONGITUD));
        }

        [TestMethod]
        public void FinMasAllaDelFichero_SeRecorta()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=900-5000", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(900, r.Desde);
            Assert.AreEqual(999, r.Hasta);
        }

        [TestMethod]
        public void Sufijo_LosUltimosBytes()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=-200", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(800, r.Desde);
            Assert.AreEqual(999, r.Hasta);
        }

        [TestMethod]
        public void SufijoMayorQueElFichero_TodoComoParcial()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=-5000", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(0, r.Desde);
            Assert.AreEqual(999, r.Hasta);
        }

        [TestMethod]
        public void EmpiezaDespuesDelFinal_NoSatisfacible()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("bytes=1000-", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.NoSatisfacible, r.Tipo);
            Assert.AreEqual("bytes */1000", r.ContentRange(LONGITUD));
        }

        [TestMethod]
        public void SufijoCero_NoSatisfacible()
        {
            Assert.AreEqual(TipoRespuestaRango.NoSatisfacible, RangoBytesHttp.Calcular("bytes=-0", LONGITUD).Tipo);
        }

        [DataTestMethod]
        [DataRow("items=0-10")]
        [DataRow("bytes=abc-")]
        [DataRow("bytes=-abc")]
        [DataRow("bytes=200-100")]
        [DataRow("bytes=")]
        [DataRow("bytes=10")]
        [DataRow("bytes=0-10,20-30")]
        public void LoQueNoSeEntiendeOVariosTramos_SeIgnoraYVaEntero(string cabecera)
        {
            Assert.AreEqual(TipoRespuestaRango.Completo, RangoBytesHttp.Calcular(cabecera, LONGITUD).Tipo);
        }

        [TestMethod]
        public void MayusculasYEspacios_SeEntiende()
        {
            ResultadoRango r = RangoBytesHttp.Calcular("  Bytes= 5 - 9 ", LONGITUD);

            Assert.AreEqual(TipoRespuestaRango.Parcial, r.Tipo);
            Assert.AreEqual(5, r.Desde);
            Assert.AreEqual(9, r.Hasta);
        }
    }
}
