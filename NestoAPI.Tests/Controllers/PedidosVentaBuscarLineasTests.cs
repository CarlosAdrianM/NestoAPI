using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PedidosVentaBuscarLineasTests
    {
        private NVEntities db;
        private PedidosVentaController controller;
        private DbSet<LinPedidoVta> fakeLinPedidoVtas;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeLinPedidoVtas = A.Fake<DbSet<LinPedidoVta>>(o => o
                .Implements<IQueryable<LinPedidoVta>>()
                .Implements<IDbAsyncEnumerable<LinPedidoVta>>());

            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLinPedidoVtas);

            ConfigurarFakeDbSet(fakeLinPedidoVtas, new List<LinPedidoVta>().AsQueryable());

            controller = new PedidosVentaController(db);
        }

        [TestMethod]
        public async Task BuscarLineas_TextoMenorDe3Caracteres_RetornaBadRequest()
        {
            var result = await controller.GetBuscarLineas("15191", "ab");

            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task BuscarLineas_TextoVacio_RetornaBadRequest()
        {
            var result = await controller.GetBuscarLineas("15191", "");

            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task BuscarLineas_TextoNull_RetornaBadRequest()
        {
            var result = await controller.GetBuscarLineas("15191", null);

            Assert.IsInstanceOfType(result, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task BuscarLineas_BuscaPorTextoContains_RetornaLineas()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                },
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900002,
                    Nº_Cliente = "15191",
                    Producto = "BB200",
                    Texto = "Acondicionador Normal 250ml",
                    Nº_Factura = "NV26/001235",
                    Fecha_Factura = new System.DateTime(2026, 2, 20),
                    Fecha_Entrega = new System.DateTime(2026, 2, 18),
                    Cantidad = 1,
                    Base_Imponible = 12.00m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                },
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900003,
                    Nº_Cliente = "99999",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001300",
                    Fecha_Factura = new System.DateTime(2026, 3, 1),
                    Fecha_Entrega = new System.DateTime(2026, 3, 1),
                    Cantidad = 5,
                    Base_Imponible = 63.75m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "Champú");

            Assert.IsInstanceOfType(result, typeof(OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual(900001, okResult.Content[0].Pedido);
            Assert.AreEqual("NV26/001234", okResult.Content[0].Factura);
        }

        [TestMethod]
        public async Task BuscarLineas_BuscaPorProductoExacto_RetornaLineas()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                },
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900002,
                    Nº_Cliente = "15191",
                    Producto = "BB200",
                    Texto = "Acondicionador Normal 250ml",
                    Nº_Factura = "NV26/001235",
                    Fecha_Factura = new System.DateTime(2026, 2, 20),
                    Fecha_Entrega = new System.DateTime(2026, 2, 18),
                    Cantidad = 1,
                    Base_Imponible = 12.00m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "AA100");

            Assert.IsInstanceOfType(result, typeof(OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual("AA100", okResult.Content[0].Producto);
        }

        [TestMethod]
        public async Task BuscarLineas_NoFiltraPorEmpresa_RetornaDeTodasLasEmpresas()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                },
                new LinPedidoVta
                {
                    Empresa = "2",
                    Número = 800001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "E226/000100",
                    Fecha_Factura = new System.DateTime(2026, 2, 1),
                    Fecha_Entrega = new System.DateTime(2026, 2, 1),
                    Cantidad = 3,
                    Base_Imponible = 38.25m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "Champú");

            Assert.IsInstanceOfType(result, typeof(OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.IsTrue(okResult.Content.Any(l => l.Empresa == "1"));
            Assert.IsTrue(okResult.Content.Any(l => l.Empresa == "2"));
        }

        [TestMethod]
        public async Task BuscarLineas_ExcluyeLineasConEstadoMenorQueEnCurso()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                },
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900002,
                    Nº_Cliente = "15191",
                    Producto = "BB200",
                    Texto = "Champú Barato Cancelado",
                    Fecha_Entrega = new System.DateTime(2026, 2, 1),
                    Cantidad = 1,
                    Base_Imponible = 5.00m,
                    Estado = Constantes.EstadosLineaVenta.PRESUPUESTO
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "Champú");

            Assert.IsInstanceOfType(result, typeof(OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            Assert.AreEqual(1, okResult.Content.Count);
            Assert.AreEqual(900001, okResult.Content[0].Pedido);
        }

        [TestMethod]
        public async Task BuscarLineas_SinResultados_RetornaListaVacia()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "ProductoInexistente");

            Assert.IsInstanceOfType(result, typeof(OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>));
            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        public async Task BuscarLineas_RetornaCamposCorrectamente()
        {
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 900001,
                    Nº_Cliente = "15191",
                    Producto = "AA100",
                    Texto = "Champú Especial 500ml",
                    Nº_Factura = "NV26/001234",
                    Fecha_Factura = new System.DateTime(2026, 1, 15),
                    Fecha_Entrega = new System.DateTime(2026, 1, 10),
                    Cantidad = 2,
                    Base_Imponible = 25.50m,
                    Estado = Constantes.EstadosLineaVenta.FACTURA
                }
            }.AsQueryable();

            ConfigurarFakeDbSet(fakeLinPedidoVtas, lineas);

            var result = await controller.GetBuscarLineas("15191", "Champú");

            var okResult = (OkNegotiatedContentResult<List<LineaPedidoVentaBusquedaDTO>>)result;
            var linea = okResult.Content[0];
            Assert.AreEqual("1", linea.Empresa);
            Assert.AreEqual(900001, linea.Pedido);
            Assert.AreEqual("NV26/001234", linea.Factura);
            Assert.AreEqual(new System.DateTime(2026, 1, 15), linea.FechaFactura);
            Assert.AreEqual("AA100", linea.Producto);
            Assert.AreEqual("Champú Especial 500ml", linea.Texto);
            Assert.AreEqual((short)2, linea.Cantidad);
            Assert.AreEqual(25.50m, linea.BaseImponible);
        }

        #region Helpers

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

        #endregion
    }
}
