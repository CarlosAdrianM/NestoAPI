using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ClientesControllerTests
    {
        // Regresión NestoAPI#201: GetClientes(empresa, filtro) lanzaba NullReferenceException
        // cuando filtro llegaba null (NestoApp manda ?filtro= al limpiar la búsqueda). Ahora debe
        // lanzar la excepción amistosa de "filtro de al menos 4 caracteres", no una NRE.
        [TestMethod]
        public void ClientesController_GetClientes_FiltroNull_NoLanzaNullReference()
        {
            ClientesController controller = new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>());

            try
            {
                _ = controller.GetClientes("1", null);
                Assert.Fail("Debía lanzar una excepción por filtro inválido");
            }
            catch (NullReferenceException)
            {
                Assert.Fail("No debe lanzar NullReferenceException");
            }
            catch (Exception ex)
            {
                Assert.AreEqual("Por favor, utilice un filtro de al menos 4 caracteres", ex.Message);
            }
        }

        // NestoAPI#327: endpoints del circuito de validación de NIF contra la AEAT

        private static ClientesController ControllerConValidacion(IServicioValidacionNif servicio)
        {
            return new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>(),
                servicio);
        }

        [TestMethod]
        public async Task CorregirNif_NifAceptado_DevuelveElResultado()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            _ = A.CallTo(() => servicio.CorregirNif("30676", "05231909H", A<string>.Ignored))
                .Returns(new ResultadoCorreccionNif { Corregido = true, Nif = "05231909H", ContactosActualizados = 2 });
            var controller = ControllerConValidacion(servicio);

            var resultado = await controller.CorregirNif(new ClientesController.CorregirNifRequest
            { Cliente = "30676", Nif = "05231909H" }) as OkNegotiatedContentResult<ResultadoCorreccionNif>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.ContactosActualizados);
        }

        [TestMethod]
        public async Task CorregirNif_NifRechazadoPorLaAeat_BadRequestConElMotivo()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            _ = A.CallTo(() => servicio.CorregirNif(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new ResultadoCorreccionNif { Corregido = false, Motivo = "La AEAT no reconoce el NIF" });
            var controller = ControllerConValidacion(servicio);

            var resultado = await controller.CorregirNif(new ClientesController.CorregirNifRequest
            { Cliente = "30676", Nif = "99999999R" }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "AEAT");
        }

        [TestMethod]
        public async Task CorregirNif_SinClienteONif_BadRequest()
        {
            var controller = ControllerConValidacion(A.Fake<IServicioValidacionNif>());

            var sinDatos = await controller.CorregirNif(null);
            var sinNif = await controller.CorregirNif(new ClientesController.CorregirNifRequest { Cliente = "30676" });

            Assert.IsInstanceOfType(sinDatos, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(sinNif, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetNifIncorrectos_VendedorNormal_FiltraPorElMismo()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            var vendedores = A.Fake<IServicioVendedores>();
            _ = A.CallTo(() => vendedores.VendedoresEquipoString("1", "JE")).Returns(new List<string>());
            _ = A.CallTo(() => servicio.ListarNifIncorrectos(A<List<string>>.That.Matches(
                    l => l != null && l.Count == 1 && l[0] == "JE")))
                .Returns(new List<ClienteNifIncorrectoDTO>
                {
                    new ClienteNifIncorrectoDTO { Cliente = "30676", Nif = "90021192", TienePedidoPendiente = true }
                });
            var controller = new ClientesController(A.Fake<IGestorClientes>(), vendedores,
                A.Fake<IGestorSincronizacion>(), servicio);

            var resultado = await controller.GetNifIncorrectos("JE") as OkNegotiatedContentResult<List<ClienteNifIncorrectoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.IsTrue(resultado.Content[0].TienePedidoPendiente);
        }

        [TestMethod]
        public async Task GetNifIncorrectos_JefeDeEquipo_VeLosDeSuEquipoTambien()
        {
            // Nesto#417 (Carlos 22/07): el servidor expande el vendedor a su equipo de
            // EquiposVenta, sin que el cliente tenga que saber quién es jefe.
            var servicio = A.Fake<IServicioValidacionNif>();
            var vendedores = A.Fake<IServicioVendedores>();
            _ = A.CallTo(() => vendedores.VendedoresEquipoString("1", "ASH"))
                .Returns(new List<string> { "ASH", "DV ", "PP" });
            List<string> filtroRecibido = null;
            _ = A.CallTo(() => servicio.ListarNifIncorrectos(A<List<string>>.Ignored))
                .Invokes((List<string> l) => filtroRecibido = l)
                .Returns(new List<ClienteNifIncorrectoDTO>());
            var controller = new ClientesController(A.Fake<IGestorClientes>(), vendedores,
                A.Fake<IGestorSincronizacion>(), servicio);

            _ = await controller.GetNifIncorrectos("ASH");

            CollectionAssert.AreEquivalent(new List<string> { "ASH", "DV", "PP" }, filtroRecibido,
                "El filtro debe llevar al jefe + su equipo, sin duplicados y sin padding");
        }

        [TestMethod]
        public async Task GetNifIncorrectos_SinVendedor_NoFiltra()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            var controller = ControllerConValidacion(servicio);

            _ = await controller.GetNifIncorrectos(null);

            A.CallTo(() => servicio.ListarNifIncorrectos(null)).MustHaveHappenedOnceExactly();
        }
    }
}
