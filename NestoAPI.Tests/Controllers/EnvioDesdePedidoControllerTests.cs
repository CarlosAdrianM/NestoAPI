using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#595 (slice 2, corte 6): POST api/EnviosAgencias/DesdePedido traduce el resultado del servicio a HTTP.</summary>
    [TestClass]
    public class EnvioDesdePedidoControllerTests
    {
        private IServicioEnvioDesdePedido servicio;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioEnvioDesdePedido>();
            controller = new EnviosAgenciasController(A.Fake<NVEntities>()) { EnvioDesdePedido = servicio };
        }

        private void Devuelve(ResultadoEnvioDesdePedido resultado)
            => A.CallTo(() => servicio.Crear(A<CrearEnvioDesdePedidoDTO>._, A<string>._)).Returns(Task.FromResult(resultado));

        [TestMethod]
        public async Task Creado_200ConElEnvio()
        {
            var dto = new EnvioDesdePedidoDTO { Envio = new EnviosAgencia { Numero = 250000 }, Flujo = FlujosEnvioDesdePedido.TRAMITAR_AL_CERRAR };
            Devuelve(ResultadoEnvioDesdePedido.Creado(dto));

            IHttpActionResult respuesta = await controller.PostEnvioDesdePedido(new CrearEnvioDesdePedidoDTO { Empresa = "1", Pedido = 927700, Peso = 2m });

            Assert.AreSame(dto, ((OkNegotiatedContentResult<EnvioDesdePedidoDTO>)respuesta).Content);
            A.CallTo(() => servicio.Crear(A<CrearEnvioDesdePedidoDTO>.That.Matches(p => p.Pedido == 927700), "NestoAPI")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Conflicto_409ConLosPendientes()
        {
            var conflicto = new ConflictoEnvioDesdePedidoDTO();
            conflicto.Pendientes.Add(new AvisoPropuestaEnvio { Codigo = CodigosAvisoPropuestaEnvio.SIN_PICKING, Tipo = TiposAvisoPropuestaEnvio.CONFIRMAR });
            Devuelve(ResultadoEnvioDesdePedido.ConPendientes(conflicto));

            var respuesta = (NegotiatedContentResult<ConflictoEnvioDesdePedidoDTO>)await controller.PostEnvioDesdePedido(new CrearEnvioDesdePedidoDTO());

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.AreSame(conflicto, respuesta.Content);
        }

        [TestMethod]
        public async Task Invalido_400ConElCodigo()
        {
            Devuelve(ResultadoEnvioDesdePedido.Invalido(CodigosErrorEnvioDesdePedido.ETIQUETA_RECOGIDA, "Haga la etiqueta de recogida desde Nesto."));

            var respuesta = (NegotiatedContentResult<ErrorEnvioDesdePedidoDTO>)await controller.PostEnvioDesdePedido(new CrearEnvioDesdePedidoDTO());

            Assert.AreEqual(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.AreEqual(CodigosErrorEnvioDesdePedido.ETIQUETA_RECOGIDA, respuesta.Content.Codigo);
            Assert.AreEqual("Haga la etiqueta de recogida desde Nesto.", respuesta.Content.Message);
        }

        [TestMethod]
        public async Task NoEncontrado_404()
        {
            Devuelve(ResultadoEnvioDesdePedido.NoEncontrado());

            Assert.IsInstanceOfType(await controller.PostEnvioDesdePedido(new CrearEnvioDesdePedidoDTO()), typeof(NotFoundResult));
        }
    }
}
