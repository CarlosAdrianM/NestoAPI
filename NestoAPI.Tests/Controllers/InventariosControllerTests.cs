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
