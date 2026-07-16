using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.SeEstaVendiendo;
using System.Security.Principal;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// Tests de regresión de NestoAPI#307: SeEstaVendiendo debe leer el usuario del JWT
    /// en vez de exigir ?usuario=. Las líneas de venta guardan Usuario como
    /// NUEVAVISION\usuario, mientras que el Identity de /oauth/token viene sin dominio.
    /// </summary>
    [TestClass]
    public class GestorSeEstaVendiendoTests
    {
        private static IPrincipal Autenticado(string nombre) =>
            new GenericPrincipal(new GenericIdentity(nombre), null);

        private static IPrincipal NoAutenticado() =>
            new GenericPrincipal(new GenericIdentity(string.Empty), null);

        [TestMethod]
        public void ResolverUsuarioExcluido_IdentitySinDominio_AnteponeDominio()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(Autenticado("Laura"), null);

            Assert.AreEqual(@"NUEVAVISION\Laura", resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_IdentityConDominio_LoDevuelveTalCual()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(Autenticado(@"NUEVAVISION\Carlos"), null);

            Assert.AreEqual(@"NUEVAVISION\Carlos", resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_IdentityYQuery_PrioridadAlToken()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(Autenticado("Laura"), @"NUEVAVISION\Pepe");

            Assert.AreEqual(@"NUEVAVISION\Laura", resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_SinIdentity_UsaLaQueryComoFallback()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(NoAutenticado(), @"NUEVAVISION\Pepe");

            Assert.AreEqual(@"NUEVAVISION\Pepe", resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_QuerySinDominio_TambienAnteponeDominio()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(NoAutenticado(), "Pepe");

            Assert.AreEqual(@"NUEVAVISION\Pepe", resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_SinIdentityNiQuery_DevuelveNull()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(NoAutenticado(), null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ResolverUsuarioExcluido_PrincipalNull_UsaLaQueryComoFallback()
        {
            string resultado = GestorSeEstaVendiendo.ResolverUsuarioExcluido(null, @"NUEVAVISION\Pepe");

            Assert.AreEqual(@"NUEVAVISION\Pepe", resultado);
        }
    }
}
