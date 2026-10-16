using FakeItEasy;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Jwt;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Owin;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#663: los endpoints de Aplázame. Los de la app usan SIEMPRE el cliente del JWT; la notificación no lleva JWT
    /// sino la clave privada de Aplázame, y tiene que llegar al controller pese al middleware JWT de OWIN.
    /// </summary>
    [TestClass]
    public class AplazameControllersTests
    {
        private static PedidosClienteController Controller(IServicioCobrosAplazame aplazame, params Claim[] claims)
        {
            return new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
                },
                ServicioAplazame = aplazame
            };
        }

        private static IServicioCobrosAplazame ServicioConConfig(ConfiguracionAplazame config)
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();
            A.CallTo(() => servicio.Configuracion).Returns(config);
            return servicio;
        }

        #region Disponible

        [TestMethod]
        public void Disponible_SinTokenDeCliente_NoAutorizado()
        {
            PedidosClienteController controller = Controller(ServicioConConfig(new ConfiguracionAplazame { Modo = ModoAplazame.Activo }));

            Assert.IsInstanceOfType(controller.GetAplazameDisponible(300), typeof(UnauthorizedResult));
        }

        [TestMethod]
        public void Disponible_EnSandbox_MiraElClienteDelToken()
        {
            ConfiguracionAplazame config = new ConfiguracionAplazame
            {
                Modo = ModoAplazame.Sandbox,
                ClavePrivada = "privada",
                ClavePublica = "publica",
                ClientesPrueba = ConfiguracionAplazame.InterpretarClientes("15191")
            };

            var dePrueba = Controller(ServicioConConfig(config), new Claim("cliente", "15191")).GetAplazameDisponible(300)
                as OkNegotiatedContentResult<DisponibilidadAplazameDTO>;
            var otro = Controller(ServicioConConfig(config), new Claim("cliente", "25299")).GetAplazameDisponible(300)
                as OkNegotiatedContentResult<DisponibilidadAplazameDTO>;

            Assert.IsTrue(dePrueba.Content.Disponible);
            Assert.IsTrue(dePrueba.Content.Sandbox);
            Assert.IsFalse(otro.Content.Disponible);
        }

        [TestMethod]
        public void Disponible_SinVerPrecios_No()
        {
            ConfiguracionAplazame config = new ConfiguracionAplazame { Modo = ModoAplazame.Activo, ClavePrivada = "privada", ClavePublica = "publica" };

            var resultado = Controller(ServicioConConfig(config), new Claim("cliente", "15191"),
                new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinPrecios")).GetAplazameDisponible(300)
                as OkNegotiatedContentResult<DisponibilidadAplazameDTO>;

            Assert.IsFalse(resultado.Content.Disponible);
        }

        #endregion

        #region Abrir el cobro y consultar su estado

        [TestMethod]
        public async Task CarritoAplazame_SinTokenDeCliente_NoAutorizado()
        {
            IServicioCobrosAplazame servicio = ServicioConConfig(new ConfiguracionAplazame { Modo = ModoAplazame.Activo });

            IHttpActionResult resultado = await Controller(servicio).PostCarritoAplazame(Peticion());

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => servicio.IniciarCobro(A<SolicitudCobroAplazame>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CarritoAplazame_UsuarioSinPrecios_NoAbreNada()
        {
            IServicioCobrosAplazame servicio = ServicioConConfig(new ConfiguracionAplazame { Modo = ModoAplazame.Activo });

            IHttpActionResult resultado = await Controller(servicio, new Claim("cliente", "15191"),
                new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "SinPrecios")).PostCarritoAplazame(Peticion());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.IniciarCobro(A<SolicitudCobroAplazame>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EstadoCobro_PreguntaPorElClienteDelToken()
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();
            A.CallTo(() => servicio.Estado(901, "1", "15191")).Returns(new EstadoCobroAplazameDTO { Estado = "Confirmado", Importe = 12.10M });

            var resultado = await Controller(servicio, new Claim("cliente", "15191")).GetCobroAplazame(901)
                as OkNegotiatedContentResult<EstadoCobroAplazameDTO>;

            Assert.AreEqual("Confirmado", resultado.Content.Estado);
            A.CallTo(() => servicio.Estado(901, "1", "15191")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EstadoCobro_DeOtroCliente_404()
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();
            A.CallTo(() => servicio.Estado(A<int>._, A<string>._, A<string>._)).Returns((EstadoCobroAplazameDTO)null);

            IHttpActionResult resultado = await Controller(servicio, new Claim("cliente", "25299")).GetCobroAplazame(901);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task EstadoCobro_SinToken_NoAutorizado()
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();

            IHttpActionResult resultado = await Controller(servicio).GetCobroAplazame(901);

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => servicio.Estado(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        private static PedidoClienteRequest Peticion()
        {
            return new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 } }
            };
        }

        #endregion

        #region Notificación

        private static AplazameController ControllerNotificacion(IServicioCobrosAplazame servicio, string autorizacion, string cuerpo)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.nuevavision.es/api/Aplazame/Notificacion")
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            };
            if (autorizacion != null)
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(autorizacion);
            }
            return new AplazameController(servicio)
            {
                Request = request,
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public async Task Notificacion_PasaLaCabeceraYElCuerpoTalCual()
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();
            A.CallTo(() => servicio.ProcesarNotificacion(A<string>._, A<string>._)).Returns(RespuestaNotificacionAplazame.Ok());

            HttpResponseMessage respuesta = await ControllerNotificacion(servicio, "Bearer clave-de-prueba", "{\"mid\":\"APP-1\"}").Notificacion();

            A.CallTo(() => servicio.ProcesarNotificacion("Bearer clave-de-prueba", "{\"mid\":\"APP-1\"}")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("{\"status\":\"ok\"}", await respuesta.Content.ReadAsStringAsync());
        }

        [TestMethod]
        public async Task Notificacion_403SinCuerpo()
        {
            IServicioCobrosAplazame servicio = A.Fake<IServicioCobrosAplazame>();
            A.CallTo(() => servicio.ProcesarNotificacion(A<string>._, A<string>._)).Returns(RespuestaNotificacionAplazame.Codigo(403));

            HttpResponseMessage respuesta = await ControllerNotificacion(servicio, null, "{}").Notificacion();

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(() => servicio.ProcesarNotificacion(null, "{}")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Notificacion_EsAnonima()
        {
            System.Reflection.MethodInfo metodo = typeof(AplazameController).GetMethod(nameof(AplazameController.Notificacion));
            Assert.IsTrue(metodo.IsDefined(typeof(AllowAnonymousAttribute), false));
        }

        /// <summary>
        /// La duda del encargo: ¿llega la cabecera «Authorization: Bearer &lt;clave privada de Aplázame&gt;» al controller,
        /// con el middleware JWT de OWIN en modo activo delante (Startup.ConfigureOAuthTokenConsumption)? La clave de
        /// Aplázame no es un JWT. Se monta el MISMO middleware (AllowedAudiences + IssuerSecurityKeyProviders, sin
        /// TokenValidationParameters, ver StartupJwtConfigurationTests) y se comprueba que el middleware no corta la
        /// petición, no contesta 401 y deja la cabecera intacta: la petición sigue sin usuario, que es lo que pide
        /// [AllowAnonymous].
        /// </summary>
        [TestMethod]
        public async Task MiddlewareJwt_UnBearerQueNoEsJwt_LlegaAlControllerConLaCabeceraIntacta()
        {
            const string cabecera = "Bearer 0123456789abcdef0123456789abcdef01234567";
            Microsoft.Owin.Builder.AppBuilder app = new Microsoft.Owin.Builder.AppBuilder();
            _ = app.UseJwtBearerAuthentication(new JwtBearerAuthenticationOptions
            {
                AuthenticationMode = AuthenticationMode.Active,
                AllowedAudiences = new[] { "audiencia-de-prueba" },
                IssuerSecurityKeyProviders = new IIssuerSecurityKeyProvider[]
                {
                    new SymmetricKeyIssuerSecurityKeyProvider("carlos", Encoding.UTF8.GetBytes("secreto-de-prueba-de-32-bytes-al-menos!!"))
                }
            });
            bool llego = false;
            string cabeceraVista = null;
            IPrincipal usuario = null;
            app.Run(contexto =>
            {
                llego = true;
                cabeceraVista = contexto.Request.Headers.Get("Authorization");
                usuario = contexto.Request.User;
                return Task.FromResult(0);
            });
            Func<IDictionary<string, object>, Task> tuberia =
                (Func<IDictionary<string, object>, Task>)app.Build(typeof(Func<IDictionary<string, object>, Task>));

            OwinContext peticion = new OwinContext();
            peticion.Request.Method = "POST";
            peticion.Request.Scheme = "https";
            peticion.Request.Path = new PathString("/api/Aplazame/Notificacion");
            peticion.Request.Headers.Set("Authorization", cabecera);
            // Lo que pone el host (Microsoft.Owin.Host.SystemWeb) y usan los middleware de autenticación
            peticion.Environment["server.OnSendingHeaders"] = new Action<Action<object>, object>((_, __) => { });
            await tuberia(peticion.Environment);

            Assert.IsTrue(llego, "el middleware JWT no corta la petición");
            Assert.AreEqual(cabecera, cabeceraVista, "y no toca la cabecera");
            Assert.IsFalse(usuario?.Identity?.IsAuthenticated == true, "no hay usuario: por eso la notificación es [AllowAnonymous]");
            Assert.AreNotEqual(401, peticion.Response.StatusCode);
        }

        #endregion
    }
}
