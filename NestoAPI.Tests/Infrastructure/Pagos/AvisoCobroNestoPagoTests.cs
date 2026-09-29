using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#565 (Carlos, 29/09/26): el cobro de un enlace de NestoPago, además del correo, avisa a quien
    /// creó el enlace en la campana de Nesto y en NestoApp.
    /// </summary>
    [TestClass]
    public class AvisoCobroNestoPagoTests
    {
        private static PagoTPV Pago(string usuario, string tipo = Constantes.TiposPagoTPV.TPV_VIRTUAL)
            => new PagoTPV
            {
                NumeroOrden = "B9BC22C32366",
                Tipo = tipo,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Importe = 1234.5M,
                Descripcion = "FRA. NV26/001234",
                Usuario = usuario,
                Estado = Constantes.EstadosPagoTPV.AUTORIZADO
            };

        [TestMethod]
        public void Destinatario_EmpleadoConOSinDominio_VendedorDeLaApp()
        {
            Assert.AreEqual("Paloma", AvisoCobroNestoPago.Destinatario("NUEVAVISION\\Paloma"));
            Assert.AreEqual("Manuel", AvisoCobroNestoPago.Destinatario("Manuel"));
            Assert.AreEqual("Sancho", AvisoCobroNestoPago.Destinatario(" nuevavision\\Sancho "));
        }

        [TestMethod]
        public void Destinatario_EnlacesCreadosPorClientes_NoSeAvisaANadie()
        {
            Assert.IsNull(AvisoCobroNestoPago.Destinatario("Info@esteticaeleden.com"));
            Assert.IsNull(AvisoCobroNestoPago.Destinatario("APP\\15191"));
            Assert.IsNull(AvisoCobroNestoPago.Destinatario("15191"));
            Assert.IsNull(AvisoCobroNestoPago.Destinatario(null));
            Assert.IsNull(AvisoCobroNestoPago.Destinatario("  "));
        }

        [TestMethod]
        public void ComponerNotificacion_EnlaceDePago_TituloCuerpoYDatos()
        {
            NotificacionPushDTO n = AvisoCobroNestoPago.ComponerNotificacion(Pago("NUEVAVISION\\Paloma"));

            Assert.AreEqual("Cobro NestoPago: 1.234,50 €", n.Titulo);
            Assert.AreEqual("El cliente 15191 ha pagado el enlace (FRA. NV26/001234).", n.Cuerpo);
            Assert.AreEqual(AvisoCobroNestoPago.TIPO_NOTIFICACION, n.Tipo);
            Assert.AreEqual("CobroNestoPago", n.Datos["tipo"]);
            Assert.AreEqual("1", n.Datos["empresa"]);
            Assert.AreEqual("15191", n.Datos["cliente"]);
            Assert.AreEqual("B9BC22C32366", n.Datos["numeroOrden"]);
            Assert.AreEqual("1234.5", n.Datos["importe"]);
        }

        [TestMethod]
        public void ComponerNotificacion_ConOtroContactoYErrorDeContabilizacion()
        {
            PagoTPV pago = Pago("Manuel");
            pago.Contacto = "2";

            NotificacionPushDTO n = AvisoCobroNestoPago.ComponerNotificacion(pago, "Falta la cuenta");

            StringAssert.StartsWith(n.Cuerpo, "El cliente 15191/2 ha pagado");
            StringAssert.Contains(n.Cuerpo, "No se ha podido contabilizar");
        }

        [TestMethod]
        public void ComponerNotificacion_CobrosDeLaTiendaYAltaDeTarjeta_NoAvisan()
        {
            Assert.IsNull(AvisoCobroNestoPago.ComponerNotificacion(Pago("NUEVAVISION\\Paloma", Constantes.TiposPagoTPV.PEDIDO_APP)));
            Assert.IsNull(AvisoCobroNestoPago.ComponerNotificacion(Pago("APP\\15191", Constantes.TiposPagoTPV.CARRITO_APP)));
            Assert.IsNull(AvisoCobroNestoPago.ComponerNotificacion(Pago("15191", Constantes.TiposPagoTPV.ALTA_TARJETA)));
            Assert.IsNull(AvisoCobroNestoPago.ComponerNotificacion(Pago("Info@esteticaeleden.com")));
        }

        [TestMethod]
        public async Task Avisar_VaALaCampanaDeNestoYALaPushDeNestoApp()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            NotificacionPushDTO n = AvisoCobroNestoPago.ComponerNotificacion(Pago("NUEVAVISION\\Paloma"));

            await AvisoCobroNestoPago.Avisar("NUEVAVISION\\Paloma", n, notificaciones);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Paloma", Constantes.Aplicaciones.NESTO, n)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAUsuario("Paloma", Constantes.Aplicaciones.NESTO_APP, n)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_SiFallaLaCampana_LaPushSaleIgualYNoLanza()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .ThrowsAsync(new InvalidOperationException("SignalR caído"));
            NotificacionPushDTO n = AvisoCobroNestoPago.ComponerNotificacion(Pago("Manuel"));

            await AvisoCobroNestoPago.Avisar("Manuel", n, notificaciones);

            A.CallTo(() => notificaciones.EnviarAUsuario("Manuel", Constantes.Aplicaciones.NESTO_APP, n)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ServicioPagos_AvisarCobroEnAplicaciones_UsaElServicioDeAvisos()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var servicio = new ServicioPagos(A.Fake<IRedsysService>(), A.Fake<NestoAPI.Infraestructure.Contabilidad.IContabilidadService>(),
                A.Fake<ILectorParametrosUsuario>(), A.Fake<IServicioCorreoElectronico>(), A.Fake<ILogService>())
            {
                CrearNotificaciones = () => notificaciones
            };

            await servicio.AvisarCobroEnAplicaciones(Pago("NUEVAVISION\\Paloma"));
            await servicio.AvisarCobroEnAplicaciones(Pago("APP\\15191", Constantes.TiposPagoTPV.CARRITO_APP));

            A.CallTo(() => notificaciones.EnviarAUsuario("Paloma", Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.EnviarAUsuario(A<string>.That.Not.IsEqualTo("Paloma"), A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }
    }
}
