using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Alquileres;
using NestoAPI.Models.Alquileres;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class AlquileresControllerTests
    {
        private IProductosAlquilerService servicio;
        private AlquileresController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IProductosAlquilerService>();
            controller = new AlquileresController(servicio);
        }

        [TestMethod]
        public async Task GetProductosAlquiler_DevuelveLaListaDelServicio()
        {
            var lista = new List<ProductoAlquilerDTO>
            {
                new ProductoAlquilerDTO { Empresa = "1", Numero = "26780", Nombre = "Aparato X", Stock = 10, StockAlquileres = 3, Diferencia = 7 },
                new ProductoAlquilerDTO { Empresa = "1", Numero = "26781", Nombre = "Aparato Y", Stock = 5, StockAlquileres = 5, Diferencia = 0 }
            };
            A.CallTo(() => servicio.LeerProductosAlquilerAsync()).Returns(Task.FromResult(lista));

            var resultado = await controller.GetProductosAlquiler();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ProductoAlquilerDTO>>));
            var ok = (OkNegotiatedContentResult<List<ProductoAlquilerDTO>>)resultado;
            Assert.AreEqual(2, ok.Content.Count);
            Assert.AreEqual("26780", ok.Content[0].Numero);
            Assert.AreEqual(7, ok.Content[0].Diferencia);
            A.CallTo(() => servicio.LeerProductosAlquilerAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetProductosAlquiler_SinProductos_DevuelveListaVacia()
        {
            A.CallTo(() => servicio.LeerProductosAlquilerAsync())
                .Returns(Task.FromResult(new List<ProductoAlquilerDTO>()));

            var resultado = await controller.GetProductosAlquiler();

            var ok = (OkNegotiatedContentResult<List<ProductoAlquilerDTO>>)resultado;
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        public async Task GetMovimientosAlquiler_DevuelveLosMovimientosDelServicio()
        {
            var lista = new List<MovimientoAlquilerDTO>
            {
                new MovimientoAlquilerDTO { NumeroOrden = 1, Producto = "26780", Texto = "Alquiler enero", Cantidad = 1, Precio = 50m, Total = 50m, Estado = 4 },
                new MovimientoAlquilerDTO { NumeroOrden = 2, Producto = "26780", Texto = "Alquiler febrero", Cantidad = 1, Precio = 50m, Total = 50m, Estado = 1 }
            };
            A.CallTo(() => servicio.LeerMovimientosAlquilerAsync("1", 12345)).Returns(Task.FromResult(lista));

            var resultado = await controller.GetMovimientosAlquiler("1", 12345);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<MovimientoAlquilerDTO>>));
            var ok = (OkNegotiatedContentResult<List<MovimientoAlquilerDTO>>)resultado;
            Assert.AreEqual(2, ok.Content.Count);
            Assert.AreEqual(2, ok.Content[1].NumeroOrden);
            A.CallTo(() => servicio.LeerMovimientosAlquilerAsync("1", 12345)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetMovimientosAlquiler_SinMovimientos_DevuelveListaVacia()
        {
            A.CallTo(() => servicio.LeerMovimientosAlquilerAsync(A<string>._, A<int>._))
                .Returns(Task.FromResult(new List<MovimientoAlquilerDTO>()));

            var resultado = await controller.GetMovimientosAlquiler("1", 999);

            var ok = (OkNegotiatedContentResult<List<MovimientoAlquilerDTO>>)resultado;
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        public async Task GetComprasAlquiler_DevuelveLasComprasDelServicio()
        {
            var lista = new List<CompraAlquilerDTO>
            {
                new CompraAlquilerDTO { NumeroOrden = 1, NumeroPedido = 555, Proveedor = "PROV1", Producto = "26780", NumSerie = "ABC123", Texto = "Compra aparato", Cantidad = 1, Precio = 300m, Total = 363m, Estado = 4 },
                new CompraAlquilerDTO { NumeroOrden = 2, NumeroPedido = 556, Proveedor = "PROV1", Producto = "26780", NumSerie = "ABC123", Texto = "Reparación", Cantidad = 1, Precio = 50m, Total = 60.5m, Estado = 1 }
            };
            A.CallTo(() => servicio.LeerComprasAlquilerAsync("26780", "ABC123")).Returns(Task.FromResult(lista));

            var resultado = await controller.GetComprasAlquiler("26780", "ABC123");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<CompraAlquilerDTO>>));
            var ok = (OkNegotiatedContentResult<List<CompraAlquilerDTO>>)resultado;
            Assert.AreEqual(2, ok.Content.Count);
            Assert.AreEqual(556, ok.Content[1].NumeroPedido);
            Assert.AreEqual("ABC123", ok.Content[0].NumSerie);
            A.CallTo(() => servicio.LeerComprasAlquilerAsync("26780", "ABC123")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetComprasAlquiler_SinCompras_DevuelveListaVacia()
        {
            A.CallTo(() => servicio.LeerComprasAlquilerAsync(A<string>._, A<string>._))
                .Returns(Task.FromResult(new List<CompraAlquilerDTO>()));

            var resultado = await controller.GetComprasAlquiler("99999", "ZZZ");

            var ok = (OkNegotiatedContentResult<List<CompraAlquilerDTO>>)resultado;
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        public async Task GetInmovilizadosAlquiler_DevuelveElExtractoDelServicio()
        {
            var lista = new List<ExtractoInmovilizadoDTO>
            {
                new ExtractoInmovilizadoDTO { NumeroOrden = 1, Concepto = "Compra aparato", NumeroDocumento = "555", Importe = 300m, ImportePendiente = 0m, Estado = 4 },
                new ExtractoInmovilizadoDTO { NumeroOrden = 2, Concepto = "Amortización", NumeroDocumento = "A001", Importe = -25m, ImportePendiente = 0m, Estado = 1 }
            };
            A.CallTo(() => servicio.LeerInmovilizadosAlquilerAsync("1", "ALQ000123")).Returns(Task.FromResult(lista));

            var resultado = await controller.GetInmovilizadosAlquiler("1", "ALQ000123");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ExtractoInmovilizadoDTO>>));
            var ok = (OkNegotiatedContentResult<List<ExtractoInmovilizadoDTO>>)resultado;
            Assert.AreEqual(2, ok.Content.Count);
            Assert.AreEqual("Compra aparato", ok.Content[0].Concepto);
            A.CallTo(() => servicio.LeerInmovilizadosAlquilerAsync("1", "ALQ000123")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetInmovilizadosAlquiler_SinExtracto_DevuelveListaVacia()
        {
            A.CallTo(() => servicio.LeerInmovilizadosAlquilerAsync(A<string>._, A<string>._))
                .Returns(Task.FromResult(new List<ExtractoInmovilizadoDTO>()));

            var resultado = await controller.GetInmovilizadosAlquiler("1", "NOEXISTE");

            var ok = (OkNegotiatedContentResult<List<ExtractoInmovilizadoDTO>>)resultado;
            Assert.AreEqual(0, ok.Content.Count);
        }
    }
}
