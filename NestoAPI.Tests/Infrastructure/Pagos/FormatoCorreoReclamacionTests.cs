using System.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Pagos;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    [TestClass]
    public class FormatoCorreoReclamacionTests
    {
        [TestMethod]
        public void ToXML_ConTodosLosCampos_GeneraXMLCompleto()
        {
            // Arrange — usar textos ASCII para evitar dependencia de codificación HtmlEncode
            var formato = new FormatoCorreoReclamacion
            {
                nombreComprador = "Juan Garcia",
                direccionComprador = "juan@test.com",
                subjectMailCliente = "Pago pendiente",
                textoLibre1 = "Utilice el boton para pagar"
            };

            // Act
            string resultado = formato.ToXML();

            // Assert
            Assert.IsTrue(resultado.StartsWith("<![CDATA["));
            Assert.IsTrue(resultado.EndsWith("]]>"));
            Assert.IsTrue(resultado.Contains("<nombreComprador>Juan Garcia</nombreComprador>"));
            Assert.IsTrue(resultado.Contains("<direccionComprador>juan@test.com</direccionComprador>"));
            Assert.IsTrue(resultado.Contains("<subjectMailCliente>Pago pendiente</subjectMailCliente>"));
            Assert.IsTrue(resultado.Contains("<textoLibre1>Utilice el boton para pagar</textoLibre1>"));
        }

        [TestMethod]
        public void ToXML_ConCamposVacios_OmiteCamposVacios()
        {
            // Arrange
            var formato = new FormatoCorreoReclamacion
            {
                nombreComprador = "Juan",
                direccionComprador = null,
                subjectMailCliente = "",
                textoLibre1 = "   "
            };

            // Act
            string resultado = formato.ToXML();

            // Assert
            Assert.IsTrue(resultado.Contains("<nombreComprador>Juan</nombreComprador>"));
            Assert.IsFalse(resultado.Contains("<direccionComprador>"));
            Assert.IsFalse(resultado.Contains("<subjectMailCliente>"));
            Assert.IsFalse(resultado.Contains("<textoLibre1>"));
        }

        [TestMethod]
        public void ToXML_ConCaracteresEspecialesHTML_CodificaCorrectamente()
        {
            // Arrange — verificar que <, > y & se codifican como entidades HTML
            var formato = new FormatoCorreoReclamacion
            {
                nombreComprador = "Pedro <Test> & Co",
                direccionComprador = null,
                subjectMailCliente = null,
                textoLibre1 = null
            };

            // Act
            string resultado = formato.ToXML();

            // Assert — HttpUtility.HtmlEncode codifica <, >, &
            Assert.IsTrue(resultado.Contains("&lt;"));
            Assert.IsTrue(resultado.Contains("&gt;"));
            Assert.IsTrue(resultado.Contains("&amp;"));
            // El texto original con < sin codificar NO debe aparecer
            Assert.IsFalse(resultado.Contains("<Test>"));
        }

        [TestMethod]
        public void ToXML_ConAcentos_UsaHtmlEncode()
        {
            // Arrange — verificar que los acentos se procesan con HtmlEncode
            var formato = new FormatoCorreoReclamacion
            {
                nombreComprador = "García",
                direccionComprador = null,
                subjectMailCliente = null,
                textoLibre1 = null
            };

            // Act
            string resultado = formato.ToXML();

            // Assert — el resultado debe coincidir con lo que produce HttpUtility.HtmlEncode
            string esperado = HttpUtility.HtmlEncode("García");
            Assert.IsTrue(resultado.Contains("<nombreComprador>" + esperado + "</nombreComprador>"));
        }

        [TestMethod]
        public void ToXML_SinCampos_DevuelveCDATAVacio()
        {
            // Arrange
            var formato = new FormatoCorreoReclamacion();

            // Act
            string resultado = formato.ToXML();

            // Assert
            Assert.AreEqual("<![CDATA[]]>", resultado);
        }
    }
}
