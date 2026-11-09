using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models.Inventarios;
using System.Net;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Al contar un producto, Nesto pregunta si ya tiene línea de inventario ese día. Si aún no
    /// se había contado, SingleAsync lanzaba «Sequence contains no elements» y salía un 500 a
    /// ELMAH (Javier, 30/09/26, producto 45487 en ALC), aunque Nesto lo trate como «no hay línea».
    /// </summary>
    [TestClass]
    public class InventariosControllerTests
    {
        private NVEntities db;
        private DbSet<Inventario> fakeInventarios;
        private DbSet<Producto> fakeProductos;
        private InventariosController controller;
        private readonly DateTime hoy = new DateTime(2026, 9, 30);

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeInventarios = A.Fake<DbSet<Inventario>>(o => o.Implements<IQueryable<Inventario>>().Implements<IDbAsyncEnumerable<Inventario>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.Inventarios).Returns(fakeInventarios);
            A.CallTo(() => db.Productos).Returns(fakeProductos);

            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "45487" }
            }.AsQueryable());

            controller = new InventariosController(db);
        }

        [TestMethod]
        public async Task GetInventario_SiElProductoNoSeHaContadoEseDia_DevuelveNotFoundYNoRevienta()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>().AsQueryable());

            var resultado = await controller.GetInventario("1", "ALC", hoy, "45487");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetInventario_SiYaTieneLinea_LaDevuelve()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>
            {
                new Inventario { Empresa = "1", Almacén = "ALC", Fecha = hoy.AddHours(10), Número = "45487", Estado = 1 }
            }.AsQueryable());

            var resultado = await controller.GetInventario("1", "ALC", hoy, "45487");

            var ok = resultado as OkNegotiatedContentResult<Inventario>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("45487", ok.Content.Número);
        }

        // ---------- NestoAPI#784 (09/11/26): dos filas pendientes del mismo producto → 500 y Nesto creaba otra ----------

        private static Inventario Pendiente(int orden, int real, string usuario = @"NUEVAVISION\Paloma", string pasillo = null, string fila = null, string columna = null)
            => new Inventario
            {
                NºOrden = orden, Empresa = "1", Almacén = "ALC", Fecha = new DateTime(2026, 9, 30), Número = "45487", Estado = 1, StockCalculado = 2, StockReal = real,
                Usuario = usuario, Fecha_Modificación = new DateTime(2026, 9, 30, 10, 50, 0), Pasillo = pasillo, Fila = fila, Columna = columna
            };

        [TestMethod]
        public async Task GetInventario_DosFilasPendientesDelProducto_409ConLasDuplicadasYNo500_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario> { Pendiente(948780, 2), Pendiente(948790, 0) }.AsQueryable());

            var resultado = await controller.GetInventario("1", "ALC", hoy, "45487");

            var conflicto = resultado as NegotiatedContentResult<YaRellenadoInventarioDTO>;
            Assert.IsNotNull(conflicto, resultado?.GetType().Name);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(CodigosConflictoInventario.FILAS_DUPLICADAS, conflicto.Content.Codigo);
            CollectionAssert.AreEqual(new[] { 948780, 948790 }, conflicto.Content.Lineas.Select(l => l.NumeroOrden).ToArray());
            Assert.AreEqual(2, conflicto.Content.Lineas[0].StockReal);
            Assert.AreEqual(@"NUEVAVISION\Paloma", conflicto.Content.Lineas[0].Usuario);
            Assert.AreEqual(
                "El 45487 tiene 2 filas sin contabilizar el 30/09/2026 en ALC (nº 948780: contado 2, Paloma, 10:50; nº 948790: contado 0, Paloma, 10:50). " +
                "Hay que dejar solo una antes de seguir contando: avise a Informática.", conflicto.Content.Message);
        }

        [TestMethod]
        public async Task GetInventario_Algete_DosFilasEnUbicacionesDistintas_ConUbicacionDevuelveLaSuya_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>
            {
                Pendiente(1, 2, pasillo: "003", fila: "001", columna: "002"), Pendiente(2, 5, pasillo: "003", fila: "001", columna: "003")
            }.AsQueryable());

            var resultado = await controller.GetInventario("1", "ALC", hoy, "45487", "003", "001", "003");

            Assert.AreEqual(2, ((OkNegotiatedContentResult<Inventario>)resultado).Content.NºOrden);
        }

        [TestMethod]
        public async Task GetInventario_Algete_SinUbicacionYEnDosUbicaciones_409PidiendoLaUbicacion_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario>
            {
                Pendiente(1, 2, pasillo: "003", fila: "001", columna: "002"), Pendiente(2, 5, pasillo: "003", fila: "001", columna: "003")
            }.AsQueryable());

            var conflicto = (NegotiatedContentResult<YaRellenadoInventarioDTO>)await controller.GetInventario("1", "ALC", hoy, "45487");

            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(CodigosConflictoInventario.VARIAS_UBICACIONES, conflicto.Content.Codigo);
            Assert.AreEqual("El 45487 tiene 2 filas sin contabilizar el 30/09/2026 en ALC, en ubicaciones distintas (003-001-002, 003-001-003): indique la ubicación.",
                conflicto.Content.Message);
        }

        [TestMethod]
        public async Task PostInventario_YaHayFilaPendienteEseDia_409YNoCreaOtra_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario> { Pendiente(948780, 2) }.AsQueryable());

            var resultado = await controller.PostInventario(new Inventario { Empresa = "1", Almacén = "ALC", Fecha = hoy.AddHours(11), Número = "45487", StockReal = 1 });

            var conflicto = resultado as NegotiatedContentResult<YaRellenadoInventarioDTO>;
            Assert.IsNotNull(conflicto, resultado?.GetType().Name);
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(CodigosConflictoInventario.YA_TIENE_FILA, conflicto.Content.Codigo);
            Assert.AreEqual(948780, conflicto.Content.Lineas.Single().NumeroOrden);
            Assert.AreEqual(
                "El 45487 ya tiene una fila sin contabilizar el 30/09/2026 en ALC (nº 948780: contado 2, Paloma, 10:50): sume lo contado a esa fila en vez de crear otra.",
                conflicto.Content.Message);
            A.CallTo(() => fakeInventarios.Add(A<Inventario>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostInventario_Algete_FilaDelProductoEnOtraUbicacion_LaCrea_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario> { Pendiente(1, 2, pasillo: "003", fila: "001", columna: "002") }.AsQueryable());

            var resultado = await controller.PostInventario(new Inventario
            {
                Empresa = "1", Almacén = "ALC", Fecha = hoy, Número = "45487", StockReal = 1, Pasillo = "003", Fila = "001", Columna = "003"
            });

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<Inventario>));
            A.CallTo(() => fakeInventarios.Add(A<Inventario>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostInventario_Algete_MismaUbicacion_409_784()
        {
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario> { Pendiente(1, 2, pasillo: "003", fila: "001", columna: "002") }.AsQueryable());

            var resultado = await controller.PostInventario(new Inventario
            {
                Empresa = "1", Almacén = "ALC", Fecha = hoy, Número = "45487", StockReal = 1, Pasillo = "003", Fila = "001", Columna = "002"
            });

            Assert.AreEqual(HttpStatusCode.Conflict, ((NegotiatedContentResult<YaRellenadoInventarioDTO>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task PostInventario_SoloHayUnaContabilizada_LaCrea_784()
        {
            Inventario contabilizada = Pendiente(1, 2);
            contabilizada.Estado = 2;
            contabilizada.NºTraspaso = 81350;
            ConfigurarFakeDbSet(fakeInventarios, new List<Inventario> { contabilizada }.AsQueryable());

            var resultado = await controller.PostInventario(new Inventario { Empresa = "1", Almacén = "ALC", Fecha = hoy, Número = "45487", StockReal = 1 });

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<Inventario>));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
