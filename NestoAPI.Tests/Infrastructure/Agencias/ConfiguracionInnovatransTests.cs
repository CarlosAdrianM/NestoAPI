using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Innovatrans/DataTrans: la clave de autenticación SOAP es el MD5 de la contraseña.
    /// </summary>
    [TestClass]
    public class ConfiguracionInnovatransTests
    {
        [TestMethod]
        public void CalcularClave_DeUnaPassword_DevuelveSuMD5HexEnMinusculas()
        {
            // Vector MD5 estándar: MD5("abc") = 900150983cd24fb0d6963f7d28e17f72.
            Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", ConfiguracionInnovatrans.CalcularClave("abc"));
        }

        [TestMethod]
        public void CalcularClave_DevuelveSiempre32CaracteresHex()
        {
            string clave = ConfiguracionInnovatrans.CalcularClave("una-password-cualquiera-mas-larga");

            Assert.AreEqual(32, clave.Length);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(clave, "^[0-9a-f]{32}$"), clave);
        }

        [TestMethod]
        public void CalcularClave_PasswordVaciaONula_DevuelveVacio()
        {
            Assert.AreEqual(string.Empty, ConfiguracionInnovatrans.CalcularClave(""));
            Assert.AreEqual(string.Empty, ConfiguracionInnovatrans.CalcularClave(null));
        }
    }
}
