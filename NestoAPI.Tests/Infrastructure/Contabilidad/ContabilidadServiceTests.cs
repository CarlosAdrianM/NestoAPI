using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#231: el mapeo terminal TPV → usuario se lee del parámetro TerminalesUsuariosTPV
    /// (JSON), con fallback al diccionario por defecto. Y el terminal de Paloma ya es el nuevo.
    /// </summary>
    [TestClass]
    public class ContabilidadServiceTests
    {
        private const string ParametroTerminales = "TerminalesUsuariosTPV";

        private static ContabilidadService ConParametro(string valor)
        {
            var lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, ParametroTerminales)).Returns(valor);
            return new ContabilidadService(lector);
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_ConParametroJson_DevuelveElUsuarioDelParametro()
        {
            var servicio = ConParametro("{\"99999\":\"Nuevo Usuario\"}");

            Assert.AreEqual("Nuevo Usuario", servicio.ObtenerUsuarioTerminal("99999"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_SinParametro_UsaDiccionarioPorDefecto()
        {
            var servicio = ConParametro(null);

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_ParametroNoParsea_UsaDiccionarioPorDefecto()
        {
            var servicio = ConParametro("esto no es json");

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_Paloma_UsaElTerminalNuevo()
        {
            var servicio = ConParametro(null);

            Assert.AreEqual("Paloma", servicio.ObtenerUsuarioTerminal("91901505888"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_TerminalDesconocido_DevuelveVacio()
        {
            var servicio = ConParametro(null);

            Assert.AreEqual(string.Empty, servicio.ObtenerUsuarioTerminal("00000000000"));
        }
    }
}
