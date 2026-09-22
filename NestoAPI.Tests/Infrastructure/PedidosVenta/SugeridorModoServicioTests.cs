using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#506 (Carlos, 22/09/26): el modo de servicio por defecto según el stock real del pedido,
    /// con los colores del correo: todo verde → Todo junto; solo verde y rojo → Ahora lo que hay, el
    /// resto de una vez; alguna rosa → Tras reponer de tiendas. Nunca «Según vaya entrando».
    /// </summary>
    [TestClass]
    public class SugeridorModoServicioTests
    {
        private IGestorStocks stocks;

        [TestInitialize]
        public void Setup()
        {
            stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock("VERDE", A<string>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock("ROSA", A<string>._)).Returns(SugeridorModoServicio.ROSA);
            A.CallTo(() => stocks.ColorStock("ROJO", A<string>._)).Returns(SugeridorModoServicio.ROJO);
        }

        private static PedidoVentaDTO Pedido(params string[] productos)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() };
            int id = 1;
            foreach (string p in productos)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { id = id++, Producto = p, Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            }
            return pedido;
        }

        [TestMethod]
        public void TodoVerde_TodoJunto()
        {
            var s = SugeridorModoServicio.Sugerir(Pedido("VERDE", "VERDE"), stocks);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, s.Modo);
            Assert.AreEqual("Todo junto", s.Nombre);
            Assert.AreEqual(2, s.LineasVerdes);
        }

        [TestMethod]
        public void VerdeYRojo_AhoraLoQueHayYElRestoDeUnaVez()
        {
            var s = SugeridorModoServicio.Sugerir(Pedido("VERDE", "ROJO"), stocks);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, s.Modo);
            Assert.AreEqual(1, s.LineasRojas);
        }

        [TestMethod]
        public void SoloRojo_TambienAhoraLoQueHay()
        {
            // No hay nada en las tiendas que reponer: «Tras reponer de tiendas» no significaría nada.
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ,
                SugeridorModoServicio.Sugerir(Pedido("ROJO"), stocks).Modo);
        }

        [TestMethod]
        public void AlgunaRosa_TrasReponerDeTiendas_HayaLoQueHaya()
        {
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, SugeridorModoServicio.Sugerir(Pedido("ROSA"), stocks).Modo);
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, SugeridorModoServicio.Sugerir(Pedido("VERDE", "ROSA"), stocks).Modo);
            // Decisión 22/09/26: rosa + rojo → 3, aunque la parte roja pueda llegar luego en más de una entrega.
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, SugeridorModoServicio.Sugerir(Pedido("VERDE", "ROSA", "ROJO"), stocks).Modo);
        }

        [TestMethod]
        public void NuncaSugiereSegunVayaEntrando()
        {
            foreach (var combinacion in new[] { new[] { "VERDE" }, new[] { "ROJO" }, new[] { "ROSA" }, new[] { "VERDE", "ROJO", "ROSA" } })
            {
                Assert.AreNotEqual(Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO, SugeridorModoServicio.Sugerir(Pedido(combinacion), stocks).Modo);
            }
        }

        [TestMethod]
        public void SinLineasDeProducto_ElDefectoDeSiempre()
        {
            // Decisión 22/09/26: solo texto o cuentas contables → no hay stock que mirar.
            var pedido = Pedido();
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 9, Producto = "62400002", texto = "Portes", Cantidad = 1, PrecioUnitario = 3.5M, tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE });

            var s = SugeridorModoServicio.Sugerir(pedido, stocks);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.POR_DEFECTO, s.Modo);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void LasLineasRegaladasCuentanIgual_YLasDeCantidadCeroNo()
        {
            // Un regalo sin stock también se sirve: cuenta como línea. Cantidad 0 no es pedido.
            var pedido = Pedido("VERDE");
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 5, Producto = "ROSA", Cantidad = 1, PrecioUnitario = 0, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 6, Producto = "ROJO", Cantidad = 0, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });

            var s = SugeridorModoServicio.Sugerir(pedido, stocks);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, s.Modo);
            Assert.AreEqual(0, s.LineasRojas, "La línea con cantidad 0 no cuenta");
        }

        [TestMethod]
        public void ParametroModoForzado_SoloUnModoValidoManda()
        {
            Assert.AreEqual((byte)1, Constantes.Pedidos.ModosServicio.ParsearModoForzado("1"));
            Assert.AreEqual((byte)4, Constantes.Pedidos.ModosServicio.ParsearModoForzado(" 4 "));
            Assert.IsNull(Constantes.Pedidos.ModosServicio.ParsearModoForzado(Constantes.Pedidos.ModosServicio.PARAMETRO_SEGUN_STOCK), "0 = según stock");
            Assert.IsNull(Constantes.Pedidos.ModosServicio.ParsearModoForzado(null));
            Assert.IsNull(Constantes.Pedidos.ModosServicio.ParsearModoForzado("stock"));
            Assert.IsNull(Constantes.Pedidos.ModosServicio.ParsearModoForzado("7"));
        }
    }
}
