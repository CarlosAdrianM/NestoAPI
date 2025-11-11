using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Impresion;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class FacturacionRutasControllerTests
    {
        private NVEntities db;
        private IServicioPedidosParaFacturacion servicioPedidos;
        private FacturacionRutasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioPedidos = A.Fake<IServicioPedidosParaFacturacion>();
            controller = new FacturacionRutasController(db, servicioPedidos);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_ConDependenciasValidas_CreaInstancia()
        {
            // Arrange, Act & Assert
            Assert.IsNotNull(controller);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            var _ = new FacturacionRutasController(null, servicioPedidos);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConServicioPedidosNull_LanzaArgumentNullException()
        {
            // Arrange, Act & Assert
            var _ = new FacturacionRutasController(db, null);
        }

        #endregion

        #region FacturarRutas Tests

        [TestMethod]
        public async Task FacturarRutas_RequestNull_RetornaBadRequest()
        {
            // Arrange
            FacturarRutasRequestDTO request = null;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task FacturarRutas_SinPedidos_RetornaOkConResponseVacio()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            // Configurar mock para retornar lista vacía
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake (Almacén)
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
            var okResult = (OkNegotiatedContentResult<FacturarRutasResponseDTO>)resultado;
            Assert.AreEqual(0, okResult.Content.PedidosProcesados);
            Assert.AreEqual(0, okResult.Content.AlbaranesCreados);
            Assert.AreEqual(0, okResult.Content.FacturasCreadas);
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioSinPermisos_Retorna403Forbidden()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            // Configurar usuario fake SIN permisos (no Almacén ni Dirección)
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.TIENDAS)); // Rol sin permisos
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            var statusResult = (StatusCodeResult)resultado;
            Assert.AreEqual(HttpStatusCode.Forbidden, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioDireccion_PermiteAcceso()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol Dirección
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioAlmacen_PermiteAcceso()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol Almacén
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
        }

        [TestMethod]
        public async Task FacturarRutas_FechaEntregaDesdeNull_UsaFechaHoy()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = null // Sin fecha
            };

            DateTime? fechaCapturada = null;
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Invokes((TipoRutaFacturacion tipo, DateTime fecha) => fechaCapturada = fecha)
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            await controller.FacturarRutas(request);

            // Assert
            Assert.IsNotNull(fechaCapturada);
            Assert.AreEqual(DateTime.Today, fechaCapturada.Value.Date);
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioConDominioAlmacen_PermiteAcceso()
        {
            // Arrange - Usuario con dominio: NUEVAVISION\Almacén
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol incluyendo dominio
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, "NUEVAVISION\\Almacén"));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>),
                "Debe permitir acceso con rol 'NUEVAVISION\\Almacén'");
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioConDominioDireccion_PermiteAcceso()
        {
            // Arrange - Usuario con dominio: NUEVAVISION\Dirección
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol incluyendo dominio
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, "NUEVAVISION\\Dirección"));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>),
                "Debe permitir acceso con rol 'NUEVAVISION\\Dirección'");
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioConDominioSinPermisos_Retorna403()
        {
            // Arrange - Usuario con dominio pero sin permisos: NUEVAVISION\Tiendas
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            // Configurar usuario fake con rol sin permisos pero con dominio
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, "NUEVAVISION\\Tiendas"));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.FacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            var statusResult = (StatusCodeResult)resultado;
            Assert.AreEqual(HttpStatusCode.Forbidden, statusResult.StatusCode,
                "Debe denegar acceso con rol 'NUEVAVISION\\Tiendas'");
        }

        #endregion

        #region PreviewFacturarRutas Tests

        [TestMethod]
        public async Task PreviewFacturarRutas_RequestNull_RetornaBadRequest()
        {
            // Arrange
            FacturarRutasRequestDTO request = null;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_SinPedidos_RetornaOkConPreviewVacio()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            // Configurar mock para retornar lista vacía
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake (Almacén)
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
            var okResult = (OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>)resultado;
            Assert.AreEqual(0, okResult.Content.NumeroPedidos);
            Assert.AreEqual(0, okResult.Content.NumeroAlbaranes);
            Assert.AreEqual(0, okResult.Content.NumeroFacturas);
            Assert.AreEqual(0, okResult.Content.NumeroNotasEntrega);
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_UsuarioSinPermisos_Retorna403Forbidden()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            // Configurar usuario fake SIN permisos (no Almacén ni Dirección)
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.TIENDAS)); // Rol sin permisos
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            var statusResult = (StatusCodeResult)resultado;
            Assert.AreEqual(HttpStatusCode.Forbidden, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_UsuarioDireccion_PermiteAcceso()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol Dirección
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_UsuarioAlmacen_PermiteAcceso()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake con rol Almacén
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_FechaEntregaDesdeNull_UsaFechaHoy()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = null // Sin fecha
            };

            DateTime? fechaCapturada = null;
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Invokes((TipoRutaFacturacion tipo, DateTime fecha) => fechaCapturada = fecha)
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            // Configurar usuario fake
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsNotNull(fechaCapturada);
            Assert.AreEqual(DateTime.Today, fechaCapturada.Value.Date);
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_ConPedidosNRM_RetornaPreviewConAlbaranesYFacturas()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutaPropia,
                FechaEntregaDesde = DateTime.Today
            };

            var pedidos = new List<CabPedidoVta>
            {
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 12345,
                    Cliente = "1001",
                    Contacto = "0",
                    NombreCliente = "Cliente Test",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta { Base_Imponible = 100m, Estado = Constantes.EstadosLineaVenta.EN_CURSO }
                    }
                }
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(pedidos));

            // Configurar usuario fake
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
            var okResult = (OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>)resultado;
            Assert.AreEqual(1, okResult.Content.NumeroPedidos);
            Assert.AreEqual(1, okResult.Content.NumeroAlbaranes, "Debe contar 1 albarán para pedido NRM");
            Assert.AreEqual(1, okResult.Content.NumeroFacturas, "Debe contar 1 factura para pedido NRM");
            Assert.AreEqual(100m, okResult.Content.BaseImponibleAlbaranes);
            Assert.AreEqual(100m, okResult.Content.BaseImponibleFacturas);
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_ConPedidosFDM_RetornaSoloAlbaranes()
        {
            // Arrange
            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = TipoRutaFacturacion.RutasAgencias,
                FechaEntregaDesde = DateTime.Today
            };

            var pedidos = new List<CabPedidoVta>
            {
                new CabPedidoVta
                {
                    Empresa = "1",
                    Número = 12346,
                    Cliente = "1002",
                    Contacto = "0",
                    NombreCliente = "Cliente FDM",
                    Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                    NotaEntrega = false,
                    MantenerJunto = false,
                    LinPedidoVtas = new List<LinPedidoVta>
                    {
                        new LinPedidoVta { Base_Imponible = 200m, Estado = Constantes.EstadosLineaVenta.EN_CURSO }
                    }
                }
            };

            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(
                A<TipoRutaFacturacion>._,
                A<DateTime>._))
                .Returns(Task.FromResult(pedidos));

            // Configurar usuario fake
            var identity = new ClaimsIdentity();
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            var principal = new ClaimsPrincipal(identity);
            controller.User = principal;

            // Act
            var resultado = await controller.PreviewFacturarRutas(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
            var okResult = (OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>)resultado;
            Assert.AreEqual(1, okResult.Content.NumeroPedidos);
            Assert.AreEqual(1, okResult.Content.NumeroAlbaranes, "Debe contar 1 albarán para pedido FDM");
            Assert.AreEqual(0, okResult.Content.NumeroFacturas, "NO debe contar facturas para pedido FDM");
            Assert.AreEqual(200m, okResult.Content.BaseImponibleAlbaranes);
            Assert.AreEqual(0m, okResult.Content.BaseImponibleFacturas);
        }

        #endregion
    }
}
