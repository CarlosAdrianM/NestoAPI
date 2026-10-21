using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#688 (corte 4): aviso «lo pendiente ya está completo» de las notas sin fecha, cuando TODO tiene stock.
    /// </summary>
    [TestClass]
    public class AvisoPendienteCompletoTests
    {
        private static readonly DateTime MARTES_10 = new DateTime(2026, 10, 20, 10, 0, 0);

        private IRepositorioServirPendienteAlHaberStock repositorio;
        private IGestorStocks stocks;
        private ILectorComercialesPedido lector;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> registrado;
        private DateTime ahora;

        [TestInitialize]
        public void Setup()
        {
            repositorio = A.Fake<IRepositorioServirPendienteAlHaberStock>();
            stocks = A.Fake<IGestorStocks>();
            lector = A.Fake<ILectorComercialesPedido>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            registrado = new List<Exception>();
            ahora = MARTES_10;
            A.CallTo(() => repositorio.MarcarAvisada(A<string>._, A<int>._, A<DateTime>._)).Returns(true);
            A.CallTo(() => lector.Leer("1", 928001)).Returns(new ComercialesPedido
            {
                Empresa = "1", Pedido = 928001, Cliente = "15191", NombreCliente = "Peluquería Marta", UsuarioPedido = "marta",
                Comerciales = new List<ComercialPedido> { new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "jesus" } } }
            });
        }

        private AvisoPendienteCompleto Aviso(Func<DateTime, bool> esLaborable = null) =>
            new AvisoPendienteCompleto(repositorio, () => stocks, lector, notificaciones, () => ahora, esLaborable ?? (d => true), ex => registrado.Add(ex));

        private void UnaNota(params LineaNotaPendiente[] lineas)
        {
            A.CallTo(() => repositorio.LeerNotasSinAvisar()).Returns(new List<NotaPendienteSinAvisar>
            {
                new NotaPendienteSinAvisar { Empresa = "1", Nota = 928050, PedidoOrigen = 928001, Cliente = "15191", Contacto = "0" }
            });
            A.CallTo(() => repositorio.LeerLineas("1", A<IEnumerable<int>>._)).Returns(lineas.ToList());
        }

        private void Stock(string producto, int stock, int pendientes, string almacen = "ALG")
        {
            A.CallTo(() => stocks.Stock(producto, almacen)).Returns(stock);
            A.CallTo(() => stocks.UnidadesPendientesEntregarAlmacen(producto, almacen)).Returns(pendientes);
        }

        private static LineaNotaPendiente Linea(string producto, int cantidad, string almacen = "ALG") =>
            new LineaNotaPendiente { Nota = 928050, Producto = producto, Almacen = almacen, Cantidad = cantidad };

        [TestMethod]
        public async Task TodoConStock_MarcaYAvisaAQuienLoHizoYAlVendedor()
        {
            UnaNota(Linea("17404", 150), Linea("38932", 2));
            Stock("17404", 200, 150);
            Stock("38932", 5, 2);

            int avisadas = await Aviso().Ejecutar();

            Assert.AreEqual(1, avisadas);
            A.CallTo(() => repositorio.MarcarAvisada("1", 928050, MARTES_10)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\marta", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisoPendienteCompleto.TIPO_NOTIFICACION))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAVendedor("1", "JE", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, registrado.Count);
        }

        [TestMethod]
        public async Task FaltaStockDeUnaLinea_NoAvisaNiMarca()
        {
            UnaNota(Linea("17404", 150), Linea("38932", 2));
            Stock("17404", 200, 150);
            Stock("38932", 1, 2);

            int avisadas = await Aviso().Ejecutar();

            Assert.AreEqual(0, avisadas);
            A.CallTo(() => repositorio.MarcarAvisada(A<string>._, A<int>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void TodoTieneStock_ElStockTieneQueCubrirTodoLoPendienteDelAlmacen()
        {
            Stock("17404", 200, 250); // hay 200 pero otros pedidos esperan más: aún no
            Assert.IsFalse(AvisoPendienteCompleto.TodoTieneStock(new[] { Linea("17404", 150) }, stocks));
            Stock("17404", 250, 250);
            Assert.IsTrue(AvisoPendienteCompleto.TodoTieneStock(new[] { Linea("17404", 150) }, stocks));
            Stock("40000", 10, 10, "REI");
            Assert.IsTrue(AvisoPendienteCompleto.TodoTieneStock(new[] { Linea("40000", 3, "REI") }, stocks), "En su almacén");
            Assert.IsFalse(AvisoPendienteCompleto.TodoTieneStock(new LineaNotaPendiente[0], stocks), "Sin líneas, nada que avisar");
        }

        [TestMethod]
        public void TodoTieneStock_SumaLasLineasDelMismoProducto()
        {
            Stock("17404", 100, 100);
            Assert.IsFalse(AvisoPendienteCompleto.TodoTieneStock(new[] { Linea("17404", 60), Linea("17404", 60) }, stocks));
        }

        [TestMethod]
        public async Task FueraDeHorarioOFestivo_NoHaceNada()
        {
            UnaNota(Linea("17404", 1));
            Stock("17404", 10, 1);

            ahora = new DateTime(2026, 10, 20, 21, 0, 0);
            Assert.AreEqual(0, await Aviso().Ejecutar());
            ahora = new DateTime(2026, 10, 24, 10, 0, 0); // sábado
            Assert.AreEqual(0, await Aviso().Ejecutar());
            ahora = MARTES_10;
            Assert.AreEqual(0, await Aviso(d => false).Ejecutar());

            A.CallTo(() => repositorio.LeerNotasSinAvisar()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinElScript_NoHayNotas_NoHaceNada()
        {
            A.CallTo(() => repositorio.LeerNotasSinAvisar()).Returns(new List<NotaPendienteSinAvisar>());

            Assert.AreEqual(0, await Aviso().Ejecutar());
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task OtraPasadaYaLaMarco_NoAvisaDosVeces()
        {
            UnaNota(Linea("17404", 1));
            Stock("17404", 10, 1);
            A.CallTo(() => repositorio.MarcarAvisada(A<string>._, A<int>._, A<DateTime>._)).Returns(false);

            Assert.AreEqual(0, await Aviso().Ejecutar());
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaElAviso_SeRegistra_YSigueConLaSiguiente()
        {
            UnaNota(Linea("17404", 1));
            Stock("17404", 10, 1);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Throws(new Exception("sin red"));

            Assert.AreEqual(0, await Aviso().Ejecutar());
            Assert.AreEqual(1, registrado.Count);
            StringAssert.Contains(registrado[0].Message, "928050");
        }

        [TestMethod]
        public void VendedoresAPush_SiElVendedorEsQuienLoHizo_SoloBuzon()
        {
            var comerciales = new ComercialesPedido
            {
                UsuarioPedido = "NUEVAVISION\\jesus",
                Comerciales = new List<ComercialPedido>
                {
                    new ComercialPedido { Vendedor = "JE", Usuarios = new List<string> { "jesus" } },
                    new ComercialPedido { Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL },
                    new ComercialPedido { Vendedor = "PEL", Usuarios = new List<string> { "ana" } }
                }
            };

            CollectionAssert.AreEqual(new[] { "PEL" }, AvisoPendienteCompleto.VendedoresAPush(comerciales));
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\jesus" }, AvisoPendienteCompleto.UsuariosBuzon(comerciales));
        }

        [TestMethod]
        public void Componer_DeUsted_ConPedidoNotaYCliente()
        {
            NotificacionPushDTO n = AvisoPendienteCompleto.Componer(
                new NotaPendienteSinAvisar { Empresa = "1", Nota = 928050, PedidoOrigen = 928001, Cliente = "15191" },
                new ComercialesPedido { Cliente = "15191", NombreCliente = "Peluquería Marta" });

            StringAssert.Contains(n.Titulo, "928050");
            StringAssert.Contains(n.Cuerpo, "pedido 928001");
            StringAssert.Contains(n.Cuerpo, "15191 - Peluquería Marta");
            StringAssert.Contains(n.Cuerpo, "póngale");
            StringAssert.Contains(n.Cuerpo, "marque");
            Assert.AreEqual("928050", n.Datos["nota"]);
            Assert.AreEqual("928001", n.Datos["pedido"]);
        }
    }
}
