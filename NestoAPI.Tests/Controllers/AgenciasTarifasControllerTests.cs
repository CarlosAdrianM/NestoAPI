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
        public void GetAgencias_DevuelveTodasConSusCamposYElFuel()
        {
            Datos(
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM   ", Ruta = "10", Identificador = "abc", PrefijoCodigoBarras = "6108352", CuentaReembolsos = "55500042", RecargoCombustible = 0.1055m },
                new AgenciaTransporte { Numero = 8, Empresa = "1  ", Nombre = "Correos Express", RecargoCombustible = 0m });

            var resultado = controller.GetAgencias() as OkNegotiatedContentResult<List<AgenciaTransporteDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            var asm = resultado.Content.Single(a => a.Numero == 1);
            Assert.AreEqual("ASM", asm.Nombre);              // trim del padding legacy
            Assert.AreEqual("1", asm.Empresa);
            Assert.AreEqual("6108352", asm.PrefijoCodigoBarras);
            Assert.AreEqual(0.1055m, asm.RecargoCombustible);
        }

        [TestMethod]
        public void PostAgencia_CreaLaAgencia()
        {
            Datos(new AgenciaTransporte { Numero = 11, Nombre = "Canteras" });
            A.CallTo(() => db.SaveChanges()).Returns(1);

            var dto = new AgenciaTransporteDTO
            {
                Numero = 12,
                Empresa = "1",
                Nombre = "Innovatrans",
                CuentaReembolsos = "55500074",
                RecargoCombustible = 0.025m
            };

            var resultado = controller.PostAgencia(dto) as OkNegotiatedContentResult<AgenciaTransporteDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("Innovatrans", resultado.Content.Nombre);
            A.CallTo(() => fakeAgencias.Add(A<AgenciaTransporte>.That.Matches(a => a.Numero == 12 && a.Nombre == "Innovatrans" && a.RecargoCombustible == 0.025m)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChanges()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PostAgencia_NumeroYaExistente_DevuelveBadRequest()
        {
            Datos(new AgenciaTransporte { Numero = 12, Nombre = "Innovatrans" });

            var dto = new AgenciaTransporteDTO { Numero = 12, Nombre = "Otra", RecargoCombustible = 0m };

            var resultado = controller.PostAgencia(dto);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PostAgencia_SinNombre_DevuelveBadRequest()
        {
            Datos();

            var resultado = controller.PostAgencia(new AgenciaTransporteDTO { Numero = 12, Nombre = "  ", RecargoCombustible = 0m });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PutAgencia_ActualizaTodosLosCampos()
        {
            var innovatrans = new AgenciaTransporte { Numero = 12, Empresa = "1  ", Nombre = "Innovatrans", RecargoCombustible = 0.025m };
            Datos(innovatrans);
            A.CallTo(() => db.SaveChanges()).Returns(1);

            var dto = new AgenciaTransporteDTO
            {
                Numero = 12,
                Empresa = "1",
                Nombre = "Innovatrans",
                Identificador = "91253",
                RecargoCombustible = 0.03m
            };

            controller.PutAgencia(12, dto);

            Assert.AreEqual("91253", innovatrans.Identificador);
            Assert.AreEqual(0.03m, innovatrans.RecargoCombustible);
            A.CallTo(() => db.SaveChanges()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void PutAgencia_NoExiste_DevuelveNotFound()
        {
            Datos();

            var resultado = controller.PutAgencia(99, new AgenciaTransporteDTO { Numero = 99, Nombre = "X", RecargoCombustible = 0m });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public void PutAgencia_RecargoNegativo_DevuelveBadRequest()
        {
            Datos(new AgenciaTransporte { Numero = 12, Nombre = "Innovatrans" });

            var resultado = controller.PutAgencia(12, new AgenciaTransporteDTO { Numero = 12, Nombre = "Innovatrans", RecargoCombustible = -0.1m });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void GetMasEconomica_SinAgenciasDadasDeAlta_DevuelveNotFound()
        {
            // Sin filas en AgenciasTransporte, ninguna tarifa entra en la comparación.
            Datos();

            var resultado = controller.GetMasEconomica("08001", peso: 3m, empresa: "1", reembolso: 0m);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public void GetMasEconomica_CodigoPostalPeninsular_DevuelveGLS()
        {
            // GLS (BusinessParcel 5kg=3,66) gana a Innovatrans Economy (5kg=4,53) en Peninsular.
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
