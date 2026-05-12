using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PlanesVentajas;
using NestoAPI.Models.PlanesVentajas;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PlanesVentajasControllerTests
    {
        private IPlanesVentajasService _servicio;
        private PlanesVentajasController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IPlanesVentajasService>();
            _controller = new PlanesVentajasController(_servicio);
            _controller.User = new GenericPrincipal(new GenericIdentity("usuarioTest"), null);
        }

        [TestMethod]
        public void PlanesVentajasController_TieneAuthorizeAttribute()
        {
            var atributos = typeof(PlanesVentajasController)
                .GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), true);
            Assert.AreEqual(1, atributos.Length);
        }

        // GET Estados ------------------------------------------------------------

        [TestMethod]
        public async Task GetEstados_DevuelveOkConListaDelServicio()
        {
            var estados = new List<EstadoPlanVentajasDTO>
            {
                new EstadoPlanVentajasDTO { Numero = 1, Descripcion = "Activo" },
                new EstadoPlanVentajasDTO { Numero = 6, Descripcion = "Cancelado" }
            };
            A.CallTo(() => _servicio.ListarEstadosAsync()).Returns(estados);

            var resultado = await _controller.GetEstados();

            var ok = resultado as OkNegotiatedContentResult<List<EstadoPlanVentajasDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Count);
        }

        // GET Empresas -----------------------------------------------------------

        [TestMethod]
        public async Task GetEmpresas_DevuelveOkConListaDelServicio()
        {
            var empresas = new List<EmpresaResumenDTO>
            {
                new EmpresaResumenDTO { Numero = "1", Nombre = "Nueva Visión" },
                new EmpresaResumenDTO { Numero = "2", Nombre = "Otra Empresa" }
            };
            A.CallTo(() => _servicio.ListarEmpresasAsync()).Returns(empresas);

            var resultado = await _controller.GetEmpresas();

            var ok = resultado as OkNegotiatedContentResult<List<EmpresaResumenDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Count);
        }

        // GET Planes -------------------------------------------------------------

        [TestMethod]
        public async Task GetPlanes_PasaLosParametrosAlServicio()
        {
            A.CallTo(() => _servicio.ListarPlanesAsync("AM", "12345", true))
                .Returns(new List<PlanVentajasDTO>());

            await _controller.GetPlanes("AM", "12345", true);

            A.CallTo(() => _servicio.ListarPlanesAsync("AM", "12345", true))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPlanes_SinParametros_UsaDefaults()
        {
            A.CallTo(() => _servicio.ListarPlanesAsync(null, null, false))
                .Returns(new List<PlanVentajasDTO>());

            await _controller.GetPlanes();

            A.CallTo(() => _servicio.ListarPlanesAsync(null, null, false))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPlanes_DevuelveOkConListaDelServicio()
        {
            var planes = new List<PlanVentajasDTO>
            {
                new PlanVentajasDTO { Numero = 1, Empresa = "1", Importe = 1000m }
            };
            A.CallTo(() => _servicio.ListarPlanesAsync(A<string>.Ignored, A<string>.Ignored, A<bool>.Ignored))
                .Returns(planes);

            var resultado = await _controller.GetPlanes();

            var ok = resultado as OkNegotiatedContentResult<List<PlanVentajasDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual(1000m, ok.Content[0].Importe);
        }

        // GET Plan ---------------------------------------------------------------

        [TestMethod]
        public async Task GetPlan_PlanExistente_DevuelveOk()
        {
            var plan = new PlanVentajasDTO { Numero = 42, Empresa = "1" };
            A.CallTo(() => _servicio.ObtenerPlanAsync(42)).Returns(plan);

            var resultado = await _controller.GetPlan(42);

            var ok = resultado as OkNegotiatedContentResult<PlanVentajasDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(42, ok.Content.Numero);
        }

        [TestMethod]
        public async Task GetPlan_PlanInexistente_DevuelveNotFound()
        {
            A.CallTo(() => _servicio.ObtenerPlanAsync(999)).Returns((PlanVentajasDTO)null);

            var resultado = await _controller.GetPlan(999);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // GET Clientes -----------------------------------------------------------

        [TestMethod]
        public async Task GetClientes_PasaEmpresaAlServicio()
        {
            A.CallTo(() => _servicio.ObtenerClientesAsync(7, "2"))
                .Returns(new List<ClientePlanVentajasDTO>());

            await _controller.GetClientes(7, "2");

            A.CallTo(() => _servicio.ObtenerClientesAsync(7, "2"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetClientes_DevuelveOkConLista()
        {
            var clientes = new List<ClientePlanVentajasDTO>
            {
                new ClientePlanVentajasDTO { NumeroCliente = "00001", Nombre = "Cliente A" }
            };
            A.CallTo(() => _servicio.ObtenerClientesAsync(A<int>.Ignored, A<string>.Ignored))
                .Returns(clientes);

            var resultado = await _controller.GetClientes(1);

            var ok = resultado as OkNegotiatedContentResult<List<ClientePlanVentajasDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Count);
        }

        // GET LineasVenta --------------------------------------------------------

        [TestMethod]
        public async Task GetLineasVenta_DevuelveOkConLista()
        {
            var lineas = new List<LineaVentaPlanDTO>
            {
                new LineaVentaPlanDTO { NumeroPedido = 100, BaseImponible = 50m }
            };
            A.CallTo(() => _servicio.ObtenerLineasVentaAsync(5, "1"))
                .Returns(lineas);

            var resultado = await _controller.GetLineasVenta(5, "1");

            var ok = resultado as OkNegotiatedContentResult<List<LineaVentaPlanDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(50m, ok.Content[0].BaseImponible);
        }

        // POST -------------------------------------------------------------------

        [TestMethod]
        public async Task PostPlan_Valido_PasaUsuarioAlServicio()
        {
            var plan = new PlanVentajasDTO
            {
                Empresa = "1",
                FechaInicio = new DateTime(2026, 1, 1),
                FechaFin = new DateTime(2026, 12, 31),
                Importe = 5000m,
                Familia = "FAM"
            };
            A.CallTo(() => _servicio.CrearPlanAsync(plan, "usuarioTest"))
                .Returns(new PlanVentajasDTO { Numero = 99 });

            var resultado = await _controller.PostPlan(plan);

            A.CallTo(() => _servicio.CrearPlanAsync(plan, "usuarioTest"))
                .MustHaveHappenedOnceExactly();
            var ok = resultado as OkNegotiatedContentResult<PlanVentajasDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(99, ok.Content.Numero);
        }

        [TestMethod]
        public async Task PostPlan_Null_DevuelveBadRequest()
        {
            var resultado = await _controller.PostPlan(null);

            Assert.IsInstanceOfType(resultado, typeof(InvalidModelStateResult));
            A.CallTo(_servicio)
                .Where(call => call.Method.Name == nameof(IPlanesVentajasService.CrearPlanAsync))
                .MustNotHaveHappened();
        }

        // PUT --------------------------------------------------------------------

        [TestMethod]
        public async Task PutPlan_Existente_DevuelveOkConPlanActualizado()
        {
            var plan = new PlanVentajasDTO { Numero = 42, Empresa = "1", Importe = 7500m };
            A.CallTo(() => _servicio.ActualizarPlanAsync(42, plan, "usuarioTest"))
                .Returns(plan);

            var resultado = await _controller.PutPlan(42, plan);

            var ok = resultado as OkNegotiatedContentResult<PlanVentajasDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(7500m, ok.Content.Importe);
        }

        [TestMethod]
        public async Task PutPlan_Inexistente_DevuelveNotFound()
        {
            var plan = new PlanVentajasDTO { Numero = 999 };
            A.CallTo(() => _servicio.ActualizarPlanAsync(999, plan, A<string>.Ignored))
                .Returns((PlanVentajasDTO)null);

            var resultado = await _controller.PutPlan(999, plan);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task PutPlan_Null_DevuelveBadRequest()
        {
            var resultado = await _controller.PutPlan(1, null);

            Assert.IsInstanceOfType(resultado, typeof(InvalidModelStateResult));
            A.CallTo(_servicio)
                .Where(call => call.Method.Name == nameof(IPlanesVentajasService.ActualizarPlanAsync))
                .MustNotHaveHappened();
        }
    }
}
