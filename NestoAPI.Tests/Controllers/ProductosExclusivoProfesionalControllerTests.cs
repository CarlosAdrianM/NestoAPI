using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#421: mantenimiento de la marca "exclusivo profesional" de la ficha de producto.
    ///
    /// Es un campo propio del producto y NO una deducción de sus categorías: los subgrupos EP*
    /// (COS/EPC, APA/EXP, PEL/EXP...) son categorías navegables normales cuyos productos sí se
    /// venden al público. Deducirlo de ahí habría dejado ~240 asignaciones sin precio ni compra.
    /// </summary>
    [TestClass]
    public class ProductosExclusivoProfesionalControllerTests
    {
        private NVEntities db;
        private DbSet<Producto> fakeProductos;
        private ProductosController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());
            controller = new ProductosController(db);
        }

        private static Producto ProductoConMarca(string numero, bool exclusivo) => new Producto
        {
            Empresa = "1",
            Número = numero,
            Nombre = "PRODUCTO DE PRUEBA",
            Grupo = "COS",
            SubGrupo = "EPC",          // subgrupo EP*: es categoría navegable, no implica nada
            Estado = 0,
            PVP = 10M,
            Usuario = "el de antes",
            ExclusivoProfesional = exclusivo
        };

        [TestMethod]
        public async Task ExclusivoProfesional_Put_MarcaElProducto()
        {
            Producto producto = ProductoConMarca("41269", false);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { producto }.AsQueryable());

            var resultado = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Empresa = "1",
                Producto = "41269",
                ExclusivoProfesional = true
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ProductoExclusivoProfesionalDTO>));
            Assert.IsTrue(producto.ExclusivoProfesional);
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_TambienSirveParaDESmarcar()
        {
            // Desmarcar tiene que funcionar igual de bien: si se marca un producto por error, deja
            // de venderse al público y nadie se entera hasta que alguien lo echa en falta.
            Producto producto = ProductoConMarca("41269", true);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { producto }.AsQueryable());

            _ = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "41269",
                ExclusivoProfesional = false
            });

            Assert.IsFalse(producto.ExclusivoProfesional);
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_AlCambiar_RepublicaElProducto()
        {
            // El bloque de sincronización de trgProductosUpd ni mira esta columna ni se aplica al
            // usuario del API, así que sin este encolado la tienda no se entera nunca del cambio.
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { ProductoConMarca("41269", false) }.AsQueryable());

            _ = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "41269",
                ExclusivoProfesional = true
            });

            A.CallTo(() => db.EncolarProductoSync("41269", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_SiNoCambiaNada_NoRepublicaNiTocaLaAuditoria()
        {
            Producto producto = ProductoConMarca("41269", true);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { producto }.AsQueryable());

            _ = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "41269",
                ExclusivoProfesional = true
            });

            Assert.AreEqual("el de antes", producto.Usuario);
            A.CallTo(() => db.EncolarProductoSync(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_NoTocaNingunOtroDatoDeLaFicha()
        {
            // La casilla existe para marcar una casilla. Ni precios, ni estado, ni la categoría:
            // el enredo de #421 vino precisamente de mezclar esta marca con la taxonomía.
            Producto producto = ProductoConMarca("41269", false);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { producto }.AsQueryable());

            _ = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "41269",
                ExclusivoProfesional = true
            });

            Assert.AreEqual(10M, producto.PVP);
            Assert.AreEqual((short)0, producto.Estado);
            Assert.AreEqual("COS", producto.Grupo);
            Assert.AreEqual("EPC", producto.SubGrupo);
            Assert.AreEqual("PRODUCTO DE PRUEBA", producto.Nombre);
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_SinEmpresa_UsaLaDeDefecto()
        {
            Producto producto = ProductoConMarca("41269", false);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto> { producto }.AsQueryable());

            var resultado = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "41269",
                ExclusivoProfesional = true
            }) as OkNegotiatedContentResult<ProductoExclusivoProfesionalDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("1", resultado.Content.Empresa);
            Assert.IsTrue(producto.ExclusivoProfesional);
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_ProductoQueNoExiste_DevuelveNotFound()
        {
            var resultado = await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO
            {
                Producto = "NoExiste",
                ExclusivoProfesional = true
            });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => db.EncolarProductoSync(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ExclusivoProfesional_Put_SinProducto_DevuelveBadRequest()
        {
            Assert.IsInstanceOfType(await controller.PutExclusivoProfesional(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(
                await controller.PutExclusivoProfesional(new ProductoExclusivoProfesionalDTO { Producto = "  " }),
                typeof(BadRequestErrorMessageResult));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
