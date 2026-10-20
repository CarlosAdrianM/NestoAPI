using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): plaza y código de barras en el servidor, equivalentes a los de Nesto
    /// (AgenciaASM, AgenciaCanteras, AgenciaGestionadaPorApi). Los casos de código de barras son los de
    /// ViewModels.Tests/AgenciaCodigoBarrasTests.vb. Nada llama a GLS: el transporte de GetPlazaXCP es un fake.
    /// </summary>
    [TestClass]
    public class PlazaYCodigoBarrasAgenciasTests
    {
        private IClientePlazaGls clienteGls;
        private List<Exception> registrados;
        private RegistroPlazasAgencias registro;

        [TestInitialize]
        public void Setup()
        {
            clienteGls = A.Fake<IClientePlazaGls>();
            registrados = new List<Exception>();
            registro = RegistroPlazasAgencias.PorDefecto(clienteGls, ex => registrados.Add(ex));
        }

        private static EnviosAgencia Envio(int numero, short servicio = 0) => new EnviosAgencia { Numero = numero, Servicio = servicio };

        // ------------------------------------------------------------ registro

        [TestMethod]
        public void Registro_EligePorNumeroDeAgencia()
        {
            Assert.IsInstanceOfType(registro.Para(Constantes.Agencias.AGENCIA_GLS), typeof(PlazaYCodigoBarrasGls));
            Assert.IsInstanceOfType(registro.Para(Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO), typeof(PlazaYCodigoBarrasGls));
            Assert.IsInstanceOfType(registro.Para(Constantes.Agencias.AGENCIA_CANTERAS), typeof(PlazaYCodigoBarrasFija));
            Assert.IsInstanceOfType(registro.Para(Constantes.Agencias.AGENCIA_INNOVATRANS), typeof(PlazaYCodigoBarrasFija));
            Assert.IsInstanceOfType(registro.Para(Constantes.Agencias.AGENCIA_CTT), typeof(PlazaYCodigoBarrasFija));
        }

        [TestMethod]
        public void Registro_AgenciaNoSoportada_ExcepcionClara()
        {
            Assert.IsFalse(registro.Soporta(Constantes.Agencias.AGENCIA_GLOVO));
            var ex = Assert.ThrowsException<AgenciaSinPlazaYCodigoBarrasException>(() => registro.Para(Constantes.Agencias.AGENCIA_GLOVO));
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLOVO, ex.Agencia);
            StringAssert.Contains(ex.Message, "no está soportada");
        }

        [TestMethod]
        public void Registro_DosImplementacionesParaLaMismaAgencia_NoSePermite()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RegistroPlazasAgencias(new IPlazaYCodigoBarrasAgencia[]
            {
                PlazaYCodigoBarrasFija.Ctt(), PlazaYCodigoBarrasFija.Ctt()
            }));
        }

        // ------------------------------------------------------------ GLS: código de barras

        [TestMethod]
        public void Gls_ServicioNormal_PrefijoDeLaAgenciaMasNumeroEnSieteCifras()
        {
            // AgenciaCodigoBarrasTests.vb: (246998, servicio 1, prefijo «9») → 90246998.
            Assert.AreEqual("90246998", registro.Para(1).CalcularCodigoBarras(Envio(246998, 1), "9"));
        }

        [TestMethod]
        public void Gls_Servicio96BusinessParcel_SuPropioPrefijo()
        {
            Assert.AreEqual("61197140246998", registro.Para(1).CalcularCodigoBarras(Envio(246998, 96), "9"));
            Assert.AreEqual("61197140246998", registro.Para(5).CalcularCodigoBarras(Envio(246998, 96), "6108352"));
        }

        [TestMethod]
        public void Gls_SinPrefijo_Null_ComoTramitadorGls()
        {
            Assert.IsNull(registro.Para(1).CalcularCodigoBarras(Envio(246998, 1), null));
            Assert.AreEqual(TramitadorGls.CalcularCodigoBarras(Envio(246221, 1), " 6108352 "),
                registro.Para(1).CalcularCodigoBarras(Envio(246221, 1), " 6108352 "));
        }

        // ------------------------------------------------------------ GLS: plaza

        private const string RespuestaPlazaEjemplo =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">" +
            "<soap:Body><GetPlazaXCPResponse xmlns=\"http://www.asmred.com/\"><GetPlazaXCPResult>" +
            "<Plaza xmlns=\"\"><Codigo>280</Codigo><Nemonico>MAD-ALC</Nemonico><Nombre>GLS MADRID ALCOBENDAS</Nombre>" +
            "<Telefono>(+34) 916 62-00 00 / 916 620 001 / 916 620 002</Telefono><Mail>alcobendas@gls-spain.es</Mail></Plaza>" +
            "</GetPlazaXCPResult></GetPlazaXCPResponse></soap:Body></soap:Envelope>";

        [TestMethod]
        public async Task Gls_Plaza_LeeNemonicoNombreTelefonoYMail()
        {
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).Returns(Task.FromResult(RespuestaPlazaEjemplo));

            PlazaAgencia plaza = await registro.Para(1).CalcularPlazaAsync("28100", 34);

            Assert.AreEqual("MAD-ALC", plaza.Nemonico);
            Assert.AreEqual("GLS MADRID ALCOBENDAS", plaza.Nombre);
            // Solo cifras («34916620000916620001916620002», 29) y recortado a 27, como AgenciaASM.calcularPlaza.
            Assert.AreEqual("349166200009166200019166200", plaza.Telefono);
            Assert.AreEqual(27, plaza.Telefono.Length);
            Assert.AreEqual("alcobendas@gls-spain.es", plaza.Email);
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Gls_Plaza_PideLaPlazaConElCpYElPais()
        {
            string enviado = null;
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).Invokes((string soap) => enviado = soap).Returns(Task.FromResult(RespuestaPlazaEjemplo));

            _ = await registro.Para(1).CalcularPlazaAsync(" 28100 ", 34);

            StringAssert.Contains(enviado, "<GetPlazaXCP xmlns=\"http://www.asmred.com/\"><codPais>34</codPais><cp>28100</cp></GetPlazaXCP>");
        }

        [TestMethod]
        public void Gls_Plaza_HijosConElEspacioDeNombresDeGls_TambienSeLeen()
        {
            string respuesta = "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
                "<GetPlazaXCPResponse xmlns=\"http://www.asmred.com/\"><GetPlazaXCPResult><Plaza><Nemonico>BCN</Nemonico>" +
                "<Nombre>BARCELONA</Nombre><Telefono>93 000 00 00</Telefono><Mail>bcn@gls.es</Mail></Plaza></GetPlazaXCPResult>" +
                "</GetPlazaXCPResponse></soap:Body></soap:Envelope>";

            PlazaAgencia plaza = RespuestaPlazaGls.Interpretar(respuesta);

            Assert.AreEqual("BCN", plaza.Nemonico);
            Assert.AreEqual("930000000", plaza.Telefono);
        }

        [TestMethod]
        public void Gls_Plaza_RespuestaSinPlazaOMalFormada_Vacia()
        {
            Assert.IsTrue(RespuestaPlazaGls.Interpretar(null).EsVacia);
            Assert.IsTrue(RespuestaPlazaGls.Interpretar("esto no es XML").EsVacia);
            Assert.IsTrue(RespuestaPlazaGls.Interpretar("<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
                "<GetPlazaXCPResponse xmlns=\"http://www.asmred.com/\"><GetPlazaXCPResult /></GetPlazaXCPResponse></soap:Body></soap:Envelope>").EsVacia);
        }

        [TestMethod]
        public async Task Gls_Plaza_FalloDeRed_VaciaSinLanzarYAElmah()
        {
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._))
                .Throws(new AgenciaRemotaException("GLS (GetPlazaXCP): la petición agotó el tiempo de espera.") { EsTransitoria = true });

            PlazaAgencia plaza = await registro.Para(1).CalcularPlazaAsync("28100", 34);

            Assert.IsTrue(plaza.EsVacia);
            Assert.AreEqual(string.Empty, plaza.Nemonico);
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "28100");
            StringAssert.Contains(registrados[0].Message, "agotó el tiempo de espera");
        }

        [TestMethod]
        public void Gls_ClienteDePlaza_EsperaComoMucho15Segundos()
        {
            Assert.AreEqual(TimeSpan.FromSeconds(15), ClientePlazaGls.ESPERA_MAXIMA);
            Assert.AreEqual("https://www.asmred.com/WebSrvs/b2b.asmx?op=GetPlazaXCP", new ClientePlazaGls().Url);
        }

        // ------------------------------------------------------------ Correos Express

        [TestMethod]
        public void CorreosExpress_YaNoEstaSoportada()
        {
            // NestoAPI#595 (20/10/26): CEX se quitó porque ya no se usa.
            Assert.IsFalse(registro.Soporta(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS));
            Assert.ThrowsException<AgenciaSinPlazaYCodigoBarrasException>(() => registro.Para(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS));
        }

        // ------------------------------------------------------------ Canteras, CTT, Innovatrans

        [TestMethod]
        public async Task Canteras_CttEInnovatrans_PlazaFijaYSinCodigo()
        {
            var esperadas = new Dictionary<int, (string Nemonico, string Nombre)>
            {
                { Constantes.Agencias.AGENCIA_CANTERAS, ("CN", "Canteras") },
                { Constantes.Agencias.AGENCIA_CTT, ("CT", "CTT") },
                { Constantes.Agencias.AGENCIA_INNOVATRANS, ("IN", "Innovatrans") }
            };
            foreach (var esperada in esperadas)
            {
                IPlazaYCodigoBarrasAgencia agencia = registro.Para(esperada.Key);
                PlazaAgencia plaza = await agencia.CalcularPlazaAsync("28001", 34);

                Assert.AreEqual(esperada.Value.Nemonico, plaza.Nemonico, $"agencia {esperada.Key}");
                Assert.AreEqual(esperada.Value.Nombre, plaza.Nombre, $"agencia {esperada.Key}");
                Assert.AreEqual(string.Empty, plaza.Telefono);
                Assert.AreEqual(string.Empty, plaza.Email);
                Assert.AreEqual(string.Empty, agencia.CalcularCodigoBarras(Envio(1), "5"), $"agencia {esperada.Key}");
                Assert.IsFalse(agencia.PlazaConsultaLaAgencia);
            }
            A.CallTo(() => clienteGls.EnviarAsync(A<string>._)).MustNotHaveHappened();
        }
    }
}
