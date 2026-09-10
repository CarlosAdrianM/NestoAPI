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
    /// NestoAPI#477: familias de variantes (color, tapizado...) para la tienda. El PUT reemplaza
    /// la familia completa (el orden es la posición), exige que la principal esté dentro y encola
    /// también a las referencias que salen. Piloto: Check 45813-45816 de Mirplay.
    /// </summary>
    [TestClass]
    public class ProductosVariantesControllerTests
    {
        private NVEntities db;
        private ProductosVariantesController controller;
        private DbSet<ProductoVariante> fakeVariantes;
        private DbSet<Producto> fakeProductos;
        private List<ProductoVariante> annadidas;
        private List<string> encolados;

        private const string PRINCIPAL = "45813";

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeVariantes = A.Fake<DbSet<ProductoVariante>>(o =>
                o.Implements<IQueryable<ProductoVariante>>().Implements<IDbAsyncEnumerable<ProductoVariante>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o =>
                o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());

            A.CallTo(() => db.ProductosVariantes).Returns(fakeVariantes);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(0));
            encolados = new List<string>();
            A.CallTo(() => db.EncolarProductoSync(A<string>._, A<string>._))
                .Invokes((string producto, string _) => encolados.Add(producto))
                .Returns(Task.FromResult(1));

            annadidas = new List<ProductoVariante>();
            A.CallTo(() => fakeVariantes.Add(A<ProductoVariante>._))
                .Invokes((ProductoVariante v) => annadidas.Add(v));

            Variantes();
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "45813", Nombre = "SILLON DE BARBERO CHECK", Familia = "Mirplay" },
                new Producto { Empresa = "1", Número = "45814", Nombre = "SILLON DE BARBERO CHECK BR", Familia = "Mirplay" },
                new Producto { Empresa = "1", Número = "45815", Nombre = "SILLON DE BARBERO CHECK G", Familia = "Mirplay" },
                new Producto { Empresa = "1", Número = "45816", Nombre = "SILLON DE BARBERO CHECK GY", Familia = "Mirplay" },
                new Producto { Empresa = "1", Número = "45736", Nombre = "LAVACABEZAS RALPH", Familia = "Mirplay" },
                new Producto { Empresa = "1", Número = "17404", Nombre = "OTRA MARCA", Familia = "Eurostil" }
            }.AsQueryable());

            controller = new ProductosVariantesController(db);
        }

        private void Variantes(params ProductoVariante[] filas)
        {
            ConfigurarFakeDbSet(fakeVariantes, filas.ToList().AsQueryable());
        }

        // Sin el relleno del char(15): SQL Server ignora los espacios finales al comparar y el
        // proveedor en memoria de los tests no, así que los datos falsos van como los compara SQL.
        private static ProductoVariante Fila(string numero, string principal, string valor, int orden)
        {
            return new ProductoVariante
            {
                Empresa = "1",
                Número = numero,
                NúmeroPrincipal = principal,
                Atributo = "Color",
                Valor = valor,
                Orden = orden
            };
        }

        private static VariantePutDTO Put(string numero, string valor, string atributo = "Color")
        {
            return new VariantePutDTO { Numero = numero, Atributo = atributo, Valor = valor };
        }

        [TestMethod]
        public async Task Get_DevuelveLaFamiliaEnOrdenConNombres()
        {
            Variantes(Fila("45814", PRINCIPAL, "Marrón", 2), Fila("45813", PRINCIPAL, "Negro", 1));

            var resultado = await controller.GetFamilia(PRINCIPAL) as OkNegotiatedContentResult<List<VarianteFamiliaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual("45813", resultado.Content[0].Numero);
            Assert.AreEqual("Negro", resultado.Content[0].Valor);
            Assert.AreEqual("SILLON DE BARBERO CHECK", resultado.Content[0].Nombre);
            Assert.AreEqual("45814", resultado.Content[1].Numero);
            Assert.AreEqual("45813", resultado.Content[1].Principal);
        }

        [TestMethod]
        public async Task GetDeReferencia_UnaHermana_DevuelveLaFamiliaEntera()
        {
            Variantes(Fila("45813", PRINCIPAL, "Negro", 1), Fila("45814", PRINCIPAL, "Marrón", 2));

            var resultado = await controller.GetFamiliaDeReferencia("45814") as OkNegotiatedContentResult<List<VarianteFamiliaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
        }

        [TestMethod]
        public async Task GetDeReferencia_SinFamilia_ListaVacia()
        {
            var resultado = await controller.GetFamiliaDeReferencia("45736") as OkNegotiatedContentResult<List<VarianteFamiliaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Content.Count);
        }

        [TestMethod]
        public async Task Put_PrincipalInexistente_NotFound()
        {
            var resultado = await controller.PutFamilia("99999", new List<VariantePutDTO> { Put("99999", "Negro") });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public void ValidarLista_SinLaPrincipal_Error()
        {
            string error = ProductosVariantesController.ValidarLista(PRINCIPAL,
                new List<(string, string, string)> { ("45814", "Color", "Marrón"), ("45815", "Color", "Gris") });

            StringAssert.Contains(error, "principal");
        }

        [TestMethod]
        public void ValidarLista_ValorRepetido_Error()
        {
            string error = ProductosVariantesController.ValidarLista(PRINCIPAL,
                new List<(string, string, string)> { ("45813", "Color", "Negro"), ("45814", "Color", "negro") });

            StringAssert.Contains(error, "mismo valor");
        }

        [TestMethod]
        public void ValidarLista_ReferenciaRepetida_Error()
        {
            string error = ProductosVariantesController.ValidarLista(PRINCIPAL,
                new List<(string, string, string)> { ("45813", "Color", "Negro"), ("45813", "Color", "Gris") });

            StringAssert.Contains(error, "repetidas");
        }

        [TestMethod]
        public void ValidarLista_ListaVacia_EsValida_DeshaceLaFamilia()
        {
            Assert.IsNull(ProductosVariantesController.ValidarLista(PRINCIPAL, new List<(string, string, string)>()));
        }

        [TestMethod]
        public async Task Put_ReferenciaDeOtraFamiliaDeProducto_BadRequest()
        {
            var resultado = await controller.PutFamilia(PRINCIPAL, new List<VariantePutDTO>
            {
                Put(PRINCIPAL, "Negro"), Put("17404", "Gris")
            });

            var bad = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(bad);
            StringAssert.Contains(bad.Message, "otra familia de producto");
        }

        [TestMethod]
        public async Task Put_ReferenciaQueYaEsVarianteDeOtraPrincipal_BadRequest()
        {
            // 45736 (Ralph) ya es variante de una familia cuya principal es 45736
            Variantes(Fila("45736", "45736", "Blanco", 1));

            var resultado = await controller.PutFamilia(PRINCIPAL, new List<VariantePutDTO>
            {
                Put(PRINCIPAL, "Negro"), Put("45736", "Gris")
            });

            var bad = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(bad);
            StringAssert.Contains(bad.Message, "ya es variante de 45736");
        }

        [TestMethod]
        public async Task Put_Correcto_ReemplazaConOrdenPorPosicionYEncolaTambienALosQueSalen()
        {
            // Antes: 45813 + 45815. Ahora: 45813 + 45814 + 45816 → 45815 sale de la familia.
            Variantes(Fila("45813", PRINCIPAL, "Negro", 1), Fila("45815", PRINCIPAL, "Gris", 2));

            var resultado = await controller.PutFamilia(PRINCIPAL, new List<VariantePutDTO>
            {
                Put("45814", "Marrón"), Put(PRINCIPAL, "Negro"), Put("45816", "Gris claro")
            });

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => fakeVariantes.RemoveRange(A<IEnumerable<ProductoVariante>>.That.Matches(r => r.Count() == 2)))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(3, annadidas.Count);
            Assert.AreEqual("45814", annadidas[0].Número);
            Assert.AreEqual(1, annadidas[0].Orden);
            Assert.AreEqual(PRINCIPAL, annadidas[1].Número);
            Assert.AreEqual(2, annadidas[1].Orden);
            Assert.IsTrue(annadidas.All(a => a.NúmeroPrincipal == PRINCIPAL && a.Atributo == "Color"));
            CollectionAssert.AreEquivalent(new[] { "45814", "45813", "45816", "45815" }, encolados,
                "se republican los tres que entran y el que sale (45815), que vuelve a producto plano");
        }

        [TestMethod]
        public async Task CargarVariante_ConFila_RellenaElDtoRecortado()
        {
            Variantes(Fila("45814", PRINCIPAL, "Marrón", 2));
            var dto = new ProductoDTO { Producto = "45814" };

            await ProductoDTO.CargarVariante(dto, db);

            Assert.IsNotNull(dto.Variante);
            Assert.AreEqual("45813", dto.Variante.Principal);
            Assert.AreEqual("Color", dto.Variante.Atributo);
            Assert.AreEqual("Marrón", dto.Variante.Valor);
            Assert.AreEqual(2, dto.Variante.Orden);
        }

        [TestMethod]
        public async Task CargarVariante_SinFila_ProductoPlano_Null()
        {
            var dto = new ProductoDTO { Producto = "45736" };

            await ProductoDTO.CargarVariante(dto, db);

            Assert.IsNull(dto.Variante);
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
