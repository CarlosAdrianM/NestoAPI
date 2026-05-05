using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PlanesVentajasControllerTests
    {
        private NVEntities db;
        private DbSet<EstadoPlanVentajas> fakeEstados;
        private PlanesVentajasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEstados = A.Fake<DbSet<EstadoPlanVentajas>>(
                o => o.Implements<IQueryable<EstadoPlanVentajas>>()
                      .Implements<IDbAsyncEnumerable<EstadoPlanVentajas>>());
            A.CallTo(() => db.EstadosPlanesVentajas).Returns(fakeEstados);

            controller = new PlanesVentajasController(db);
        }

        [TestMethod]
        public async Task GetEstados_DevuelveOkConTodosLosEstados()
        {
            var datos = new List<EstadoPlanVentajas>
            {
                new EstadoPlanVentajas { Numero = 1, Descripcion = "Activo" },
                new EstadoPlanVentajas { Numero = 6, Descripcion = "Cancelado" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeEstados, datos);

            var respuesta = await controller.GetEstados();

            var ok = respuesta as OkNegotiatedContentResult<List<EstadoPlanVentajas>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Count);
        }

        [TestMethod]
        public async Task GetEstados_OrdenaPorNumeroAscendente()
        {
            var datos = new List<EstadoPlanVentajas>
            {
                new EstadoPlanVentajas { Numero = 6, Descripcion = "Cancelado" },
                new EstadoPlanVentajas { Numero = 1, Descripcion = "Activo" },
                new EstadoPlanVentajas { Numero = 3, Descripcion = "En curso" }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeEstados, datos);

            var respuesta = await controller.GetEstados();

            var ok = respuesta as OkNegotiatedContentResult<List<EstadoPlanVentajas>>;
            Assert.IsNotNull(ok);
            CollectionAssert.AreEqual(
                new[] { 1, 3, 6 },
                ok.Content.Select(e => e.Numero).ToArray());
        }

        [TestMethod]
        public async Task GetEstados_SinEstados_DevuelveOkConListaVacia()
        {
            ConfigurarFakeDbSet(fakeEstados, Enumerable.Empty<EstadoPlanVentajas>().AsQueryable());

            var respuesta = await controller.GetEstados();

            var ok = respuesta as OkNegotiatedContentResult<List<EstadoPlanVentajas>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(0, ok.Content.Count);
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
