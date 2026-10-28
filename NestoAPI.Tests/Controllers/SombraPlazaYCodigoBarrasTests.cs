using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): sombra de la plaza y el código de barras en el PUT api/EnviosAgencias (el que manda
    /// Nesto tras insertar, cuando el código pasa de vacío a un valor). Si difiere, PlazaCodigoBarrasDifiereInfo a ELMAH;
    /// nunca rompe el PUT; con GLS no se pide la plaza (no se llama a GLS en el PUT).
    /// </summary>
    [TestClass]
    public class SombraPlazaYCodigoBarrasTests
    {
        private IClientePlazaGls clienteGls;
        private EnviosAgenciasController controller;
        private List<Exception> registrados;
        private Dictionary<int, AgenciaTransporte> agencias;

        [TestInitialize]
        public void Setup()
        {
            clienteGls = A.Fake<IClientePlazaGls>();
            registrados = new List<Exception>();
            agencias = new Dictionary<int, AgenciaTransporte>
            {
                { 1, new AgenciaTransporte { Numero = 1, Nombre = "ASM", PrefijoCodigoBarras = "6108352" } },
                { 8, new AgenciaTransporte { Numero = 8, Nombre = "Correos Express", PrefijoCodigoBarras = "1234" } },
                { 13, new AgenciaTransporte { Numero = 13, Nombre = "CTT" } }
            };
            controller = new EnviosAgenciasController(A.Fake<NVEntities>())
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null), // NestoAPI#698: sin BD en los tests
                PlazasAgencias = RegistroPlazasAgencias.PorDefecto(clienteGls, ex => registrados.Add(ex)),
                CargarAgenciaTransporte = numero => Task.FromResult(agencias.TryGetValue(numero, out AgenciaTransporte a) ? a : null),
                RegistrarEnElmah = ex => registrados.Add(ex)
            };
        }

        private static EnviosAgencia Envio(int agencia, short servicio, string codigoBarras) => new EnviosAgencia
        {
            Numero = 246998, Empresa = "1  ", Agencia = agencia, Pedido = 927700, Estado = Constantes.Agencias.ESTADO_EN_CURSO,
            Servicio = servicio, CodPostal = "28001", Pais = 34, CodigoBarras = codigoBarras
        };

        [TestMethod]
        public async Task Gls_CodigoComoElDelServidor_NoRegistraNadaNiLlamaAGls()
        {
            EnviosAgencia envio = Envio(1, 96, "61197140246998");

            await controller.SombraPlazaYCodigoBarras(envio, existia: true, codigoBarrasAnterior: null);

            Assert.AreEqual(0, registrados.Count, registrados.Count > 0 ? registrados[0].Message : null);
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Gls_CodigoDistinto_RegistraLosDosValores()
        {
            // Nesto#412: el envío con el prefijo de otra agencia.
            EnviosAgencia envio = Envio(1, 1, "90246998");

            await controller.SombraPlazaYCodigoBarras(envio, existia: true, codigoBarrasAnterior: "  ");

            Assert.AreEqual(1, registrados.Count);
            Assert.IsInstanceOfType(registrados[0], typeof(PlazaCodigoBarrasDifiereInfo));
            StringAssert.Contains(registrados[0].Message, "Envío 246998 (agencia 1, pedido 927700)");
            StringAssert.Contains(registrados[0].Message, "código de barras: Nesto=90246998 / servidor=61083520246998");
            Assert.IsFalse(registrados[0].Message.Contains("nemónico"), "Con GLS no se compara la plaza");
        }

        [TestMethod]
        public async Task CorreosExpress_YaNoSoportada_NoComparaNada()
        {
            // NestoAPI#595 (20/10/26): CEX se quitó; un PUT de un envío antiguo no registra nada.
            EnviosAgencia envio = Envio(8, 93, "9312340002469982");

            await controller.SombraPlazaYCodigoBarras(envio, existia: true, codigoBarrasAnterior: null);

            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Ctt_SinCodigo_ComparaSoloLaPlaza()
        {
            EnviosAgencia envio = Envio(13, 48, string.Empty);
            envio.Nemonico = "CT ";
            envio.NombrePlaza = "CTT";

            await controller.SombraPlazaYCodigoBarras(envio, existia: true, codigoBarrasAnterior: null);
            Assert.AreEqual(0, registrados.Count, registrados.Count > 0 ? registrados[0].Message : null);

            envio.Nemonico = "MAD";
            await controller.SombraPlazaYCodigoBarras(envio, existia: true, codigoBarrasAnterior: null);
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "nemónico: Nesto=MAD / servidor=CT");
        }

        [TestMethod]
        public async Task NoEsElPutQuePoneElCodigo_NoCompara()
        {
            // Ya tenía código, el envío es nuevo (no existía), no está en curso o GLS aún sin código: nada.
            await controller.SombraPlazaYCodigoBarras(Envio(1, 1, "90246998"), existia: true, codigoBarrasAnterior: "61083520246998");
            await controller.SombraPlazaYCodigoBarras(Envio(1, 1, "90246998"), existia: false, codigoBarrasAnterior: null);
            EnviosAgencia tramitado = Envio(1, 1, "90246998");
            tramitado.Estado = Constantes.Agencias.ESTADO_TRAMITADO;
            await controller.SombraPlazaYCodigoBarras(tramitado, existia: true, codigoBarrasAnterior: null);
            await controller.SombraPlazaYCodigoBarras(Envio(1, 1, null), existia: true, codigoBarrasAnterior: null);

            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task AgenciaNoSoportada_NoCompara()
        {
            await controller.SombraPlazaYCodigoBarras(Envio(Constantes.Agencias.AGENCIA_GLOVO, 0, "LO-QUE-SEA"), existia: true, codigoBarrasAnterior: null);

            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task SiFallaLaComparacion_SeRegistraYNoLanza()
        {
            controller.CargarAgenciaTransporte = numero => throw new InvalidOperationException("BD caída");

            await controller.SombraPlazaYCodigoBarras(Envio(1, 1, "90246998"), existia: true, codigoBarrasAnterior: null);

            Assert.AreEqual(1, registrados.Count);
            Assert.IsNotInstanceOfType(registrados[0], typeof(PlazaCodigoBarrasDifiereInfo));
            StringAssert.Contains(registrados[0].Message, "BD caída");
        }

        [TestMethod]
        public void Comparador_SinPrefijoEnLaAgencia_LoDice()
        {
            List<string> diferencias = ComparadorPlazaYCodigoBarras.Diferencias(Envio(1, 1, "90246998"), compararCodigo: true, codigoEsperado: null, plazaEsperada: null);

            CollectionAssert.AreEqual(new List<string> { "código de barras: Nesto=90246998 / servidor=(sin prefijo en la agencia)" }, diferencias);
        }
    }
}
