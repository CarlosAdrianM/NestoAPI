using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Fix 06/11/26: el 03/11 el correo «Pedido entregado a la agencia» en segundo plano (CorreoEntregaAgenciaJobs) se cayó 6 veces
    /// con «The ObjectContext instance has been disposed»: EnvioAgenciaDTO tocaba envio.AgenciasTransporte de una entidad cuyo
    /// contexto ya estaba cerrado. El cuerpo del correo ya no navega el envío: la agencia llega leída aparte.
    /// </summary>
    [TestClass]
    public class CorreoEntregaAgenciaContextoCerradoTests
    {
        /// <summary>Como un proxy de EF con carga perezosa cuyo contexto ya se ha cerrado.</summary>
        private class EnvioDeContextoCerrado : EnviosAgencia
        {
            public override AgenciaTransporte AgenciasTransporte
            {
                get => throw new ObjectDisposedException(null, "The ObjectContext instance has been disposed and can no longer be used for operations that require a connection.");
                set { }
            }
        }

        private static AgenciaTransporte Gls() => new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_GLS, Nombre = "ASM", Identificador = "123" };

        private static T Rellenar<T>(T envio) where T : EnviosAgencia
        {
            envio.Numero = 4242;
            envio.Empresa = "1";
            envio.Agencia = Constantes.Agencias.AGENCIA_GLS;
            envio.Cliente = "15191";
            envio.Pedido = 987654;
            envio.CodigoBarras = "61771001234567";
            envio.CodPostal = "28110";
            envio.Fecha = new DateTime(2026, 11, 3);
            return envio;
        }

        [TestMethod]
        public void CuerpoCorreoEntrega_EnvioDeContextoCerrado_NoLanzaYSaleIgualQueConLaAgenciaCargada()
        {
            var cerrado = Rellenar(new EnvioDeContextoCerrado());
            var cargado = Rellenar(new EnviosAgencia());
            cargado.AgenciasTransporte = Gls();

            string cuerpoCerrado = GestorEnviosAgencia.CuerpoCorreoEntrega(cerrado, Gls(), false, false, "<pie/>").ToString();
            string cuerpoCargado = GestorEnviosAgencia.CuerpoCorreoEntrega(cargado, Gls(), false, false, "<pie/>").ToString();

            Assert.AreEqual(cuerpoCargado, cuerpoCerrado);
            StringAssert.Contains(cuerpoCerrado, "la agencia responsable de la entrega es ASM");
            StringAssert.Contains(cuerpoCerrado, "61771001234567");
            StringAssert.Contains(cuerpoCerrado, "<pie/>");
            Assert.IsFalse(cuerpoCerrado.Contains("error, agencia no definida"), "El enlace de seguimiento sale de la agencia leída aparte");
        }

        [TestMethod]
        public void CuerpoCorreoEntrega_RecogidaDeContextoCerrado_NoLanza()
        {
            var cerrado = Rellenar(new EnvioDeContextoCerrado());

            string cuerpo = GestorEnviosAgencia.CuerpoCorreoEntrega(cerrado, Gls(), true, true, "").ToString();

            StringAssert.Contains(cuerpo, "recoger el paquete");
        }

        [TestMethod]
        public void EnvioAgenciaDTO_ConLaAgenciaAparte_NoTocaLaNavegacion()
        {
            var dto = new EnvioAgenciaDTO(Rellenar(new EnvioDeContextoCerrado()), Gls());

            Assert.AreEqual("ASM", dto.AgenciaNombre);
            Assert.AreEqual("123", dto.AgenciaIdentificador);
        }

        [TestMethod]
        public void EnvioAgenciaDTO_SinAgenciaAparte_SigueLeyendoLaNavegacion()
        {
            var envio = Rellenar(new EnviosAgencia());
            envio.AgenciasTransporte = Gls();

            var dto = new EnvioAgenciaDTO(envio);

            Assert.AreEqual("ASM", dto.AgenciaNombre);
            Assert.AreEqual("123", dto.AgenciaIdentificador);
        }
    }
}
