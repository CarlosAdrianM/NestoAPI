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
    /// <summary>
    /// NestoAPI#414: mantenimiento de categorías secundarias de producto. El PUT reemplaza la
    /// lista completa (el orden es la posición) y encola el producto en Nesto_sync.
    /// </summary>
    [TestClass]
    public class ProductosCategoriasSecundariasControllerTests
    {
        private NVEntities db;
        private ProductosCategoriasSecundariasController controller;
        private DbSet<ProductoCategoriaSecundaria> fakeCategorias;
        private DbSet<Producto> fakeProductos;
        private DbSet<SubGruposProducto> fakeSubgrupos;
        private DbSet<GruposProducto> fakeGrupos;
        private List<ProductoCategoriaSecundaria> annadidas;

        private const string PRODUCTO = "17404";

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeCategorias = A.Fake<DbSet<ProductoCategoriaSecundaria>>(o =>
                o.Implements<IQueryable<ProductoCategoriaSecundaria>>().Implements<IDbAsyncEnumerable<ProductoCategoriaSecundaria>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o =>
                o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            fakeSubgrupos = A.Fake<DbSet<SubGruposProducto>>(o =>
                o.Implements<IQueryable<SubGruposProducto>>().Implements<IDbAsyncEnumerable<SubGruposProducto>>());
            fakeGrupos = A.Fake<DbSet<GruposProducto>>(o =>
                o.Implements<IQueryable<GruposProducto>>().Implements<IDbAsyncEnumerable<GruposProducto>>());

            A.CallTo(() => db.ProductosCategoriasSecundarias).Returns(fakeCategorias);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.SubGruposProductoes).Returns(fakeSubgrupos);
            A.CallTo(() => db.GruposProductoes).Returns(fakeGrupos);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(0));
            A.CallTo(() => db.EncolarProductoSync(A<string>._, A<string>._)).Returns(Task.FromResult(1));

            annadidas = new List<ProductoCategoriaSecundaria>();
            A.CallTo(() => fakeCategorias.Add(A<ProductoCategoriaSecundaria>._))
                .Invokes((ProductoCategoriaSecundaria c) => annadidas.Add(c));

            ConfigurarFakeDbSet(fakeCategorias, new List<ProductoCategoriaSecundaria>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = PRODUCTO }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeSubgrupos, new List<SubGruposProducto>
            {
                new SubGruposProducto { Empresa = "1", Grupo = "PEL", Número = "OFE", Descripción = "Ofertas del mes" },
                new SubGruposProducto { Empresa = "1", Grupo = "APA", Número = "EXP", Descripción = "Exclusivo Profesional" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeGrupos, new List<GruposProducto>
            {
                new GruposProducto { Empresa = "1", Número = "PEL", Descripción = "Peluquería" },
                new GruposProducto { Empresa = "1", Número = "APA", Descripción = "Aparatología" }
            }.AsQueryable());

            controller = new ProductosCategoriasSecundariasController(db);
        }

        private ProductoCategoriaSecundaria Fila(int orden, string grupo, string subgrupo, string descSubgrupo)
        {
            return new ProductoCategoriaSecundaria
            {
                Empresa = "1",
                Número = PRODUCTO,
                Orden = orden,
                Grupo = grupo,
                SubGrupo = subgrupo,
                SubGruposProducto = new SubGruposProducto
                {
                    Empresa = "1",
                    Grupo = grupo,
                    Número = subgrupo,
                    Descripción = descSubgrupo
                }
            };
        }

        [TestMethod]
        public async Task Get_DevuelveLasCategoriasOrdenadasYConDescripciones()
        {
            // Se cargan desordenadas a propósito: manda la columna Orden, no el orden físico.
            ConfigurarFakeDbSet(fakeCategorias, new List<ProductoCategoriaSecundaria>
            {
                Fila(2, "APA", "EXP", "Exclusivo Profesional"),
                Fila(1, "PEL", "OFE", "Ofertas del mes")
            }.AsQueryable());

            var resultado = await controller.GetCategoriasSecundarias(PRODUCTO)
                as OkNegotiatedContentResult<List<CategoriaSecundariaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual("PEL", resultado.Content[0].Grupo);
            Assert.AreEqual("Peluquería", resultado.Content[0].DescripcionGrupo);
            Assert.AreEqual("OFE", resultado.Content[0].Subgrupo);
            Assert.AreEqual("Ofertas del mes", resultado.Content[0].DescripcionSubgrupo);
            Assert.AreEqual("APA", resultado.Content[1].Grupo);
        }

        [TestMethod]
        public async Task Put_ProductoInexistente_NotFound()
        {
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            var resultado = await controller.PutCategoriasSecundarias(PRODUCTO,
                new List<CategoriaSecundariaPutDTO> { new CategoriaSecundariaPutDTO { Grupo = "PEL", Subgrupo = "OFE" } });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Put_SubgrupoInexistente_BadRequest()
        {
            var resultado = await controller.PutCategoriasSecundarias(PRODUCTO,
                new List<CategoriaSecundariaPutDTO> { new CategoriaSecundariaPutDTO { Grupo = "XXX", Subgrupo = "YYY" } });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Put_CategoriasRepetidas_BadRequest()
        {
            var resultado = await controller.PutCategoriasSecundarias(PRODUCTO,
                new List<CategoriaSecundariaPutDTO>
                {
                    new CategoriaSecundariaPutDTO { Grupo = "PEL", Subgrupo = "OFE" },
                    new CategoriaSecundariaPutDTO { Grupo = "PEL", Subgrupo = "OFE" }
                });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Put_Correcto_ReemplazaConOrdenPorPosicionYEncola()
        {
            // Había una lista anterior: el PUT la reemplaza entera (añadir/quitar/reordenar son
            // la misma operación para la pantalla).
            ConfigurarFakeDbSet(fakeCategorias, new List<ProductoCategoriaSecundaria>
            {
                Fila(1, "APA", "EXP", "Exclusivo Profesional")
            }.AsQueryable());

            var resultado = await controller.PutCategoriasSecundarias(PRODUCTO,
                new List<CategoriaSecundariaPutDTO>
                {
                    new CategoriaSecundariaPutDTO { Grupo = "PEL", Subgrupo = "OFE" },
                    new CategoriaSecundariaPutDTO { Grupo = "APA", Subgrupo = "EXP" }
                });

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => fakeCategorias.RemoveRange(A<IEnumerable<ProductoCategoriaSecundaria>>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, annadidas.Count);
            Assert.AreEqual(1, annadidas[0].Orden, "El orden es la posición en la lista, empezando en 1");
            Assert.AreEqual("PEL", annadidas[0].Grupo);
            Assert.AreEqual(2, annadidas[1].Orden);
            Assert.AreEqual("APA", annadidas[1].Grupo);
            // Y el producto queda encolado para que la pasada de sync lo republique
            A.CallTo(() => db.EncolarProductoSync(PRODUCTO, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_ListaVacia_QuitaTodasYEncola()
        {
            ConfigurarFakeDbSet(fakeCategorias, new List<ProductoCategoriaSecundaria>
            {
                Fila(1, "PEL", "OFE", "Ofertas del mes")
            }.AsQueryable());

            var resultado = await controller.PutCategoriasSecundarias(PRODUCTO,
                new List<CategoriaSecundariaPutDTO>());

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => fakeCategorias.RemoveRange(A<IEnumerable<ProductoCategoriaSecundaria>>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, annadidas.Count);
            A.CallTo(() => db.EncolarProductoSync(PRODUCTO, A<string>._)).MustHaveHappenedOnceExactly();
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
