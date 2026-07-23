using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Bancos;
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
    /// Nesto#425: GET api/Bancos/Selector — lista ligera por empresa para el SelectorBanco
    /// (combo con el nombre del banco en vez del código pelado).
    /// </summary>
    [TestClass]
    public class BancosControllerTests
    {
        private NVEntities db;
        private DbSet<Banco> fakeBancos;
        private BancosController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeBancos = A.Fake<DbSet<Banco>>(o => o.Implements<IQueryable<Banco>>().Implements<IDbAsyncEnumerable<Banco>>());
            A.CallTo(() => db.Bancos).Returns(fakeBancos);
            controller = new BancosController(db);
        }

        private void ConFakeDbSet(IQueryable<Banco> data)
        {
            A.CallTo(() => ((IDbAsyncEnumerable<Banco>)fakeBancos).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<Banco>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<Banco>)fakeBancos).Provider)
                .Returns(new TestDbAsyncQueryProvider<Banco>(data.Provider));
            A.CallTo(() => ((IQueryable<Banco>)fakeBancos).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<Banco>)fakeBancos).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<Banco>)fakeBancos).GetEnumerator()).Returns(data.GetEnumerator());
        }

        [TestMethod]
        public async Task GetBancosSelector_FiltraPorEmpresaYDevuelveDTOsLigerosSinPadding()
        {
            ConFakeDbSet(new List<Banco>
            {
                new Banco { Empresa = "1", Número = "5  ", Descripción = "La Caixa  ", Entidad = "2100", Sucursal = "6273" },
                new Banco { Empresa = "1", Número = "1  ", Descripción = "Sabadell  ", Entidad = "0081", Sucursal = "5199" },
                new Banco { Empresa = "3", Número = "2  ", Descripción = "Banco de la empresa espejo", Entidad = "0049", Sucursal = "1111" }
            }.AsQueryable());

            var resultado = await controller.GetBancosSelector("1");

            var ok = resultado as OkNegotiatedContentResult<List<BancoSelectorDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Count, "Solo los bancos de la empresa pedida");
            Assert.AreEqual("1", ok.Content[0].Numero, "Ordenados por número y sin padding");
            Assert.AreEqual("Sabadell", ok.Content[0].Nombre);
            Assert.AreEqual("La Caixa", ok.Content[1].Nombre);
            Assert.AreEqual("2100", ok.Content[1].Entidad);
        }

        [TestMethod]
        public async Task GetBancosSelector_EmpresaSinBancos_DevuelveListaVacia()
        {
            ConFakeDbSet(new List<Banco>().AsQueryable());

            var resultado = await controller.GetBancosSelector("1");

            var ok = resultado as OkNegotiatedContentResult<List<BancoSelectorDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(0, ok.Content.Count);
        }
    }
}
