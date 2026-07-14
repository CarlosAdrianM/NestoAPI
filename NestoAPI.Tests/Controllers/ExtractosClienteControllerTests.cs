using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests de ExtractosClienteController.
    /// Issue NestoAPI#168 (TiendasNuevaVision#29): parámetro opcional filtrarPorEmpresa.
    /// </summary>
    [TestClass]
    public class ExtractosClienteControllerTests
    {
        private NVEntities db;
        private DbSet<ExtractoCliente> fakeExtractos;
        private ExtractosClienteController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>().Implements<IDbAsyncEnumerable<ExtractoCliente>>());
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractos);

            controller = new ExtractosClienteController(A.Fake<IServicioCorreoElectronico>(), db);
        }

        [TestMethod]
        public void GetExtractosCliente_SinFiltrarPorEmpresa_DevuelveMovimientosDeTodasLasEmpresas()
        {
            // Arrange: misma Número de cliente en empresas 1 y 2.
            var datos = new List<ExtractoCliente>
            {
                Crear("1", "CLI1", "1", new DateTime(2026, 1, 15)),
                Crear("2", "CLI1", "1", new DateTime(2026, 2, 10)),
                Crear("3", "CLI1", "1", new DateTime(2026, 3, 5))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            // Act (default filtrarPorEmpresa = false)
            var resultado = controller.GetExtractosCliente(
                empresa: "1",
                cliente: "CLI1",
                tipoApunte: "1",
                fechaDesde: new DateTime(2026, 1, 1),
                fechaHasta: new DateTime(2026, 12, 31))
                .ToList();

            // Assert: salen los 3 (retrocompatibilidad con Nesto/NestoApp)
            Assert.AreEqual(3, resultado.Count);
        }

        [TestMethod]
        public void GetExtractosCliente_ConFiltrarPorEmpresa_DevuelveSoloLaEmpresaPedida()
        {
            // Arrange: dos movimientos en empresa "1" y uno en empresa "2".
            var datos = new List<ExtractoCliente>
            {
                Crear("1", "CLI1", "1", new DateTime(2026, 1, 15)),
                Crear("1", "CLI1", "1", new DateTime(2026, 2, 10)),
                Crear("2", "CLI1", "1", new DateTime(2026, 3, 5))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            // Act
            var resultado = controller.GetExtractosCliente(
                empresa: "1",
                cliente: "CLI1",
                tipoApunte: "1",
                fechaDesde: new DateTime(2026, 1, 1),
                fechaHasta: new DateTime(2026, 12, 31),
                filtrarPorEmpresa: true)
                .ToList();

            // Assert: solo las dos de empresa "1"
            Assert.AreEqual(2, resultado.Count);
            Assert.IsTrue(resultado.All(e => e.empresa == "1"));
        }

        [TestMethod]
        public void GetExtractosCliente_ConFiltrarPorEmpresa_YSinMovimientosDeEsaEmpresa_DevuelveVacio()
        {
            // Arrange: cliente con compras únicamente en empresas 2 y 3. Al pedir
            // filtrarPorEmpresa=true empresa="1" no debería devolver nada.
            var datos = new List<ExtractoCliente>
            {
                Crear("2", "CLI1", "1", new DateTime(2026, 1, 15)),
                Crear("3", "CLI1", "1", new DateTime(2026, 2, 10))
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractos, datos);

            // Act
            var resultado = controller.GetExtractosCliente(
                empresa: "1",
                cliente: "CLI1",
                tipoApunte: "1",
                fechaDesde: new DateTime(2026, 1, 1),
                fechaHasta: new DateTime(2026, 12, 31),
                filtrarPorEmpresa: true)
                .ToList();

            Assert.AreEqual(0, resultado.Count);
        }

        // ----- DeudaVencida (Nesto#340, 1C.8 slice 2) -----

        [TestMethod]
        public void GetDeudaVencida_SumaSoloLoVencidoDeLasEmpresas1Y3()
        {
            var vencidoHace30 = ConDeuda(Crear("1", "9471", "1", DateTime.Today), -30, 100.50m);
            var vencidoEspejo = ConDeuda(Crear("3", "9471", "1", DateTime.Today), -10, 49.50m);
            var noVencido = ConDeuda(Crear("1", "9471", "1", DateTime.Today), 30, 500m);
            var otraEmpresa = ConDeuda(Crear("5", "9471", "1", DateTime.Today), -30, 999m);
            var pagado = ConDeuda(Crear("1", "9471", "1", DateTime.Today), -30, 0m);
            var otroCliente = ConDeuda(Crear("1", "11111", "1", DateTime.Today), -30, 999m);
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                vencidoHace30, vencidoEspejo, noVencido, otraEmpresa, pagado, otroCliente
            }.AsQueryable());

            var resultado = controller.GetDeudaVencida("9471", "0")
                as System.Web.Http.Results.OkNegotiatedContentResult<decimal>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(150m, resultado.Content, "100,50 (emp. 1) + 49,50 (espejo): sin lo no vencido, pagado, de otras empresas u otros clientes");
        }

        [TestMethod]
        public void GetDeudaVencida_SinMovimientos_DevuelveCero()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>().AsQueryable());

            var resultado = controller.GetDeudaVencida("9471", "0")
                as System.Web.Http.Results.OkNegotiatedContentResult<decimal>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0m, resultado.Content);
        }

        // ----- PutExtractoCliente (#297: CK_ExtractoCliente = un impagado no puede tener CCC) -----

        [TestMethod]
        public async System.Threading.Tasks.Task Put_PonerCccAUnImpagado_Devuelve400SinGuardar()
        {
            var impagado = Crear("1", "9471", Constantes.ExtractosCliente.TiposApunte.IMPAGADO, DateTime.Today);
            impagado.Nº_Orden = 77;
            impagado.FechaVto = DateTime.Today;
            impagado.Estado = "";
            impagado.FormaPago = "RCB";
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { impagado }.AsQueryable());

            var resultado = await controller.PutExtractoCliente(new ExtractoClienteDTO
            {
                id = 77,
                vencimiento = DateTime.Today,
                ccc = "1",
                estado = "",
                formaPago = "RCB"
            });

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.BadRequestErrorMessageResult));
            StringAssert.Contains(((System.Web.Http.Results.BadRequestErrorMessageResult)resultado).Message, "impagado");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async System.Threading.Tasks.Task Put_PonerCccAUnApunteNormal_Guarda()
        {
            var factura = Crear("1", "9471", Constantes.ExtractosCliente.TiposApunte.FACTURA, DateTime.Today);
            factura.Nº_Orden = 78;
            factura.FechaVto = DateTime.Today;
            factura.Estado = "";
            factura.FormaPago = "RCB";
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { factura }.AsQueryable());

            var resultado = await controller.PutExtractoCliente(new ExtractoClienteDTO
            {
                id = 78,
                vencimiento = DateTime.Today,
                ccc = "1",
                estado = "",
                formaPago = "RCB"
            });

            Assert.IsInstanceOfType(resultado, typeof(System.Web.Http.Results.StatusCodeResult));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        private static ExtractoCliente ConDeuda(ExtractoCliente extracto, int diasHastaVencimiento, decimal importePendiente)
        {
            extracto.FechaVto = DateTime.Today.AddDays(diasHastaVencimiento);
            extracto.ImportePdte = importePendiente;
            return extracto;
        }

        private static ExtractoCliente Crear(string empresa, string numero, string tipoApunte, DateTime fecha) =>
            new ExtractoCliente
            {
                Empresa = empresa,
                Número = numero,
                TipoApunte = tipoApunte,
                Fecha = fecha,
                Contacto = "0",
                Nº_Documento = "",
                Efecto = "",
                Concepto = "",
                Vendedor = "",
                CCC = "",
                Ruta = "",
                Estado = "",
                FormaPago = "",
                Delegación = "",
                FormaVenta = "",
                Usuario = ""
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
