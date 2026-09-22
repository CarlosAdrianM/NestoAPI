using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#515: ColorStock tiene dos usos que no son el mismo problema.
    /// El correo de pedido colorea un pedido YA GRABADO (sus unidades están dentro de
    /// UnidadesPendientesEntregarAlmacen, así que no hay que restarlas otra vez) y el sugeridor de modo
    /// de servicio colorea un pedido que TODAVÍA NO EXISTE (hay que restar a mano lo que se pide).
    /// </summary>
    [TestClass]
    public class GestorStocksColorStockTests
    {
        private const string PRODUCTO = "38093";
        private const string ALMACEN = "ALG";

        private IServicioGestorStocks servicio;
        private GestorStocks gestor;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioGestorStocks>();
            gestor = new GestorStocks(servicio);
            // El caso de la issue: 7 unidades en Algete y 4 en Reina, nada pendiente de entregar.
            A.CallTo(() => servicio.Stock(PRODUCTO, ALMACEN)).Returns(7);
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(0);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes(PRODUCTO)).Returns(11);
        }

        [TestMethod]
        public void ColorStock_SiSePidenMasDeLasQueHayEnElAlmacenPeroHayEnElConjunto_EsRosa()
        {
            // 7 en el almacén, 11 en total, se piden 8: hay que traer 1 de una tienda.
            Assert.AreEqual("DeepPink", gestor.ColorStock(PRODUCTO, ALMACEN, 8));
        }

        [TestMethod]
        public void ColorStock_SiNoLlegaNiSumandoTodosLosAlmacenes_EsRojo()
        {
            Assert.AreEqual("red", gestor.ColorStock(PRODUCTO, ALMACEN, 50));
        }

        [TestMethod]
        public void ColorStock_ElLimiteExactoDelAlmacenSigueSiendoVerde()
        {
            Assert.AreEqual("green", gestor.ColorStock(PRODUCTO, ALMACEN, 7));
            Assert.AreEqual("DeepPink", gestor.ColorStock(PRODUCTO, ALMACEN, 8), "Una unidad más ya hay que traerla");
        }

        [TestMethod]
        public void ColorStock_ElLimiteExactoDelConjuntoSigueSiendoRosa()
        {
            Assert.AreEqual("DeepPink", gestor.ColorStock(PRODUCTO, ALMACEN, 11));
            Assert.AreEqual("red", gestor.ColorStock(PRODUCTO, ALMACEN, 12));
        }

        [TestMethod]
        public void ColorStock_LoPendienteDeEntregarSigueContando()
        {
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(5);

            Assert.AreEqual("green", gestor.ColorStock(PRODUCTO, ALMACEN, 2), "7 - 5 pendientes = 2 libres");
            Assert.AreEqual("DeepPink", gestor.ColorStock(PRODUCTO, ALMACEN, 3));
        }

        [TestMethod]
        public void ColorStock_LaFirmaDeSiempre_ColoreaIgualQueAntes_ParaElCorreoDePedido()
        {
            // Regresión: el correo pinta un pedido ya grabado; sus unidades están en pendientes de
            // entregar, así que restarlas otra vez las contaría dos veces.
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(8);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes(PRODUCTO)).Returns(3);
            Assert.AreEqual("DeepPink", gestor.ColorStock(PRODUCTO, ALMACEN));

            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(7);
            Assert.AreEqual("green", gestor.ColorStock(PRODUCTO, ALMACEN));

            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(8);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes(PRODUCTO)).Returns(-1);
            Assert.AreEqual("red", gestor.ColorStock(PRODUCTO, ALMACEN));
        }

        [TestMethod]
        public void ColorStock_ConCantidadCero_EsLaFirmaDeSiempre()
        {
            A.CallTo(() => servicio.UnidadesPendientesEntregarAlmacen(PRODUCTO, ALMACEN)).Returns(8);
            A.CallTo(() => servicio.UnidadesDisponiblesTodosLosAlmacenes(PRODUCTO)).Returns(3);

            Assert.AreEqual(gestor.ColorStock(PRODUCTO, ALMACEN), gestor.ColorStock(PRODUCTO, ALMACEN, 0));
        }
    }
}
