using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: orquestación de "facturar el pedido de Amazon y subir el PDF de la factura"
    /// (feed UPLOAD_VAT_INVOICE). El pedido se localiza por empresa+número, el AmazonOrderId sale
    /// de los comentarios y el marketplace se resuelve preguntando a Amazon (no se persiste).
    /// </summary>
    [TestClass]
    public class ServicioFacturasAmazonTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 922000;
        private const string AMAZON_ORDER_ID = "171-1234567-1234567";
        private const string MARKETPLACE_ES = "A1RKKUPIHCS9HS";
        private const string MARKETPLACE_TURQUIA = "A33AVAJ2PDY3EV";

        private NVEntities db;
        private IGestorFacturas gestor;
        private IAmazonFeedsGateway gateway;
        private IAlmacenFacturasAmazon almacen;
        private ServicioFacturasAmazon servicio;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            gestor = A.Fake<IGestorFacturas>();
            gateway = A.Fake<IAmazonFeedsGateway>();
            almacen = A.Fake<IAlmacenFacturasAmazon>();
            servicio = new ServicioFacturasAmazon(db, gestor, gateway, almacen);

            ConfigurarPedido(new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Comentarios = $"FBA {AMAZON_ORDER_ID}\r\nCumplimiento por Amazon" });
            ConfigurarLineas();

            A.CallTo(() => gateway.ObtenerPedidoAsync(AMAZON_ORDER_ID))
                .Returns(Task.FromResult(new AmazonPedidoInfo { AmazonOrderId = AMAZON_ORDER_ID, MarketplaceId = MARKETPLACE_ES, SalesChannel = "Amazon.es" }));
            A.CallTo(() => gateway.CrearDocumentoFeedAsync(ServicioFacturasAmazon.CONTENT_TYPE_PDF))
                .Returns(Task.FromResult(new AmazonFeedDocumento { FeedDocumentId = "doc-1", Url = "https://subida" }));
            A.CallTo(() => gateway.CrearFeedAsync(A<string>._, A<string>._, A<string>._, A<IReadOnlyDictionary<string, string>>._))
                .Returns(Task.FromResult("feed-1"));
            A.CallTo(() => gestor.FacturasEnPDF(A<List<Factura>>._, false, A<string>._, false))
                .Returns(new ByteArrayContent(new byte[] { 1, 2, 3 }));
        }

        [TestMethod]
        public async Task FacturarYSubir_PedidoYaFacturado_NoVuelveAFacturarYSubeElPdf()
        {
            // El Nº_Factura de las líneas viene con relleno (legacy): se recorta.
            ConfigurarLineas(
                new LinPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.FACTURA, Nº_Factura = "NV26100200  " });

            SubirFacturaAmazonResponseDTO respuesta = await servicio.FacturarYSubirAsync(EMPRESA, PEDIDO, "carlos");

            Assert.AreEqual("NV26100200", respuesta.NumeroFactura);
            Assert.AreEqual("feed-1", respuesta.FeedId);
            Assert.AreEqual(EstadosFacturaAmazon.ENVIADA, respuesta.Estado);
            Assert.AreEqual(AMAZON_ORDER_ID, respuesta.AmazonOrderId);
            A.CallTo(() => gestor.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => gateway.SubirDocumentoAsync("https://subida", A<byte[]>.That.IsSameSequenceAs(new byte[] { 1, 2, 3 }), ServicioFacturasAmazon.CONTENT_TYPE_PDF))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => almacen.Registrar(A<AmazonFacturaSubida>.That.Matches(f =>
                    f.Pedido == PEDIDO && f.NumeroFactura == "NV26100200" && f.FeedId == "feed-1"
                    && f.Estado == EstadosFacturaAmazon.ENVIADA && f.MarketplaceId == MARKETPLACE_ES)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturarYSubir_PedidoSinFacturar_CreaLaFacturaYPropagaLosAvisos()
        {
            A.CallTo(() => gestor.CrearFactura(EMPRESA, PEDIDO, "carlos", "carlos"))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO
                {
                    NumeroFactura = "NV26100300",
                    Empresa = EMPRESA,
                    NumeroPedido = PEDIDO,
                    Avisos = new List<string> { "NIF no registrado en la AEAT" }
                }));

            SubirFacturaAmazonResponseDTO respuesta = await servicio.FacturarYSubirAsync(EMPRESA, PEDIDO, "carlos");

            Assert.AreEqual("NV26100300", respuesta.NumeroFactura);
            CollectionAssert.Contains(respuesta.Avisos, "NIF no registrado en la AEAT");
            A.CallTo(() => gestor.CrearFactura(EMPRESA, PEDIDO, "carlos", "carlos")).MustHaveHappenedOnceExactly();
            // El feed lleva el número de factura recién creado como InvoiceNumber.
            A.CallTo(() => gateway.CrearFeedAsync(ServicioFacturasAmazon.FEED_TYPE_FACTURAS, MARKETPLACE_ES, "doc-1",
                    A<IReadOnlyDictionary<string, string>>.That.Matches(o => o["metadata:InvoiceNumber"] == "NV26100300")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FacturarYSubir_PedidoSinAmazonOrderId_LanzaYNoTocaNada()
        {
            ConfigurarPedido(new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Comentarios = "Pedido manual" });

            await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(
                () => servicio.FacturarYSubirAsync(EMPRESA, PEDIDO, "carlos"));

            A.CallTo(() => gestor.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => gateway.CrearDocumentoFeedAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarYSubir_MarketplaceNoSoportado_LanzaSinFacturarNiSubir()
        {
            // Turquía no admite el feed de facturas: no se factura ni se sube nada.
            A.CallTo(() => gateway.ObtenerPedidoAsync(AMAZON_ORDER_ID))
                .Returns(Task.FromResult(new AmazonPedidoInfo { AmazonOrderId = AMAZON_ORDER_ID, MarketplaceId = MARKETPLACE_TURQUIA, SalesChannel = "Amazon.com.tr" }));

            await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(
                () => servicio.FacturarYSubirAsync(EMPRESA, PEDIDO, "carlos"));

            A.CallTo(() => gestor.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => gateway.CrearDocumentoFeedAsync(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => almacen.Registrar(A<AmazonFacturaSubida>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarYSubir_PedidoConVariasFacturas_Lanza()
        {
            ConfigurarLineas(
                new LinPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.FACTURA, Nº_Factura = "NV26100200" },
                new LinPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.FACTURA, Nº_Factura = "NV26100201" });

            await Assert.ThrowsExceptionAsync<System.InvalidOperationException>(
                () => servicio.FacturarYSubirAsync(EMPRESA, PEDIDO, "carlos"));
        }

        [TestMethod]
        public void ConstruirFeedOptions_SoloOrderIdFacturaYTipo_SinImportes()
        {
            // Sin TotalAmount/TotalVATAmount: son solo para vendedores VCS y si van deben cuadrar
            // al céntimo con Amazon; al omitirlos tampoco hay problema de divisas no EUR.
            IReadOnlyDictionary<string, string> opciones =
                ServicioFacturasAmazon.ConstruirFeedOptions(AMAZON_ORDER_ID, "NV26100200");

            Assert.AreEqual(3, opciones.Count);
            Assert.AreEqual(AMAZON_ORDER_ID, opciones["metadata:OrderId"]);
            Assert.AreEqual("NV26100200", opciones["metadata:InvoiceNumber"]);
            Assert.AreEqual("Invoice", opciones["metadata:DocumentType"]);
        }

        [TestMethod]
        public void MarketplacesSoportados_EuropeosSiTurquiaYEmiratosNo()
        {
            Assert.IsTrue(ServicioFacturasAmazon.MarketplacesSoportados.ContainsKey(MARKETPLACE_ES));
            Assert.IsTrue(ServicioFacturasAmazon.MarketplacesSoportados.ContainsKey("A1PA6795UKMFR9")); // DE
            Assert.IsFalse(ServicioFacturasAmazon.MarketplacesSoportados.ContainsKey(MARKETPLACE_TURQUIA));
            Assert.IsFalse(ServicioFacturasAmazon.MarketplacesSoportados.ContainsKey("A2VIGQ35RCS4UG")); // AE
        }

        [TestMethod]
        public void ConsultarSubidas_MapeaLasFilasDelAlmacen()
        {
            A.CallTo(() => almacen.ObtenerVarias(EMPRESA, A<IReadOnlyCollection<int>>._))
                .Returns(new List<AmazonFacturaSubida>
                {
                    new AmazonFacturaSubida { Pedido = PEDIDO, NumeroFactura = "NV26100200 ", Estado = "DONE " }
                });

            IReadOnlyList<FacturaSubidaAmazonDTO> subidas = servicio.ConsultarSubidas(EMPRESA, new[] { PEDIDO });

            Assert.AreEqual(1, subidas.Count);
            Assert.AreEqual("NV26100200", subidas[0].NumeroFactura);
            Assert.AreEqual("DONE", subidas[0].Estado);
        }

        private void ConfigurarPedido(params CabPedidoVta[] pedidos)
        {
            var fakeSet = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeSet);
            ConfigurarFakeDbSet(fakeSet, pedidos.AsQueryable());
        }

        private void ConfigurarLineas(params LinPedidoVta[] lineas)
        {
            var fakeSet = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeSet);
            ConfigurarFakeDbSet(fakeSet, lineas.AsQueryable());
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
