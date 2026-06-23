using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#195 (Fase 3): tests del endpoint POST api/FacturacionRutas/AgruparPorPO.
    /// (En fichero propio porque el FacturacionRutasControllerTests.cs original está huérfano
    /// del csproj y con referencias obsoletas; ver nota en el PR.)
    /// </summary>
    [TestClass]
    public class FacturacionRutasControllerAgruparPorPOTests
    {
        private NVEntities db;
        private IServicioPedidosParaFacturacion servicioPedidos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioPedidos = A.Fake<IServicioPedidosParaFacturacion>();
        }

        [TestMethod]
        public async Task AgruparPorPO_UsuarioSinPermisos_Retorna403Forbidden()
        {
            var controller = new FacturacionRutasController(db, servicioPedidos, A.Fake<IServicioAgruparPorPO>());
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.TIENDAS)); // sin permisos
            controller.User = new ClaimsPrincipal(identity);

            var resultado = await controller.AgruparPorPO();

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
        }

        [TestMethod]
        public async Task AgruparPorPO_ConPermisos_LlamaEvaluarYProcesarParaEmpresaPorDefectoYRetornaOk()
        {
            var servicioAgrupar = A.Fake<IServicioAgruparPorPO>();
            var resultadoFake = new ResultadoAgrupacionPO();
            resultadoFake.Facturas.Add(new CrearFacturaResponseDTO { NumeroFactura = "FAC-1" });
            A.CallTo(() => servicioAgrupar.EvaluarYProcesar(A<string>._, A<string>._))
                .Returns(Task.FromResult(resultadoFake));

            var controller = new FacturacionRutasController(db, servicioPedidos, servicioAgrupar);
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Test"));
            controller.User = new ClaimsPrincipal(identity);

            var resultado = await controller.AgruparPorPO();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoAgrupacionPO>));
            var ok = (OkNegotiatedContentResult<ResultadoAgrupacionPO>)resultado;
            Assert.AreEqual(1, ok.Content.Facturas.Count);
            // Debe agrupar para la empresa por defecto y con el usuario autenticado (con dominio).
            A.CallTo(() => servicioAgrupar.EvaluarYProcesar(
                Constantes.Empresas.EMPRESA_POR_DEFECTO, "NUEVAVISION\\Test")).MustHaveHappened();
        }

        [TestMethod]
        public async Task FacturarRutas_AgrupaPorPOParaEmpresaPorDefectoYVuelcaResultadoEnRespuesta()
        {
            // Sin pedidos de ruta: aislamos el efecto de la agrupación por PO (no entra el flujo
            // normal de facturación, que instanciaría servicios reales con el db falso).
            A.CallTo(() => servicioPedidos.ObtenerPedidosParaFacturar(A<string>._, A<DateTime>._))
                .Returns(Task.FromResult(new List<CabPedidoVta>()));

            var servicioAgrupar = A.Fake<IServicioAgruparPorPO>();
            var resultadoFake = new ResultadoAgrupacionPO();
            resultadoFake.Facturas.Add(new CrearFacturaResponseDTO { NumeroFactura = "FAC-PO-1" });
            resultadoFake.Errores.Add(new ErrorAgrupacionPO { Cliente = "CLI-9", SuPedido = "PO-9", Mensaje = "descuadre" });
            A.CallTo(() => servicioAgrupar.EvaluarYProcesar(A<string>._, A<string>._))
                .Returns(Task.FromResult(resultadoFake));

            var controller = new FacturacionRutasController(db, servicioPedidos, servicioAgrupar);
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Test"));
            controller.User = new ClaimsPrincipal(identity);

            var request = new FacturarRutasRequestDTO { TipoRuta = "16", FechaEntregaDesde = DateTime.Today };
            var resultado = await controller.FacturarRutas(request);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FacturarRutasResponseDTO>));
            var response = ((OkNegotiatedContentResult<FacturarRutasResponseDTO>)resultado).Content;
            CollectionAssert.Contains(response.FacturasPorPO, "FAC-PO-1");
            Assert.AreEqual(1, response.ErroresPorPO.Count);
            StringAssert.Contains(response.ErroresPorPO[0], "PO-9");
            // La agrupación por PO debe lanzarse para la empresa por defecto.
            A.CallTo(() => servicioAgrupar.EvaluarYProcesar(
                Constantes.Empresas.EMPRESA_POR_DEFECTO, "NUEVAVISION\\Test")).MustHaveHappened();
        }
    }
}
