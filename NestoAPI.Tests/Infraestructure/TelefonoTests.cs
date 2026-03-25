using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;

namespace NestoAPI.Tests.Infraestructure
{
    [TestClass]
    public class TelefonoTests
    {
        [TestMethod]
        public void FijoUnico_ConFijoYMovil_DevuelveFijo()
        {
            var telefono = new Telefono("916001234/654321987");
            Assert.AreEqual("916001234", telefono.FijoUnico());
        }

        [TestMethod]
        public void MovilUnico_ConFijoYMovil_DevuelveMovil()
        {
            var telefono = new Telefono("916001234/654321987");
            Assert.AreEqual("654321987", telefono.MovilUnico());
        }

        [TestMethod]
        public void FijoUnico_SoloMovil_DevuelveVacio()
        {
            var telefono = new Telefono("654321987");
            Assert.AreEqual(string.Empty, telefono.FijoUnico());
        }

        [TestMethod]
        public void MovilUnico_SoloFijo_DevuelveVacio()
        {
            var telefono = new Telefono("916001234");
            Assert.AreEqual(string.Empty, telefono.MovilUnico());
        }

        [TestMethod]
        public void Telefono_ConEspaciosYParentesis_LimpiaCorrectamente()
        {
            var telefono = new Telefono("(91) 600-12 34 / 654 321 987");
            Assert.AreEqual("916001234", telefono.FijoUnico());
            Assert.AreEqual("654321987", telefono.MovilUnico());
        }

        [TestMethod]
        public void Telefono_Null_NoFalla()
        {
            var telefono = new Telefono(null);
            Assert.AreEqual(string.Empty, telefono.FijoUnico());
            Assert.AreEqual(string.Empty, telefono.MovilUnico());
        }

        [TestMethod]
        public void MovilUnico_Empieza7_LoDetecta()
        {
            var telefono = new Telefono("712345678");
            Assert.AreEqual("712345678", telefono.MovilUnico());
        }
    }
}
