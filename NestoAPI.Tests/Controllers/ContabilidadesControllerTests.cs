using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340 (Agencias 1D): GET api/Contabilidades/Saldo sustituye al Aggregate EF de
    /// AgenciaService.CalcularSumaContabilidad (saldo Debe - Haber de la cuenta de reembolsos desde 2019).
    /// </summary>
    [TestClass]
    public class ContabilidadesControllerTests
    {
        private NVEntities db;
        private DbSet<Contabilidad> fakeContabilidad;
        private ContabilidadesController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeContabilidad = A.Fake<DbSet<Contabilidad>>(o => o.Implements<IQueryable<Contabilidad>>().Implements<IDbAsyncEnumerable<Contabilidad>>());
            A.CallTo(() => db.Contabilidades).Returns(fakeContabilidad);
            controller = new ContabilidadesController(db);
        }

        private static Contabilidad Apunte(string empresa, string cuenta, decimal debe, decimal haber, DateTime fecha)
            => new Contabilidad { Empresa = empresa, Nº_Cuenta = cuenta, Debe = debe, Haber = haber, Fecha = fecha };

        [TestMethod]
        public void GetSaldo_SumaDebeMenosHaberDeLaCuentaYEmpresa_Desde2019PorDefecto()
        {
            ConfigurarFakeDbSet(fakeContabilidad, new List<Contabilidad>
            {
                Apunte("1", "55500043", 100m, 0m, new DateTime(2026, 9, 1)),
                Apunte("1", "55500043", 0m, 40m, new DateTime(2026, 9, 2)),
                Apunte("1", "55500043", 500m, 0m, new DateTime(2018, 12, 31)), // anterior a 2019: fuera
                Apunte("1", "55500044", 999m, 0m, new DateTime(2026, 9, 1)),   // otra cuenta
                Apunte("3", "55500043", 999m, 0m, new DateTime(2026, 9, 1))    // otra empresa
            }.AsQueryable());

            var resultado = controller.GetSaldo("1", "55500043") as OkNegotiatedContentResult<decimal>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(60m, resultado.Content);
        }

        [TestMethod]
        public void GetSaldo_SinApuntes_DevuelveCero_YConDesdeAcota()
        {
            ConfigurarFakeDbSet(fakeContabilidad, new List<Contabilidad>
            {
                Apunte("1", "55500043", 100m, 0m, new DateTime(2026, 1, 1)),
                Apunte("1", "55500043", 25m, 0m, new DateTime(2026, 9, 1))
            }.AsQueryable());

            Assert.AreEqual(0m, (controller.GetSaldo("1", "NOEXISTE") as OkNegotiatedContentResult<decimal>).Content);
            Assert.AreEqual(25m, (controller.GetSaldo("1", "55500043", new DateTime(2026, 6, 1)) as OkNegotiatedContentResult<decimal>).Content);
        }

        [TestMethod]
        public void GetSaldo_SinEmpresaOCuenta_BadRequest()
        {
            Assert.IsInstanceOfType(controller.GetSaldo("", "55500043"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(controller.GetSaldo("1", " "), typeof(BadRequestErrorMessageResult));
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
