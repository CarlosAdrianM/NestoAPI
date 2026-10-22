using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#692: GET/PUT api/Clientes/PreferenciasNotificaciones («Notificaciones que recibe» de TNV), con el cliente
    /// siempre del JWT de la app. Por defecto, todas encendidas.
    /// </summary>
    [TestClass]
    public class PreferenciasNotificacionesControllerTests
    {
        private const string CLIENTE = "15191";
        private const string EMAIL = "lidia@peluqueria.es";

        private IRepositorioPreferenciasNotificaciones repositorio;
        private HashSet<string> bajas;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPreferenciasNotificaciones>();
            bajas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            A.CallTo(() => repositorio.LeerBajas(A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult<ISet<string>>(bajas));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._))
                .Invokes((string e, string c, string tipo, string u) => bajas.Add(tipo)).Returns(Task.CompletedTask);
            A.CallTo(() => repositorio.QuitarBaja(A<string>._, A<string>._, A<string>._))
                .Invokes((string e, string c, string tipo) => bajas.Remove(tipo)).Returns(Task.CompletedTask);
        }

        private PreferenciasNotificacionesController Controller(params Claim[] claims)
            => new PreferenciasNotificacionesController(new ServicioPreferenciasNotificaciones(repositorio))
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
            };

        private PreferenciasNotificacionesController ControllerCliente()
            => Controller(new Claim("cliente", CLIENTE + " "), new Claim(ClaimTypes.Email, EMAIL));

        [TestMethod]
        public async Task Get_PorDefectoTodasEncendidas()
        {
            var ok = await ControllerCliente().GetPreferenciasNotificaciones() as OkNegotiatedContentResult<List<PreferenciaNotificacionDTO>>;

            Assert.IsNotNull(ok);
            CollectionAssert.AreEqual(new[] { "EventosYCursos", "AvisosReposicion" }, ok.Content.Select(p => p.Tipo).ToList());
            Assert.AreEqual("Eventos y cursos", ok.Content[0].Nombre);
            Assert.AreEqual("Avisos de reposición", ok.Content[1].Nombre);
            Assert.IsTrue(ok.Content.All(p => p.Activo));
            A.CallTo(() => repositorio.LeerBajas("1", CLIENTE)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Put_ApagarYEncender_SoloEseTipo()
        {
            var ok = await ControllerCliente().PutPreferenciaNotificacion("eventosycursos", new PreferenciaNotificacionRequest { Activo = false })
                as OkNegotiatedContentResult<List<PreferenciaNotificacionDTO>>;

            Assert.IsFalse(ok.Content.Single(p => p.Tipo == TiposNotificacionCliente.EVENTOS_Y_CURSOS).Activo);
            Assert.IsTrue(ok.Content.Single(p => p.Tipo == TiposNotificacionCliente.AVISOS_REPOSICION).Activo);
            A.CallTo(() => repositorio.DarDeBaja("1", CLIENTE, TiposNotificacionCliente.EVENTOS_Y_CURSOS, EMAIL)).MustHaveHappenedOnceExactly();

            ok = await ControllerCliente().PutPreferenciaNotificacion("EventosYCursos", new PreferenciaNotificacionRequest { Activo = true })
                as OkNegotiatedContentResult<List<PreferenciaNotificacionDTO>>;
            Assert.IsTrue(ok.Content.All(p => p.Activo));
        }

        [TestMethod]
        public async Task Put_Errores_SinClienteSinActivoOTipoDesconocido()
        {
            Assert.IsInstanceOfType(await Controller(new Claim(ClaimTypes.Email, EMAIL))
                .PutPreferenciaNotificacion("EventosYCursos", new PreferenciaNotificacionRequest { Activo = false }), typeof(UnauthorizedResult));
            Assert.IsInstanceOfType(await Controller(new Claim("IsEmployee", "true")).GetPreferenciasNotificaciones(), typeof(UnauthorizedResult));
            Assert.IsInstanceOfType(await ControllerCliente().PutPreferenciaNotificacion("EventosYCursos", null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await ControllerCliente().PutPreferenciaNotificacion("Pedidos", new PreferenciaNotificacionRequest { Activo = false }),
                typeof(NotFoundResult));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ClientesDeBaja_SiFallaLaTabla_NadieDeBaja()
        {
            A.CallTo(() => repositorio.ClientesDeBaja(A<string>._, A<string>._)).Throws(new InvalidOperationException("Invalid object name"));

            ISet<string> deBaja = await ServicioPreferenciasNotificaciones.ClientesDeBaja(repositorio, "1", TiposNotificacionCliente.EVENTOS_Y_CURSOS);

            Assert.AreEqual(0, deBaja.Count);
        }
    }
}
