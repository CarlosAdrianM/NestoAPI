using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#12: «No está» en el hueco de la parada, pero hay en otro: la reserva del picking pasa a ese hueco en vez
    /// de darlo por falta.
    /// </summary>
    [TestClass]
    public class CambioHuecoPickingTests
    {
        private static ReservaEnHueco Reserva(int ubicacion, int linea, int pedido, int cantidad)
        {
            return new ReservaEnHueco { Ubicacion = ubicacion, Linea = linea, Pedido = pedido, EmpresaLinea = "1", Almacen = "ALG", Cantidad = cantidad };
        }

        private static LibreEnHueco Libre(int ubicacion, int cantidad)
        {
            return new LibreEnHueco { Ubicacion = ubicacion, Cantidad = cantidad };
        }

        // ---- Qué reserva pasa a qué fila libre ----

        [TestMethod]
        public void Repartir_UnaReservaYUnaFilaLibreConDeSobra_SeCambiaLoPedido()
        {
            List<MovimientoDeReserva> plan = PlanificadorCambioHueco.Repartir(new[] { Reserva(10, 501, 927519, 5) }, new[] { Libre(20, 8) }, 2);

            MovimientoDeReserva movimiento = plan.Single();
            Assert.AreEqual(10, movimiento.Reserva.Ubicacion);
            Assert.AreEqual(20, movimiento.Libre.Ubicacion);
            Assert.AreEqual(2, movimiento.Cantidad, "Solo lo que no se ha encontrado, no toda la reserva");
        }

        [TestMethod]
        public void Repartir_VariasReservasYVariasFilasLibres_SeCasanEnOrdenSinPasarseDeNinguna()
        {
            // Dos pedidos reservan en el hueco (3 y 2); en el otro hueco hay dos filas libres (2 y 4)
            List<MovimientoDeReserva> plan = PlanificadorCambioHueco.Repartir(
                new[] { Reserva(10, 501, 927600, 3), Reserva(11, 502, 927519, 2) },
                new[] { Libre(20, 2), Libre(21, 4) }, 5);

            CollectionAssert.AreEqual(new[] { "10>20:2", "10>21:1", "11>21:2" },
                plan.Select(m => $"{m.Reserva.Ubicacion}>{m.Libre.Ubicacion}:{m.Cantidad}").ToArray());
        }

        [TestMethod]
        public void Repartir_SiNoHayBastanteLibre_SeCambiaHastaDondeLlega()
        {
            List<MovimientoDeReserva> plan = PlanificadorCambioHueco.Repartir(new[] { Reserva(10, 501, 927519, 5) }, new[] { Libre(20, 3) }, 5);

            Assert.AreEqual(3, plan.Sum(m => m.Cantidad));
        }

        [TestMethod]
        public void Repartir_NuncaMasDeLoReservadoEnElHuecoDeOrigen()
        {
            List<MovimientoDeReserva> plan = PlanificadorCambioHueco.Repartir(new[] { Reserva(10, 501, 927519, 2) }, new[] { Libre(20, 50) }, 9);

            Assert.AreEqual(2, plan.Sum(m => m.Cantidad));
        }

        [TestMethod]
        public void Repartir_SinReservasOSinLibres_NoHayNadaQueCambiar()
        {
            Assert.AreEqual(0, PlanificadorCambioHueco.Repartir(new ReservaEnHueco[0], new[] { Libre(20, 5) }, 2).Count);
            Assert.AreEqual(0, PlanificadorCambioHueco.Repartir(new[] { Reserva(10, 501, 927519, 2) }, null, 2).Count);
        }

        // ---- El servicio ----

        private static CambiarHuecoPickingDTO Cambio(string origen = "001/007/002", string destino = "003001002", int cantidad = 2, string producto = " 22624 ")
        {
            return new CambiarHuecoPickingDTO { Producto = producto, HuecoOrigen = origen, HuecoDestino = destino, Cantidad = cantidad };
        }

        [TestMethod]
        public async Task Cambiar_MandaLosHuecosComoLaEtiquetaYElProductoLimpio()
        {
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();
            A.CallTo(() => repositorio.Cambiar("1", 99633, "22624", "001007002", "003001002", 2, "Andrey")).Returns(2);

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoPicking(repositorio).Cambiar("1", 99633, Cambio(), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.Cambiado, resultado.Estado);
            Assert.AreEqual(2, resultado.Movidas);
            Assert.IsNull(resultado.Mensaje);
        }

        [TestMethod]
        public async Task Cambiar_SiSoloHabiaParteLibre_LoDice()
        {
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();
            A.CallTo(() => repositorio.Cambiar(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<int>._, A<string>._)).Returns(1);

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoPicking(repositorio).Cambiar("1", 99633, Cambio(cantidad: 2), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.Cambiado, resultado.Estado);
            Assert.AreEqual(1, resultado.Movidas);
            StringAssert.Contains(resultado.Mensaje, "Solo había 1");
        }

        [TestMethod]
        public async Task Cambiar_SiYaNoHayNadaQueCambiar_NoSePuede()
        {
            // Otro mozo lo ha cogido de ese hueco, o la reserva ya no está donde decía la PDA
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();
            A.CallTo(() => repositorio.Cambiar(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<int>._, A<string>._)).Returns(0);

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoPicking(repositorio).Cambiar("1", 99633, Cambio(), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.NoSePuede, resultado.Estado);
        }

        [TestMethod]
        [DataRow("", "003001002", 2, "22624")]
        [DataRow("001007002", "no es un hueco", 2, "22624")]
        [DataRow("001007002", "001/007/002", 2, "22624")]
        [DataRow("001007002", "003001002", 0, "22624")]
        [DataRow("001007002", "003001002", 2, " ")]
        public async Task Cambiar_LoQueNoVale_NoTocaNada(string origen, string destino, int cantidad, string producto)
        {
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoPicking(repositorio)
                .Cambiar("1", 99633, Cambio(origen, destino, cantidad, producto), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.NoValido, resultado.Estado);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerAlternativas_PreguntaSinElHuecoDondeNoEstaba()
        {
            IRepositorioCambioHueco repositorio = A.Fake<IRepositorioCambioHueco>();
            var otros = new List<HuecoAlternativoDTO> { new HuecoAlternativoDTO { Ubicacion = "003/001/002", Codigo = "003001002", Cantidad = 6 } };
            A.CallTo(() => repositorio.LeerAlternativas("1", 99633, "22624", "001007002")).Returns(otros);

            List<HuecoAlternativoDTO> alternativas = await new ServicioCambioHuecoPicking(repositorio).LeerAlternativas("1", 99633, " 22624 ", "001/007/002");

            Assert.AreSame(otros, alternativas);
        }

        // ---- Las rutas ----

        private static AlmacenController Controlador(IServicioCambioHuecoPicking cambioHueco)
        {
            return new AlmacenController(A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(),
                A.Fake<IServicioRecepcionCompras>(), A.Fake<IServicioRecepcionReposiciones>(), null, null, cambioHueco)
            {
                User = new GenericPrincipal(new GenericIdentity("Andrey", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };
        }

        [TestMethod]
        public async Task PostCambiarHueco_Cambiado_DevuelveLasUnidadesConElUsuarioDelToken()
        {
            IServicioCambioHuecoPicking servicio = A.Fake<IServicioCambioHuecoPicking>();
            CambiarHuecoPickingDTO cambio = Cambio();
            A.CallTo(() => servicio.Cambiar("1", 99633, cambio, "Andrey"))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Movidas = 2 });

            IHttpActionResult respuesta = await Controlador(servicio).PostCambiarHueco(99633, cambio);

            Assert.AreEqual(2, ((OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)respuesta).Content.Movidas);
        }

        [TestMethod]
        public async Task PostCambiarHueco_YaNoSePuede_409ConElMotivo()
        {
            IServicioCambioHuecoPicking servicio = A.Fake<IServicioCambioHuecoPicking>();
            A.CallTo(() => servicio.Cambiar(A<string>._, A<int>._, A<CambiarHuecoPickingDTO>._, A<string>._))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoSePuede, Mensaje = "No se ha cambiado nada" });

            var respuesta = (NegotiatedContentResult<string>)await Controlador(servicio).PostCambiarHueco(99633, Cambio());

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.AreEqual("No se ha cambiado nada", respuesta.Content);
        }

        [TestMethod]
        public async Task PostCambiarHueco_NoValido_400()
        {
            IServicioCambioHuecoPicking servicio = A.Fake<IServicioCambioHuecoPicking>();
            A.CallTo(() => servicio.Cambiar(A<string>._, A<int>._, A<CambiarHuecoPickingDTO>._, A<string>._))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoValido, Mensaje = "Falta el producto." });

            Assert.IsInstanceOfType(await Controlador(servicio).PostCambiarHueco(99633, new CambiarHuecoPickingDTO()), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetAlternativasDelPicking_DevuelveLosOtrosHuecos()
        {
            IServicioCambioHuecoPicking servicio = A.Fake<IServicioCambioHuecoPicking>();
            var otros = new List<HuecoAlternativoDTO> { new HuecoAlternativoDTO { Ubicacion = "003/001/002", Codigo = "003001002", Cantidad = 6 } };
            A.CallTo(() => servicio.LeerAlternativas("1", 99633, "22624", "001007002")).Returns(otros);

            IHttpActionResult respuesta = await Controlador(servicio).GetAlternativasDelPicking(99633, "22624", "001007002");

            Assert.AreSame(otros, ((OkNegotiatedContentResult<List<HuecoAlternativoDTO>>)respuesta).Content);
        }
    }
}
