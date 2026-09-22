using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// odoo-custom-addons#21 / #23 (punto 4): en Nesto viejo se usaron "0" y "1" como «sin código de
    /// barras». Hacia la tienda y Odoo tienen que viajar como vacío: un EAN de un dígito rompe la
    /// unicidad del código en Odoo y descarta el producto entero.
    /// </summary>
    [TestClass]
    public class NormalizarCodigoBarrasTests
    {
        [DataTestMethod]
        [DataRow("0")]
        [DataRow("1")]
        [DataRow(" 1 ")]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(null)]
        public void LosRellenosYElVacio_ViajanComoNull(string codigo)
        {
            Assert.IsNull(Constantes.Productos.NormalizarCodigoBarras(codigo));
        }

        [TestMethod]
        public void UnEanReal_ViajaRecortado()
        {
            Assert.AreEqual("4779044932412", Constantes.Productos.NormalizarCodigoBarras(" 4779044932412 "));
        }

        [TestMethod]
        public void UnCodigoCortoQueNoEsRelleno_SeRespeta()
        {
            // Solo "0" y "1" son relleno; "10" o "01" son códigos (raros, pero no nuestros para decidir).
            Assert.AreEqual("10", Constantes.Productos.NormalizarCodigoBarras("10"));
            Assert.AreEqual("01", Constantes.Productos.NormalizarCodigoBarras("01"));
        }
    }
}
