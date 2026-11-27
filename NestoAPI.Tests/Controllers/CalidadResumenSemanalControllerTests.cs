using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Infraestructure.Calidad.ResumenSemanal;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#914: POST api/Calidad/ResumenSemanal/Enviar (Dirección e Informática; ensayo por defecto).</summary>
    [TestClass]
    public class CalidadResumenSemanalControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27); // viernes
        private IServicioCorreoElectronico correo;
        private IRegistroCorreoResumenSemanal registro;
        private IServicioCalidadAlmacen almacen;

        [TestInitialize]
        public void Preparar()
        {
            ServicioCorreoResumenSemanal.ReiniciarAvisos();
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            registro = A.Fake<IRegistroCorreoResumenSemanal>();
            A.CallTo(() => registro.EnviadoEl(A<DateTime>._)).Returns(null);
            almacen = A.Fake<IServicioCalidadAlmacen>();
            A.CallTo(() => almacen.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new ResumenCalidadAlmacenDTO());
        }

        private CalidadResumenSemanalController Controller(string destinatario, params string[] roles)
        {
            IRepositorioTablero repo = A.Fake<IRepositorioTablero>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.LeerSemanas(A<string>._, A<TipoDuenoKpi?>._, A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new List<SemanaKpiGuardada>());
            A.CallTo(() => repo.LeerUmbrales(A<string>._)).Returns(new List<UmbralesKpi>());
            IServicioEncuestas encuestas = A.Fake<IServicioEncuestas>();
            A.CallTo(() => encuestas.SombraEntre(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new SimulacionEncuestasDTO());
            ILectorParametrosUsuario parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).Returns(destinatario);
            var servicio = new ServicioCorreoResumenSemanal(() => repo, () => almacen, () => encuestas, parametros, correo, () => registro,
                () => HOY.AddHours(10), ex => { });
            return new CalidadResumenSemanalController(() => servicio, () => HOY)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };
        }

        [DataTestMethod]
        [DataRow("Almacén")]
        [DataRow("Administración")]
        public void SinGrupo_403DeUsted(string rol)
        {
            var r = (NegotiatedContentResult<string>)Controller("alguien@ejemplo.test", rol).PostEnviar();

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(CalidadResumenSemanalController.MENSAJE_SIN_PERMISO, r.Content);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PorDefecto_EsEnsayoDeLaUltimaSemanaCerrada_YNoManda()
        {
            var r = Controller("alguien@ejemplo.test", "Dirección").PostEnviar() as OkNegotiatedContentResult<ResultadoCorreoResumenSemanalDTO>;

            Assert.IsNotNull(r);
            Assert.IsTrue(r.Content.Ensayo);
            Assert.AreEqual(new DateTime(2026, 11, 16), r.Content.Semana);
            Assert.AreEqual("Calidad: semana del 16 al 22/11/2026", r.Content.Asunto);
            StringAssert.StartsWith(r.Content.Html, "<!DOCTYPE html>");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SinEnsayo_LoMandaAlConfigurado()
        {
            var r = Controller("alguien@ejemplo.test", "Informática").PostEnviar(new DateTime(2026, 11, 18), ensayo: false)
                as OkNegotiatedContentResult<ResultadoCorreoResumenSemanalDTO>;

            Assert.IsNotNull(r);
            Assert.IsTrue(r.Content.Enviado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To.Count == 1 && m.To[0].Address == "alguien@ejemplo.test")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => registro.Apuntar(new DateTime(2026, 11, 16), "alguien@ejemplo.test", A<string>._, @"NUEVAVISION\Prueba")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void SinEnsayo_SinDestinatario_409()
        {
            var r = Controller(null, "Dirección").PostEnviar(ensayo: false) as NegotiatedContentResult<ResultadoCorreoResumenSemanalDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(HttpStatusCode.Conflict, r.StatusCode);
            Assert.AreEqual(EstadoCorreoResumenSemanal.SinDestinatario.ToString(), r.Content.Estado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SinEnsayo_YaMandada_409()
        {
            A.CallTo(() => registro.EnviadoEl(A<DateTime>._)).Returns(new DateTime(2026, 11, 23, 7, 30, 0));

            var r = Controller("alguien@ejemplo.test", "Dirección").PostEnviar(ensayo: false) as NegotiatedContentResult<ResultadoCorreoResumenSemanalDTO>;

            Assert.AreEqual(HttpStatusCode.Conflict, r.StatusCode);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SemanaSinAcabar_400()
        {
            Assert.IsInstanceOfType(Controller("alguien@ejemplo.test", "Dirección").PostEnviar(new DateTime(2026, 11, 24)), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller("alguien@ejemplo.test", "Dirección").PostEnviar(new DateTime(2024, 1, 1)), typeof(BadRequestErrorMessageResult));
        }
    }
}
