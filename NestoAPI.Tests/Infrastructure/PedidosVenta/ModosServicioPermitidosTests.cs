using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#518 (Carlos, 23/09/26): solo se pueden elegir los modos de servicio con sentido para el pedido.
    /// Tienda → solo «Según vaya entrando»; Algete todo verde → solo «Todo junto»; Algete sin rosas → todos
    /// menos «Tras reponer de tiendas»; con alguna rosa, sin líneas o almacén de otro tipo → todos.
    /// Al guardar, un modo no permitido se RECHAZA con el que vale (no se corrige en silencio).
    /// </summary>
    [TestClass]
    public class ModosServicioPermitidosTests
    {
        private IGestorStocks stocks;

        [TestInitialize]
        public void Setup()
        {
            stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("VERDE"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("ROSA"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROSA);
            A.CallTo(() => stocks.ColorStock(A<string>.That.StartsWith("ROJO"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROJO);
        }

        private static PedidoVentaDTO Pedido(string almacen, params string[] productos)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 0, cliente = "15191", contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() };
            int id = 1;
            foreach (string p in productos)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { id = id++, Producto = p, Cantidad = 1, PrecioUnitario = 10, almacen = almacen, tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            }
            return pedido;
        }

        private static List<byte> Permitidos(ModosServicioPermitidos.TipoAlmacen tipo, int verdes, int rosas, int rojas, bool hayLineas = true)
            => ModosServicioPermitidos.Permitidos(ModosServicioPermitidos.Calcular(tipo, verdes, rosas, rojas, hayLineas));

        // ---- Núcleo puro: la tabla completa ----

        [TestMethod]
        public void Tienda_SoloSegunVayaEntrando_PaseLoQuePaseConElStock()
        {
            CollectionAssert.AreEqual(new List<byte> { M.SEGUN_VAYA_ENTRANDO }, Permitidos(ModosServicioPermitidos.TipoAlmacen.Tienda, 3, 0, 0));
            CollectionAssert.AreEqual(new List<byte> { M.SEGUN_VAYA_ENTRANDO }, Permitidos(ModosServicioPermitidos.TipoAlmacen.Tienda, 1, 2, 1));
        }

        [TestMethod]
        public void Algete_TodoVerde_SoloTodoJunto_ConMotivoEnLosDemas()
        {
            var modos = ModosServicioPermitidos.Calcular(ModosServicioPermitidos.TipoAlmacen.Central, 3, 0, 0, true);

            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO }, ModosServicioPermitidos.Permitidos(modos));
            StringAssert.Contains(modos.Single(m => m.Modo == M.TRAS_REPONER_DE_TIENDAS).Motivo, "stock en Algete");
            Assert.IsNull(modos.Single(m => m.Modo == M.TODO_JUNTO).Motivo);
        }

        [TestMethod]
        public void Algete_SinRosas_TodosMenosTrasReponer()
        {
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ },
                Permitidos(ModosServicioPermitidos.TipoAlmacen.Central, 2, 0, 1), "Verde + rojo");
            CollectionAssert.AreEqual(new List<byte> { M.TODO_JUNTO, M.SEGUN_VAYA_ENTRANDO, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ },
                Permitidos(ModosServicioPermitidos.TipoAlmacen.Central, 0, 0, 2), "Solo rojo");
        }

        [TestMethod]
        public void Algete_ConAlgunaRosa_Todos()
        {
            Assert.AreEqual(4, Permitidos(ModosServicioPermitidos.TipoAlmacen.Central, 1, 1, 1).Count);
        }

        [TestMethod]
        public void SinLineasDeProducto_OAlmacenDeOtroTipo_Todos()
        {
            Assert.AreEqual(4, Permitidos(ModosServicioPermitidos.TipoAlmacen.Central, 0, 0, 0, hayLineas: false).Count);
            Assert.AreEqual(4, Permitidos(ModosServicioPermitidos.TipoAlmacen.Otro, 3, 0, 0).Count, "Amazon (AMZ) no se restringe");
        }

        [TestMethod]
        public void Clasificar_ConAlgunaLineaDeAlgeteMandaAlgete_ySoloEsTiendaSiTodasSonDeTienda()
        {
            LineaPedidoVentaDTO L(string a) => new LineaPedidoVentaDTO { almacen = a };
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Central, ModosServicioPermitidos.Clasificar(new[] { L("REI"), L("ALG ") }));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Tienda, ModosServicioPermitidos.Clasificar(new[] { L("REI"), L("ALC") }));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Otro, ModosServicioPermitidos.Clasificar(new[] { L("AMZ") }));
            Assert.AreEqual(ModosServicioPermitidos.TipoAlmacen.Otro, ModosServicioPermitidos.Clasificar(new[] { L("REI"), L("AMZ") }));
        }

        // ---- Sugerencia (lo que devuelve POST api/PedidosVenta/ModoServicioSugerido) ----

        [TestMethod]
        public void Sugerir_EnTienda_SugiereSegunVayaEntrando_SinMirarElStock()
        {
            var s = SugeridorModoServicio.Sugerir(Pedido("REI", "VERDE1", "ROSA1"), stocks);

            Assert.AreEqual(M.SEGUN_VAYA_ENTRANDO, s.Modo);
            CollectionAssert.AreEqual(new List<byte> { M.SEGUN_VAYA_ENTRANDO }, s.ModosPermitidos);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sugerir_ElModoSugeridoEstaSiempreEntreLosPermitidos()
        {
            foreach (var pedido in new[] { Pedido("ALG", "VERDE1"), Pedido("ALG", "VERDE1", "ROJO1"), Pedido("ALG", "ROSA1"), Pedido("ALC", "ROJO1"), Pedido("AMZ", "VERDE1") })
            {
                var s = SugeridorModoServicio.Sugerir(pedido, stocks);
                CollectionAssert.Contains(s.ModosPermitidos, s.Modo);
                Assert.AreEqual(4, s.Modos.Count);
            }
        }

        [TestMethod]
        public void AplicarForzado_PermitidoSeRespeta_NoPermitidoSeProponeElDelStockYSeExplica()
        {
            var todoVerde = SugeridorModoServicio.Sugerir(Pedido("ALG", "VERDE1"), stocks);

            var forzado3 = SugeridorModoServicio.AplicarForzado(todoVerde, M.TRAS_REPONER_DE_TIENDAS);
            Assert.AreEqual(M.TODO_JUNTO, forzado3.Modo);
            StringAssert.Contains(forzado3.Motivo, "ModoServicioPorDefecto");
            Assert.AreEqual(M.TODO_JUNTO, todoVerde.Modo, "No se toca la sugerencia de la caché");

            var conRosa = SugeridorModoServicio.Sugerir(Pedido("ALG", "ROSA1"), stocks);
            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, SugeridorModoServicio.AplicarForzado(conRosa, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ).Modo);
        }

        // ---- Al guardar ----

        [TestMethod]
        public void AlCrear_ModoNoPermitido_SeRechazaConElQueValeYUnMensajeAccionable()
        {
            var pedido = Pedido("ALG", "VERDE1", "VERDE2");
            pedido.modoServicio = M.TRAS_REPONER_DE_TIENDAS;

            var ex = Assert.ThrowsException<ModoServicioNoPermitidoException>(() => ValidadorModoServicio.ComprobarAlCrear(pedido, stocks));

            Assert.AreEqual(M.TODO_JUNTO, ex.ModoSugerido);
            Assert.AreEqual(ModoServicioNoPermitidoException.CODIGO, ex.GetErrorCode());
            StringAssert.Contains(ex.Message, "El stock ha cambiado");
            StringAssert.Contains(ex.Message, "Elige «Todo junto»");
            Assert.AreEqual((byte)M.TODO_JUNTO, ex.Context.AdditionalData["modoSugerido"]);
        }

        [TestMethod]
        public void AlCrear_EnTienda_NoSeRechaza_SeCorrigeASegunVayaEntrando()
        {
            // Decisión de Carlos (23/09/26): en tienda el cliente está delante; no se rechaza, se guarda con el 2.
            var pedido = Pedido("ALC", "VERDE1");
            pedido.modoServicio = M.TODO_JUNTO;

            byte? corregido = ValidadorModoServicio.ComprobarAlCrear(pedido, stocks);

            Assert.AreEqual((byte?)M.SEGUN_VAYA_ENTRANDO, corregido);
        }

        [TestMethod]
        public void Sugerir_Amazon_SinRestriccionPeroTodoJuntoPorDefecto()
        {
            // Decisión de Carlos (23/09/26): AMZ no se restringe, pero por defecto sale todo junto.
            SugeridorModoServicio.Sugerencia s = SugeridorModoServicio.Sugerir(Pedido("AMZ", "ROJO1"), stocks);

            Assert.AreEqual(M.TODO_JUNTO, s.Modo);
            Assert.AreEqual(4, s.ModosPermitidos.Count);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AlCrear_SinModo_NoSeRechaza_NiModoPermitido()
        {
            ValidadorModoServicio.ComprobarAlCrear(Pedido("ALG", "VERDE1"), stocks);
            var pedido = Pedido("ALG", "VERDE1", "ROJO1");
            pedido.modoServicio = M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ;
            ValidadorModoServicio.ComprobarAlCrear(pedido, stocks);
        }

        [TestMethod]
        public void AlCrear_MarketplaceEnModo1DesdeAlgeteOAmazon_NuncaSeRechaza()
        {
            foreach (var pedido in new[] { Pedido("ALG", "ROSA1", "ROJO1"), Pedido("AMZ", "ROJO1") })
            {
                pedido.modoServicio = M.TODO_JUNTO;
                ValidadorModoServicio.ComprobarAlCrear(pedido, stocks);
            }
        }

        [TestMethod]
        public void AlModificar_SinCambioDeModo_NoSeComprueba()
        {
            var pedido = Pedido("ALG", "VERDE1");
            ValidadorModoServicio.ComprobarAlModificar(pedido, M.TRAS_REPONER_DE_TIENDAS, M.TRAS_REPONER_DE_TIENDAS, stocks);
            A.CallTo(() => stocks.ColorStock(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AlModificar_LasRojasCuentanComoPosiblesRosas_PeroTodoVerdeYTiendaSeAplican()
        {
            // Sus unidades ya están reservadas: una rosa puede salir roja. No se rechaza «Tras reponer» por eso.
            ValidadorModoServicio.ComprobarAlModificar(Pedido("ALG", "VERDE1", "ROJO1"), M.TODO_JUNTO, M.TRAS_REPONER_DE_TIENDAS, stocks);

            Assert.ThrowsException<ModoServicioNoPermitidoException>(() =>
                ValidadorModoServicio.ComprobarAlModificar(Pedido("ALG", "VERDE1"), M.TODO_JUNTO, M.TRAS_REPONER_DE_TIENDAS, stocks));
            Assert.AreEqual((byte?)M.SEGUN_VAYA_ENTRANDO,
                ValidadorModoServicio.ComprobarAlModificar(Pedido("REI", "ROJO1"), M.SEGUN_VAYA_ENTRANDO, M.TODO_JUNTO, stocks),
                "En tienda no se rechaza: se corrige al 2");
        }
    }
}
