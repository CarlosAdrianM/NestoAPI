using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Facturas.SeriesFactura;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests para verificar que las series de factura tienen correctamente configuradas
    /// las propiedades necesarias para QuestPDF (Issue #83)
    /// </summary>
    [TestClass]
    public class SeriesFacturaQuestPdfTests
    {
        #region Tests de UrlLogo

        [TestMethod]
        public void SerieNV_TieneUrlLogo()
        {
            var serie = new SerieNV();
            Assert.IsFalse(string.IsNullOrEmpty(serie.UrlLogo),
                "SerieNV debe tener UrlLogo configurado");
            Assert.IsTrue(serie.UrlLogo.StartsWith("http"),
                "UrlLogo debe ser una URL válida");
        }

        [TestMethod]
        public void SerieUL_TieneUrlLogo()
        {
            var serie = new SerieUL();
            Assert.IsFalse(string.IsNullOrEmpty(serie.UrlLogo),
                "SerieUL debe tener UrlLogo configurado");
            Assert.IsTrue(serie.UrlLogo.Contains("unionlaser"),
                "UrlLogo de UL debe apuntar al dominio de Unión Láser");
        }

        [TestMethod]
        public void SerieEV_TieneUrlLogo()
        {
            var serie = new SerieEV();
            Assert.IsFalse(string.IsNullOrEmpty(serie.UrlLogo),
                "SerieEV debe tener UrlLogo configurado");
            Assert.IsTrue(serie.UrlLogo.Contains("evavisnu"),
                "UrlLogo de EV debe apuntar al dominio de Eva Visnú");
        }

        [TestMethod]
        public void SerieGB_NoTieneUrlLogo()
        {
            var serie = new SerieGB();
            Assert.IsNull(serie.UrlLogo,
                "SerieGB no debe tener UrlLogo (formato ticket sin logo)");
        }

        [TestMethod]
        public void SerieCV_TieneUrlLogo_IgualQueNV()
        {
            var serieNV = new SerieNV();
            var serieCV = new SerieCV();
            Assert.AreEqual(serieNV.UrlLogo, serieCV.UrlLogo,
                "SerieCV debe usar el mismo logo que NV");
        }

        [TestMethod]
        public void SerieRV_TieneUrlLogo_IgualQueNV()
        {
            var serieNV = new SerieNV();
            var serieRV = new SerieRV();
            Assert.AreEqual(serieNV.UrlLogo, serieRV.UrlLogo,
                "SerieRV debe usar el mismo logo que NV");
        }

        [TestMethod]
        public void SerieRC_TieneUrlLogo_IgualQueNV()
        {
            var serieNV = new SerieNV();
            var serieRC = new SerieRC();
            Assert.AreEqual(serieNV.UrlLogo, serieRC.UrlLogo,
                "SerieRC debe usar el mismo logo que NV");
        }

        #endregion

        #region Tests de EsDescargable

        [TestMethod]
        public void SerieNV_EsDescargable()
        {
            var serie = new SerieNV();
            Assert.IsTrue(serie.EsDescargable,
                "SerieNV debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieUL_EsDescargable()
        {
            var serie = new SerieUL();
            Assert.IsTrue(serie.EsDescargable,
                "SerieUL debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieEV_EsDescargable()
        {
            var serie = new SerieEV();
            Assert.IsTrue(serie.EsDescargable,
                "SerieEV debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieGB_NoEsDescargable()
        {
            var serie = new SerieGB();
            Assert.IsFalse(serie.EsDescargable,
                "SerieGB NO debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieCV_EsDescargable()
        {
            var serie = new SerieCV();
            Assert.IsTrue(serie.EsDescargable,
                "SerieCV debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieRV_EsDescargable()
        {
            var serie = new SerieRV();
            Assert.IsTrue(serie.EsDescargable,
                "SerieRV debe permitir descarga de PDFs");
        }

        [TestMethod]
        public void SerieRC_EsDescargable()
        {
            var serie = new SerieRC();
            Assert.IsTrue(serie.EsDescargable,
                "SerieRC debe permitir descarga de PDFs");
        }

        #endregion

        #region Tests de EsImprimible

        [TestMethod]
        public void TodasLasSeries_SonImprimibles()
        {
            // Todas las series deben ser imprimibles
            Assert.IsTrue(new SerieNV().EsImprimible, "SerieNV debe ser imprimible");
            Assert.IsTrue(new SerieUL().EsImprimible, "SerieUL debe ser imprimible");
            Assert.IsTrue(new SerieEV().EsImprimible, "SerieEV debe ser imprimible");
            Assert.IsTrue(new SerieGB().EsImprimible, "SerieGB debe ser imprimible");
            Assert.IsTrue(new SerieCV().EsImprimible, "SerieCV debe ser imprimible");
            Assert.IsTrue(new SerieRV().EsImprimible, "SerieRV debe ser imprimible");
            Assert.IsTrue(new SerieRC().EsImprimible, "SerieRC debe ser imprimible");
        }

        #endregion

        #region Tests de consistencia con ISerieFactura

        [TestMethod]
        public void TodasLasSeries_ImplementanISerieFactura()
        {
            // Verificar que todas las series implementan correctamente ISerieFactura
            ISerieFactura[] series = new ISerieFactura[]
            {
                new SerieNV(),
                new SerieUL(),
                new SerieEV(),
                new SerieGB(),
                new SerieCV(),
                new SerieRV(),
                new SerieRC()
            };

            foreach (var serie in series)
            {
                // Verificar que las propiedades básicas no lanzan excepción
                var urlLogo = serie.UrlLogo;
                var esDescargable = serie.EsDescargable;
                var esImprimible = serie.EsImprimible;
                var rutaInforme = serie.RutaInforme;
                var notas = serie.Notas;
            }
        }

        #endregion

        #region Tests de regla de negocio: GB no permite descarga pero sí impresión

        [TestMethod]
        public void SerieGB_NoPuedeDescargarse_PeroPuedeImprimirse()
        {
            var serie = new SerieGB();

            // Esta es la regla de negocio específica para GB:
            // - NO permite descarga (para evitar que lleguen facturas incorrectas a clientes)
            // - SÍ permite impresión física (para facturación de rutas)
            Assert.IsFalse(serie.EsDescargable, "GB no debe ser descargable");
            Assert.IsTrue(serie.EsImprimible, "GB debe ser imprimible");
            Assert.IsNull(serie.CorreoDesdeFactura, "GB no debe tener correo configurado");
        }

        #endregion

        #region Tests de UsaFormatoTicket

        [TestMethod]
        public void SerieGB_UsaFormatoTicket()
        {
            var serie = new SerieGB();
            Assert.IsTrue(serie.UsaFormatoTicket,
                "SerieGB debe usar formato ticket");
        }

        [TestMethod]
        public void SeriesEstandar_NoUsanFormatoTicket()
        {
            // Todas las series excepto GB usan formato factura estándar
            Assert.IsFalse(new SerieNV().UsaFormatoTicket, "SerieNV no debe usar formato ticket");
            Assert.IsFalse(new SerieUL().UsaFormatoTicket, "SerieUL no debe usar formato ticket");
            Assert.IsFalse(new SerieEV().UsaFormatoTicket, "SerieEV no debe usar formato ticket");
            Assert.IsFalse(new SerieCV().UsaFormatoTicket, "SerieCV no debe usar formato ticket");
            Assert.IsFalse(new SerieRV().UsaFormatoTicket, "SerieRV no debe usar formato ticket");
            Assert.IsFalse(new SerieRC().UsaFormatoTicket, "SerieRC no debe usar formato ticket");
        }

        [TestMethod]
        public void SerieConFormatoTicket_PuedeNoTenerUrlLogo()
        {
            // Si UsaFormatoTicket=true, UrlLogo puede ser null (es válido)
            var serie = new SerieGB();
            Assert.IsTrue(serie.UsaFormatoTicket);
            Assert.IsNull(serie.UrlLogo);
            // No debe haber problema - esta combinación es válida
        }

        [TestMethod]
        public void SeriesSinFormatoTicket_DebenTenerUrlLogo()
        {
            // Todas las series que NO usan formato ticket deben tener UrlLogo
            ISerieFactura[] seriesEstandar = new ISerieFactura[]
            {
                new SerieNV(),
                new SerieUL(),
                new SerieEV(),
                new SerieCV(),
                new SerieRV(),
                new SerieRC()
            };

            foreach (var serie in seriesEstandar)
            {
                Assert.IsFalse(serie.UsaFormatoTicket,
                    $"Serie {serie.GetType().Name} no debe usar formato ticket");
                Assert.IsFalse(string.IsNullOrEmpty(serie.UrlLogo),
                    $"Serie {serie.GetType().Name} debe tener UrlLogo porque no usa formato ticket");
            }
        }

        #endregion

        #region Tests de validación de configuración consistente

        [TestMethod]
        public void TodasLasSeries_TienenConfiguracionConsistente()
        {
            // Regla: Si UsaFormatoTicket=false, entonces UrlLogo no puede ser null
            // Regla: Si UsaFormatoTicket=true, UrlLogo puede ser null (formato ticket no usa logo)
            ISerieFactura[] todasLasSeries = new ISerieFactura[]
            {
                new SerieNV(),
                new SerieUL(),
                new SerieEV(),
                new SerieGB(),
                new SerieCV(),
                new SerieRV(),
                new SerieRC()
            };

            foreach (var serie in todasLasSeries)
            {
                if (!serie.UsaFormatoTicket)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(serie.UrlLogo),
                        $"Serie {serie.GetType().Name}: Si UsaFormatoTicket=false, UrlLogo no puede ser null");
                }
                // Si UsaFormatoTicket=true, UrlLogo puede ser null o no - ambas opciones son válidas
            }
        }

        #endregion
    }
}
