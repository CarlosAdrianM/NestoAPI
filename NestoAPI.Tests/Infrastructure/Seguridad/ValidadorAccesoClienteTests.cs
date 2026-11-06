using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Seguridad;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure.Seguridad
{
    [TestClass]
    public class ValidadorAccesoClienteTests
    {
        private const string CLIENTE_SOLICITADO = "15191";

        #region Empleados (IsEmployee)

        [TestMethod]
        public void ValidarAcceso_Empleado_PermiteAccesoATodoCliente()
        {
            // Arrange
            var identity = CrearIdentidadEmpleado();

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO);

            // Assert
            Assert.IsTrue(resultado.Autorizado);
            Assert.AreEqual("Empleado autorizado", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_Empleado_PermiteAccesoACualquierCliente()
        {
            // Arrange
            var identity = CrearIdentidadEmpleado();

            // Act - Diferentes clientes
            var resultado1 = ValidadorAccesoCliente.ValidarAcceso(identity, "00001");
            var resultado2 = ValidadorAccesoCliente.ValidarAcceso(identity, "99999");

            // Assert
            Assert.IsTrue(resultado1.Autorizado);
            Assert.IsTrue(resultado2.Autorizado);
        }

        #endregion

        #region Clientes (TiendasNuevaVision)

        [TestMethod]
        public void ValidarAcceso_ClienteTiendaOnline_PermiteAccesoASuPropioCliente()
        {
            // Arrange
            var identity = CrearIdentidadCliente("15191");

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, "15191");

            // Assert
            Assert.IsTrue(resultado.Autorizado);
            Assert.AreEqual("Cliente accediendo a sus propios recursos", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_ClienteTiendaOnline_DeniegaAccesoAOtroCliente()
        {
            // Arrange
            var identity = CrearIdentidadCliente("15191");

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, "99999");

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual("Cliente no puede acceder a recursos de otro cliente", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_ClienteTiendaOnline_ComparaClientesSinEspacios()
        {
            // Arrange - El claim puede tener espacios
            var identity = CrearIdentidadCliente("15191  ");

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, "  15191");

            // Assert
            Assert.IsTrue(resultado.Autorizado);
        }

        #endregion

        #region Vendedores (NestoApp)

        [TestMethod]
        public void ValidarAcceso_Vendedor_DeniegaAccesoPorAhora()
        {
            // Arrange
            var identity = CrearIdentidadVendedor("NV");

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO);

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.IsTrue(resultado.Motivo.Contains("Vendedor NV"));
            Assert.IsTrue(resultado.Motivo.Contains("FUTURO"));
        }

        [TestMethod]
        public void ValidarAcceso_VendedorConComprobador_ClienteSuyo_Permitido()
        {
            var identity = CrearIdentidadVendedor("NV");
            string vendedorVisto = null, clienteVisto = null;

            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, " 15191 ", (v, c) => { vendedorVisto = v; clienteVisto = c; return true; });

            Assert.IsTrue(resultado.Autorizado);
            Assert.AreEqual("NV", vendedorVisto);
            Assert.AreEqual("15191", clienteVisto, "llega sin espacios");
        }

        [TestMethod]
        public void ValidarAcceso_VendedorConComprobador_ClienteAjeno_Denegado()
        {
            var identity = CrearIdentidadVendedor("NV");

            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO, (v, c) => false);

            Assert.IsFalse(resultado.Autorizado);
            StringAssert.Contains(resultado.Motivo, "no es suyo");
        }

        [TestMethod]
        public void ValidarAcceso_VendedorSinComprobador_DeniegaComoSiempre()
        {
            var identity = CrearIdentidadVendedor("NV");

            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO, null);

            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual(ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO).Motivo, resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_VendedorSinCodigo_DenegadoSinPreguntar()
        {
            var identity = new ClaimsIdentity("JWT");
            identity.AddClaim(new Claim("IsVendedor", "true"));
            bool preguntado = false;

            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO, (v, c) => preguntado = true);

            Assert.IsFalse(resultado.Autorizado);
            Assert.IsFalse(preguntado);
        }

        [TestMethod]
        public void ValidarAcceso_EmpleadoConComprobador_PermitidoSinPreguntar()
        {
            bool preguntado = false;

            var resultado = ValidadorAccesoCliente.ValidarAcceso(CrearIdentidadEmpleado(), CLIENTE_SOLICITADO, (v, c) => preguntado = true);

            Assert.IsTrue(resultado.Autorizado);
            Assert.IsFalse(preguntado);
        }

        [TestMethod]
        public void ValidarAcceso_ClienteTiendaConComprobador_ComoSiempre()
        {
            bool preguntado = false;
            var identity = CrearIdentidadCliente("15191");

            var suyo = ValidadorAccesoCliente.ValidarAcceso(identity, "15191", (v, c) => preguntado = true);
            var otro = ValidadorAccesoCliente.ValidarAcceso(identity, "99999", (v, c) => preguntado = true);

            Assert.IsTrue(suyo.Autorizado);
            Assert.IsFalse(otro.Autorizado);
            Assert.AreEqual("Cliente no puede acceder a recursos de otro cliente", otro.Motivo);
            Assert.IsFalse(preguntado);
        }

        #endregion

        #region Casos de error

        [TestMethod]
        public void ValidarAcceso_IdentityNull_DeniegaAcceso()
        {
            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(null, CLIENTE_SOLICITADO);

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual("Usuario no autenticado", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_ClienteSolicitadoNull_DeniegaAcceso()
        {
            // Arrange
            var identity = CrearIdentidadEmpleado();

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, null);

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual("Cliente no especificado", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_ClienteSolicitadoVacio_DeniegaAcceso()
        {
            // Arrange
            var identity = CrearIdentidadEmpleado();

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, "   ");

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual("Cliente no especificado", resultado.Motivo);
        }

        [TestMethod]
        public void ValidarAcceso_UsuarioSinClaimReconocido_DeniegaAcceso()
        {
            // Arrange - Usuario autenticado pero sin claims específicos
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Name, "usuario@test.com"));

            // Act
            var resultado = ValidadorAccesoCliente.ValidarAcceso(identity, CLIENTE_SOLICITADO);

            // Assert
            Assert.IsFalse(resultado.Autorizado);
            Assert.AreEqual("Usuario sin permisos suficientes", resultado.Motivo);
        }

        #endregion

        #region Helpers

        private ClaimsIdentity CrearIdentidadEmpleado()
        {
            var identity = new ClaimsIdentity("JWT");
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\usuario"));
            identity.AddClaim(new Claim("IsEmployee", "true"));
            return identity;
        }

        private ClaimsIdentity CrearIdentidadCliente(string codigoCliente)
        {
            var identity = new ClaimsIdentity("JWT");
            identity.AddClaim(new Claim(ClaimTypes.Email, "cliente@test.com"));
            identity.AddClaim(new Claim("cliente", codigoCliente));
            return identity;
        }

        private ClaimsIdentity CrearIdentidadVendedor(string codigoVendedor)
        {
            var identity = new ClaimsIdentity("JWT");
            identity.AddClaim(new Claim(ClaimTypes.Name, "vendedor@test.com"));
            identity.AddClaim(new Claim("IsVendedor", "true"));
            identity.AddClaim(new Claim("Vendedor", codigoVendedor));
            return identity;
        }

        #endregion
    }
}
