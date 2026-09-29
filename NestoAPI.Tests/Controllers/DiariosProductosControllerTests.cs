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
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#554: traspaso de diario sin SQL concatenado y solo con usuario autenticado.</summary>
    [TestClass]
    public class DiariosProductosControllerTests
    {
        private NVEntities db;
        private string sqlEjecutado;
        private object[] parametrosEjecutados;
        private int filasQueDevuelve;
        private DiariosProductosController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            DbSet<DiarioProducto> diarios = A.Fake<DbSet<DiarioProducto>>(o => o.Implements<IQueryable<DiarioProducto>>().Implements<IDbAsyncEnumerable<DiarioProducto>>());
            ConfigurarFakeDbSet(diarios, new List<DiarioProducto>
            {
                new DiarioProducto { Empresa = "1", Número = "General", Sistema = false },
                new DiarioProducto { Empresa = "1", Número = "Repo2", Sistema = false },
                new DiarioProducto { Empresa = "1", Número = "Sistemas", Sistema = true },
                new DiarioProducto { Empresa = "1", Número = "_EntregFac", Sistema = false }
            });
            A.CallTo(() => db.DiariosProductos).Returns(diarios);
            sqlEjecutado = null;
            parametrosEjecutados = null;
            filasQueDevuelve = 3;
            controller = new DiariosProductosController(db, (sql, parametros) =>
            {
                sqlEjecutado = sql;
                parametrosEjecutados = parametros;
                return Task.FromResult(filasQueDevuelve);
            });
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        [TestMethod]
        public void Controlador_ExigeAutenticacion()
        {
            Assert.IsNotNull(typeof(DiariosProductosController).GetCustomAttribute<AuthorizeAttribute>(),
                "Antes el POST era anónimo");
        }

        [TestMethod]
        public async Task Post_IntentoDeInyeccion_NoLlegaAlSql()
        {
            var resultado = await controller.PostDiarioProducto(new ParametrosDiarioProducto
            {
                diarioOrigen = "General",
                diarioDestino = "Repo2",
                almacen = "ALG'; DELETE FROM PreExtrProducto; --"
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<bool>));
            Assert.AreEqual("UPDATE PreExtrProducto SET Diario = @p0 WHERE Diario = @p1 AND Almacén = @p2", sqlEjecutado);
            CollectionAssert.AreEqual(new object[] { "Repo2", "General", "ALG'; DELETE FROM PreExtrProducto; --" }, parametrosEjecutados,
                "El texto del usuario va como parámetro, nunca dentro del SQL");
        }

        [TestMethod]
        public async Task Post_DiarioQueNoExiste_NoEjecutaNada()
        {
            var resultado = await controller.PostDiarioProducto(new ParametrosDiarioProducto
            {
                diarioOrigen = "General' OR '1'='1",
                diarioDestino = "Repo2"
            });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.IsNull(sqlEjecutado);
        }

        [TestMethod]
        public async Task Post_DiarioDeSistemaOInterno_NoSePuedeTraspasar()
        {
            var aSistema = await controller.PostDiarioProducto(new ParametrosDiarioProducto { diarioOrigen = "General", diarioDestino = "Sistemas" });
            var desdeInterno = await controller.PostDiarioProducto(new ParametrosDiarioProducto { diarioOrigen = "_EntregFac", diarioDestino = "Repo2" });

            Assert.IsInstanceOfType(aSistema, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(desdeInterno, typeof(BadRequestErrorMessageResult));
            Assert.IsNull(sqlEjecutado);
        }

        [TestMethod]
        public async Task Post_TodosLosAlmacenes_SinFiltroDeAlmacen()
        {
            var resultado = await controller.PostDiarioProducto(new ParametrosDiarioProducto
            {
                diarioOrigen = " General ",
                diarioDestino = "Repo2",
                almacen = "(todos)"
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<bool>));
            Assert.AreEqual("UPDATE PreExtrProducto SET Diario = @p0 WHERE Diario = @p1", sqlEjecutado);
            CollectionAssert.AreEqual(new object[] { "Repo2", "General" }, parametrosEjecutados);
        }

        [TestMethod]
        public async Task Post_SinMovimientos_BadRequest()
        {
            filasQueDevuelve = 0;

            var resultado = await controller.PostDiarioProducto(new ParametrosDiarioProducto { diarioOrigen = "General", diarioDestino = "Repo2" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
