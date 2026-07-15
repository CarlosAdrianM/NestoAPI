using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#300: matching de la población del envío contra el catálogo de DataTrans
    /// (BuscarPoblacion). Los casos son los CINCO envíos reales rechazados con 405 entre el
    /// 30/06 y el 15/07/26, con las poblaciones que devuelve el catálogo real para cada CP.
    /// </summary>
    [TestClass]
    public class CanalizadorPoblacionDataTransTests
    {
        [TestMethod]
        public void ElegirPoblacion_TildeDistinta_CasaConElTextoDelCatalogo()
        {
            // Envío 246812 (CP 33401): "AVILÉS" debe casar con "AVILES" (el catálogo va sin tildes).
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("AVILÉS",
                new List<string> { "AVILES", "CALIERO (ENTREVIÑAS AVILES)", "ENTREVIÑAS (AVILES)", "SABLERA, LA (ENTREVIÑAS AVILES)", "SAN CRISTOBAL (CASTRILLON)" });

            Assert.AreEqual("AVILES", resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_ArticuloDistinto_CasaConElTextoDelCatalogo()
        {
            // Envío 246655 (CP 28750): "SAN AGUSTÍN DE GUADALIX" vs "SAN AGUSTIN DEL GUADALIX" (DE/DEL).
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("SAN AGUSTÍN DE GUADALIX",
                new List<string> { "SAN AGUSTIN DEL GUADALIX", "VALDELAGUA (SAN AGUSTIN DEL GUADALIX)" });

            Assert.AreEqual("SAN AGUSTIN DEL GUADALIX", resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_NombreCompuesto_CasaPorContencion()
        {
            // Envío 246459 (CP 20008): "SAN SEBASTIAN" está contenido en "DONOSTIA-SAN SEBASTIAN".
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("SAN SEBASTIAN",
                new List<string> { "DONOSTIA-SAN SEBASTIAN", "IGELDO" });

            Assert.AreEqual("DONOSTIA-SAN SEBASTIAN", resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_VarianteOrtografica_CasaPorTokensParecidos()
        {
            // Envío 246127 (CP 07590): "CALA RAJADA" vs "CALA RATJADA" (grafía castellana/catalana).
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("CALA RAJADA",
                new List<string> { "CALA GAT", "CALA LLITERAS", "CALA RATJADA", "ES CARREGADOR", "PEDRUSCADA", "SON MOLL" });

            Assert.AreEqual("CALA RATJADA", resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_CatalogoConUnaSolaPoblacion_DevuelveEsa()
        {
            // Con una sola población DTX canaliza por CP e ignora el texto (les hemos mandado
            // "IBIZA" contra su "EIVISSA" y funcionó): si solo hay una, es esa.
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("IBIZA",
                new List<string> { "EIVISSA" });

            Assert.AreEqual("EIVISSA", resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_SinMatchRazonable_DevuelveNull()
        {
            string resultado = CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("MOSTOLES",
                new List<string> { "VILLAPERI", "LUGONES", "SAN CLAUDIO" });

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ElegirPoblacion_EntradasVacias_DevuelveNull()
        {
            Assert.IsNull(CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo(null,
                new List<string> { "AVILES", "OTRA" }));
            Assert.IsNull(CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("AVILES", new List<string>()));
            Assert.IsNull(CanalizadorPoblacionDataTrans.ElegirPoblacionCatalogo("AVILES", null));
        }
    }
}
