using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ServicioFacturasCrearFacturaTests
    {
        private NVEntities db;
        private DbSet<CabPedidoVta> fakePedidos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakePedidos = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakePedidos);
            A.CallTo(() => fakePedidos.Include(A<string>.Ignored)).Returns(fakePedidos);
        }

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

        [TestMethod]
        public async Task CrearFactura_PedidoFDM_NoAgrupada_RetornaPeriodoFacturacionSinFacturar()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 100,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                Agrupada = false,
                LinPedidoVtas = new List<LinPedidoVta>()
            };

            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var servicio = new ServicioFacturas(db);

            // Act
            CrearFacturaResponseDTO resultado = await servicio.CrearFactura("1", 100, "test");

            // Assert
            Assert.AreEqual(Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES, resultado.NumeroFactura);
            Assert.AreEqual("1", resultado.Empresa);
            Assert.AreEqual(100, resultado.NumeroPedido);
        }

        [TestMethod]
        public async Task CrearFactura_PedidoFDM_Agrupada_NoBloqueaYContinuaFacturacion()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 200,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                Agrupada = true,
                IVA = null, // Sin IVA para que falle despues del check FDM
                LinPedidoVtas = new List<LinPedidoVta>()
            };

            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var servicio = new ServicioFacturas(db);

            // Act & Assert
            // Si Agrupada=true, el metodo NO retorna el periodo FDM sino que continua
            // y lanza FacturacionException por falta de IVA (siguiente validacion)
            var ex = await Assert.ThrowsExceptionAsync<FacturacionException>(
                () => servicio.CrearFactura("1", 200, "test"));
            Assert.IsTrue(ex.Message.Contains("IVA"));
        }

        [TestMethod]
        public async Task CrearFactura_PedidoNormal_NoAgrupada_NoBloqueaPorFDM()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 300,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Agrupada = false,
                IVA = null, // Sin IVA para que falle despues del check FDM
                LinPedidoVtas = new List<LinPedidoVta>()
            };

            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var servicio = new ServicioFacturas(db);

            // Act & Assert
            // Un pedido NRM no se bloquea por FDM, continua y falla por IVA
            var ex = await Assert.ThrowsExceptionAsync<FacturacionException>(
                () => servicio.CrearFactura("1", 300, "test"));
            Assert.IsTrue(ex.Message.Contains("IVA"));
        }
    }
}
