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
        private DbSet<ProductoCodigoBarras> fakeCodigos;
        private ProductosController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => fakeProductos.Include(A<string>.Ignored)).Returns(fakeProductos);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            fakeCodigos = A.Fake<DbSet<ProductoCodigoBarras>>(o => o.Implements<IQueryable<ProductoCodigoBarras>>().Implements<IDbAsyncEnumerable<ProductoCodigoBarras>>());
            A.CallTo(() => db.ProductosCodigosBarras).Returns(fakeCodigos);
            ConfigurarCodigos();
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

        private void ConfigurarCodigos(params ProductoCodigoBarras[] codigos)
        {
            ConfigurarFakeDbSet(fakeCodigos, codigos.AsQueryable());
        }

        private static ProductoCodigoBarras Alternativo(string producto, string codigo, bool activo = true)
            => new ProductoCodigoBarras { Empresa = "1", Producto = producto, Codigo = codigo, Cantidad = 1, Activo = activo, Origen = "Almacen" };

        /// <summary>NestoAPI#605: el código leído es un código alternativo (activo) de un producto, no el de su ficha.</summary>
        [TestMethod]
        public async Task GetProductoPorCodigoBarras_CodigoAlternativo_EncuentraElProducto()
        {
            Producto guantes = CrearProducto("32565", "8437017506379", "GUANTES T/M");
            guantes.Estado = 0;
            ConfigurarProductos(guantes);
            ConfigurarCodigos(Alternativo("32565", "8437017509999"));

            List<Producto> encontrados = await controller.BuscarPorCodigoBarras("1", "8437017509999", incluirNumero: false);

            Assert.AreEqual("32565", encontrados.Single().Número);
        }

        /// <summary>NestoAPI#605: si un producto lo tiene de principal (ficha) y otro de alternativo, primero el principal.</summary>
        [TestMethod]
        public async Task GetProductoPorCodigoBarras_PrincipalEnUnoYAlternativoEnOtro_DevuelveElPrincipalSin409()
        {
            Producto tallaP = CrearProducto("32564", "8437017506362", "GUANTES T/P");
            Producto tallaM = CrearProducto("32565", "8437017506379", "GUANTES T/M");
            ConfigurarProductos(tallaP, tallaM);
            ConfigurarCodigos(Alternativo("32565", "8437017506362"));

            List<Producto> encontrados = await controller.BuscarPorCodigoBarras("1", "8437017506362", incluirNumero: true);

            Assert.AreEqual("32564", encontrados.Single().Número);
        }

        [TestMethod]
        public async Task GetProductoPorCodigoBarras_CodigoAlternativoDeBaja_NoEncuentraNada()
        {
            ConfigurarProductos(CrearProducto("32565", "8437017506379", "GUANTES T/M"));
            ConfigurarCodigos(Alternativo("32565", "8437017509999", activo: false));

            IHttpActionResult resultado = await controller.GetProducto("8437017509999");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
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
        public async Task GetProducto_ProductoSinFamiliaNiSubgrupoNiPVP_DevuelveFichaSinReventar()
        {
            // NestoAPI#369: un producto a medio dar de alta (Familia, SubGrupo y PVP a NULL)
            // reventaba la ficha con NullReferenceException al escanear su código de barras
            Producto producto = CrearProducto("45464", "8437005216235", "Producto a medias");
            producto.Estado = 1;
            producto.Kits = new List<Kit>();
            ConfigurarProductos(producto);

            IHttpActionResult resultado = await controller.GetProducto("1", "45464", false);

            var okResult = resultado as OkNegotiatedContentResult<ProductoDTO>;
            Assert.IsNotNull(okResult, "Debe devolver la ficha aunque falten datos del alta");
            Assert.AreEqual("45464", okResult.Content.Producto);
            Assert.IsNull(okResult.Content.Familia);
            Assert.IsNull(okResult.Content.Subgrupo);
            Assert.AreEqual(0, okResult.Content.PrecioProfesional);
        }

        [TestMethod]
        public async Task GetProductoPorCodigoBarras_SinCoincidencias_DevuelveNotFound()
        {
            ConfigurarProductos(CrearProducto("45114", "0000000000000", "Producto A"));

            IHttpActionResult resultado = await controller.GetProducto("8436566609883");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // NestoAPI#583: la importación de PrestaShop necesita el IVA de la ficha (los cursos son EX) en vez de
        // deducirlo del precio de la tienda.
        [TestMethod]
        public async Task GetProducto_PorEmpresaYNumero_DevuelveElIvaDeLaFicha()
        {
            Producto curso = CrearProducto("90004", null, "CURSO DE DEPILACION ELECTRICA");
            curso.IVA_Repercutido = "EX ";
            curso.PVP = 165;
            ConfigurarProductos(curso);

            IHttpActionResult resultado = await controller.GetProducto("1", "90004");

            var ok = resultado as OkNegotiatedContentResult<ProductoPlantillaDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("EX", ok.Content.iva);
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
