using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#242: este fichero estuvo huérfano del csproj (nunca se compiló) y quedó obsoleto
    /// respecto a dos refactorizaciones: TipoRuta pasó de enum a string (Id de TipoRutaFactory) y
    /// el controller incorporó la agrupación por PO (#195). Resucitado contra la API actual; la
    /// semántica de los contadores del preview se cubre en GestorFacturacionRutasTests.
    /// </summary>
    [TestClass]
    public class FacturacionRutasControllerTests
    {
        private NVEntities db;
        private IServicioPedidosParaFacturacion servicioPedidos;
        private IServicioAgruparPorPO servicioAgruparPorPO;
        private FacturacionRutasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioPedidos = A.Fake<IServicioPedidosParaFacturacion>();
            servicioAgruparPorPO = A.Fake<IServicioAgruparPorPO>();
            _ = A.CallTo(() => servicioAgruparPorPO.EvaluarYProcesar(A<string>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoAgrupacionPO()));
            controller = new FacturacionRutasController(db, servicioPedidos, servicioAgruparPorPO);
        }

        private void ConfigurarUsuario(string rol, string nombre = "NUEVAVISION\\Test")
        {
            // "TestAuth" hace la identidad autenticada (sin authenticationType, IsAuthenticated
            // es false y FacturarRutas devolvería Unauthorized aunque tenga rol).
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, rol));
            identity.AddClaim(new Claim(ClaimTypes.Name, nombre));
            controller.User = new ClaimsPrincipal(identity);
        }

        private void ConfigurarSinPedidos()
        {
            _ = A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));
        }

        #region Constructor

        [TestMethod]
        public void Constructor_ConDependenciasValidas_CreaInstancia()
        {
            Assert.IsNotNull(controller);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            _ = new FacturacionRutasController(null, servicioPedidos);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ConServicioPedidosNull_LanzaArgumentNullException()
        {
            _ = new FacturacionRutasController(db, null);
        }

        #endregion

        #region FacturarRutas

        [TestMethod]
        public async Task FacturarRutas_RequestNull_RetornaBadRequest()
        {
            var resultado = await controller.FacturarRutas(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task FacturarRutas_SinPedidos_RetornaOkConResponseVacio()
        {
            ConfigurarSinPedidos();
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
            var okResult = (OkNegotiatedContentResult<FacturarRutasResponseDTO>)resultado;
            Assert.AreEqual(0, okResult.Content.PedidosProcesados);
            Assert.AreEqual(0, okResult.Content.AlbaranesCreados);
            Assert.AreEqual(0, okResult.Content.FacturasCreadas);
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioSinPermisos_Retorna403Forbidden()
        {
            ConfigurarUsuario(Constantes.GruposSeguridad.TIENDAS); // rol sin permisos
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioDireccion_PermiteAcceso()
        {
            ConfigurarSinPedidos();
            ConfigurarUsuario(Constantes.GruposSeguridad.DIRECCION);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioConDominioAlmacen_PermiteAcceso()
        {
            // IsInRoleSinDominio: el rol puede venir como "NUEVAVISION\Almacén" (Windows)
            ConfigurarSinPedidos();
            ConfigurarUsuario("NUEVAVISION\\Almacén");
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>),
                "Debe permitir acceso con rol 'NUEVAVISION\\Almacén'");
        }

        [TestMethod]
        public async Task FacturarRutas_UsuarioConDominioSinPermisos_Retorna403()
        {
            ConfigurarUsuario("NUEVAVISION\\Tiendas");
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode,
                "Debe denegar acceso con rol 'NUEVAVISION\\Tiendas'");
        }

        [TestMethod]
        public async Task FacturarRutas_FechaEntregaDesdeNull_UsaFechaHoy()
        {
            DateTime? fechaCapturada = null;
            _ = A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._))
                .Invokes((string tipo, DateTime fecha) => fechaCapturada = fecha)
                .Returns(Task.FromResult(new List<CabPedidoVta>()));
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = null };

            _ = await controller.FacturarRutas(request);

            Assert.IsNotNull(fechaCapturada);
            Assert.AreEqual(DateTime.Today, fechaCapturada.Value.Date);
        }

        [TestMethod]
        public async Task FacturarRutas_PasaElTipoDeRutaDelRequestAlServicio()
        {
            string tipoCapturado = null;
            _ = A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._))
                .Invokes((string tipo, DateTime fecha) => tipoCapturado = tipo)
                .Returns(Task.FromResult(new List<CabPedidoVta>()));
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);
            var request = new FacturarRutasRequestDTO { TipoRuta = "AGENCIA", FechaEntregaDesde = DateTime.Today };

            _ = await controller.FacturarRutas(request);

            Assert.AreEqual("AGENCIA", tipoCapturado, "El TipoRuta (Id de TipoRutaFactory) viaja como string al servicio");
        }

        #endregion

        #region PreviewFacturarRutas

        [TestMethod]
        public async Task PreviewFacturarRutas_RequestNull_RetornaBadRequest()
        {
            var resultado = await controller.PreviewFacturarRutas(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_SinPedidos_RetornaOkConPreviewVacio()
        {
            ConfigurarSinPedidos();
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.PreviewFacturarRutas(request);

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
            ConfigurarUsuario(Constantes.GruposSeguridad.TIENDAS);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.PreviewFacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_UsuarioDireccion_PermiteAcceso()
        {
            ConfigurarSinPedidos();
            ConfigurarUsuario(Constantes.GruposSeguridad.DIRECCION);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = DateTime.Today };

            var resultado = await controller.PreviewFacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PreviewFacturacionRutasResponseDTO>));
        }

        [TestMethod]
        public async Task PreviewFacturarRutas_FechaEntregaDesdeNull_UsaFechaHoy()
        {
            DateTime? fechaCapturada = null;
            _ = A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._))
                .Invokes((string tipo, DateTime fecha) => fechaCapturada = fecha)
                .Returns(Task.FromResult(new List<CabPedidoVta>()));
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);
            var request = new FacturarRutasRequestDTO { TipoRuta = "PROPIA", FechaEntregaDesde = null };

            _ = await controller.PreviewFacturarRutas(request);

            Assert.IsNotNull(fechaCapturada);
            Assert.AreEqual(DateTime.Today, fechaCapturada.Value.Date);
        }

        #endregion

        #region FacturarPedido (Agencias: mismo camino que la facturación de rutas)

        private FacturacionRutasController ControllerConGestor(IGestorFacturacionRutas gestor)
            => new FacturacionRutasController(db, servicioPedidos, servicioAgruparPorPO, () => gestor);

        [TestMethod]
        public async Task FacturarPedido_PedidoConAlgoQueFacturar_LoProcesaConElMismoNucleoQueLasRutas()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            var pedido = new CabPedidoVta { Empresa = "1", Número = 927519 };
            var respuesta = new FacturarRutasResponseDTO();
            A.CallTo(() => servicioPedidos.ObtenerPedidoParaFacturar("1", 927519, DateTime.Today)).Returns(pedido);
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).Returns(respuesta);
            controller = ControllerConGestor(gestor);
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN, "Andre");

            var resultado = await controller.FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = 927519 })
                as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            Assert.AreSame(respuesta, resultado?.Content);
            A.CallTo(() => gestor.FacturarRutas(
                    A<List<CabPedidoVta>>.That.Matches(l => l.Count == 1 && l[0] == pedido),
                    "Andre",
                    DateTime.Today))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioAgruparPorPO.EvaluarYProcesar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarPedido_SinNadaQueFacturar_DevuelveUnErrorClaroSinLlamarAlGestor()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            A.CallTo(() => servicioPedidos.ObtenerPedidoParaFacturar(A<string>._, A<int>._, A<DateTime>._)).Returns((CabPedidoVta)null);
            controller = ControllerConGestor(gestor);
            ConfigurarUsuario(Constantes.GruposSeguridad.ALMACEN);

            var resultado = await controller.FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = 5 })
                as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            Assert.AreEqual(1, resultado.Content.PedidosConErrores.Count);
            StringAssert.Contains(resultado.Content.PedidosConErrores[0].MensajeError, "no tiene líneas para facturar");
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarPedido_SinPermisos_DevuelveForbidden()
        {
            ConfigurarUsuario("Tiendas");

            var resultado = await controller.FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = 5 });

            Assert.AreEqual(HttpStatusCode.Forbidden, (resultado as StatusCodeResult)?.StatusCode);
        }

        #endregion
    }
}
