using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class AgenciasTarifasControllerTests
    {
        private NVEntities db;
        private DbSet<AgenciaTransporte> fakeAgencias;
        private AgenciasTarifasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeAgencias = A.Fake<DbSet<AgenciaTransporte>>(o => o
                .Implements<IQueryable<AgenciaTransporte>>()
                .Implements<IDbAsyncEnumerable<AgenciaTransporte>>());
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeAgencias);
            controller = new AgenciasTarifasController(db);
        }

        private void Datos(params AgenciaTransporte[] agencias)
        {
            ConfigurarFakeDbSet(fakeAgencias, agencias.AsQueryable());
        }

        [TestMethod]
        public void GetRecargosCombustible_AgrupaPorNumeroYDevuelveElFuel()
        {
            Datos(
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "GLS", RecargoCombustible = 0.1055m },
                new AgenciaTransporte { Numero = 1, Empresa = "3  ", Nombre = "GLS", RecargoCombustible = 0.1055m },
                new AgenciaTransporte { Numero = 8, Empresa = "1  ", Nombre = "CEX", RecargoCombustible = 0m });

            var resultado = controller.GetRecargosCombustible() as OkNegotiatedContentResult<List<RecargoCombustibleAgenciaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count); // distinto por Numero
            Assert.AreEqual(0.1055m, resultado.Content.Single(a => a.Numero == 1).RecargoCombustible);
            Assert.AreEqual(0m, resultado.Content.Single(a => a.Numero == 8).RecargoCombustible);
        }

        [TestMethod]
        public void PutRecargoCombustible_ActualizaTodasLasEmpresasDeLaAgencia()
        {
            var gls1 = new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "GLS", RecargoCombustible = 0.10m };
            var gls3 = new AgenciaTransporte { Numero = 1, Empresa = "3  ", Nombre = "GLS", RecargoCombustible = 0.10m };
            Datos(gls1, gls3);
            A.CallTo(() => db.SaveChanges()).Returns(1);

            controller.PutRecargoCombustible(1, new RecargoCombustibleAgenciaDTO { RecargoCombustible = 0.1055m });

            Assert.AreEqual(0.1055m, gls1.RecargoCombustible);
            Assert.AreEqual(0.1055m, gls3.RecargoCombustible);
            A.CallTo(() => db.SaveChanges()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutRecargoCombustible_Negativo_DevuelveBadRequest()
        {
            var resultado = controller.PutRecargoCombustible(1, new RecargoCombustibleAgenciaDTO { RecargoCombustible = -0.1m });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void GetMasEconomica_CodigoPostalPeninsular_DevuelveGLS()
        {
            // Solo GLS está portada todavía; BusinessParcel cubre Peninsular -> es la elegida.
            Datos(new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "GLS", RecargoCombustible = 0m });

            var resultado = controller.GetMasEconomica("08001", peso: 3m, empresa: "1", reembolso: 0m)
                as OkNegotiatedContentResult<OpcionEnvioAgencia>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.AgenciaId);
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
