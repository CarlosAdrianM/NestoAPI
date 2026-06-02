using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ProductosControllerCodigoBarrasTests
    {
        private NVEntities db;
        private DbSet<Producto> fakeProductos;
        private ProductosController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            controller = new ProductosController(db, A.Fake<IGestorSincronizacion>());
        }

        private static Producto CrearProducto(string numero, string codBarras, string nombre)
        {
            return new Producto
            {
                Empresa = "1",
                Número = numero,
                CodBarras = codBarras,
                Nombre = nombre
            };
        }

        private void ConfigurarProductos(params Producto[] productos)
        {
            ConfigurarFakeDbSet(fakeProductos, productos.AsQueryable());
        }

        [TestMethod]
        public async Task GetProducto_CodigoBarrasCompartidoPorVarios_DevuelveConflictConLista()
        {
            // Dos productos con el mismo código de barras (8436566609883) y ninguno con ese Número.
            ConfigurarProductos(
                CrearProducto("45114", "8436566609883", "Producto A"),
                CrearProducto("45115", "8436566609883", "Producto B"));

            IHttpActionResult resultado = await controller.GetProducto("1", "8436566609883", "2817", "0", 1);

            var contentResult = resultado as NegotiatedContentResult<List<ProductoCodigoBarrasDuplicadoDTO>>;
            Assert.IsNotNull(contentResult, "Debe devolver la lista de candidatos");
            Assert.AreEqual(HttpStatusCode.Conflict, contentResult.StatusCode);
            Assert.AreEqual(2, contentResult.Content.Count);
            CollectionAssert.AreEquivalent(
                new[] { "45114", "45115" },
                contentResult.Content.Select(p => p.producto).ToList());
        }

        [TestMethod]
        public async Task GetProducto_CodigoBarrasSinCoincidencias_DevuelveNotFound()
        {
            ConfigurarProductos(CrearProducto("45114", "0000000000000", "Producto A"));

            IHttpActionResult resultado = await controller.GetProducto("1", "8436566609883", "2817", "0", 1);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetProductoPorCodigoBarras_Compartido_DevuelveConflictConLista()
        {
            // Sobrecarga GetProducto(codigoBarras)
            ConfigurarProductos(
                CrearProducto("45114", "8436566609883", "Producto A"),
                CrearProducto("45115", "8436566609883", "Producto B"));

            IHttpActionResult resultado = await controller.GetProducto("8436566609883");

            var contentResult = resultado as NegotiatedContentResult<List<ProductoCodigoBarrasDuplicadoDTO>>;
            Assert.IsNotNull(contentResult, "Debe devolver la lista de candidatos");
            Assert.AreEqual(HttpStatusCode.Conflict, contentResult.StatusCode);
            Assert.AreEqual(2, contentResult.Content.Count);
        }

        [TestMethod]
        public async Task GetProductoPorCodigoBarras_SinCoincidencias_DevuelveNotFound()
        {
            ConfigurarProductos(CrearProducto("45114", "0000000000000", "Producto A"));

            IHttpActionResult resultado = await controller.GetProducto("8436566609883");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
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
