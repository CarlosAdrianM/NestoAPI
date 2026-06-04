using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PlanesVentajas;
using NestoAPI.Models;
using NestoAPI.Models.PlanesVentajas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PlanesVentajas
{
    [TestClass]
    public class PlanesVentajasServiceTests
    {
        [TestMethod]
        public async Task ListarPlanesAsync_DevuelveLosPlanesOrdenadosPorFechaFin()
        {
            // NestoAPI#219: el orden por FechaFin se hace AHORA en memoria, no en SQL. En SQL, con los
            // joins de navegación y las subconsultas del filtro, EF6 duplicaba 'Número' en el ORDER BY y
            // rompía con 500. Un fake (LINQ to Objects) no reproduce ese fallo de traducción SQL, pero
            // este test protege que el método sigue devolviendo la lista ordenada por FechaFin.
            IQueryable<PlanVentajas> planes = new List<PlanVentajas>
            {
                CrearPlan(2, new DateTime(2026, 12, 31)),
                CrearPlan(1, new DateTime(2026, 6, 30)),
                CrearPlan(3, new DateTime(2026, 9, 15))
            }.AsQueryable();

            NVEntities db = A.Fake<NVEntities>();
            DbSet<PlanVentajas> fakeSet = A.Fake<DbSet<PlanVentajas>>(o => o
                .Implements<IQueryable<PlanVentajas>>()
                .Implements<IDbAsyncEnumerable<PlanVentajas>>());
            ConfigurarFakeDbSet(fakeSet, planes);
            A.CallTo(() => db.PlanesVentajas).Returns(fakeSet);

            PlanesVentajasService servicio = new PlanesVentajasService(db);

            List<PlanVentajasDTO> resultado = await servicio.ListarPlanesAsync(null, null, incluirCancelados: true);

            CollectionAssert.AreEqual(
                new[] { new DateTime(2026, 6, 30), new DateTime(2026, 9, 15), new DateTime(2026, 12, 31) },
                resultado.Select(r => r.FechaFin).ToArray());
        }

        private static PlanVentajas CrearPlan(int numero, DateTime fechaFin)
        {
            return new PlanVentajas
            {
                Numero = numero,
                Empresa = "1",
                FechaInicio = new DateTime(2026, 1, 1),
                FechaFin = fechaFin,
                Importe = 100,
                Familia = "F",
                Estado = 1,
                Comentarios = "",
                Empresa1 = new Empresa { Nombre = "Nueva Visión" },
                EstadosPlanVentaja = new EstadoPlanVentajas { Descripcion = "Activo" },
                PlanVentajasClientes = new List<PlanVentajasCliente>()
            };
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
