using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#340 (Fase 2, retirada de flags 20/08/26): el flag MotorPdfFacturas llevaba semanas
    /// al 100% en QuestPDF (el "(defecto)" y todos los usuarios): el selector por parámetro se
    /// retiró y el generador es SIEMPRE QuestPDF, sin consultar ParametrosUsuario. Estos tests
    /// sustituyen a los del selector por flag.
    /// </summary>
    [TestClass]
    public class GestorFacturasMotorSelectionTests
    {
        [TestMethod]
        public void ObtenerGeneradorPdf_SiempreQuestPdfSinConsultarParametros()
        {
            var lectorParametros = A.Fake<ILectorParametrosUsuario>();
            var gestor = new GestorFacturas(A.Fake<IServicioFacturas>(), lectorParametros);

            IGeneradorPdfFacturas conUsuario = gestor.ObtenerGeneradorPdf("Carlos");
            IGeneradorPdfFacturas sinUsuario = gestor.ObtenerGeneradorPdf(null);

            Assert.IsInstanceOfType(conUsuario, typeof(GeneradorPdfFacturasQuestPdf));
            Assert.IsInstanceOfType(sinUsuario, typeof(GeneradorPdfFacturasQuestPdf));
            A.CallTo(() => lectorParametros.LeerParametro(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }
    }
}
