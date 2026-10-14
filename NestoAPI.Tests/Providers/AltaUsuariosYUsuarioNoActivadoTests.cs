using FakeItEasy;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin;
using Microsoft.Owin.Security.OAuth;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Providers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Providers
{
    /// <summary>
    /// NestoAPI#624: los usuarios dados de alta por api/accounts/create nacían con EmailConfirmed = 0 y
    /// /oauth/token los rechazaba siempre (caso real 13/10/26: los mozos de Ariadna no podían entrar).
    /// Ahora nacen confirmados y, si aun así alguien entra sin confirmar, el mensaje dice qué hacer;
    /// pero solo cuando la contraseña es correcta, para no revelar qué usuarios existen.
    /// </summary>
    [TestClass]
    public class AltaUsuariosYUsuarioNoActivadoTests
    {
        private const string CONTRASENNA_BUENA = "Contraseña-buena-1";

        // ---- Alta por api/accounts/create ----

        [TestMethod]
        public void AltaAdministracion_ElUsuarioNaceConElCorreoConfirmado()
        {
            var modelo = new CreateUserBindingModel
            {
                Username = "Andre",
                Email = "andre@example.com",
                FirstName = "Andre",
                LastName = "Mozo",
                Password = CONTRASENNA_BUENA,
                ConfirmPassword = CONTRASENNA_BUENA
            };

            ApplicationUser usuario = AccountsController.NuevoUsuarioAltaAdministracion(modelo);

            Assert.IsTrue(usuario.EmailConfirmed, "si nace sin confirmar, /oauth/token no le deja entrar nunca");
            Assert.AreEqual("Andre", usuario.UserName);
            Assert.AreEqual("andre@example.com", usuario.Email);
        }

        // ---- /oauth/token con grant_type=password ----

        [TestMethod]
        public async Task Token_ContrasennaCorrectaYCorreoSinConfirmar_DiceQueNoEstaActivado()
        {
            OAuthGrantResourceOwnerCredentialsContext contexto = ContextoLogin(
                UsuarioGuardado("Pedro", emailConfirmado: false), "Pedro", CONTRASENNA_BUENA);

            await new CustomOAuthProvider().GrantResourceOwnerCredentials(contexto);

            Assert.IsTrue(contexto.HasError);
            Assert.AreEqual("invalid_grant", contexto.Error, "mismo código de siempre: NestoApp y Ariadna no cambian");
            Assert.AreEqual("Tu usuario aún no está activado: avisa a informática.", contexto.ErrorDescription);
            Assert.IsFalse(contexto.IsValidated);
        }

        [TestMethod]
        public async Task Token_ContrasennaIncorrectaYCorreoSinConfirmar_NoRevelaQueElUsuarioExiste()
        {
            OAuthGrantResourceOwnerCredentialsContext contexto = ContextoLogin(
                UsuarioGuardado("Pedro", emailConfirmado: false), "Pedro", "otra-contraseña");

            await new CustomOAuthProvider().GrantResourceOwnerCredentials(contexto);

            Assert.IsTrue(contexto.HasError);
            Assert.AreEqual("invalid_grant", contexto.Error);
            Assert.AreEqual("The user name or password is incorrect.", contexto.ErrorDescription);
            Assert.IsFalse(contexto.IsValidated);
        }

        [TestMethod]
        public async Task Token_UsuarioQueNoExiste_MismoMensajeQueContrasennaIncorrecta()
        {
            OAuthGrantResourceOwnerCredentialsContext contexto = ContextoLogin(null, "NoExiste", CONTRASENNA_BUENA);

            await new CustomOAuthProvider().GrantResourceOwnerCredentials(contexto);

            Assert.AreEqual("invalid_grant", contexto.Error);
            Assert.AreEqual("The user name or password is incorrect.", contexto.ErrorDescription);
        }

        // ---- Dobles ----

        private static ApplicationUser UsuarioGuardado(string nombre, bool emailConfirmado)
        {
            return new ApplicationUser
            {
                UserName = nombre,
                Email = nombre.ToLower() + "@example.com",
                EmailConfirmed = emailConfirmado,
                PasswordHash = new PasswordHasher().HashPassword(CONTRASENNA_BUENA)
            };
        }

        private static OAuthGrantResourceOwnerCredentialsContext ContextoLogin(ApplicationUser usuarioGuardado, string usuario, string contrasenna)
        {
            IUserStore<ApplicationUser> almacen = A.Fake<IUserStore<ApplicationUser>>(o => o.Implements<IUserPasswordStore<ApplicationUser>>());
            _ = A.CallTo(() => almacen.FindByNameAsync(A<string>._)).Returns(Task.FromResult<ApplicationUser>(null));
            if (usuarioGuardado != null)
            {
                _ = A.CallTo(() => almacen.FindByNameAsync(usuarioGuardado.UserName)).Returns(Task.FromResult(usuarioGuardado));
                _ = A.CallTo(() => ((IUserPasswordStore<ApplicationUser>)almacen).GetPasswordHashAsync(usuarioGuardado))
                    .Returns(Task.FromResult(usuarioGuardado.PasswordHash));
            }

            var owin = new OwinContext();
            _ = owin.Set(new ApplicationUserManager(almacen));

            return new OAuthGrantResourceOwnerCredentialsContext(owin, new OAuthAuthorizationServerOptions(),
                null, usuario, contrasenna, new List<string>());
        }
    }
}
