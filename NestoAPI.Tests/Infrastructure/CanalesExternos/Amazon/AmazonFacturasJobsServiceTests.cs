using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Models.CanalesExternos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: el job que cierra el bucle de las facturas subidas a Amazon — consulta
    /// getFeed de las filas ENVIADA y persiste el estado final con su informe.
    /// </summary>
    [TestClass]
    public class AmazonFacturasJobsServiceTests
    {
        private IAlmacenFacturasAmazon almacen;
        private IAmazonFeedsGateway gateway;
        private AmazonFacturasJobsService job;

        [TestInitialize]
        public void Inicializar()
        {
            almacen = A.Fake<IAlmacenFacturasAmazon>();
            gateway = A.Fake<IAmazonFeedsGateway>();
            job = new AmazonFacturasJobsService(almacen, gateway);
        }

        [TestMethod]
        public async Task ProcesarPendientes_FeedTerminado_GuardaEstadoEInforme()
        {
            A.CallTo(() => almacen.ObtenerPendientesResultado()).Returns(new List<AmazonFacturaSubida>
            {
                new AmazonFacturaSubida { Id = 7, FeedId = "feed-1", Pedido = 922000 }
            });
            A.CallTo(() => gateway.ObtenerFeedAsync("feed-1"))
                .Returns(Task.FromResult(new AmazonFeedEstado { FeedId = "feed-1", ProcessingStatus = "DONE", ResultFeedDocumentId = "doc-r" }));
            A.CallTo(() => gateway.DescargarInformeFeedAsync("doc-r")).Returns(Task.FromResult("1 de 1 correcto"));

            await job.ProcesarPendientesAsync();

            A.CallTo(() => almacen.ActualizarResultado(7, "DONE", "1 de 1 correcto")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarPendientes_FeedEnProceso_NoTocaLaFila()
        {
            A.CallTo(() => almacen.ObtenerPendientesResultado()).Returns(new List<AmazonFacturaSubida>
            {
                new AmazonFacturaSubida { Id = 7, FeedId = "feed-1" }
            });
            A.CallTo(() => gateway.ObtenerFeedAsync("feed-1"))
                .Returns(Task.FromResult(new AmazonFeedEstado { FeedId = "feed-1", ProcessingStatus = "IN_PROGRESS" }));

            await job.ProcesarPendientesAsync();

            A.CallTo(() => almacen.ActualizarResultado(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EsEstadoFinal_SoloDoneFatalCancelled()
        {
            Assert.IsTrue(AmazonFacturasJobsService.EsEstadoFinal("DONE"));
            Assert.IsTrue(AmazonFacturasJobsService.EsEstadoFinal("FATAL"));
            Assert.IsTrue(AmazonFacturasJobsService.EsEstadoFinal("CANCELLED"));
            Assert.IsFalse(AmazonFacturasJobsService.EsEstadoFinal("IN_QUEUE"));
            Assert.IsFalse(AmazonFacturasJobsService.EsEstadoFinal("IN_PROGRESS"));
            Assert.IsFalse(AmazonFacturasJobsService.EsEstadoFinal(null));
        }
    }
}
