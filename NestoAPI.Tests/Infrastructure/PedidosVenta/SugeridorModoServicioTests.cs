using System.Collections.Generic;
using System.Linq;
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
            // NestoAPI#515: el sugeridor pide el color CON la cantidad, porque el pedido aún no existe.
            // El producto se llama como su color; el sufijo distingue referencias distintas del mismo color
            // (desde #515 el color se pide una vez por producto y almacén, no una por línea).
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("VERDE"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("ROSA"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROSA);
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("ROJO"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROJO);
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
            var s = SugeridorModoServicio.Sugerir(Pedido("VERDE1", "VERDE2"), stocks);

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
        public void SoloRojo_TodoJunto()
        {
            // NestoAPI#561 (Carlos, 29/09/26, pedido 927293): sin nada con stock, «ahora lo que hay» no serviría nada
            // (salían solo los portes) y «Tras reponer» tampoco (no hay nada en las tiendas): todo junto.
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO,
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
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
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
        public void SePideElColorConLaCantidadDeLaLinea()
        {
            // NestoAPI#515: sin la cantidad, 7 unidades en el almacén salían verdes aunque se pidieran 8.
            var pedido = Pedido();
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 1, Producto = "VERDE", Cantidad = 8, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });

            _ = SugeridorModoServicio.Sugerir(pedido, stocks);

            A.CallTo(() => stocks.ColorStock("VERDE", "ALG", 8)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void DosLineasDelMismoProductoYAlmacen_ConsumenElMismoStock_YSeMiranJuntas()
        {
            // NestoAPI#515: la línea normal y la de regalo de una oferta son las dos tipoLinea PRODUCTO y
            // salen del mismo almacén: si se preguntan por separado, el stock se cuenta dos veces.
            var pedido = Pedido();
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 1, Producto = "VERDE", Cantidad = 6, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 2, Producto = "VERDE", Cantidad = 1, PrecioUnitario = 0, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });

            var s = SugeridorModoServicio.Sugerir(pedido, stocks);

            A.CallTo(() => stocks.ColorStock("VERDE", "ALG", 7)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, s.LineasVerdes, "Un solo color por producto y almacén");
        }

        [TestMethod]
        public void ElMismoProductoEnDosAlmacenes_SeMiraPorSeparado()
        {
            var pedido = Pedido();
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 1, Producto = "VERDE", Cantidad = 3, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 2, Producto = "VERDE", Cantidad = 2, PrecioUnitario = 10, almacen = "REI", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });

            var s = SugeridorModoServicio.Sugerir(pedido, stocks);

            A.CallTo(() => stocks.ColorStock("VERDE", "ALG", 3)).MustHaveHappenedOnceExactly();
            A.CallTo(() => stocks.ColorStock("VERDE", "REI", 2)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, s.LineasVerdes);
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

        // ----- NestoAPI#606 (kits, Carlos 13/10/26): «Sí, contar kits también ahí» -----

        /// <summary>
        /// Pedido 928288: 70 × 43192 con 64 en Algete y 10 en Reina (rosa: «Tras reponer de tiendas»), pero el kit 43191
        /// (70 × 43192) tiene 11 libres en Algete: desmontando uno sale todo hoy. Mismos datos que la fecha de entrega.
        /// </summary>
        private static DatosSombraModoServicio Datos928288(int stockKit = 11, int pendientesKit = 0)
        {
            var datos = new DatosSombraModoServicio();
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("43192", "ALG"), 64);
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("43192", "REI"), 10);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave("43192"), 74);
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave("43191", "ALG"), stockKit);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave("43191", "ALG"), pendientesKit);
            datos.Kits.Add(new ComponenteKitFechaEntregaAgencia { Kit = "43191", Componente = "43192", Cantidad = 70 });
            return datos;
        }

        private static PedidoVentaDTO Pedido928288()
        {
            PedidoVentaDTO pedido = Pedido("43192");
            pedido.Lineas.First().Cantidad = 70;
            return pedido;
        }

        private static IFuenteKitsModoServicio Fuente(DatosSombraModoServicio datos)
        {
            IFuenteKitsModoServicio fuente = A.Fake<IFuenteKitsModoServicio>();
            A.CallTo(() => fuente.LeerConKits(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(datos);
            return fuente;
        }

        [TestMethod]
        public void Kits_928288_SinKitsTrasReponer_ConKitsTodoJunto()
        {
            DatosSombraModoServicio datos = Datos928288();
            IGestorStocks colores = new GestorStocksSombra(datos.Resumen);

            SugeridorModoServicio.Sugerencia sinKits = SugeridorModoServicio.Sugerir(Pedido928288(), colores);
            SugeridorModoServicio.Sugerencia conKits = SugeridorModoServicio.Sugerir(Pedido928288(), colores, Fuente(datos));

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, sinKits.Modo, "Sin kits, lo de siempre");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, conKits.Modo);
            Assert.AreEqual(1, conKits.LineasVerdes);
            Assert.AreEqual(0, conKits.LineasRosas);
            StringAssert.EndsWith(conKits.Motivo, SugeridorModoServicio.MOTIVO_CON_KITS);
            CollectionAssert.AreEqual(new List<byte> { Constantes.Pedidos.ModosServicio.TODO_JUNTO }, conKits.ModosPermitidos,
                "Sale todo hoy: como cualquier pedido todo verde");
        }

        [TestMethod]
        public void Kits_LosDebenOtrosPedidos_NoCuentan()
        {
            DatosSombraModoServicio datos = Datos928288(stockKit: 11, pendientesKit: 11);

            SugeridorModoServicio.Sugerencia s = SugeridorModoServicio.Sugerir(Pedido928288(), new GestorStocksSombra(datos.Resumen), Fuente(datos));

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, s.Modo);
            Assert.IsFalse(s.Motivo.Contains(SugeridorModoServicio.MOTIVO_CON_KITS));
        }

        [TestMethod]
        public void Kits_SiFallaLaLectura_LaSugerenciaDeSiempre()
        {
            DatosSombraModoServicio datos = Datos928288();
            IFuenteKitsModoServicio fuente = A.Fake<IFuenteKitsModoServicio>();
            A.CallTo(() => fuente.LeerConKits(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Throws(new System.InvalidOperationException("BD caída"));

            SugeridorModoServicio.Sugerencia s = SugeridorModoServicio.Sugerir(Pedido928288(), new GestorStocksSombra(datos.Resumen), fuente);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, s.Modo);
        }

        [TestMethod]
        public void Kits_TodoVerdeOFueraDeAlgete_NoSeLeen()
        {
            IFuenteKitsModoServicio fuente = A.Fake<IFuenteKitsModoServicio>();
            PedidoVentaDTO mixto = Pedido("VERDE", "ROSA");
            mixto.Lineas.Last().almacen = "AMZ";

            _ = SugeridorModoServicio.Sugerir(Pedido("VERDE1", "VERDE2"), stocks, fuente);
            _ = SugeridorModoServicio.Sugerir(mixto, stocks, fuente);

            A.CallTo(() => fuente.LeerConKits(A<string>._, A<int?>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Kits_SoloLeeLasLineasDeAlgeteSinStock_YExcluyeElPropioPedido()
        {
            DatosSombraModoServicio datos = Datos928288();
            IFuenteKitsModoServicio fuente = Fuente(datos);
            PedidoVentaDTO pedido = Pedido928288();
            pedido.numero = 928288;
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 2, Producto = "VERDE", Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            IGestorStocks colores = A.Fake<IGestorStocks>();
            A.CallTo(() => colores.ColorStock("VERDE", A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => colores.ColorStock("43192", A<string>._, 70)).Returns(SugeridorModoServicio.ROSA);
            A.CallTo(() => colores.ColorStock("43192", A<string>._, 64)).Returns(SugeridorModoServicio.VERDE);

            SugeridorModoServicio.Sugerencia s = SugeridorModoServicio.Sugerir(pedido, colores, fuente);

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, s.Modo);
            A.CallTo(() => fuente.LeerConKits("1", 928288, A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "43192" }))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Kits_LaPlantillaYElPedidoSinModo_CuentanLosKits()
        {
            DatosSombraModoServicio datos = Datos928288();
            var controller = new NestoAPI.Controllers.PedidosVentaController(A.Fake<NVEntities>())
            {
                LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                Stocks = () => new GestorStocksSombra(datos.Resumen),
                FuenteKits = () => Fuente(datos)
            };
            PedidoVentaDTO pedido = Pedido928288();
            pedido.cliente = "KITS606"; // sin caché por huella compartida con otros tests

            var respuesta = controller.PostModoServicioSugerido(pedido) as System.Web.Http.Results.OkNegotiatedContentResult<SugeridorModoServicio.Sugerencia>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, respuesta.Content.Modo, "Plantilla");
            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TODO_JUNTO, controller.ModoServicioAlCrear(Pedido928288()), "PostPedidoVenta sin modo");
        }

        [TestMethod]
        public void Kits_ConUnDobleDeStocks_ElControladorNoVaALaBD()
        {
            DatosSombraModoServicio datos = Datos928288();
            var controller = new NestoAPI.Controllers.PedidosVentaController(A.Fake<NVEntities>())
            {
                LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                Stocks = () => new GestorStocksSombra(datos.Resumen)
            };

            Assert.AreEqual(Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS, controller.ModoServicioAlCrear(Pedido928288()));
        }
    }
}
