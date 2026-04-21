using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue NestoAPI#168 (TiendasNuevaVision#29): el check "¿tiene compras recientes?"
    /// debe contar compras en cualquier empresa, no solo la 1.
    /// </summary>
    [TestClass]
    public class ClienteHelperTests
    {
        private NVEntities db;
        private DbSet<ExtractoCliente> fakeExtractos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>().Implements<IDbAsyncEnumerable<ExtractoCliente>>());
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractos);
        }

        [TestMethod]
        public async Task ClienteConComprasRecientes_SoloComprasEnEmpresa2_DevuelveTrue()
        {
            // Regla 2: debe considerar cliente al que tiene compras en cualquier empresa.
            var datos = new List<ExtractoCliente>
            {
                CrearFactura(empresa: "2", cliente: "CLI1", importe: 100m, fecha: DateTime.Now.AddDays(-30))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            bool resultado = await ClienteHelper.ClienteConComprasRecientesAsync(db, "CLI1");

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public async Task ClienteConComprasRecientes_ComprasSoloEnEmpresa1_DevuelveTrue()
        {
            var datos = new List<ExtractoCliente>
            {
                CrearFactura(empresa: "1", cliente: "CLI1", importe: 100m, fecha: DateTime.Now.AddDays(-30))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            bool resultado = await ClienteHelper.ClienteConComprasRecientesAsync(db, "CLI1");

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public async Task ClienteConComprasRecientes_SinCompras_DevuelveFalse()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>().AsQueryable());

            bool resultado = await ClienteHelper.ClienteConComprasRecientesAsync(db, "CLI1");

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public async Task ClienteConComprasRecientes_ComprasHaceMasDeUnAño_DevuelveFalse()
        {
            var datos = new List<ExtractoCliente>
            {
                CrearFactura(empresa: "1", cliente: "CLI1", importe: 100m, fecha: DateTime.Now.AddDays(-400))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            bool resultado = await ClienteHelper.ClienteConComprasRecientesAsync(db, "CLI1");

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public async Task ClienteConComprasRecientes_SoloApuntesNoFactura_DevuelveFalse()
        {
            var datos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Empresa = "2",
                    Número = "CLI1",
                    TipoApunte = "2",  // no es FACTURA
                    Importe = 100m,
                    Fecha = DateTime.Now.AddDays(-30)
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            bool resultado = await ClienteHelper.ClienteConComprasRecientesAsync(db, "CLI1");

            Assert.IsFalse(resultado);
        }

        private static ExtractoCliente CrearFactura(string empresa, string cliente, decimal importe, DateTime fecha) =>
            new ExtractoCliente
            {
                Empresa = empresa,
                Número = cliente,
                TipoApunte = Constantes.ExtractosCliente.TiposApunte.FACTURA,
                Importe = importe,
                Fecha = fecha
            };

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
