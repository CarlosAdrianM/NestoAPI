using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Seguridad;
using System.Linq;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    [TestClass]
    public class ClaimsVendedorHelperTests
    {
        #region AñadirClaimsVendedor

        [TestMethod]
        public void AñadirClaimsVendedor_UsuarioConVendedor_AñadeAmbosClaimsl()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            var servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario("usuario@test.com"))
                .Returns("NV");

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(identity, "usuario@test.com", servicio);

            // Assert
            Assert.IsTrue(resultado);
            Assert.AreEqual("true", identity.FindFirst("IsVendedor")?.Value);
            Assert.AreEqual("NV", identity.FindFirst("Vendedor")?.Value);
        }

        [TestMethod]
        public void AñadirClaimsVendedor_UsuarioSinVendedor_NoAñadeClaims()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            var servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario("usuario@test.com"))
                .Returns(null);

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(identity, "usuario@test.com", servicio);

            // Assert
            Assert.IsFalse(resultado);
            Assert.IsNull(identity.FindFirst("IsVendedor"));
            Assert.IsNull(identity.FindFirst("Vendedor"));
        }

        [TestMethod]
        public void AñadirClaimsVendedor_VendedorVacio_NoAñadeClaims()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            var servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario("usuario@test.com"))
                .Returns("   ");

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(identity, "usuario@test.com", servicio);

            // Assert
            Assert.IsFalse(resultado);
            Assert.IsNull(identity.FindFirst("IsVendedor"));
        }

        [TestMethod]
        public void AñadirClaimsVendedor_IdentityNull_DevuelveFalse()
        {
            // Arrange
            var servicio = A.Fake<IServicioUsuarioVendedor>();

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(null, "usuario@test.com", servicio);

            // Assert
            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void AñadirClaimsVendedor_UserNameNull_DevuelveFalse()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            var servicio = A.Fake<IServicioUsuarioVendedor>();

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(identity, null, servicio);

            // Assert
            Assert.IsFalse(resultado);
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AñadirClaimsVendedor_UserNameVacio_DevuelveFalse()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            var servicio = A.Fake<IServicioUsuarioVendedor>();

            // Act
            var resultado = ClaimsVendedorHelper.AñadirClaimsVendedor(identity, "   ", servicio);

            // Assert
            Assert.IsFalse(resultado);
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AñadirClaimsVendedor_NoSobrescribeClaimsExistentes()
        {
            // Arrange
            var identity = new ClaimsIdentity("JWT");
            identity.AddClaim(new Claim(ClaimTypes.Email, "usuario@test.com"));
            identity.AddClaim(new Claim(ClaimTypes.Name, "Usuario Test"));

            var servicio = A.Fake<IServicioUsuarioVendedor>();
            A.CallTo(() => servicio.ObtenerVendedorDeUsuario("usuario@test.com"))
                .Returns("NV");

            // Act
            ClaimsVendedorHelper.AñadirClaimsVendedor(identity, "usuario@test.com", servicio);

            // Assert - Claims originales siguen existiendo
            Assert.AreEqual("usuario@test.com", identity.FindFirst(ClaimTypes.Email)?.Value);
            Assert.AreEqual("Usuario Test", identity.FindFirst(ClaimTypes.Name)?.Value);
            // Y los nuevos también
            Assert.AreEqual("true", identity.FindFirst("IsVendedor")?.Value);
            Assert.AreEqual("NV", identity.FindFirst("Vendedor")?.Value);
            // Total de 4 claims
            Assert.AreEqual(4, identity.Claims.Count());
        }

        #endregion
    }
}
