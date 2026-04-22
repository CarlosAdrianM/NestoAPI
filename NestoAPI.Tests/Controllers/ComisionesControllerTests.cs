using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Comisiones;
using NestoAPI.Models.Comisiones;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ComisionesControllerTests
    {
        private IComisionesLecturaService _lectura;
        private ComisionesController _controller;

        [TestInitialize]
        public void Setup()
        {
            _lectura = A.Fake<IComisionesLecturaService>();
            _controller = new ComisionesController(_lectura);
            _controller.User = new GenericPrincipal(new GenericIdentity("testuser"), null);
        }

        // ----- GetComisionesAntiguas -----

        [TestMethod]
        public async Task GetComisionesAntiguas_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2016, 1, 1);
            DateTime hasta = new DateTime(2016, 12, 31);

            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync("1", desde, hasta, "PA"))
                .Returns(new ComisionesAntiguasDTO());

            await _controller.GetComisionesAntiguas(desde, hasta, "PA");

            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync("1", desde, hasta, "PA"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetComisionesAntiguas_DevuelveOkConElResultadoDelServicio()
        {
            var resultado = new ComisionesAntiguasDTO
            {
                VentaCos = 1000m,
                TotalComision = 250m,
                VentaUL = 50m
            };

            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync(
                    A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(resultado);

            var respuesta = await _controller.GetComisionesAntiguas(
                new DateTime(2016, 1, 1), new DateTime(2016, 12, 31), "PA");

            var ok = respuesta as OkNegotiatedContentResult<ComisionesAntiguasDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1000m, ok.Content.VentaCos);
            Assert.AreEqual(250m, ok.Content.TotalComision);
        }

        [TestMethod]
        public async Task GetComisionesAntiguas_CuandoServicioDevuelveNull_DevuelveNotFound()
        {
            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync(
                    A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns((ComisionesAntiguasDTO)null);

            var respuesta = await _controller.GetComisionesAntiguas(
                new DateTime(2016, 1, 1), new DateTime(2016, 12, 31), "PA");

            Assert.IsInstanceOfType(respuesta, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetComisionesAntiguas_UsaEmpresa1PorDefecto()
        {
            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync(
                    A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(new ComisionesAntiguasDTO());

            await _controller.GetComisionesAntiguas(
                new DateTime(2016, 1, 1), new DateTime(2016, 12, 31), "PA");

            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync(
                    "1", A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetComisionesAntiguas_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _lectura.LeerComisionesAntiguasAsync(
                    A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Throws(new InvalidOperationException("Error del SP"));

            await _controller.GetComisionesAntiguas(
                new DateTime(2016, 1, 1), new DateTime(2016, 12, 31), "PA");
        }

        // ----- GetPedidosVendedor -----

        [TestMethod]
        public async Task GetPedidosVendedor_PasaVendedorAlServicio()
        {
            A.CallTo(() => _lectura.LeerPedidosVendedorAsync("PA"))
                .Returns(new List<PedidoVendedorComisionDTO>());

            await _controller.GetPedidosVendedor("PA");

            A.CallTo(() => _lectura.LeerPedidosVendedorAsync("PA"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPedidosVendedor_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<PedidoVendedorComisionDTO>
            {
                new PedidoVendedorComisionDTO
                {
                    NumeroOrden = 1,
                    Numero = 12345,
                    Vendedor = "PA",
                    Nombre = "Cliente Prueba",
                    Direccion = "Calle Mayor 1",
                    BaseImponible = 100.50m
                }
            };

            A.CallTo(() => _lectura.LeerPedidosVendedorAsync(A<string>.Ignored))
                .Returns(lista);

            var respuesta = await _controller.GetPedidosVendedor("PA");

            var ok = respuesta as OkNegotiatedContentResult<List<PedidoVendedorComisionDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual(12345, ok.Content[0].Numero);
            Assert.AreEqual("Calle Mayor 1", ok.Content[0].Direccion);
        }

        [TestMethod]
        public async Task GetPedidosVendedor_ListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _lectura.LeerPedidosVendedorAsync(A<string>.Ignored))
                .Returns(new List<PedidoVendedorComisionDTO>());

            var respuesta = await _controller.GetPedidosVendedor("XX");

            var ok = respuesta as OkNegotiatedContentResult<List<PedidoVendedorComisionDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetPedidosVendedor_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _lectura.LeerPedidosVendedorAsync(A<string>.Ignored))
                .Throws(new InvalidOperationException("Error de BD"));

            await _controller.GetPedidosVendedor("PA");
        }

        // ----- GetVentasVendedor -----

        [TestMethod]
        public async Task GetVentasVendedor_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 1, 1);
            DateTime hasta = new DateTime(2026, 1, 31);

            A.CallTo(() => _lectura.LeerVentasVendedorAsync(desde, hasta, "PA"))
                .Returns(new List<VentaVendedorComisionDTO>());

            await _controller.GetVentasVendedor(desde, hasta, "PA");

            A.CallTo(() => _lectura.LeerVentasVendedorAsync(desde, hasta, "PA"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetVentasVendedor_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<VentaVendedorComisionDTO>
            {
                new VentaVendedorComisionDTO
                {
                    NumeroOrden = 1,
                    Vendedor = "PA",
                    Grupo = "COS",
                    Familia = "CHAMPU",
                    Direccion = "Calle Mayor 1",
                    BaseImponible = 45.20m,
                    FechaAlbaran = new DateTime(2026, 1, 15)
                }
            };

            A.CallTo(() => _lectura.LeerVentasVendedorAsync(
                    A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(lista);

            var respuesta = await _controller.GetVentasVendedor(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), "PA");

            var ok = respuesta as OkNegotiatedContentResult<List<VentaVendedorComisionDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual("COS", ok.Content[0].Grupo);
            Assert.AreEqual(45.20m, ok.Content[0].BaseImponible);
        }

        [TestMethod]
        public async Task GetVentasVendedor_ListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _lectura.LeerVentasVendedorAsync(
                    A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(new List<VentaVendedorComisionDTO>());

            var respuesta = await _controller.GetVentasVendedor(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), "PA");

            var ok = respuesta as OkNegotiatedContentResult<List<VentaVendedorComisionDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetVentasVendedor_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _lectura.LeerVentasVendedorAsync(
                    A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Throws(new InvalidOperationException("Error de BD"));

            await _controller.GetVentasVendedor(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), "PA");
        }
    }
}
