using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#532: GET/PUT api/Clientes/PreferenciasCorreo, con el cliente siempre del JWT de la app.</summary>
    [TestClass]
    public class PreferenciasCorreoControllerTests
    {
        private const string CLIENTE = "15191";
        private const string EMAIL = "lidia@peluqueria.es";

        private IRepositorioPreferenciasCorreo repositorio;
        private BajasCorreosPostCompra bajas;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioPreferenciasCorreo>();
            bajas = new BajasCorreosPostCompra();
            A.CallTo(() => repositorio.LeerBajas(A<string>._, A<string>._, A<string>._)).ReturnsLazily(() => Task.FromResult(bajas));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Invokes((string e, string c, string tipo, string m, string u) => bajas.Agregar(c, null, tipo))
                .Returns(Task.CompletedTask);
        }

        private PreferenciasCorreoController Controller(params Claim[] claims)
            => new PreferenciasCorreoController(new ServicioPreferenciasCorreo(repositorio))
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
            };

        private PreferenciasCorreoController ControllerCliente()
            => Controller(new Claim("cliente", CLIENTE + " "), new Claim(ClaimTypes.Email, EMAIL));

        [TestMethod]
        public async Task Get_SinClaimCliente_Unauthorized()
        {
            IHttpActionResult resultado = await Controller(new Claim("IsEmployee", "true")).GetPreferenciasCorreo();
            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task Get_DevuelveUnaPreferenciaPorTipo()
        {
            bajas.Agregar(CLIENTE, null, TiposCorreoCliente.VIDEOS_COMPRAS);

            var ok = await ControllerCliente().GetPreferenciasCorreo() as OkNegotiatedContentResult<List<PreferenciaCorreoDTO>>;

            Assert.IsNotNull(ok);
            PreferenciaCorreoDTO reposicion = ok.Content.Single(p => p.Tipo == "RecordatorioReposicion");
            Assert.AreEqual("Aviso de reposición", reposicion.Nombre);
            Assert.IsTrue(reposicion.Activo);
            Assert.IsFalse(ok.Content.Single(p => p.Tipo == "VideosCompras").Activo);
            A.CallTo(() => repositorio.LeerBajas("1", CLIENTE, EMAIL)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Put_Apagar_DaDeBajaAlClienteDelToken()
        {
            var ok = await ControllerCliente().PutPreferenciaCorreo("RecordatorioReposicion", new PreferenciaCorreoRequest { Activo = false })
                as OkNegotiatedContentResult<List<PreferenciaCorreoDTO>>;

            Assert.IsNotNull(ok);
            Assert.IsFalse(ok.Content.Single(p => p.Tipo == "RecordatorioReposicion").Activo);
            Assert.IsTrue(ok.Content.Single(p => p.Tipo == "VideosCompras").Activo);
            A.CallTo(() => repositorio.DarDeBaja("1", CLIENTE, TiposCorreoCliente.RECORDATORIO_REPOSICION, A<string>._, EMAIL))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_SinActivo_BadRequest()
        {
            IHttpActionResult resultado = await ControllerCliente().PutPreferenciaCorreo("RecordatorioReposicion", new PreferenciaCorreoRequest());
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            resultado = await ControllerCliente().PutPreferenciaCorreo("RecordatorioReposicion", null);
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Put_TipoQueNoExiste_NotFound()
        {
            IHttpActionResult resultado = await ControllerCliente().PutPreferenciaCorreo("Facturas", new PreferenciaCorreoRequest { Activo = false });
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_SinClaimCliente_UnauthorizedYNoToca()
        {
            IHttpActionResult resultado = await Controller(new Claim(ClaimTypes.Email, EMAIL))
                .PutPreferenciaCorreo("RecordatorioReposicion", new PreferenciaCorreoRequest { Activo = false });
            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => repositorio.DarDeBaja(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
