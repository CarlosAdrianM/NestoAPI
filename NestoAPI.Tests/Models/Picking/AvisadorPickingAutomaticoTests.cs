using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Linq;
using System.Net.Mail;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#361: el picking de las 11h lo lanza una tarea del Task Scheduler, sin nadie
    /// mirando la pantalla. Antes, si no salía picking, el almacén no podía distinguir entre "no
    /// había nada", "ha fallado algo" y "la tarea ni se ejecutó", y acababa preguntando a
    /// Informática, que lo miraba en ELMAH. Estos tests fijan que el aviso separa esos casos.
    /// </summary>
    [TestClass]
    public class AvisadorPickingAutomaticoTests
    {
        private static readonly DateTime MOMENTO = new DateTime(2026, 8, 24, 11, 0, 0);

        private static NestoBusinessException SinTrabajo()
        {
            return new NestoBusinessException(
                "No hay stock suficiente para asignar picking a ninguna línea",
                new ErrorContext { ErrorCode = Constantes.Picking.ERROR_SIN_STOCK })
            {
                IsWarning = true
            };
        }

        [TestMethod]
        public void EsPickingSinTrabajo_ExcepcionDeSinStock_LaReconoce()
        {
            Assert.IsTrue(AvisadorPickingAutomatico.EsPickingSinTrabajo(SinTrabajo()));
        }

        [TestMethod]
        public void EsPickingSinTrabajo_CualquierOtroFallo_NoEsSinTrabajo()
        {
            // "La ubicación está descuadrada", un timeout, un NullReference... son fallos de verdad.
            Assert.IsFalse(AvisadorPickingAutomatico.EsPickingSinTrabajo(
                new Exception("La ubicación está descuadrada o el stock está en otra empresa")));
            Assert.IsFalse(AvisadorPickingAutomatico.EsPickingSinTrabajo(new NullReferenceException()));
            // Una excepción de negocio distinta tampoco: p. ej. el pedido que no existe (#398).
            Assert.IsFalse(AvisadorPickingAutomatico.EsPickingSinTrabajo(
                new NestoBusinessException("No existe el pedido 924645",
                    new ErrorContext { ErrorCode = "PICKING_PEDIDO_NO_EXISTE" })));
        }

        [TestMethod]
        public void Asunto_SinTrabajo_NoSuenaAAlarma()
        {
            string asunto = AvisadorPickingAutomatico.AsuntoPara(SinTrabajo());

            Assert.IsFalse(asunto.Contains("AVISO"), $"No es un fallo, no debe alarmar: '{asunto}'");
            Assert.IsTrue(asunto.Contains("no había nada"), asunto);
        }

        [TestMethod]
        public void Asunto_Fallo_SiSuenaAAlarma()
        {
            string asunto = AvisadorPickingAutomatico.AsuntoPara(new Exception("Ubicación descuadrada"));

            Assert.IsTrue(asunto.Contains("AVISO"), asunto);
            Assert.IsTrue(asunto.Contains("fallado"), asunto);
        }

        [TestMethod]
        public void Cuerpo_SinTrabajo_DiceQueLaTareaSiSeEjecuto()
        {
            // Esta es la información que hoy tiene que pedir el almacén a Informática.
            string cuerpo = AvisadorPickingAutomatico.CuerpoPara(SinTrabajo(), MOMENTO);

            Assert.IsTrue(cuerpo.Contains("24/08/2026 11:00"), cuerpo);
            Assert.IsTrue(cuerpo.Contains("no había ningún pedido"), cuerpo);
            Assert.IsTrue(cuerpo.Contains("No hay que hacer nada"), cuerpo);
        }

        [TestMethod]
        public void Cuerpo_Fallo_LlevaElMotivo()
        {
            string cuerpo = AvisadorPickingAutomatico.CuerpoPara(
                new Exception("La ubicación está descuadrada"), MOMENTO);

            Assert.IsTrue(cuerpo.Contains("La ubicación está descuadrada"), cuerpo);
            Assert.IsTrue(cuerpo.Contains("Informática"), cuerpo);
        }

        [TestMethod]
        public void Cuerpo_MotivoConHtml_SeEscapa()
        {
            // El mensaje de la excepción es texto ajeno: no puede romper el HTML del correo.
            string cuerpo = AvisadorPickingAutomatico.CuerpoPara(
                new Exception("Error en <producto> & \"almacén\""), MOMENTO);

            Assert.IsFalse(cuerpo.Contains("<producto>"), cuerpo);
            Assert.IsTrue(cuerpo.Contains("&lt;producto&gt;"), cuerpo);
        }

        [TestMethod]
        public void Destinatarios_SinParametro_VanAlAlmacen()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            var avisador = new AvisadorPickingAutomatico(A.Fake<IServicioCorreoElectronico>(), lector);

            CollectionAssert.AreEqual(new[] { Constantes.Correos.ALMACEN }, avisador.Destinatarios());
        }

        [TestMethod]
        public void Destinatarios_ConParametro_UsaLasDireccionesConfiguradas()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._,
                Constantes.ParametrosUsuario.CORREO_AVISO_PICKING_AUTOMATICO))
                .Returns("almacen@nuevavision.es; carlosadrian@nuevavision.es");
            var avisador = new AvisadorPickingAutomatico(A.Fake<IServicioCorreoElectronico>(), lector);

            string[] destinatarios = avisador.Destinatarios();

            Assert.AreEqual(2, destinatarios.Length);
            Assert.IsTrue(destinatarios.Any(d => d.Trim() == "carlosadrian@nuevavision.es"));
        }

        [TestMethod]
        public void Destinatarios_SiElParametroRevienta_NoSePierdeElAviso()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._))
                .Throws(new Exception("BD caída"));
            var avisador = new AvisadorPickingAutomatico(A.Fake<IServicioCorreoElectronico>(), lector);

            CollectionAssert.AreEqual(new[] { Constantes.Correos.ALMACEN }, avisador.Destinatarios());
        }

        [TestMethod]
        public void Avisar_MandaElCorreoAlDestinatarioConElAsuntoCorrecto()
        {
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            var avisador = new AvisadorPickingAutomatico(correo, lector);

            avisador.Avisar(SinTrabajo(), MOMENTO);

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m =>
                m.To.Any(d => d.Address == Constantes.Correos.ALMACEN)
                && m.Subject.Contains("no había nada")
                && m.IsBodyHtml))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Avisar_SiFallaElEnvio_NoLanza()
        {
            // Un fallo al avisar no puede tapar ni cambiar el resultado del picking.
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Throws(new Exception("SMTP caído"));
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            var avisador = new AvisadorPickingAutomatico(correo, lector);

            avisador.Avisar(SinTrabajo(), MOMENTO);
        }
    }
}
