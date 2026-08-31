using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Http;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Issue #189: los controllers que estaban MIXTOS/ABIERTOS quedan protegidos con [Authorize]
    /// de clase, y lo anónimo va marcado con [AllowAnonymous] explícito y razonado. Estos tests
    /// por reflexión blindan que nadie quite los atributos sin querer (un 401 por atributo no
    /// llega a ELMAH, así que una regresión aquí sería silenciosa).
    /// </summary>
    [TestClass]
    public class AutorizacionControllersTests
    {
        private static void AssertAuthorizeDeClase(Type controller)
        {
            Assert.IsTrue(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any(),
                $"{controller.Name} debe llevar [Authorize] a nivel de clase (issue #189)");
        }

        private static void AssertAllowAnonymous(Type controller, string metodo)
        {
            MethodInfo info = controller.GetMethods().FirstOrDefault(m => m.Name == metodo);
            Assert.IsNotNull(info, $"No existe el método {metodo} en {controller.Name}");
            Assert.IsTrue(info.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false).Any(),
                $"{controller.Name}.{metodo} debe llevar [AllowAnonymous] explícito (issue #189)");
        }

        [TestMethod]
        public void AccountsController_TieneAuthorizeDeClase_YLoAnonimoExplicito()
        {
            AssertAuthorizeDeClase(typeof(AccountsController));
            // Recuperación de contraseña: la llama quien NO puede iniciar sesión.
            AssertAllowAnonymous(typeof(AccountsController), "OlvideMiContrasenna");
        }

        [TestMethod]
        public void AccountsController_CreateUser_ExigeRolAdmin()
        {
            // Issue #189: era [AllowAnonymous] (registro abierto a internet); auditado que ningún
            // cliente lo usa, pasa a Admin. Si algún flujo legítimo lo necesita anónimo, revisar
            // la issue antes de relajarlo.
            MethodInfo createUser = typeof(AccountsController).GetMethods().First(m => m.Name == "CreateUser");
            AuthorizeAttribute authorize = createUser.GetCustomAttributes<AuthorizeAttribute>(inherit: false).FirstOrDefault();
            Assert.IsNotNull(authorize, "CreateUser debe llevar [Authorize]");
            Assert.AreEqual("Admin", authorize.Roles);
            Assert.IsFalse(createUser.GetCustomAttributes<AllowAnonymousAttribute>(inherit: false).Any(),
                "CreateUser no puede volver a ser anónimo sin revisar la issue #189");
        }

        [TestMethod]
        public void EnviosAgenciasController_TieneAuthorizeDeClase_YElCorreoDeEntregaAnonimo()
        {
            AssertAuthorizeDeClase(typeof(EnviosAgenciasController));
            // Se llama desde navegador externo con CORS abierto, sin sesión de NestoAPI.
            AssertAllowAnonymous(typeof(EnviosAgenciasController), "EnviarCorreoEntregaAgencia");
        }

        [TestMethod]
        public void VentasClienteController_TieneAuthorizeDeClase()
        {
            AssertAuthorizeDeClase(typeof(VentasClienteController));
        }

        [TestMethod]
        public void RolesController_TieneAuthorizeDeClaseConRolAdmin()
        {
            AuthorizeAttribute authorize = typeof(RolesController)
                .GetCustomAttributes<AuthorizeAttribute>(inherit: false).FirstOrDefault();
            Assert.IsNotNull(authorize, "RolesController debe llevar [Authorize] de clase");
            Assert.AreEqual("Admin", authorize.Roles);
        }

        [TestMethod]
        public void PedidosVentaController_TieneAuthorizeDeClase()
        {
            // Issue #186: sin [Authorize], un JWT caducado no daba 401 y el pedido se creaba como
            // anónimo (ELMAH sin usuario). Este test impide que el atributo desaparezca.
            AssertAuthorizeDeClase(typeof(PedidosVentaController));
        }

        [TestMethod]
        public void SyncWebhookController_EsAnonimoExplicito()
        {
            // Lo llama Google Pub/Sub (push) sin JWT nuestro: anónimo a propósito y documentado.
            Assert.IsTrue(typeof(SyncWebhookController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: false).Any(),
                "SyncWebhookController debe llevar [AllowAnonymous] explícito de clase (issue #189)");
        }

        /// <summary>
        /// NestoAPI#427/#430: AuthController se quedó fuera de esta red cuando se hizo la #189, y
        /// es precisamente el controlador que EMITE todOS los tokens del sistema.
        ///
        /// No puede llevar [Authorize] de clase —es el sitio al que se llama justo cuando todavía
        /// no se tiene token—, así que la red aquí es otra: un inventario. Cada endpoint anónimo
        /// está listado con el motivo por el que puede serlo. Si alguien añade uno nuevo, este
        /// test se pone rojo y le obliga a decidir explícitamente en vez de que un endpoint
        /// anónimo entre de tapadillo en el controlador más sensible de la API.
        /// </summary>
        [TestMethod]
        public void AuthController_TodoEndpointAnonimoEstaInventariadoYRazonado()
        {
            var anonimosPorDiseno = new Dictionary<string, string>
            {
                ["RequestCodeAsync"] = "Paso 1 del login por correo: se llama sin tener token.",
                ["ValidateCodeAsync"] = "Paso 2: canjea el código recibido por correo.",
                ["GetToken"] = "Canjea el tokenForValidation firmado con HMAC.",
                ["RefreshToken"] = "La app llama con el token CADUCADO; desde #430 se valida su firma.",
                ["RefreshOAuthToken"] = "Igual, para los tokens de NestoApp; desde #430 se valida su firma.",
                ["GetWindowsToken"] = "Nesto escritorio, autenticado por Windows contra el AD.",
                ["PrestashopLogin"] = "El módulo de PrestaShop; protegido con [ApiKey], no con JWT."
            };

            List<string> acciones = typeof(AuthController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetCustomAttributes<System.Web.Http.Filters.ActionFilterAttribute>(true).Any()
                         || m.GetCustomAttributes(true).Any(a => a.GetType().Name.StartsWith("Http")))
                .Select(m => m.Name)
                .Distinct()
                .ToList();

            List<string> sinInventariar = acciones.Where(a => !anonimosPorDiseno.ContainsKey(a)).ToList();

            Assert.AreEqual(0, sinInventariar.Count,
                "Endpoints nuevos en AuthController sin decidir su autenticación. AuthController " +
                "es anónimo entero: cualquier endpoint que se añada queda expuesto a internet. " +
                "Añádelo al inventario con su motivo, o protégelo: " + string.Join(", ", sinInventariar));
        }
    }
}
