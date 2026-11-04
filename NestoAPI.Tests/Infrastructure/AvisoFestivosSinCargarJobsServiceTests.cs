using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Festivos;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>NestoAPI#779: aviso a Dirección si el calendario no tiene festivos del año que toca (en diciembre, el siguiente).</summary>
    [TestClass]
    public class AvisoFestivosSinCargarJobsServiceTests
    {
        private IServicioNotificacionesPush notificaciones;
        private IServicioCorreoElectronico correo;
        private List<int> aniosConsultados;

        [TestInitialize]
        public void Preparar()
        {
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            correo = A.Fake<IServicioCorreoElectronico>();
            aniosConsultados = new List<int>();
        }

        private AvisoFestivosSinCargarJobsService Servicio(bool hay) => new AvisoFestivosSinCargarJobsService(
            anio => { aniosConsultados.Add(anio); return hay; },
            () => new List<string> { "Carlos", @"NUEVAVISION\Ana", "carlos" },
            notificaciones, correo);

        [DataTestMethod]
        [DataRow(2026, 12, 2027, DisplayName = "1 de diciembre: el año siguiente")]
        [DataRow(2027, 1, 2027, DisplayName = "1 de enero: el año en curso")]
        [DataRow(2027, 6, 2027, DisplayName = "1 de junio: el año en curso")]
        public void AnioAComprobar(int anio, int mes, int esperado)
        {
            Assert.AreEqual(esperado, AvisoFestivosSinCargarJobsService.AnioAComprobar(new DateTime(anio, mes, 1)));
        }

        [TestMethod]
        public async Task SinFestivosDelAnioSiguiente_AvisaADireccionPorCampanaYCorreo()
        {
            int avisados = await Servicio(hay: false).ComprobarAsync(new DateTime(2026, 12, 1));

            Assert.AreEqual(2, avisados, "Cada usuario una vez, con el dominio");
            CollectionAssert.AreEqual(new[] { 2027 }, aniosConsultados);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisoFestivosSinCargarJobsService.TIPO_NOTIFICACION && n.Titulo.Contains("2027"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Ana", A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m =>
                m.To.Count == 1 && m.To[0].Address == Constantes.Correos.CORREO_DIRECCION && m.Subject.Contains("2027"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConFestivos_NoAvisa()
        {
            int avisados = await Servicio(hay: true).ComprobarAsync(new DateTime(2026, 12, 1));

            Assert.AreEqual(0, avisados);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Notificacion_DeUsted()
        {
            NotificacionPushDTO n = AvisoFestivosSinCargarJobsService.CrearNotificacion(2027);

            StringAssert.Contains(n.Cuerpo, "Cárguelos");
            Assert.AreEqual("2027", n.Datos["anio"]);
        }
    }
}
