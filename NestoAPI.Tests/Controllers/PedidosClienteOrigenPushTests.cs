using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#709 (TNV#95): guardar con qué push se abrió la app antes del pedido, y el informe por tipo de push.</summary>
    [TestClass]
    public class PedidosClienteOrigenPushTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 27, 12, 0, 0); // martes

        private static PedidoClienteRequest ConPush(string tipo = "reposicion", string campana = null, DateTime? fecha = null)
        {
            return new PedidoClienteRequest { OrigenPushTipo = tipo, OrigenPushCampana = campana, OrigenPushFecha = fecha ?? AHORA.AddHours(-2) };
        }

        #region Guardado

        [TestMethod]
        public async Task Registrar_ConOrigenPush_InsertaLaFilaDelPedido()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            var errores = new List<Exception>();

            bool guardado = await RegistroOrigenPushPedido.Registrar(repositorio, ConPush("chequeregalo", " NAVIDAD26 "), "1  ", 926001, AHORA, errores.Add);

            Assert.IsTrue(guardado);
            Assert.AreEqual(0, errores.Count);
            A.CallTo(() => repositorio.Insertar(A<FilaOrigenPushPedido>.That.Matches(f =>
                f.Empresa == "1" && f.Pedido == 926001 && f.Tipo == "chequeregalo" && f.Campana == "NAVIDAD26"
                && f.FechaApertura == AHORA.AddHours(-2) && f.FechaPedido == AHORA))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Registrar_SinOrigenPush_NoTocaLaBaseDeDatos()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();

            bool guardado = await RegistroOrigenPushPedido.Registrar(repositorio, new PedidoClienteRequest(), "1", 926001, AHORA, _ => { });

            Assert.IsFalse(guardado);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Registrar_TablaAusente_NoInsertaAvisaYNoLanza()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            var errores = new List<Exception>();

            bool guardado = await RegistroOrigenPushPedido.Registrar(repositorio, ConPush(), "1", 926001, AHORA, errores.Add);

            Assert.IsFalse(guardado);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "Issue709_PedidosOrigenPush.sql");
            A.CallTo(() => repositorio.Insertar(A<FilaOrigenPushPedido>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Registrar_FallaElInsert_NoLanzaYAvisa()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.Insertar(A<FilaOrigenPushPedido>._)).ThrowsAsync(new InvalidOperationException("timeout"));
            var errores = new List<Exception>();

            bool guardado = await RegistroOrigenPushPedido.Registrar(repositorio, ConPush(), "1", 926001, AHORA, errores.Add);

            Assert.IsFalse(guardado);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "926001");
        }

        [TestMethod]
        public void ConstruirFila_PushDeHaceMasDeOchoDias_NoSeAtribuye()
        {
            Assert.IsNull(RegistroOrigenPushPedido.ConstruirFila(ConPush(fecha: AHORA.AddDays(-9)), "1", 926001, AHORA));
            Assert.IsNotNull(RegistroOrigenPushPedido.ConstruirFila(ConPush(fecha: AHORA.AddDays(-7)), "1", 926001, AHORA));
        }

        [TestMethod]
        public void ConstruirFila_FechaFuturaOSinFecha_SeGuardaSinFechaDeApertura()
        {
            FilaOrigenPushPedido futura = RegistroOrigenPushPedido.ConstruirFila(ConPush(fecha: AHORA.AddDays(2)), "1", 926001, AHORA);
            var peticionSinFecha = new PedidoClienteRequest { OrigenPushTipo = "reposicion" };
            FilaOrigenPushPedido sinFecha = RegistroOrigenPushPedido.ConstruirFila(peticionSinFecha, "1", 926001, AHORA);

            Assert.IsNull(futura.FechaApertura);
            Assert.IsNull(sinFecha.FechaApertura);
            Assert.IsNull(sinFecha.Campana);
            Assert.AreEqual("reposicion", sinFecha.Tipo);
        }

        [TestMethod]
        public void ConstruirFila_TipoEnBlanco_NoHayOrigen()
        {
            Assert.IsNull(RegistroOrigenPushPedido.ConstruirFila(ConPush(tipo: "  "), "1", 926001, AHORA));
        }

        #endregion

        #region Informe

        private static FilaPedidoAppOrigenPush Pedido(int numero, DateTime fecha, string tipo, string campana, decimal baseImponible)
        {
            return new FilaPedidoAppOrigenPush { Empresa = "1", Pedido = numero, FechaPedido = fecha, Tipo = tipo, Campana = campana, BaseImponible = baseImponible };
        }

        [TestMethod]
        public void Calcular_AgrupaPorSemanaYPorPush_ConLosSinPushAlFinal()
        {
            var filas = new List<FilaPedidoAppOrigenPush>
            {
                Pedido(1, new DateTime(2026, 10, 19, 10, 0, 0), "reposicion", null, 100m),   // lunes semana 19
                Pedido(2, new DateTime(2026, 10, 25, 22, 0, 0), "reposicion", null, 50m),    // domingo semana 19
                Pedido(3, new DateTime(2026, 10, 21, 9, 0, 0), null, null, 80m),             // sin push semana 19
                Pedido(4, new DateTime(2026, 10, 26, 9, 0, 0), "chequeregalo", "NAV26", 200m), // semana 26
                Pedido(5, new DateTime(2026, 10, 27, 9, 0, 0), "  ", null, 30m)              // en blanco = sin push
            };

            InformeOrigenPushDTO informe = InformeOrigenPushPedidos.Calcular(filas, new DateTime(2026, 10, 19), new DateTime(2026, 11, 2));

            Assert.AreEqual(2, informe.Semanas.Count);
            SemanaOrigenPushDTO primera = informe.Semanas[0];
            Assert.AreEqual(new DateTime(2026, 10, 19), primera.Lunes);
            Assert.AreEqual(3, primera.PedidosApp);
            Assert.AreEqual(2, primera.PedidosConPush);
            Assert.AreEqual(230m, primera.BaseImponibleApp);
            Assert.AreEqual(150m, primera.BaseImponibleConPush);
            Assert.AreEqual("reposicion", primera.Grupos[0].Tipo);
            Assert.IsTrue(primera.Grupos.Last().SinPush);

            SemanaOrigenPushDTO segunda = informe.Semanas[1];
            Assert.AreEqual(new DateTime(2026, 10, 26), segunda.Lunes);
            Assert.AreEqual(1, segunda.PedidosConPush);
            Assert.AreEqual(230m, segunda.BaseImponibleApp);

            Assert.AreEqual(3, informe.Totales.Count);
            GrupoOrigenPushDTO sinPush = informe.Totales.Last();
            Assert.IsTrue(sinPush.SinPush);
            Assert.AreEqual(2, sinPush.Pedidos);
            Assert.AreEqual(110m, sinPush.BaseImponible);
            GrupoOrigenPushDTO cheque = informe.Totales.Single(t => t.Tipo == "chequeregalo");
            Assert.AreEqual("NAV26", cheque.Campana);
            Assert.AreEqual(200m, cheque.BaseImponible);
        }

        [TestMethod]
        public void Calcular_SinFilas_InformeVacio()
        {
            InformeOrigenPushDTO informe = InformeOrigenPushPedidos.Calcular(null, AHORA.Date, AHORA.Date.AddDays(1));

            Assert.AreEqual(0, informe.Semanas.Count);
            Assert.AreEqual(0, informe.Totales.Count);
        }

        private static PedidosClienteController Controller(string grupo, IRepositorioOrigenPushPedidos repositorio)
        {
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RepositorioOrigenPush = repositorio,
                Hoy = () => AHORA,
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { grupo });
            return controller;
        }

        [TestMethod]
        public async Task GetInformeOrigenPush_SinSerDireccionNiInformatica_Forbidden()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();
            PedidosClienteController controller = Controller("NUEVAVISION\\Vendedores", repositorio);

            var resultado = await controller.GetInformeOrigenPush() as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.LeerPedidosApp(A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetInformeOrigenPush_PorDefecto_DoceSemanasDesdeUnLunesConHoyIncluido()
        {
            var repositorio = A.Fake<IRepositorioOrigenPushPedidos>();
            A.CallTo(() => repositorio.LeerPedidosApp(A<DateTime>._, A<DateTime>._))
                .Returns(new List<FilaPedidoAppOrigenPush> { Pedido(1, AHORA, "reposicion", null, 10m) });
            PedidosClienteController controller = Controller("NUEVAVISION\\Dirección", repositorio);

            var resultado = await controller.GetInformeOrigenPush() as OkNegotiatedContentResult<InformeOrigenPushDTO>;

            Assert.IsNotNull(resultado);
            // Lunes 26/10 menos 11 semanas = lunes 10/08; hasta el miércoles 28/10 (exclusivo)
            A.CallTo(() => repositorio.LeerPedidosApp(new DateTime(2026, 8, 10), new DateTime(2026, 10, 28))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Content.Totales.Single().Pedidos);
        }

        [TestMethod]
        public async Task GetInformeOrigenPush_Informatica_HastaAnteriorADesde_BadRequest()
        {
            PedidosClienteController controller = Controller("NUEVAVISION\\Informatica", A.Fake<IRepositorioOrigenPushPedidos>());

            var resultado = await controller.GetInformeOrigenPush(new DateTime(2026, 11, 1), new DateTime(2026, 10, 1));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        #endregion
    }
}
