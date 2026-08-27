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
    /// NestoAPI#406: mantenimiento de familias. Existe para poder marcar y desmarcar
    /// PublicoIgualQueProfesional sin entrar a la base de datos a mano.
    /// </summary>
    [TestClass]
    public class FamiliasControllerTests
    {
        private NVEntities db;
        private DbSet<Familia> fakeFamilias;
        private DbSet<Producto> fakeProductos;
        private FamiliasController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeFamilias = A.Fake<DbSet<Familia>>(o => o.Implements<IQueryable<Familia>>().Implements<IDbAsyncEnumerable<Familia>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());

            A.CallTo(() => db.Familias).Returns(fakeFamilias);
            A.CallTo(() => db.Productos).Returns(fakeProductos);

            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());

            controller = new FamiliasController(db);
        }

        private static Familia Familia(string numero, string descripcion, bool publicoIgual) => new Familia
        {
            Empresa = "1",
            Número = numero,
            Descripción = descripcion,
            Estado = 0,
            PublicoIgualQueProfesional = publicoIgual,
            Usuario = "alguien",
            C_ComisiónFija = 3.5M,
            C_DtoMáximoComisión = 10M
        };

        [TestMethod]
        public async Task Familias_Get_DevuelveLasFamiliasConSuMarca()
        {
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>
            {
                Familia("Staleks", "Staleks", true),
                Familia("Lisap", "Lisap", false)
            }.AsQueryable());

            var resultado = await controller.GetFamilias() as OkNegotiatedContentResult<List<FamiliaMantenimientoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.IsTrue(resultado.Content.Single(f => f.Numero == "Staleks").PublicoIgualQueProfesional);
            Assert.IsFalse(resultado.Content.Single(f => f.Numero == "Lisap").PublicoIgualQueProfesional);
        }

        [TestMethod]
        public async Task Familias_Put_MarcaLaFamilia()
        {
            Familia familia = Familia("Staleks", "Staleks", false);
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia> { familia }.AsQueryable());

            var resultado = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Empresa = "1",
                Numero = "Staleks",
                PublicoIgualQueProfesional = true
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FamiliaMantenimientoDTO>));
            Assert.IsTrue(familia.PublicoIgualQueProfesional);
        }

        [TestMethod]
        public async Task Familias_Put_TambienSirveParaDESmarcar()
        {
            // Desmarcar tiene que funcionar igual de bien: si alguien marca la familia por error,
            // sus productos se quedarían publicados a precio de profesional.
            Familia familia = Familia("Staleks", "Staleks", true);
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia> { familia }.AsQueryable());

            _ = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Numero = "Staleks",
                PublicoIgualQueProfesional = false
            });

            Assert.IsFalse(familia.PublicoIgualQueProfesional);
        }

        [TestMethod]
        public async Task Familias_Put_NoTocaLosCamposDeComisiones()
        {
            // La pantalla existe para marcar una casilla. Los porcentajes de comisión mueven dinero
            // de los vendedores y no deben poder cambiarse por aquí ni por descuido.
            Familia familia = Familia("Staleks", "Staleks", false);
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia> { familia }.AsQueryable());

            _ = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Numero = "Staleks",
                Descripcion = "INTENTO DE CAMBIAR EL NOMBRE",
                Estado = -1,
                PublicoIgualQueProfesional = true
            });

            Assert.AreEqual(3.5M, familia.C_ComisiónFija);
            Assert.AreEqual(10M, familia.C_DtoMáximoComisión);
            Assert.AreEqual("Staleks", familia.Descripción, "La descripción tampoco se toca");
            Assert.AreEqual((short)0, familia.Estado, "Ni el estado");
        }

        [TestMethod]
        public async Task Familias_Put_AlCambiar_RepublicaLosProductosVivosDeLaFamilia()
        {
            // Sin esto, la web se queda con el precio anterior hasta que algo toque cada producto.
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia> { Familia("Staleks", "Staleks", false) }.AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "45001", Familia = "Staleks", Estado = 0 },
                new Producto { Empresa = "1", Número = "45002", Familia = "Staleks", Estado = -1 },
                new Producto { Empresa = "1", Número = "30001", Familia = "Lisap", Estado = 0 }
            }.AsQueryable());

            _ = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Numero = "Staleks",
                PublicoIgualQueProfesional = true
            });

            A.CallTo(() => db.EncolarProductoSync("45001", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.EncolarProductoSync("45002", A<string>._)).MustNotHaveHappened();
            A.CallTo(() => db.EncolarProductoSync("30001", A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Familias_Put_SiNoCambiaNada_NoRepublicaNiTocaLaAuditoria()
        {
            Familia familia = Familia("Staleks", "Staleks", true);
            familia.Usuario = "el de antes";
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia> { familia }.AsQueryable());

            _ = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Numero = "Staleks",
                PublicoIgualQueProfesional = true
            });

            Assert.AreEqual("el de antes", familia.Usuario);
            A.CallTo(() => db.EncolarProductoSync(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Familias_Put_FamiliaQueNoExiste_DevuelveNotFound()
        {
            var resultado = await controller.PutFamilia(new FamiliaMantenimientoDTO
            {
                Numero = "NoExiste",
                PublicoIgualQueProfesional = true
            });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Familias_Put_SinFamilia_DevuelveBadRequest()
        {
            Assert.IsInstanceOfType(await controller.PutFamilia(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(
                await controller.PutFamilia(new FamiliaMantenimientoDTO { Numero = "  " }),
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
