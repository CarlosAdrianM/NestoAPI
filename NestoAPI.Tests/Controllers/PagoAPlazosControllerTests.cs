using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#864: los endpoints del pago a plazos de un pedido: permisos (empleados; el vendedor de NestoApp solo los pedidos
    /// de sus clientes; un cliente de la tienda, nunca) y que cada respuesta del núcleo sale con su código y su mensaje.
    /// </summary>
    [TestClass]
    public class PagoAPlazosControllerTests
    {
        private const int PEDIDO = 930500;
        private const string CLIENTE = "15191";

        private IServicioPagoAPlazos servicio;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioPagoAPlazos>();
            A.CallTo(() => servicio.Disponible(A<string>._, A<int>._))
                .Returns(ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>.Bien(new DisponibilidadPagoAPlazosDTO { Disponible = true, Canal = "App" }));
            A.CallTo(() => servicio.Solicitar(A<string>._, A<int>._, A<PeticionPagoAPlazosDTO>._, A<string>._))
                .Returns(ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>.Bien(new SolicitudPagoAPlazosDTO { Estado = "Pendiente" }));
            A.CallTo(() => servicio.Estado(A<string>._, A<int>._))
                .Returns(ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Bien(new EstadoPagoAPlazosDTO { Estado = "Aprobado" }));
            A.CallTo(() => servicio.Cancelar(A<string>._, A<int>._, A<string>._, A<string>._))
                .Returns(ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Bien(new EstadoPagoAPlazosDTO { Estado = "Cancelado" }));
            A.CallTo(() => servicio.ClienteDelPedido(A<string>._, PEDIDO)).Returns(CLIENTE);
            A.CallTo(() => servicio.ClienteDelPedido(A<string>._, 1)).Returns((string)null);
        }

        private PagoAPlazosController Controlador(params Claim[] claims)
        {
            ClaimsIdentity identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma"));
            identity.AddClaims(claims);
            return new PagoAPlazosController(servicio)
            {
                User = new ClaimsPrincipal(identity),
                Request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/x"),
                Configuration = new HttpConfiguration(),
                // Los clientes del vendedor JE: solo el 15191
                EsClienteDelVendedor = (empresa, usuario, vendedor, cliente) => vendedor == "JE" && cliente == CLIENTE
            };
        }

        private PagoAPlazosController Empleado() => Controlador(new Claim("IsEmployee", "true"));
        private PagoAPlazosController Vendedor(string codigo = "JE") => Controlador(new Claim("IsVendedor", "true"), new Claim("Vendedor", codigo));
        private PagoAPlazosController ClienteDeLaTienda(string cliente = CLIENTE) => Controlador(new Claim("cliente", cliente));

        private static async Task<HttpStatusCode> Codigo(IHttpActionResult resultado)
            => (await resultado.ExecuteAsync(CancellationToken.None)).StatusCode;

        private static async Task<string> Texto(IHttpActionResult resultado)
            => await (await resultado.ExecuteAsync(CancellationToken.None)).Content.ReadAsStringAsync();

        #region Permisos

        [TestMethod]
        public async Task PagoAPlazosController_Empleado_Puede_SinMirarDeQuienEsElCliente()
        {
            IHttpActionResult resultado = await Empleado().GetDisponible("1", PEDIDO);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<DisponibilidadPagoAPlazosDTO>));
            A.CallTo(() => servicio.ClienteDelPedido(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PagoAPlazosController_ClienteDeLaTienda_403EnTodoLoDeEmpleados()
        {
            PagoAPlazosController controlador = ClienteDeLaTienda();

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetDisponible("1", PEDIDO)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.PostSolicitud("1", PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetEstado("1", PEDIDO)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.DeleteSolicitud("1", PEDIDO)));
            A.CallTo(() => servicio.Solicitar(A<string>._, A<int>._, A<PeticionPagoAPlazosDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.Cancelar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PagoAPlazosController_SinAutenticar_403()
        {
            PagoAPlazosController controlador = new PagoAPlazosController(servicio)
            {
                User = new ClaimsPrincipal(new ClaimsIdentity()),
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetDisponible("1", PEDIDO)));
        }

        [TestMethod]
        public async Task PagoAPlazosController_Vendedor_ConElPedidoDeSuCliente_Puede()
        {
            PagoAPlazosController controlador = Vendedor("JE");

            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await controlador.GetDisponible("1", PEDIDO)));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await controlador.PostSolicitud("1", PEDIDO, new PeticionPagoAPlazosDTO())));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await controlador.GetEstado("1", PEDIDO)));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await controlador.DeleteSolicitud("1", PEDIDO, "motivo")));
        }

        [TestMethod]
        public async Task PagoAPlazosController_Vendedor_ConElPedidoDeUnClienteQueNoEsSuyo_403YNoSeHaceNada()
        {
            PagoAPlazosController controlador = Vendedor("OTRO");

            IHttpActionResult disponible = await controlador.GetDisponible("1", PEDIDO);
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(disponible));
            StringAssert.Contains(await Texto(disponible), "sus clientes");
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.PostSolicitud("1", PEDIDO, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetEstado("1", PEDIDO)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.DeleteSolicitud("1", PEDIDO)));
            A.CallTo(() => servicio.Disponible(A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.Solicitar(A<string>._, A<int>._, A<PeticionPagoAPlazosDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.Estado(A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => servicio.Cancelar(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PagoAPlazosController_Vendedor_PedidoQueNoExiste_404()
        {
            Assert.AreEqual(HttpStatusCode.NotFound, await Codigo(await Vendedor().GetDisponible("1", 1)));
        }

        [TestMethod]
        public void PagoAPlazosController_Permisos_ReglaPura()
        {
            ClaimsPrincipal Usuario(params Claim[] claims) => new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            Func<string, string, string, string, bool> suyo = (e, u, v, c) => c == CLIENTE;

            Assert.IsTrue(PermisosPagoAPlazos.PuedeConElCliente(Usuario(new Claim("IsEmployee", "true")), "1", "99999", suyo));
            Assert.IsTrue(PermisosPagoAPlazos.PuedeConElCliente(Usuario(new Claim("IsVendedor", "true"), new Claim("Vendedor", "JE")), "1", CLIENTE, suyo));
            Assert.IsFalse(PermisosPagoAPlazos.PuedeConElCliente(Usuario(new Claim("IsVendedor", "true"), new Claim("Vendedor", "JE")), "1", "99999", suyo));
            Assert.IsFalse(PermisosPagoAPlazos.PuedeConElCliente(Usuario(new Claim("IsVendedor", "true"), new Claim("Vendedor", "JE")), "1", null, suyo));
            Assert.IsFalse(PermisosPagoAPlazos.PuedeConElCliente(Usuario(new Claim("cliente", CLIENTE)), "1", CLIENTE, suyo));
            Assert.IsFalse(PermisosPagoAPlazos.PuedeConElCliente(null, "1", CLIENTE, suyo));
            Assert.IsFalse(PermisosPagoAPlazos.PuedeConElCliente(new ClaimsPrincipal(new ClaimsIdentity()), "1", CLIENTE, suyo));
        }

        #endregion

        #region Respuestas

        [TestMethod]
        public async Task PagoAPlazosController_Disponible_200ConElDto()
        {
            var resultado = await Empleado().GetDisponible("1", PEDIDO) as OkNegotiatedContentResult<DisponibilidadPagoAPlazosDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.Disponible);
            A.CallTo(() => servicio.Disponible("1", PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosController_Solicitar_PasaLaPeticionYElUsuario()
        {
            PeticionPagoAPlazosDTO peticion = new PeticionPagoAPlazosDTO { Canal = "Enlace" };

            var resultado = await Empleado().PostSolicitud("1", PEDIDO, peticion) as OkNegotiatedContentResult<SolicitudPagoAPlazosDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("Pendiente", resultado.Content.Estado);
            A.CallTo(() => servicio.Solicitar("1", PEDIDO, peticion, A<string>.That.Contains("Paloma"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosController_Cancelar_PasaElMotivo()
        {
            var resultado = await Empleado().DeleteSolicitud("1", PEDIDO, "El cliente prefiere tarjeta") as OkNegotiatedContentResult<EstadoPagoAPlazosDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.Cancelar("1", PEDIDO, "El cliente prefiere tarjeta", A<string>.That.Contains("Paloma"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosController_LosErroresDelNucleo_SalenConSuCodigoYSuMensaje()
        {
            (HttpStatusCode codigo, string mensaje)[] casos =
            {
                (HttpStatusCode.BadRequest, "El canal tiene que ser «App» o «Enlace»."),
                (HttpStatusCode.NotFound, "No existe el pedido 930500."),
                (HttpStatusCode.Conflict, "El pedido ya está pagado."),
                (HttpStatusCode.BadGateway, "No se ha podido abrir el pago a plazos con Financiera Falsa."),
                (HttpStatusCode.ServiceUnavailable, ServicioPagoAPlazos.MENSAJE_SIN_SCRIPT)
            };
            foreach ((HttpStatusCode codigo, string mensaje) in casos)
            {
                A.CallTo(() => servicio.Solicitar(A<string>._, A<int>._, A<PeticionPagoAPlazosDTO>._, A<string>._))
                    .Returns(ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>.Mal(codigo, mensaje));

                IHttpActionResult resultado = await Empleado().PostSolicitud("1", PEDIDO, null);

                Assert.AreEqual(codigo, await Codigo(resultado));
                string cuerpo = await Texto(resultado);
                StringAssert.Contains(cuerpo, "\"Message\"");
                StringAssert.Contains(cuerpo, mensaje.Substring(0, 12));
            }
        }

        [TestMethod]
        public async Task PagoAPlazosController_SinElScript_503EnDisponibleYEstado()
        {
            A.CallTo(() => servicio.Disponible(A<string>._, A<int>._))
                .Returns(ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>.Mal(HttpStatusCode.ServiceUnavailable, ServicioPagoAPlazos.MENSAJE_SIN_SCRIPT));
            A.CallTo(() => servicio.Estado(A<string>._, A<int>._))
                .Returns(ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Mal(HttpStatusCode.ServiceUnavailable, ServicioPagoAPlazos.MENSAJE_SIN_SCRIPT));

            IHttpActionResult disponible = await Empleado().GetDisponible("1", PEDIDO);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, await Codigo(disponible));
            StringAssert.Contains(await Texto(disponible), "Issue864_PagoAPlazos.sql");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, await Codigo(await Empleado().GetEstado("1", PEDIDO)));
        }

        #endregion

        #region La app de clientes

        [TestMethod]
        public async Task PagoAPlazosController_ParaElCliente_ConElClienteDelToken()
        {
            A.CallTo(() => servicio.ParaElCliente("1", CLIENTE, PEDIDO))
                .Returns(ResultadoPagoAPlazos<PagoAPlazosClienteDTO>.Bien(new PagoAPlazosClienteDTO { Pedido = PEDIDO, Estado = "Pendiente", UrlPago = "https://x" }));

            var resultado = await ClienteDeLaTienda().GetParaElCliente(PEDIDO) as OkNegotiatedContentResult<PagoAPlazosClienteDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("https://x", resultado.Content.UrlPago);
            A.CallTo(() => servicio.ParaElCliente("1", CLIENTE, PEDIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosController_ParaElCliente_SinSerCliente_401()
        {
            Assert.IsInstanceOfType(await Empleado().GetParaElCliente(PEDIDO), typeof(UnauthorizedResult));
            Assert.IsInstanceOfType(await Vendedor().GetParaElCliente(PEDIDO), typeof(UnauthorizedResult));
            A.CallTo(() => servicio.ParaElCliente(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PagoAPlazosController_ParaElCliente_PedidoDeOtroOSinSolicitud_404()
        {
            A.CallTo(() => servicio.ParaElCliente(A<string>._, A<string>._, A<int>._))
                .Returns(ResultadoPagoAPlazos<PagoAPlazosClienteDTO>.Mal(HttpStatusCode.NotFound, "Este pedido no tiene ningún pago a plazos pendiente."));

            Assert.AreEqual(HttpStatusCode.NotFound, await Codigo(await ClienteDeLaTienda().GetParaElCliente(PEDIDO)));
        }

        #endregion

        #region Notificación del proveedor

        [TestMethod]
        public async Task PagoAPlazosController_Notificacion_PasaElProveedorLaCabeceraYElCuerpo_YContestaLoQueDigaElProveedor()
        {
            A.CallTo(() => servicio.ProcesarNotificacion("Falsa", "Bearer abc", "{\"mid\":\"PAP-7\"}"))
                .Returns(new RespuestaNotificacionProveedor { CodigoHttp = 200, Cuerpo = "{\"status\":\"ok\"}" });
            PagoAPlazosController controlador = new PagoAPlazosController(servicio)
            {
                Request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Falsa")
                {
                    Content = new StringContent("{\"mid\":\"PAP-7\"}", Encoding.UTF8, "application/json")
                },
                Configuration = new HttpConfiguration()
            };
            controlador.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "abc");

            HttpResponseMessage respuesta = await controlador.Notificacion("Falsa");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("{\"status\":\"ok\"}", await respuesta.Content.ReadAsStringAsync());
            Assert.AreEqual("application/json", respuesta.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task PagoAPlazosController_Notificacion_SinCuerpoEnLaRespuesta_SoloElCodigo()
        {
            A.CallTo(() => servicio.ProcesarNotificacion(A<string>._, A<string>._, A<string>._))
                .Returns(new RespuestaNotificacionProveedor { CodigoHttp = 403 });
            PagoAPlazosController controlador = new PagoAPlazosController(servicio)
            {
                Request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Falsa"),
                Configuration = new HttpConfiguration()
            };

            HttpResponseMessage respuesta = await controlador.Notificacion("Falsa");

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(string.Empty, respuesta.Content == null ? string.Empty : await respuesta.Content.ReadAsStringAsync());
            A.CallTo(() => servicio.ProcesarNotificacion("Falsa", null, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosController_Notificacion_SiFallaElNucleo_500ParaQueElProveedorReintente()
        {
            A.CallTo(() => servicio.ProcesarNotificacion(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin base de datos"));
            PagoAPlazosController controlador = new PagoAPlazosController(servicio)
            {
                Request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Falsa"),
                Configuration = new HttpConfiguration()
            };

            HttpResponseMessage respuesta = await controlador.Notificacion("Falsa");

            Assert.AreEqual(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        }

        [TestMethod]
        public void PagoAPlazosController_LaNotificacionEsAnonima_YLoDemasPideAutenticacion()
        {
            Assert.IsTrue(typeof(PagoAPlazosController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
            Assert.IsTrue(typeof(PagoAPlazosController).GetMethod(nameof(PagoAPlazosController.Notificacion))
                .GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);
            foreach (string metodo in new[] { nameof(PagoAPlazosController.GetDisponible), nameof(PagoAPlazosController.PostSolicitud),
                nameof(PagoAPlazosController.GetEstado), nameof(PagoAPlazosController.DeleteSolicitud), nameof(PagoAPlazosController.GetParaElCliente) })
            {
                Assert.AreEqual(0, typeof(PagoAPlazosController).GetMethod(metodo).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length, metodo);
            }
        }

        [TestMethod]
        public void PagoAPlazosController_LasRutasSonNeutras()
        {
            foreach (System.Reflection.MethodInfo metodo in typeof(PagoAPlazosController).GetMethods())
            {
                foreach (RouteAttribute ruta in metodo.GetCustomAttributes(typeof(RouteAttribute), true))
                {
                    StringAssert.Contains(ruta.Template, "PagoAPlazos");
                    Assert.IsFalse(ruta.Template.IndexOf("aplazame", StringComparison.OrdinalIgnoreCase) >= 0, ruta.Template);
                }
            }
        }

        #endregion
    }
}
