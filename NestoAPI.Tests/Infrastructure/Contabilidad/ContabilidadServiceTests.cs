using System.Collections.Generic;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#231: el mapeo terminal TPV → usuario se lee de la tabla TerminalesUsuariosTPV
    /// (vía IRepositorioTerminalesTPV), con fallback al diccionario por defecto. Y el terminal de
    /// Paloma ya es el nuevo.
    /// </summary>
    [TestClass]
    public class ContabilidadServiceTests
    {
        private static ContabilidadService ConMapa(Dictionary<string, string> mapa)
        {
            var repositorio = A.Fake<IRepositorioTerminalesTPV>();
            A.CallTo(() => repositorio.LeerMapa()).Returns(mapa);
            return new ContabilidadService(repositorio);
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_ConMapaDeBD_DevuelveElUsuarioDeLaTabla()
        {
            var servicio = ConMapa(new Dictionary<string, string> { { "99999", "Nuevo Usuario" } });

            Assert.AreEqual("Nuevo Usuario", servicio.ObtenerUsuarioTerminal("99999"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_SinMapaDeBD_UsaDiccionarioPorDefecto()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_MapaVacio_UsaDiccionarioPorDefecto()
        {
            var servicio = ConMapa(new Dictionary<string, string>());

            Assert.AreEqual("Victoria", servicio.ObtenerUsuarioTerminal("91900804275"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_Paloma_UsaElTerminalNuevo()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual("Paloma", servicio.ObtenerUsuarioTerminal("91901505888"));
        }

        [TestMethod]
        public void ObtenerUsuarioTerminal_TerminalDesconocido_DevuelveVacio()
        {
            var servicio = ConMapa(null);

            Assert.AreEqual(string.Empty, servicio.ObtenerUsuarioTerminal("00000000000"));
        }
    }
}
