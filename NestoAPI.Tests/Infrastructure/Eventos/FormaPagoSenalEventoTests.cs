using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// 29/10/26 (primera señal real, cliente 22709): el cobro era un enlace de tarjeta y la inscripción creada desde la señal
    /// decía «Transferencia». La forma de pago sale del apunte.
    /// </summary>
    [TestClass]
    public class FormaPagoSenalEventoTests
    {
        [TestMethod]
        public void FormaPagoDeLaSenal_SegunLaDelApunte()
        {
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ServicioInscripcionesEventos.FormaPagoDeLaSenal("TAR"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ServicioInscripcionesEventos.FormaPagoDeLaSenal(" tar "));
            Assert.AreEqual(FormaPagoInscripcionEvento.Efectivo, ServicioInscripcionesEventos.FormaPagoDeLaSenal("EFC"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Transferencia, ServicioInscripcionesEventos.FormaPagoDeLaSenal("TRN"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Transferencia, ServicioInscripcionesEventos.FormaPagoDeLaSenal(null));
        }
    }
}
