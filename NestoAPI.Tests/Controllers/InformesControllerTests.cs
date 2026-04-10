using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class InformesControllerTests
    {
        private IInformesService _servicio;
        private InformesController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IInformesService>();
            _controller = new InformesController(_servicio);
            _controller.User = new GenericPrincipal(new GenericIdentity("testuser"), null);
        }

        [TestMethod]
        public async Task GetResumenVentas_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 1, 1);
            DateTime hasta = new DateTime(2026, 3, 31);
            bool soloFacturas = true;

            A.CallTo(() => _servicio.LeerResumenVentasAsync(desde, hasta, soloFacturas))
                .Returns(new List<ResumenVentasDTO>());

            await _controller.GetResumenVentas(desde, hasta, soloFacturas);

            A.CallTo(() => _servicio.LeerResumenVentasAsync(desde, hasta, soloFacturas))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetResumenVentas_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ResumenVentasDTO>
            {
                new ResumenVentasDTO
                {
                    Grupo = "NV",
                    Vendedor = "AM",
                    NombreVendedor = "Ana Martínez",
                    VtaNV = 1500m,
                    VtaCV = 200m,
                    VtaVC = 50m,
                    VtaUL = 0m,
                    VtaTotal = 1750m
                },
                new ResumenVentasDTO
                {
                    Grupo = "CV",
                    Vendedor = "JG",
                    NombreVendedor = "Juan García",
                    VtaNV = 0m,
                    VtaCV = 800m,
                    VtaVC = 100m,
                    VtaUL = 50m,
                    VtaTotal = 950m
                }
            };

            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31), false);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ResumenVentasDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ResumenVentasDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual("Ana Martínez", okResult.Content[0].NombreVendedor);
            Assert.AreEqual(1750m, okResult.Content[0].VtaTotal);
            Assert.AreEqual("Juan García", okResult.Content[1].NombreVendedor);
        }

        [TestMethod]
        public async Task GetResumenVentas_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Returns(new List<ResumenVentasDTO>());

            var resultado = await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), true);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ResumenVentasDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ResumenVentasDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetResumenVentas_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31), false);
        }

        [TestMethod]
        public void InformesController_TieneAuthorizeAttribute()
        {
            var authorizeAttributes = typeof(InformesController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

            Assert.IsTrue(authorizeAttributes.Length > 0,
                "InformesController debe tener [Authorize] a nivel de clase");
        }

        // ----- ControlPedidos (1A.2) -----

        [TestMethod]
        public async Task GetControlPedidos_LlamaAlServicio()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(new List<ControlPedidosDTO>());

            await _controller.GetControlPedidos();

            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetControlPedidos_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ControlPedidosDTO>
            {
                new ControlPedidosDTO
                {
                    Pedido = 12345,
                    Producto = "38697",
                    Ruta = "MAD",
                    Cliente = "15191",
                    Vendedor = "AM",
                    Nombre = "Crema Hidratante",
                    Familia = "Eva Visnú",
                    CantidadPedido = 2,
                    CantidadTotal = 5
                },
                new ControlPedidosDTO
                {
                    Pedido = 12346,
                    Producto = "12345",
                    Ruta = "BCN",
                    Cliente = "20001",
                    Vendedor = "JG",
                    Nombre = "Champú",
                    Familia = "Lisap",
                    CantidadPedido = 1,
                    CantidadTotal = 1
                }
            };

            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(lista);

            var resultado = await _controller.GetControlPedidos();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ControlPedidosDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ControlPedidosDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual(12345, okResult.Content[0].Pedido);
            Assert.AreEqual("Crema Hidratante", okResult.Content[0].Nombre);
            Assert.AreEqual(5, okResult.Content[0].CantidadTotal);
            Assert.AreEqual("Champú", okResult.Content[1].Nombre);
        }

        [TestMethod]
        public async Task GetControlPedidos_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(new List<ControlPedidosDTO>());

            var resultado = await _controller.GetControlPedidos();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ControlPedidosDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ControlPedidosDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetControlPedidos_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetControlPedidos();
        }

        // ----- DetalleRapports (1A.3) -----

        [TestMethod]
        public async Task GetDetalleRapports_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 4, 1);
            DateTime hasta = new DateTime(2026, 4, 10);
            string listaVendedores = "AM,JG,MR";

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(desde, hasta, listaVendedores))
                .Returns(new List<DetalleRapportsDTO>());

            await _controller.GetDetalleRapports(desde, hasta, listaVendedores);

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(desde, hasta, listaVendedores))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetDetalleRapports_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO
                {
                    Usuario = "AM",
                    Empresa = "1",
                    NombreEmpresa = "Nueva Visión",
                    Cliente = "15191",
                    Direccion = "Calle Mayor 1",
                    Comentarios = "Llamada para reposición",
                    HoraLlamada = new DateTime(2026, 4, 10, 10, 30, 0),
                    EstadoCliente = 0,
                    AcumuladoMes = 1500,
                    Tipo = "Llamada",
                    Pedido = true,
                    Vendedor = "AM",
                    CodigoPostal = "28001",
                    Poblacion = "Madrid",
                    EstadoRapport = 9
                },
                new DetalleRapportsDTO
                {
                    Usuario = "JG",
                    Vendedor = "JG",
                    Cliente = "20001",
                    Pedido = false,
                    EstadoRapport = 9
                }
            };

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "AM,JG");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<DetalleRapportsDTO>>));
            var okResult = (OkNegotiatedContentResult<List<DetalleRapportsDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual("Nueva Visión", okResult.Content[0].NombreEmpresa);
            Assert.AreEqual(1500, okResult.Content[0].AcumuladoMes);
            Assert.IsTrue(okResult.Content[0].Pedido.Value);
            Assert.AreEqual("JG", okResult.Content[1].Vendedor);
            Assert.IsFalse(okResult.Content[1].Pedido.Value);
        }

        [TestMethod]
        public async Task GetDetalleRapports_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(new List<DetalleRapportsDTO>());

            var resultado = await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<DetalleRapportsDTO>>));
            var okResult = (OkNegotiatedContentResult<List<DetalleRapportsDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetDetalleRapports_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "AM");
        }
    }
}
