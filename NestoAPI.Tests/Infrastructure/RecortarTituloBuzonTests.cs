using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>ELMAH 16/10/26: un título de aviso de más de 200 caracteres hacía fallar el guardado del buzón.</summary>
    [TestClass]
    public class RecortarTituloBuzonTests
    {
        [TestMethod]
        public void RecortarTitulo_MasDe200_SeQuedaEn200ConPuntosSuspensivos()
        {
            string largo = new string('a', 260);

            string recortado = ServicioNotificacionesPush.RecortarTitulo(largo);

            Assert.AreEqual(ServicioNotificacionesPush.LONGITUD_TITULO_BUZON, recortado.Length);
            Assert.IsTrue(recortado.EndsWith("…"));
        }

        [TestMethod]
        public void RecortarTitulo_CortoONulo_NoCambia()
        {
            Assert.AreEqual("Enrique ha comentado", ServicioNotificacionesPush.RecortarTitulo("Enrique ha comentado"));
            Assert.IsNull(ServicioNotificacionesPush.RecortarTitulo(null));
            string justo = new string('b', 200);
            Assert.AreEqual(justo, ServicioNotificacionesPush.RecortarTitulo(justo));
        }
    }
}
