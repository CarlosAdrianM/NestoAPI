using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#534 (corte 1): el job nace APAGADO y, en modo sombra, solo escribe a administración.
    /// También el texto cordial del aviso que recibirá el cliente en el corte 2.
    /// </summary>
    [TestClass]
    public class AvisosFacturasVencidasJobsServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);

        private ILectorParametrosUsuario lector;
        private IServicioCorreoElectronico correo;
        private List<MailMessage> enviados;
        private int vecesCalculado;
        private int diasPedidos;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            correo = A.Fake<IServicioCorreoElectronico>();
            enviados = new List<MailMessage>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviados.Add(m))
                .Returns(true);
            vecesCalculado = 0;
            diasPedidos = 0;
        }

        private void Parametro(string clave, string valor)
            => A.CallTo(() => lector.LeerParametro("1", "(defecto)", clave)).Returns(valor);

        private Task<List<AvisoFacturaVencidaDTO>> Calcular(int dias, DateTime hoy, List<AvisoFacturaVencidaDTO> resultado)
        {
            vecesCalculado++;
            diasPedidos = dias;
            return Task.FromResult(resultado);
        }

        private static AvisoFacturaVencidaDTO Aviso(string factura = "NV2612000", string motivo = null,
            string destinatarios = "cobros@ana.es", string nombre = "PELUQUERÍA ANA")
            => new AvisoFacturaVencidaDTO
            {
                NOrden = 1,
                Cliente = "15191",
                Contacto = "0",
                Nombre = nombre,
                Factura = factura,
                FechaFactura = new DateTime(2026, 8, 14),
                Vencimiento = new DateTime(2026, 9, 13),
                Importe = 1234.5m,
                DiasVencida = 11,
                Destinatarios = destinatarios,
                Motivo = motivo
            };

        private Task<ModoAvisoFacturasVencidas> Ejecutar(List<AvisoFacturaVencidaDTO> resultado)
            => AvisosFacturasVencidasJobsService.Procesar(lector, correo,
                (d, h) => Calcular(d, h, resultado), () => "ES06 2100 6273 9002 0006 3554", HOY);

        [TestMethod]
        public async Task Procesar_SinParametro_EstaApagadoYNoHaceNada()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, null);

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, modo);
            Assert.AreEqual(0, vecesCalculado, "Apagado ni siquiera consulta la BD");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_ValoresNoReconocidos_EstanApagados()
        {
            foreach (string valor in new[] { "0", "", "1", "Activo", "sombras" })
            {
                Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, valor);
                Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() }), valor);
            }
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_SiFallaLaLecturaDelParametro_QuedaApagado()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("BD caída"));

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, modo);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_EnSombra_MandaUnSoloCorreoYSoloAAdministracion()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, " sombra ");

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(),
                Aviso(factura: "NV2612001", destinatarios: "otro@cliente.es", motivo: "No se avisa. Retenido: envío INCIDENTADO")
            });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Sombra, modo);
            MailMessage mail = enviados.Single();
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.To.Single().Address);
            Assert.AreEqual(0, mail.CC.Count);
            Assert.AreEqual(0, mail.Bcc.Count);
            StringAssert.Contains(mail.Subject, "[Sombra]");
            StringAssert.Contains(mail.Subject, "1 se avisarían");
            StringAssert.Contains(mail.Body, "NV2612000");
            StringAssert.Contains(mail.Body, "NV2612001");
            StringAssert.Contains(mail.Body, "INCIDENTADO");
            StringAssert.Contains(mail.Body, "cobros@ana.es");
            StringAssert.Contains(mail.Body, "Factura NV2612000 pendiente de pago", "Lleva el correo de muestra del primero");
        }

        [TestMethod]
        public async Task Procesar_EnSombraSinCandidatos_MandaIgualElCorreoParaSaberQueCorre()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");

            await Ejecutar(new List<AvisoFacturaVencidaDTO>());

            StringAssert.Contains(enviados.Single().Body, "Hoy no se avisaría a nadie");
        }

        [TestMethod]
        public async Task Procesar_DiasDelParametro_SinParametroCinco()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, "8");
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(8, diasPedidos);

            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, null);
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(5, diasPedidos);

            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, "-3");
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(5, diasPedidos);
        }

        [TestMethod]
        public void NormalizarIban_VariasCuentasEnUnaFrase()
        {
            Assert.AreEqual("ES06 2100 A o ES91 0049 B",
                AvisosFacturasVencidasJobsService.NormalizarIban("ES06 2100 A\r\nES91 0049 B\r\n"));
            Assert.IsNull(AvisosFacturasVencidasJobsService.NormalizarIban("  "));
        }

        [TestMethod]
        public void Plantilla_TextoCordialConLosDatosDeLaFactura()
        {
            string texto = PlantillaAvisoFacturaVencida.CuerpoTexto(Aviso(), "ES06 2100 6273 9002 0006 3554");

            StringAssert.StartsWith(texto, "Hola, PELUQUERÍA ANA:");
            StringAssert.Contains(texto, "la factura NV2612000, del 14/08/2026, por importe de 1.234,50 €");
            StringAssert.Contains(texto, "desde su vencimiento el 13/09/2026");
            StringAssert.Contains(texto, "Si ya has hecho la transferencia, no hace falta que hagas nada");
            StringAssert.Contains(texto, "a la cuenta ES06 2100 6273 9002 0006 3554 indicando el número de factura en el concepto");
            StringAssert.Contains(texto, "responde a este correo y lo vemos");
            Assert.IsTrue(texto.EndsWith("Administración, Nueva Visión"));
            Assert.AreEqual("Factura NV2612000 pendiente de pago", PlantillaAvisoFacturaVencida.Asunto(Aviso()));
        }

        [TestMethod]
        public void Plantilla_SinNombreNiCuenta_NoDejaHuecos()
        {
            string texto = PlantillaAvisoFacturaVencida.CuerpoTexto(Aviso(nombre: " "), null);

            StringAssert.StartsWith(texto, "Hola:");
            StringAssert.Contains(texto, "a la cuenta de Nueva Visión que figura en la factura");
        }

        [TestMethod]
        public void Plantilla_Html_EscapaLosDatos()
        {
            string html = PlantillaAvisoFacturaVencida.CuerpoHtml(Aviso(nombre: "A & B <S.L.>"), "ES06");

            StringAssert.Contains(html, "Hola, A &amp; B &lt;S.L.&gt;:");
            Assert.IsFalse(html.Contains("<S.L.>"));
            StringAssert.StartsWith(html, "<p>");
        }
    }
}
