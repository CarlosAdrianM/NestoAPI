using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#494 (Carlos 28/09/26): en una recogida en origen de CTT el cliente tiene que pegar la
    /// etiqueta en el paquete: se la mandamos en PDF en el correo de «Pedido entregado a la agencia».
    /// </summary>
    [TestClass]
    public class RecogidaCTTCorreoTests
    {
        private static EnviosAgencia Recogida(string codigo = "0082800082809772578999", string email = "cliente@ejemplo.es") => new EnviosAgencia
        {
            Numero = 249200,
            Empresa = "1  ",
            Agencia = Constantes.Agencias.AGENCIA_CTT,
            Retorno = (byte)AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN,
            CodigoBarras = codigo,
            Email = email,
            Cliente = "29606",
            Pedido = 927115
        };

        [TestMethod]
        public void ExtraerPdf_DataComoObjeto_DevuelveLosBytes()
        {
            byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4 prueba");
            JToken json = JObject.Parse("{\"data\":{\"label_type_code\":\"PDF\",\"label\":\"" + Convert.ToBase64String(pdf) + "\"}}");

            CollectionAssert.AreEqual(pdf, AgenciaRemotaCTT.ExtraerPdf(json));
        }

        [TestMethod]
        public void ExtraerPdf_DataComoLista_DevuelveLosBytes()
        {
            byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4 lista");
            JToken json = JObject.Parse("{\"data\":[{\"label\":\"" + Convert.ToBase64String(pdf) + "\"}]}");

            CollectionAssert.AreEqual(pdf, AgenciaRemotaCTT.ExtraerPdf(json));
        }

        [TestMethod]
        public void ExtraerPdf_SinLabelOBase64Roto_Null()
        {
            Assert.IsNull(AgenciaRemotaCTT.ExtraerPdf(JObject.Parse("{\"data\":{\"thermal_label\":[\"^XA^XZ\"]}}")));
            Assert.IsNull(AgenciaRemotaCTT.ExtraerPdf(JObject.Parse("{\"data\":{\"label\":\"esto no es base64 !!\"}}")));
            Assert.IsNull(AgenciaRemotaCTT.ExtraerPdf(null));
        }

        [TestMethod]
        public void EsRecogidaEnOrigenCTT_SoloCTTConRetorno2YCodigo()
        {
            Assert.IsTrue(GestorEnviosAgencia.EsRecogidaEnOrigenCTT(Recogida()));

            EnviosAgencia conRetorno = Recogida();
            conRetorno.Retorno = (byte)AgenciaRemotaCTT.RETORNO_CON_RETORNO;
            Assert.IsFalse(GestorEnviosAgencia.EsRecogidaEnOrigenCTT(conRetorno), "Con retorno: el repartidor ya lleva la etiqueta");

            EnviosAgencia gls = Recogida();
            gls.Agencia = 1;
            Assert.IsFalse(GestorEnviosAgencia.EsRecogidaEnOrigenCTT(gls));

            Assert.IsFalse(GestorEnviosAgencia.EsRecogidaEnOrigenCTT(Recogida(codigo: null)));
        }

        [TestMethod]
        public void ParrafoRecogida_ConEtiqueta_PideImprimirlaYPegarla()
        {
            string html = GestorEnviosAgencia.ParrafoRecogida("CTT", llevaEtiqueta: true);

            StringAssert.Contains(html, "recoger el paquete");
            StringAssert.Contains(html, "Imprima la etiqueta que le adjuntamos en PDF y péguela en el paquete");
            Assert.IsFalse(html.Contains("ya hemos enviado su pedido"));
        }

        [TestMethod]
        public void ParrafoRecogida_SinEtiqueta_NoPrometeUnAdjunto()
        {
            string html = GestorEnviosAgencia.ParrafoRecogida("CTT", llevaEtiqueta: false);

            Assert.IsFalse(html.Contains("adjuntamos"));
            StringAssert.Contains(html, "le haremos llegar la etiqueta");
        }

        [TestMethod]
        public async Task EnviarCorreo_RecogidaConCorreoCambiadoEnElCuerpo_NoPideEtiquetaNiManda()
        {
            // El endpoint es anónimo: si el correo o el código no coinciden con la BD, no se manda nada.
            IAgenciaRemota agencia = A.Fake<IAgenciaRemota>(o => o.Implements<IEtiquetaPdfRemota>());
            var gestor = new GestorEnviosAgencia
            {
                CrearAgenciaRemota = _ => agencia,
                LeerEnvioDeBd = _ => Recogida(codigo: "0082800082809700000000")
            };

            await gestor.EnviarCorreoEntregaAgencia(Recogida(email: "otro@malicioso.es"));

            A.CallTo(() => ((IEtiquetaPdfRemota)agencia).ObtenerEtiquetaPdfAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarCorreo_RecogidaQueYaNoEsRecogidaEnBd_NoPideEtiqueta()
        {
            IAgenciaRemota agencia = A.Fake<IAgenciaRemota>(o => o.Implements<IEtiquetaPdfRemota>());
            EnviosAgencia enBd = Recogida();
            enBd.Retorno = 0;
            var gestor = new GestorEnviosAgencia
            {
                CrearAgenciaRemota = _ => agencia,
                LeerEnvioDeBd = _ => enBd
            };

            await gestor.EnviarCorreoEntregaAgencia(Recogida());

            A.CallTo(() => ((IEtiquetaPdfRemota)agencia).ObtenerEtiquetaPdfAsync(A<string>._)).MustNotHaveHappened();
        }
    }
}
