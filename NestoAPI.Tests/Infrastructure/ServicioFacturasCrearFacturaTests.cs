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

        // NestoAPI#304: el CCC de la cabecera debe existir en la tabla CCC para
        // (empresa, cliente, contacto); si no, el SP revienta con FK_CabFacturaVta_CCC.

        [TestMethod]
        public async Task CrearFactura_CCCInexistente_LanzaAntesDeLlamarAlSPConMensajeClaro()
        {
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 922300,
                Nº_Cliente = "15191",
                Contacto = "0",
                CCC = "3",
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());
            ConfigurarFakeCCCs(new List<CCC>()); // la cuenta 3 ya no existe

            var servicio = new ServicioFacturas(db);

            var ex = await Assert.ThrowsExceptionAsync<FacturacionException>(
                () => servicio.CrearFactura("1", 922300, "test"));
            StringAssert.Contains(ex.Message, "cuenta bancaria");
            StringAssert.Contains(ex.Message, "922300");
            StringAssert.Contains(ex.Message, "'3'");
        }

        [TestMethod]
        public async Task CrearFactura_CCCExistente_NoLanzaLaValidacionDeCCC()
        {
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 922301,
                Nº_Cliente = "15191",
                Contacto = "0",
                CCC = "3",
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());
            ConfigurarFakeCCCs(new List<CCC>
            {
                new CCC { Empresa = "1", Cliente = "15191", Contacto = "0", Número = "3" }
            });

            var servicio = new ServicioFacturas(db);

            // Con el CCC válido, el flujo pasa la validación y falla más adelante (fakes sin SP);
            // lo que importa es que NO sea el error de cuenta bancaria.
            try
            {
                _ = await servicio.CrearFactura("1", 922301, "test");
            }
            catch (System.Exception ex)
            {
                Assert.IsFalse(ex.Message.Contains("cuenta bancaria"),
                    $"No debía lanzar la validación de CCC: {ex.Message}");
            }
        }

        [TestMethod]
        public async Task CrearFactura_SinCCC_NoValidaCuentaBancaria()
        {
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 922302,
                Nº_Cliente = "15191",
                Contacto = "0",
                CCC = null, // pedido sin cuenta (p. ej. transferencia)
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());
            ConfigurarFakeCCCs(new List<CCC>());

            var servicio = new ServicioFacturas(db);

            try
            {
                _ = await servicio.CrearFactura("1", 922302, "test");
            }
            catch (System.Exception ex)
            {
                Assert.IsFalse(ex.Message.Contains("cuenta bancaria"),
                    $"Sin CCC no hay nada que validar: {ex.Message}");
            }
        }

        private void ConfigurarFakeCCCs(List<CCC> cccs)
        {
            var fakeCCCs = A.Fake<DbSet<CCC>>(o => o.Implements<IQueryable<CCC>>().Implements<IDbAsyncEnumerable<CCC>>());
            A.CallTo(() => db.CCCs).Returns(fakeCCCs);
            ConfigurarFakeDbSet(fakeCCCs, cccs.AsQueryable());
        }

        [TestMethod]
        public async Task CrearFactura_RutaInexistente_LanzaAntesDeLlamarAlSP()
        {
            // NestoAPI#276: si la ruta del pedido no existe en Rutas, el SP prdCrearFacturaVta fallaría al
            // insertar el apunte en ExtractoCliente (FK_ExtractoCliente_Rutas) dejando el @@TRANCOUNT
            // descuadrado. Debemos cortar antes con un mensaje claro sobre la ruta.
            var pedido = new CabPedidoVta
            {
                Empresa = "2",
                Número = 400,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Agrupada = false,
                IVA = "G", // con IVA, para pasar la validación de IVA y llegar a la de ruta
                Ruta = "16 ", // ruta que NO existe en Rutas
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            // db.Rutas vacío -> la ruta "16" no existe
            var fakeRutas = A.Fake<DbSet<Ruta>>(o => o.Implements<IQueryable<Ruta>>().Implements<IDbAsyncEnumerable<Ruta>>());
            ConfigurarFakeDbSet(fakeRutas, new List<Ruta>().AsQueryable());
            A.CallTo(() => db.Rutas).Returns(fakeRutas);

            var servicio = new ServicioFacturas(db);

            var ex = await Assert.ThrowsExceptionAsync<FacturacionException>(
                () => servicio.CrearFactura("2", 400, "test"));
            Assert.IsTrue(ex.Message.Contains("ruta"), $"El mensaje debe mencionar la ruta. Actual: {ex.Message}");
            Assert.IsTrue(ex.Message.Contains("16"), "El mensaje debe indicar la ruta problemática.");
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
