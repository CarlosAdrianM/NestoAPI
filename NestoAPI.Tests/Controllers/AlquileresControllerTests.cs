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
    }
}
