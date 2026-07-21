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
        private NVEntities db;

        [TestInitialize]
        public void Initialize()
        {
            db = A.Fake<NVEntities>();
            ConfigurarSet(db, new List<Empresa>
            {
                new Empresa { Número = "1  ", Nombre = "Nueva Visión" }
            }.AsQueryable(), s => A.CallTo(() => db.Empresas).Returns(s));
            ConfigurarSet(db, new List<EstadoPlanVentajas>
            {
                new EstadoPlanVentajas { Numero = 1, Descripcion = "Activo" }
            }.AsQueryable(), s => A.CallTo(() => db.EstadosPlanesVentajas).Returns(s));
            ConfigurarSet(db, new List<PlanVentajasCliente>
            {
                new PlanVentajasCliente { NumeroContrato = 1, Cliente = "15191" },
                new PlanVentajasCliente { NumeroContrato = 1, Cliente = "38404" },
                new PlanVentajasCliente { NumeroContrato = 2, Cliente = "12345" }
            }.AsQueryable(), s => A.CallTo(() => db.PlanesVentajasClientes).Returns(s));
        }

        [TestMethod]
        public async Task ListarPlanesAsync_DevuelveLosPlanesOrdenadosPorFechaFin()
        {
            // NestoAPI#219: el orden por FechaFin se hace AHORA en memoria, no en SQL. En SQL, con los
            // joins de navegación y las subconsultas del filtro, EF6 duplicaba 'Número' en el ORDER BY y
            // rompía con 500. Un fake (LINQ to Objects) no reproduce ese fallo de traducción SQL, pero
            // este test protege que el método sigue devolviendo la lista ordenada por FechaFin.
            ConfigurarPlanes(
                CrearPlan(2, new DateTime(2026, 12, 31)),
                CrearPlan(1, new DateTime(2026, 6, 30)),
                CrearPlan(3, new DateTime(2026, 9, 15)));

            PlanesVentajasService servicio = new PlanesVentajasService(db);

            List<PlanVentajasDTO> resultado = await servicio.ListarPlanesAsync(null, null, incluirCancelados: true);

            CollectionAssert.AreEqual(
                new[] { new DateTime(2026, 6, 30), new DateTime(2026, 9, 15), new DateTime(2026, 12, 31) },
                resultado.Select(r => r.FechaFin).ToArray());
        }

        [TestMethod]
        public async Task ListarPlanesAsync_ComponeEmpresaEstadoYClientesSinUsarLasNavegaciones()
        {
            // NestoAPI#341 (resto de #219): quitar el ORDER BY no bastó — la proyección con navegaciones
            // (p.Empresa1.Nombre, p.EstadosPlanVentaja.Descripcion, p.PlanVentajasClientes) seguía
            // generando el SQL con 'Número' duplicado y el listado base devolvía 500 ("La columna 'Número'
            // se ha especificado varias veces para 'Project1'"). Ahora el listado materializa los planes
            // planos y compone empresa/estado/clientes desde sus propios DbSets. Los planes de este test
            // NO llevan las navegaciones cargadas (como en la realidad, con LazyLoading desactivado): con
            // la implementación antigua esto revienta; con la nueva, los datos salen igual de completos.
            ConfigurarPlanes(CrearPlan(1, new DateTime(2026, 6, 30)));

            PlanesVentajasService servicio = new PlanesVentajasService(db);

            List<PlanVentajasDTO> resultado = await servicio.ListarPlanesAsync(null, null, incluirCancelados: true);

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("Nueva Visión", resultado[0].EmpresaNombre, "El nombre sale de db.Empresas, con Trim del padding");
            Assert.AreEqual("Activo", resultado[0].EstadoDescripcion, "La descripción sale de db.EstadosPlanesVentajas");
            CollectionAssert.AreEquivalent(new[] { "15191", "38404" }, resultado[0].Clientes.ToArray(),
                "Los clientes salen de db.PlanesVentajasClientes filtrando por el número de plan");
        }

        [TestMethod]
        public async Task ListarPlanesAsync_SinCancelados_FiltraElEstadoCancelado()
        {
            PlanVentajas cancelado = CrearPlan(2, new DateTime(2026, 12, 31));
            cancelado.Estado = 6;
            ConfigurarPlanes(CrearPlan(1, new DateTime(2026, 6, 30)), cancelado);

            PlanesVentajasService servicio = new PlanesVentajasService(db);

            List<PlanVentajasDTO> resultado = await servicio.ListarPlanesAsync(null, null, incluirCancelados: false);

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(1, resultado[0].Numero);
        }

        private void ConfigurarPlanes(params PlanVentajas[] planes)
        {
            ConfigurarSet(db, planes.AsQueryable(), s => A.CallTo(() => db.PlanesVentajas).Returns(s));
        }

        private static PlanVentajas CrearPlan(int numero, DateTime fechaFin)
        {
            // Sin Empresa1/EstadosPlanVentaja/PlanVentajasClientes a propósito: el listado no debe tocarlas
            return new PlanVentajas
            {
                Numero = numero,
                Empresa = "1",
                FechaInicio = new DateTime(2026, 1, 1),
                FechaFin = fechaFin,
                Importe = 100,
                Familia = "F",
                Estado = 1,
                Comentarios = ""
            };
        }

        private static void ConfigurarSet<T>(NVEntities db, IQueryable<T> data, Action<DbSet<T>> asignar) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o
                .Implements<IQueryable<T>>()
                .Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).Returns(data.GetEnumerator());
            asignar(fakeSet);
        }
    }
}
