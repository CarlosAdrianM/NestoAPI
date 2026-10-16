using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// ELMAH 16/10/26 08:19: el fichero de tarjetas de Caixabank cargado con el botón de movimientos
    /// del banco llegaba a api/Bancos/CargarFichero y reventaba con un NullReferenceException (no hay
    /// registro 11 de cabecera). Ahora es un error de negocio que dice qué botón usar.
    /// </summary>
    [TestClass]
    public class LeerCuaderno43Tests
    {
        private const string FICHERO_TARJETAS =
            "00010137815100   151026\r" +
            "10329515704151020261510202621006273900200063554\r" +
            "201001151026001329515701510261304010130444BSCHESMMXXX-BANCO SANT000043990 0100%000000440 N000043550 978002244000414DNN\r" +
            "903295157041510202615102026000107847 000000769 000107078 000000000\r" +
            "3000000009\r";

        [TestMethod]
        public async Task LeerCuaderno43_FicheroDeTarjetas_DiceQueSeCargueConElBotonDeTarjetas()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => GestorContabilidad.LeerCuaderno43(FICHERO_TARJETAS));

            StringAssert.Contains(ex.Message, "tarjetas");
            StringAssert.Contains(ex.Message, "botón");
        }

        [TestMethod]
        public async Task LeerCuaderno43_FicheroDeTarjetasConSaltosWindows_TambienLoReconoce()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => GestorContabilidad.LeerCuaderno43(FICHERO_TARJETAS.Replace("\r", Environment.NewLine)));

            StringAssert.Contains(ex.Message, "fichero de tarjetas");
        }

        [TestMethod]
        public async Task LeerCuaderno43_FicheroSinCabecera_DaErrorDeNegocioGenerico()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => GestorContabilidad.LeerCuaderno43("hola\r\n\r\nesto no es un cuaderno 43\r\n"));

            StringAssert.Contains(ex.Message, "cuaderno 43");
            Assert.IsFalse(ex.Message.Contains("botón del fichero de tarjetas"));
        }
    }
}
