using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Providers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Providers
{
    /// <summary>
    /// NestoAPI#575 (fase 2): Ariadna entra por /oauth/token igual que NestoApp. Para que el servidor sepa
    /// desde qué aplicación viene cada usuario, Ariadna manda client_id=Ariadna y el token lleva el claim
    /// «app». Todo lo que NO es Ariadna (NestoApp sin client_id al entrar, con client_id=NestoApp al
    /// renovar) sigue EXACTAMENTE como antes: NestoApp no hace nunca logout y un fallo aquí dejaría
    /// fuera a todos los vendedores.
    /// </summary>
    [TestClass]
    public class AplicacionClienteOAuthTests
    {
        private static OAuthValidateClientAuthenticationContext ContextoValidacion(Dictionary<string, string> formulario)
        {
            var parametros = new ReadableStringCollection(formulario.ToDictionary(p => p.Key, p => new[] { p.Value }));
            return new OAuthValidateClientAuthenticationContext(new OwinContext(), new OAuthAuthorizationServerOptions(), parametros);
        }

        // ---- Qué aplicación se reconoce ----

        [TestMethod]
        public void Reconocida_SoloAriadnaSeDistingue()
        {
            Assert.AreEqual("Ariadna", AplicacionClienteOAuth.Reconocida("Ariadna"));
            Assert.AreEqual("Ariadna", AplicacionClienteOAuth.Reconocida(" ariadna "));
            Assert.IsNull(AplicacionClienteOAuth.Reconocida(null), "sin client_id: NestoApp, como siempre");
            Assert.IsNull(AplicacionClienteOAuth.Reconocida("NestoApp"), "NestoApp no cambia nada");
            Assert.IsNull(AplicacionClienteOAuth.Reconocida("cualquiera"));
        }

        // ---- ValidateClientAuthentication ----

        [TestMethod]
        public async Task Validar_NestoAppAlEntrar_SinClientId_IgualQueAntes()
        {
            var contexto = ContextoValidacion(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["username"] = "manuel", ["password"] = "x"
            });

            await new CustomOAuthProvider().ValidateClientAuthentication(contexto);

            Assert.IsTrue(contexto.IsValidated);
            Assert.IsNull(contexto.ClientId);
        }

        [TestMethod]
        public async Task Validar_NestoAppAlRenovar_ConClientIdNestoApp_IgualQueAntes()
        {
            var contexto = ContextoValidacion(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = "abc", ["client_id"] = "NestoApp"
            });

            await new CustomOAuthProvider().ValidateClientAuthentication(contexto);

            Assert.IsTrue(contexto.IsValidated);
            Assert.IsNull(contexto.ClientId, "como hasta ahora: Validated() sin client_id");
        }

        [TestMethod]
        public async Task Validar_Ariadna_QuedaElClientId()
        {
            var contexto = ContextoValidacion(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["username"] = "Santiago", ["password"] = "x", ["client_id"] = "Ariadna"
            });

            await new CustomOAuthProvider().ValidateClientAuthentication(contexto);

            Assert.IsTrue(contexto.IsValidated);
            Assert.AreEqual("Ariadna", contexto.ClientId);
        }

        // ---- El claim y el refresh_token ----

        [TestMethod]
        public void Marcar_Ariadna_PoneElClaimYLoDejaEnElTicketParaRenovar()
        {
            var identidad = new ClaimsIdentity("JWT");
            var propiedades = new AuthenticationProperties();

            AplicacionClienteOAuth.Marcar(identidad, propiedades, "Ariadna");

            Assert.AreEqual("Ariadna", identidad.FindFirst(AplicacionClienteOAuth.TIPO_CLAIM)?.Value);
            Assert.AreEqual("Ariadna", propiedades.Dictionary[AplicacionClienteOAuth.CLAVE_PROPIEDAD]);
        }

        [TestMethod]
        public void Marcar_SinAplicacion_NoTocaNada()
        {
            var identidad = new ClaimsIdentity("JWT");
            var propiedades = new AuthenticationProperties();

            AplicacionClienteOAuth.Marcar(identidad, propiedades, null);

            Assert.IsNull(identidad.FindFirst(AplicacionClienteOAuth.TIPO_CLAIM));
            Assert.IsFalse(propiedades.Dictionary.ContainsKey(AplicacionClienteOAuth.CLAVE_PROPIEDAD));
        }

        [TestMethod]
        public void DelTicket_RefreshDeAriadna_SigueSiendoAriadna()
        {
            var propiedades = new AuthenticationProperties(new Dictionary<string, string>
            {
                [AplicacionClienteOAuth.CLAVE_PROPIEDAD] = "Ariadna"
            });

            Assert.AreEqual("Ariadna", AplicacionClienteOAuth.DelTicket(propiedades));
        }

        [TestMethod]
        public void DelTicket_RefreshDeNestoApp_YaEmitidosOSinPropiedad_NoEsAriadna()
        {
            Assert.IsNull(AplicacionClienteOAuth.DelTicket(new AuthenticationProperties()));
            Assert.IsNull(AplicacionClienteOAuth.DelTicket(null));
            Assert.IsNull(AplicacionClienteOAuth.DelTicket(new AuthenticationProperties(new Dictionary<string, string>
            {
                [AplicacionClienteOAuth.CLAVE_PROPIEDAD] = "NestoApp"
            })));
        }
    }
}
