using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.PedidosVenta;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>Sugerencia 396 de Novedades: GET api/PedidosVenta/{empresa}/{numero}/DatosTransferencia.</summary>
    [TestClass]
    public class PedidosVentaTransferenciaControllerTests
    {
        private IServicioDatosTransferenciaPedido servicio;
        private PedidosVentaTransferenciaController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioDatosTransferenciaPedido>();
            controller = new PedidosVentaTransferenciaController(servicio)
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma") }, "Bearer"))
            };
        }

        [TestMethod]
        public void GetDatosTransferencia_TieneAuthorize()
        {
            Assert.IsTrue(typeof(PedidosVentaTransferenciaController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Any());
        }

        [TestMethod]
        public async Task GetDatosTransferencia_PedidoConCuenta_DevuelveLosDatos()
        {
            var datos = new DatosTransferenciaPedidoDTO { Pedido = 927160, Iban = "ES91 2100 0418 4502 0005 1332", Texto = "IBAN: ..." };
            A.CallTo(() => servicio.Leer("1", 927160)).Returns(datos);

            var resultado = await controller.GetDatosTransferencia("1  ", 927160) as OkNegotiatedContentResult<DatosTransferenciaPedidoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreSame(datos, resultado.Content);
        }

        [TestMethod]
        public async Task GetDatosTransferencia_PedidoQueNoExiste_404()
        {
            A.CallTo(() => servicio.Leer(A<string>._, A<int>._)).Returns(Task.FromResult<DatosTransferenciaPedidoDTO>(null));

            IHttpActionResult resultado = await controller.GetDatosTransferencia("1", 1);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetDatosTransferencia_EmpresaSinCuenta_400ConElMotivo()
        {
            A.CallTo(() => servicio.Leer(A<string>._, A<int>._)).Returns(new DatosTransferenciaPedidoDTO { Pedido = 927160, Iban = null });

            var resultado = await controller.GetDatosTransferencia("1", 927160) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "cuenta bancaria");
        }

        [TestMethod]
        public async Task GetDatosTransferencia_ClienteDeLaTienda_403()
        {
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", "29606") }, "Bearer"));

            var resultado = await controller.GetDatosTransferencia("1", 927160) as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => servicio.Leer(A<string>._, A<int>._)).MustNotHaveHappened();
        }
    }
}
