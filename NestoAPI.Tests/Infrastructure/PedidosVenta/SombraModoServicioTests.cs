using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#563: la sombra compara el sugeridor de colores con el de causas SIN tocar lo que ve el usuario: apagada
    /// por defecto, en segundo plano y tragándose cualquier fallo (con ELMAH moderado).
    /// </summary>
    [TestClass]
    public class SombraModoServicioTests
    {
        private Func<string> leerInterruptorOriginal;
        private Func<IRepositorioSombraModoServicio> crearRepositorioOriginal;
        private Action<Action> lanzadorOriginal;
        private Action<Exception> registradorOriginal;
        private Func<DateTime> ahoraOriginal;

        private IRepositorioSombraModoServicio repositorio;
        private List<Exception> avisos;
        private DateTime ahora;

        [TestInitialize]
        public void Setup()
        {
            leerInterruptorOriginal = SombraModoServicio.LeerInterruptor;
            crearRepositorioOriginal = SombraModoServicio.CrearRepositorio;
            lanzadorOriginal = SombraModoServicio.Lanzador;
            registradorOriginal = SombraModoServicio.Registrador;
            ahoraOriginal = SombraModoServicio.Ahora;

            repositorio = A.Fake<IRepositorioSombraModoServicio>();
            A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(Datos927312());
            avisos = new List<Exception>();
            ahora = new DateTime(2026, 9, 29, 12, 0, 0);

            SombraModoServicio.Reiniciar();
            SombraModoServicio.LeerInterruptor = () => "1";
            SombraModoServicio.CrearRepositorio = () => repositorio;
            SombraModoServicio.Lanzador = trabajo => trabajo(); // síncrono para poder comprobarlo
            SombraModoServicio.Registrador = ex => avisos.Add(ex);
            SombraModoServicio.Ahora = () => ahora;
        }

        [TestCleanup]
        public void Cleanup()
        {
            SombraModoServicio.LeerInterruptor = leerInterruptorOriginal;
            SombraModoServicio.CrearRepositorio = crearRepositorioOriginal;
            SombraModoServicio.Lanzador = lanzadorOriginal;
            SombraModoServicio.Registrador = registradorOriginal;
            SombraModoServicio.Ahora = ahoraOriginal;
            SombraModoServicio.Reiniciar();
        }

        private static void Poner(DatosSombraModoServicio datos, string producto, int stockAlg, int pendientesAlg = 0, short estado = 0, int stockTiendas = 0)
        {
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, "ALG"), stockAlg);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), stockAlg + stockTiendas);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, "ALG"), pendientesAlg);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto), pendientesAlg);
            datos.Estados[ResumenStocksProductos.Clave(producto)] = estado;
        }

        private static readonly string[] PRODUCTOS_927312 = { "24092", "23532", "35944", "35946", "34100", "33932", "34101" };

        private static DatosSombraModoServicio Datos927312()
        {
            var datos = new DatosSombraModoServicio();
            Poner(datos, "24092", 9, stockTiendas: 4);
            Poner(datos, "23532", 17, stockTiendas: 7);
            Poner(datos, "35944", 0, estado: 1);
            Poner(datos, "35946", 1, pendientesAlg: 1, estado: 1);
            Poner(datos, "34100", 0, estado: 1);
            Poner(datos, "33932", 0, estado: 1);
            Poner(datos, "34101", 0, estado: 1);
            return datos;
        }

        private static PedidoVentaDTO Pedido(string cliente, params string[] productos)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 0, cliente = cliente, contacto = "0", Usuario = "NUEVAVISION\\prueba", Lineas = new List<LineaPedidoVentaDTO>() };
            foreach (string p in productos)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = p, Cantidad = 1, PrecioUnitario = 10, almacen = "ALG", tipoLinea = Constantes.TiposLineaVenta.PRODUCTO });
            }
            return pedido;
        }

        // ---- La comparación ----

        [TestMethod]
        public void Comparar_927312_ColoresDicenAhoraLoQueHay_CausasDicenTodoJunto()
        {
            var instantanea = InstantaneaSombraModoServicio.Desde(SombraModoServicio.ORIGEN_CREAR, Pedido("1", PRODUCTOS_927312), M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ);

            FilaSombraModoServicio fila = SombraModoServicio.Comparar(instantanea, Datos927312());

            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, fila.ModoColores);
            Assert.AreEqual(M.TODO_JUNTO, fila.ModoCausas);
            Assert.IsFalse(fila.MismoModo);
            Assert.IsTrue(fila.MismosPermitidos, "Los dos permiten 1, 2 y 4");
            Assert.AreEqual("1,2,4", fila.PermitidosCausas);
            StringAssert.Contains(fila.Causas, "35946@ALG x1: sobre pedido 1 [red]");
            StringAssert.Contains(fila.Causas, "24092@ALG x1: libres 1 [green]");
        }

        [TestMethod]
        public void Comparar_927293_LosDosCoinciden_SoloTodoJunto()
        {
            var datos = new DatosSombraModoServicio();
            Poner(datos, "45149", 1, pendientesAlg: 1);
            var instantanea = InstantaneaSombraModoServicio.Desde(SombraModoServicio.ORIGEN_CREAR, Pedido("1", "45149"), M.TODO_JUNTO);

            FilaSombraModoServicio fila = SombraModoServicio.Comparar(instantanea, datos);

            Assert.AreEqual(M.TODO_JUNTO, fila.ModoColores);
            Assert.AreEqual(M.TODO_JUNTO, fila.ModoCausas);
            Assert.AreEqual("1", fila.PermitidosColores);
            Assert.AreEqual("1", fila.PermitidosCausas);
            Assert.IsTrue(fila.Coincide);
        }

        [TestMethod]
        public void Instantanea_SoloLineasDeProducto_YSinTocarElPedido()
        {
            var pedido = Pedido("1", "A");
            pedido.numero = 927293;
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "62400002", Cantidad = 1, tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE });

            var instantanea = InstantaneaSombraModoServicio.Desde(SombraModoServicio.ORIGEN_CREAR, pedido, 1);

            Assert.AreEqual(1, instantanea.PedidoReducido.Lineas.Count);
            Assert.AreEqual(927293, instantanea.Pedido);
            Assert.AreEqual(2, pedido.Lineas.Count);
            Assert.AreNotSame(pedido.Lineas.First(), instantanea.PedidoReducido.Lineas.First());
        }

        // ---- Interruptor ----

        [TestMethod]
        public void InterpretarModo_SinFilaOCualquierOtraCosa_Apagado()
        {
            Assert.AreEqual(ModoSombraModoServicio.Apagado, SombraModoServicio.InterpretarModo(null));
            Assert.AreEqual(ModoSombraModoServicio.Apagado, SombraModoServicio.InterpretarModo("0"));
            Assert.AreEqual(ModoSombraModoServicio.Apagado, SombraModoServicio.InterpretarModo("sí"));
            Assert.AreEqual(ModoSombraModoServicio.Todo, SombraModoServicio.InterpretarModo("1  "));
            Assert.AreEqual(ModoSombraModoServicio.Todo, SombraModoServicio.InterpretarModo("todo"));
            Assert.AreEqual(ModoSombraModoServicio.Diferencias, SombraModoServicio.InterpretarModo("Diferencias"));
        }

        [TestMethod]
        public void Apagada_NoTocaLaBaseDeDatos()
        {
            SombraModoServicio.LeerInterruptor = () => null;

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);

            A.CallTo(repositorio).MustNotHaveHappened();
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void SiNoSePuedeLeerElInterruptor_Apagada()
        {
            SombraModoServicio.LeerInterruptor = () => throw new InvalidOperationException("sin BD");

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);

            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public void Encendida_GuardaLaComparacionExcluyendoElPropioPedido()
        {
            var pedido = Pedido("1", PRODUCTOS_927312);
            pedido.numero = 927312;
            pedido.EsPresupuesto = true;

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_CREAR, pedido, M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ);

            A.CallTo(() => repositorio.LeerDatos("1", 927312, A<IEnumerable<string>>.That.Matches(p => p.Count() == 7))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Guardar(A<FilaSombraModoServicio>.That.Matches(f =>
                f.Pedido == 927312 && f.EsPresupuesto && f.Origen == "Crear" && f.ModoPedido == M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ
                && f.ModoCausas == M.TODO_JUNTO && !f.MismoModo))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ModoDiferencias_LoQueCoincide_NoSeGuarda()
        {
            SombraModoServicio.LeerInterruptor = () => "Diferencias";
            var datos = new DatosSombraModoServicio();
            Poner(datos, "45149", 1, pendientesAlg: 1);
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Returns(datos);

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "45149"), M.TODO_JUNTO);

            A.CallTo(() => repositorio.Guardar(A<FilaSombraModoServicio>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SinTabla_NoLeeNiEscribe_YLoAvisaUnaVez()
        {
            A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);
            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);

            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.ExisteTabla()).MustHaveHappenedOnceExactly(); // se recuerda 10 minutos
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "Issue563_SombraModoServicio.sql");
        }

        [TestMethod]
        public void FalloAlLeer_NoSale_YElmahModerado()
        {
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Throws(new TimeoutException("timeout"));

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);
            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);
            ahora = ahora.Add(SombraModoServicio.INTERVALO_ELMAH).AddMinutes(1);
            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);

            Assert.AreEqual(2, avisos.Count, "Uno al principio y otro pasado el intervalo, con el recuento de los callados");
            StringAssert.Contains(avisos[1].Message, "1 fallo más");
        }

        [TestMethod]
        public void FalloAlLanzar_NoSale()
        {
            SombraModoServicio.Lanzador = _ => throw new InvalidOperationException("sin hilos");

            SombraModoServicio.Registrar(SombraModoServicio.ORIGEN_PLANTILLA, Pedido("1", "A"), 1);

            Assert.AreEqual(1, avisos.Count);
        }

        // ---- El endpoint: la sombra no cambia la respuesta ----

        [TestMethod]
        public void PostModoServicioSugerido_ConLaSombraEncendidaYRota_DevuelveLoMismoQueApagada()
        {
            IGestorStocks stocks = A.Fake<IGestorStocks>();
            A.CallTo(() => stocks.ColorStock(A<string>.That.Matches(p => p == "24092" || p == "23532"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.VERDE);
            A.CallTo(() => stocks.ColorStock(A<string>.That.Matches(p => p != "24092" && p != "23532"), A<string>._, A<int>._)).Returns(SugeridorModoServicio.ROJO);
            A.CallTo(() => stocks.Stock(A<string>._, A<string>._)).Returns(5);
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).Throws(new InvalidOperationException("BD caída"));
            A.CallTo(() => repositorio.Guardar(A<FilaSombraModoServicio>._)).Throws(new InvalidOperationException("BD caída"));

            SugeridorModoServicio.Sugerencia Llamar(string cliente)
            {
                var controller = new PedidosVentaController(A.Fake<NVEntities>())
                {
                    LectorParametros = A.Fake<ILectorParametrosUsuario>(),
                    Stocks = () => stocks
                };
                var respuesta = controller.PostModoServicioSugerido(Pedido(cliente, PRODUCTOS_927312)) as OkNegotiatedContentResult<SugeridorModoServicio.Sugerencia>;
                Assert.IsNotNull(respuesta);
                return respuesta.Content;
            }

            SombraModoServicio.LeerInterruptor = () => "0";
            SugeridorModoServicio.Sugerencia apagada = Llamar("SOMBRA563A"); // clientes distintos: sin caché por huella
            SombraModoServicio.LeerInterruptor = () => "1";
            SombraModoServicio.Reiniciar();
            SugeridorModoServicio.Sugerencia encendida = Llamar("SOMBRA563B");

            Assert.AreEqual(M.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, apagada.Modo);
            Assert.AreEqual(apagada.Modo, encendida.Modo);
            Assert.AreEqual(apagada.Motivo, encendida.Motivo);
            CollectionAssert.AreEqual(apagada.ModosPermitidos, encendida.ModosPermitidos);
            A.CallTo(() => repositorio.LeerDatos(A<string>._, A<int?>._, A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, avisos.Count);
        }
    }
}
