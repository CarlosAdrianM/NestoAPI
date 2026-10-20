using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.Agencias;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#595 (slice 5, Ariadna#13): POST api/Almacen/Pedidos/{pedido}/Etiqueta, los códigos de la respuesta.</summary>
    [TestClass]
    public class AlmacenEtiquetaPackingTests
    {
        private IServicioEtiquetaPacking etiquetas;
        private AlmacenController controller;
        private IPrincipal usuario;

        [TestInitialize]
        public void Preparar()
        {
            etiquetas = A.Fake<IServicioEtiquetaPacking>();
            usuario = new GenericPrincipal(new GenericIdentity("Andre", "Bearer"), new[] { "Almacén" });
            controller = Crear(etiquetas);
        }

        private AlmacenController Crear(IServicioEtiquetaPacking servicio) => new AlmacenController(
            A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(), A.Fake<IServicioRecepcionCompras>(),
            A.Fake<IServicioRecepcionReposiciones>(), etiquetaPacking: servicio)
        {
            User = usuario,
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration()
        };

        private void Devuelve(ResultadoEtiquetaPacking resultado)
            => A.CallTo(() => etiquetas.Imprimir(926940, A<PeticionEtiquetaPackingDTO>._, A<IPrincipal>._)).Returns(resultado);

        [TestMethod]
        public void LaRutaEsPorDominioYEsUnPost()
        {
            System.Reflection.MethodInfo metodo = typeof(AlmacenController).GetMethod(nameof(AlmacenController.PostEtiquetaDelPedido));
            var ruta = (RouteAttribute)metodo.GetCustomAttributes(typeof(RouteAttribute), false)[0];
            Assert.AreEqual("Pedidos/{pedido:int}/Etiqueta", ruta.Template);
            Assert.AreEqual(1, metodo.GetCustomAttributes(typeof(HttpPostAttribute), false).Length);
        }

        [TestMethod]
        public async Task Hecha_200ConLaEtiquetaYPasaLaPeticionYElUsuario()
        {
            var etiqueta = new EtiquetaPackingDTO { Envio = 5001, Impresa = true };
            Devuelve(ResultadoEtiquetaPacking.Hecha(etiqueta));
            var peticion = new PeticionEtiquetaPackingDTO { Peso = 2m };

            IHttpActionResult resultado = await controller.PostEtiquetaDelPedido(926940, peticion);

            Assert.AreSame(etiqueta, ((OkNegotiatedContentResult<EtiquetaPackingDTO>)resultado).Content);
            A.CallTo(() => etiquetas.Imprimir(926940, peticion, usuario)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Conflicto_409ConLosPendientes()
        {
            var conflicto = new ConflictoEnvioDesdePedidoDTO { Pendientes = new List<AvisoPropuestaEnvio> { new AvisoPropuestaEnvio { Codigo = "SinPicking" } } };
            Devuelve(ResultadoEtiquetaPacking.ConPendientes(conflicto));

            var resultado = (NegotiatedContentResult<ConflictoEnvioDesdePedidoDTO>)await controller.PostEtiquetaDelPedido(926940, new PeticionEtiquetaPackingDTO());

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            Assert.AreSame(conflicto, resultado.Content);
        }

        [TestMethod]
        public async Task Invalida_400ConElCodigo()
        {
            Devuelve(ResultadoEtiquetaPacking.Invalida(CodigosEtiquetaPacking.SIN_BULTOS, "Cierre antes las cajas."));

            var resultado = (NegotiatedContentResult<ErrorEnvioDesdePedidoDTO>)await controller.PostEtiquetaDelPedido(926940, new PeticionEtiquetaPackingDTO());

            Assert.AreEqual(HttpStatusCode.BadRequest, resultado.StatusCode);
            Assert.AreEqual(CodigosEtiquetaPacking.SIN_BULTOS, resultado.Content.Codigo);
        }

        [TestMethod]
        public async Task NoEncontrado_404()
        {
            Devuelve(ResultadoEtiquetaPacking.NoEncontrado());

            Assert.IsInstanceOfType(await controller.PostEtiquetaDelPedido(926940, new PeticionEtiquetaPackingDTO()), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task FalloAgencia_502ConElEnvioGrabado()
        {
            var etiqueta = new EtiquetaPackingDTO { Envio = 5001, Codigo = CodigosEtiquetaPacking.IMPRIMIR_FALLIDO, Message = "No se ha impreso." };
            Devuelve(ResultadoEtiquetaPacking.Fallo(etiqueta));

            var resultado = (NegotiatedContentResult<EtiquetaPackingDTO>)await controller.PostEtiquetaDelPedido(926940, new PeticionEtiquetaPackingDTO());

            Assert.AreEqual(HttpStatusCode.BadGateway, resultado.StatusCode);
            Assert.AreEqual(5001, resultado.Content.Envio);
        }

        [TestMethod]
        public async Task SinServicio_503()
        {
            var resultado = (NegotiatedContentResult<string>)await Crear(null).PostEtiquetaDelPedido(926940, new PeticionEtiquetaPackingDTO());

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
        }
    }
}
