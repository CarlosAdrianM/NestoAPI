using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#178: cada cliente solo ve y borra SUS tarjetas, y el token de Redsys no sale
    /// nunca por la API.
    /// </summary>
    [TestClass]
    public class TarjetasControllerTests
    {
        private ITarjetaClienteStore store;
        private IServicioPagos servicioPagos;

        [TestInitialize]
        public void Setup()
        {
            store = A.Fake<ITarjetaClienteStore>();
            servicioPagos = A.Fake<IServicioPagos>();
        }

        private TarjetasController CrearController(params Claim[] claims)
        {
            return new TarjetasController(store, servicioPagos)
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
                }
            };
        }

        [TestMethod]
        public void GetTarjetas_SinClaimCliente_Unauthorized()
        {
            // Un empleado o un vendedor no tienen claim "cliente": este endpoint es del canal app
            TarjetasController controller = CrearController(new Claim("IsVendedor", "true"));

            var resultado = controller.GetTarjetas();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => store.ListarActivas(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetTarjetas_ClienteAutenticado_DevuelveSusTarjetasSinElToken()
        {
            TarjetasController controller = CrearController(new Claim("cliente", "15191"));
            A.CallTo(() => store.ListarActivas(A<string>._, "15191")).Returns(new List<TarjetaCliente>
            {
                new TarjetaCliente
                {
                    Id = 7,
                    Cliente = "15191",
                    TokenRedsys = "SECRETO",
                    UltimosDigitos = "1234",
                    MarcaTarjeta = "Visa",
                    Activa = true,
                    FechaCaducidad = new DateTime(2027, 12, 31)
                }
            });

            var resultado = controller.GetTarjetas();

            var ok = resultado as OkNegotiatedContentResult<List<TarjetaClienteDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual(7, ok.Content[0].Id);
            Assert.AreEqual("1234", ok.Content[0].UltimosDigitos);
            Assert.AreEqual("Visa", ok.Content[0].MarcaTarjeta);
            Assert.IsFalse(ok.Content[0].Caducada);
            // El DTO ni siquiera tiene dónde poner el token: si esto compila, no se filtra
        }

        [TestMethod]
        public async System.Threading.Tasks.Task PostAltaTarjeta_SinClaimCliente_Unauthorized()
        {
            TarjetasController controller = CrearController(new Claim("IsVendedor", "true"));

            var resultado = await controller.PostAltaTarjeta(null);

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
            A.CallTo(() => servicioPagos.IniciarAltaTarjeta(A<SolicitudAltaTarjeta>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async System.Threading.Tasks.Task PostAltaTarjeta_ClienteAutenticado_ArrancaElAltaConSuCliente()
        {
            // El cliente sale del JWT: la app no puede dar de alta tarjetas de otro
            TarjetasController controller = CrearController(
                new Claim("cliente", "15191"),
                new Claim(ClaimTypes.Email, "cliente@test.com"));
            A.CallTo(() => servicioPagos.IniciarAltaTarjeta(A<SolicitudAltaTarjeta>._, A<string>._))
                .Returns(new RespuestaIniciarPago { IdPago = 42 });

            var resultado = await controller.PostAltaTarjeta(new TarjetasController.AltaTarjetaRequest
            {
                UrlOk = "nestotiendas://pago/ok",
                UrlKo = "nestotiendas://pago/ko"
            });

            var ok = resultado as OkNegotiatedContentResult<RespuestaIniciarPago>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(42, ok.Content.IdPago);
            A.CallTo(() => servicioPagos.IniciarAltaTarjeta(A<SolicitudAltaTarjeta>.That.Matches(s =>
                s.Cliente == "15191"
                && s.Correo == "cliente@test.com"
                && s.UrlOk == "nestotiendas://pago/ok"), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void DeleteTarjeta_DeOtroCliente_NotFoundYNoDesactiva()
        {
            TarjetasController controller = CrearController(new Claim("cliente", "15191"));
            A.CallTo(() => store.ObtenerPorId(7)).Returns(new TarjetaCliente { Id = 7, Cliente = "99999", Activa = true });

            var resultado = controller.DeleteTarjeta(7);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => store.Desactivar(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void DeleteTarjeta_Propia_LaDesactiva()
        {
            TarjetasController controller = CrearController(new Claim("cliente", "15191"));
            A.CallTo(() => store.ObtenerPorId(7)).Returns(new TarjetaCliente { Id = 7, Cliente = "15191", Activa = true });

            var resultado = controller.DeleteTarjeta(7);

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => store.Desactivar(7, A<string>._)).MustHaveHappenedOnceExactly();
        }
    }
}
