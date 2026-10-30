using FakeItEasy;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.Owin;
using Microsoft.Owin.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Owin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#749: el panel de Hangfire solo para Dirección e Informática con autenticación de Windows. Antes el filtro
    /// devolvía true y cualquiera podía lanzar jobs con POST /hangfire/recurring/trigger.
    /// </summary>
    [TestClass]
    public class HangfireAuthorizationFilterTests
    {
        private static readonly Func<IPrincipal, string, bool> EnDireccion = (u, g) => g == "Dirección";
        private static readonly Func<IPrincipal, string, bool> EnInformatica = (u, g) => g == "Informática";
        private static readonly Func<IPrincipal, string, bool> EnAlmacen = (u, g) => g == "Almacén";

        private static IPrincipal Windows(string nombre = @"NUEVAVISION\carlos")
            => new GenericPrincipal(new GenericIdentity(nombre, "Negotiate"), new string[0]);

        private static Dictionary<string, object> Entorno(string metodo, string ruta, IPrincipal usuario)
        {
            var env = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["owin.RequestMethod"] = metodo,
                ["owin.RequestScheme"] = "https",
                ["owin.RequestPathBase"] = string.Empty,
                ["owin.RequestPath"] = ruta,
                ["owin.RequestQueryString"] = string.Empty,
                ["owin.RequestProtocol"] = "HTTP/1.1",
                ["owin.RequestHeaders"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["Host"] = new[] { "api.nuevavision.es" } },
                ["owin.RequestBody"] = Stream.Null,
                ["owin.ResponseHeaders"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                ["owin.ResponseBody"] = new MemoryStream(),
                ["owin.CallCancelled"] = System.Threading.CancellationToken.None,
                ["owin.Version"] = "1.0"
            };
            if (usuario != null)
            {
                env["server.User"] = usuario;
            }
            return env;
        }

        private static DashboardContext Contexto(IDictionary<string, object> env)
            => new OwinDashboardContext(A.Fake<JobStorage>(), new DashboardOptions(), env);

        [TestMethod]
        public void Authorize_Anonimo_NoEntraYPideCredencialesDeWindows()
        {
            var env = Entorno("GET", "/", null);
            var filtro = new HangfireAuthorizationFilter(EnDireccion);

            Assert.IsFalse(filtro.Authorize(Contexto(env)));

            var cabeceras = (IDictionary<string, string[]>)env["owin.ResponseHeaders"];
            CollectionAssert.AreEquivalent(new[] { "Negotiate", "NTLM" },
                cabeceras["WWW-Authenticate"].SelectMany(v => v.Split(',')).Select(v => v.Trim()).ToArray());
        }

        [TestMethod]
        public void Authorize_UsuarioNoAutenticado_NoEntra()
        {
            var env = Entorno("GET", "/", new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]));

            Assert.IsFalse(new HangfireAuthorizationFilter((u, g) => true).Authorize(Contexto(env)));
        }

        [TestMethod]
        public void Authorize_Direccion_Entra()
        {
            Assert.IsTrue(new HangfireAuthorizationFilter(EnDireccion).Authorize(Contexto(Entorno("GET", "/", Windows()))));
        }

        [TestMethod]
        public void Authorize_Informatica_Entra()
        {
            Assert.IsTrue(new HangfireAuthorizationFilter(EnInformatica).Authorize(Contexto(Entorno("POST", "/recurring/trigger", Windows()))));
        }

        [TestMethod]
        public void Authorize_OtroGrupo_NoEntraYNoRepiteElReto()
        {
            var env = Entorno("GET", "/", Windows(@"NUEVAVISION\andre"));

            Assert.IsFalse(new HangfireAuthorizationFilter(EnAlmacen).Authorize(Contexto(env)));
            Assert.IsFalse(((IDictionary<string, string[]>)env["owin.ResponseHeaders"]).ContainsKey("WWW-Authenticate"),
                "Autenticado sin permiso es 403: no tiene sentido volver a pedir credenciales");
        }

        [TestMethod]
        public void Authorize_SiFallaLaComprobacionDelGrupo_NoEntra()
        {
            var filtro = new HangfireAuthorizationFilter((u, g) => throw new InvalidOperationException("sin controlador de dominio"));

            Assert.IsFalse(filtro.Authorize(Contexto(Entorno("GET", "/", Windows()))));
        }

        [TestMethod]
        public void Authorize_JwtConRolDireccion_NoEntra()
        {
            // El JWT de empleado lleva los grupos como roles, pero el panel solo acepta una identidad de Windows
            var jwt = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, @"NUEVAVISION\carlos"),
                new Claim(ClaimTypes.Role, @"NUEVAVISION\Dirección"),
                new Claim(ClaimTypes.Role, @"NUEVAVISION\Informática")
            }, "JWT"));

            Assert.IsFalse(new HangfireAuthorizationFilter().Authorize(Contexto(Entorno("GET", "/", jwt))));
        }

        [TestMethod]
        public void EstaEnGrupoWindows_IdentidadQueNoEsDeWindows_False()
        {
            Assert.IsFalse(HangfireAuthorizationFilter.EstaEnGrupoWindows(Windows(), "Dirección"));
            Assert.IsFalse(HangfireAuthorizationFilter.EstaEnGrupoWindows(null, "Dirección"));
        }

        [TestMethod]
        public void GruposPermitidos_SoloDireccionEInformatica()
        {
            CollectionAssert.AreEquivalent(new[] { "Dirección", "Informática" }, HangfireAuthorizationFilter.GRUPOS_PERMITIDOS.ToArray());
        }

        // De extremo a extremo con el middleware de Hangfire, como en Startup: lo que pasó hoy (POST de trigger anónimo)

        private static Func<IDictionary<string, object>, Task> Panel(HangfireAuthorizationFilter filtro)
        {
            var app = new AppBuilder();
            _ = app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = new[] { filtro } }, A.Fake<JobStorage>());
            app.Run(c =>
            {
                c.Response.StatusCode = 404;
                return Task.FromResult(0);
            });
            return (Func<IDictionary<string, object>, Task>)app.Build(typeof(Func<IDictionary<string, object>, Task>));
        }

        private static async Task<int> Pedir(HangfireAuthorizationFilter filtro, string metodo, string ruta, IPrincipal usuario)
        {
            var env = Entorno(metodo, ruta, usuario);
            await Panel(filtro)(env);
            return new OwinContext(env).Response.StatusCode;
        }

        [TestMethod]
        public async Task Panel_PostDeTriggerAnonimo_401()
        {
            Assert.AreEqual(401, await Pedir(new HangfireAuthorizationFilter(), "POST", "/hangfire/recurring/trigger", null));
        }

        [TestMethod]
        public async Task Panel_GetAnonimo_401()
        {
            Assert.AreEqual(401, await Pedir(new HangfireAuthorizationFilter(), "GET", "/hangfire", null));
        }

        [TestMethod]
        public async Task Panel_PostDeTriggerSinPermiso_403()
        {
            Assert.AreEqual(403, await Pedir(new HangfireAuthorizationFilter(EnAlmacen), "POST", "/hangfire/recurring/trigger", Windows()));
        }
    }
}
