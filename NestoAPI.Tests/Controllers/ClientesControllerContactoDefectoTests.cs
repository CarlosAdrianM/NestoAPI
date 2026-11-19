using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#879 (corte 3): facturación = ficha principal; entrega por defecto = ContactoDefecto de la principal. GET y PUT
    /// api/Clientes/ContactoDefecto y su regla pura.
    /// </summary>
    [TestClass]
    public class ClientesControllerContactoDefectoTests
    {
        private static FichaContactoDefecto Ficha(string contacto, short? estado, bool principal = false, string contactoDefecto = null) =>
            new FichaContactoDefecto { Contacto = contacto, Estado = estado, ClientePrincipal = principal, ContactoDefecto = contactoDefecto };

        /// <summary>El 42143: principal 0 (facturación), entrega por defecto 0; contacto 1 activo, 2 anulado.</summary>
        private static List<FichaContactoDefecto> Fichas42143(string contactoDefecto = "0  ") => new List<FichaContactoDefecto>
        {
            Ficha("0  ", 0, principal: true, contactoDefecto: contactoDefecto),
            Ficha("1  ", 0),
            Ficha("2  ", -1)
        };

        private static IPrincipal Usuario(string nombre) =>
            new ClaimsPrincipal(new ClaimsIdentity(new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) }, "prueba"));

        private static ClientesController Controlador(IRepositorioContactoDefecto repositorio) =>
            new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                RepositorioContactoDefecto = repositorio,
                User = Usuario("Pepe")
            };

        // ---------------------------------------------------------------- regla pura

        [TestMethod]
        public void Regla_ContactoActivoDelCliente_SePuede()
        {
            Assert.IsNull(ReglaContactoDefecto.MotivoParaNoCambiar(Fichas42143(), "42143", "1"));
        }

        [TestMethod]
        public void Regla_ContactoQueNoEsDelCliente_Motivo()
        {
            Assert.AreEqual("El contacto 7 no es del cliente 42143.", ReglaContactoDefecto.MotivoParaNoCambiar(Fichas42143(), "42143", "7"));
        }

        [TestMethod]
        public void Regla_ContactoAnulado_Motivo()
        {
            StringAssert.Contains(ReglaContactoDefecto.MotivoParaNoCambiar(Fichas42143(), "42143", "2"), "está anulado");
        }

        [TestMethod]
        public void Regla_SinPrincipalActiva_Motivo()
        {
            var fichas = new List<FichaContactoDefecto> { Ficha("0", -1, principal: true), Ficha("1", 0) };
            StringAssert.Contains(ReglaContactoDefecto.MotivoParaNoCambiar(fichas, "42143", "1"), "no tiene una ficha principal activa");
        }

        [TestMethod]
        public void Regla_SinContactoOSinCliente_Motivo()
        {
            Assert.IsNotNull(ReglaContactoDefecto.MotivoParaNoCambiar(Fichas42143(), "42143", "  "));
            Assert.AreEqual("No existe el cliente 99999.", ReglaContactoDefecto.MotivoParaNoCambiar(new List<FichaContactoDefecto>(), "99999", "0"));
        }

        [TestMethod]
        public void Regla_PrincipalYEntrega_LaPrincipalActivaYSuContactoDefecto()
        {
            var fichas = new List<FichaContactoDefecto>
            {
                Ficha("0", -1, principal: true, contactoDefecto: "0"), // anulada: no manda
                Ficha("1", 0, principal: true, contactoDefecto: "2  "),
                Ficha("2", 0)
            };
            Assert.AreEqual("1", ReglaContactoDefecto.Principal(fichas).Contacto);
            Assert.AreEqual("2", ReglaContactoDefecto.ContactoEntrega(fichas));
            Assert.IsNull(ReglaContactoDefecto.ContactoEntrega(new List<FichaContactoDefecto> { Ficha("0", 0, principal: true, contactoDefecto: " ") }));
        }

        [TestMethod]
        public void Regla_YaEsLaDeEntrega_SoloSiLoEsEnTodasLasPrincipalesActivas()
        {
            Assert.IsTrue(ReglaContactoDefecto.YaEsLaDeEntrega(Fichas42143("0  "), "0"));
            Assert.IsFalse(ReglaContactoDefecto.YaEsLaDeEntrega(Fichas42143("0  "), "1"));
            var dosPrincipales = new List<FichaContactoDefecto>
            {
                Ficha("0", 0, principal: true, contactoDefecto: "1"),
                Ficha("1", 0, principal: true, contactoDefecto: "0")
            };
            Assert.IsFalse(ReglaContactoDefecto.YaEsLaDeEntrega(dosPrincipales, "1"));
        }

        [TestMethod]
        public void TextoRastro_DiceClienteYContacto()
        {
            Assert.AreEqual("Empresa 1 · cliente 42143 · entrega por defecto: contacto 1", RepositorioContactoDefecto.TextoRastro("1", "42143 ", "1 "));
            Assert.AreEqual("Empresa 1 · cliente 42143 · entrega por defecto: sin contacto", RepositorioContactoDefecto.TextoRastro("1", "42143", null));
        }

        // ---------------------------------------------------------------- GET

        [TestMethod]
        public async Task Get_DevuelveFacturacionYEntrega()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas("1", "42143")).Returns(Fichas42143("1  "));
            var ok = (OkNegotiatedContentResult<ContactoDefectoClienteDTO>)await Controlador(repositorio).GetContactoDefecto(null, " 42143 ");
            Assert.AreEqual("1", ok.Content.Empresa);
            Assert.AreEqual("0", ok.Content.ContactoFacturacion);
            Assert.AreEqual("1", ok.Content.ContactoDefecto);
        }

        [TestMethod]
        public async Task Get_ClienteQueNoExiste_404()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<string>._)).Returns(new List<FichaContactoDefecto>());
            Assert.IsInstanceOfType(await Controlador(repositorio).GetContactoDefecto("1", "99999"), typeof(NotFoundResult));
        }

        // ---------------------------------------------------------------- PUT

        [TestMethod]
        public async Task Put_ContactoValido_EscribeEnLaPrincipalConRastroYUsuario()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas("1", "42143")).Returns(Fichas42143("0  "));

            var ok = (OkNegotiatedContentResult<ContactoDefectoClienteDTO>)await Controlador(repositorio)
                .PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Empresa = "1", Cliente = "42143", Contacto = " 1 " });

            Assert.IsTrue(ok.Content.Cambiado);
            Assert.AreEqual("1", ok.Content.ContactoDefecto);
            Assert.AreEqual("0", ok.Content.ContactoFacturacion, "La de facturación no cambia");
            A.CallTo(() => repositorio.Guardar("1", "42143",
                    A<IEnumerable<string>>.That.Matches(p => p.SequenceEqual(new[] { "0" })), "0", "1", A<string>.That.Contains("Pepe")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_YaEraLaDeEntrega_NoEscribeNiDejaRastro()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas("1", "42143")).Returns(Fichas42143("1  "));
            var ok = (OkNegotiatedContentResult<ContactoDefectoClienteDTO>)await Controlador(repositorio)
                .PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Empresa = "1", Cliente = "42143", Contacto = "1" });
            Assert.IsFalse(ok.Content.Cambiado);
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<IEnumerable<string>>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_ContactoAnuladoODeOtroCliente_400SinEscribir()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas("1", "42143")).Returns(Fichas42143());
            var anulado = await Controlador(repositorio).PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Empresa = "1", Cliente = "42143", Contacto = "2" });
            var deOtro = await Controlador(repositorio).PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Empresa = "1", Cliente = "42143", Contacto = "9" });
            StringAssert.Contains(((BadRequestErrorMessageResult)anulado).Message, "anulado");
            StringAssert.Contains(((BadRequestErrorMessageResult)deOtro).Message, "no es del cliente");
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<IEnumerable<string>>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_ClienteQueNoExiste_404()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<string>._)).Returns(new List<FichaContactoDefecto>());
            var resultado = await Controlador(repositorio).PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Empresa = "1", Cliente = "99999", Contacto = "0" });
            Assert.AreEqual(HttpStatusCode.NotFound, ((NegotiatedContentResult<string>)resultado).StatusCode);
        }

        [TestMethod]
        public async Task Put_SinCuerpoOSinCliente_400()
        {
            var repositorio = A.Fake<IRepositorioContactoDefecto>();
            Assert.IsInstanceOfType(await Controlador(repositorio).PutContactoDefecto(null), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(await Controlador(repositorio).PutContactoDefecto(new PeticionContactoDefectoClienteDTO { Contacto = "1" }), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Endpoints_ConAuthorize()
        {
            foreach (string metodo in new[] { nameof(ClientesController.GetContactoDefecto), nameof(ClientesController.PutContactoDefecto) })
            {
                Assert.IsTrue(typeof(ClientesController).GetMethod(metodo)
                    .GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), true).Any(), metodo);
            }
        }
    }
}
