using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Etiquetas de hueco (Carlos 05/10/26): las etiquetas de 30×20 mm que van en cada hueco de la estantería, con el código
    /// PPPFFFCCC que lee Ariadna. Se imprimen en la impresora de etiquetas de producto del usuario (ImpresoraCodBarras), desde
    /// Nesto (un pasillo entero o huecos sueltos) y desde Ariadna (el hueco que no tiene etiqueta). Un solo núcleo en la API.
    /// </summary>
    [TestClass]
    public class EtiquetasHuecoAlmacenTests
    {
        private const string EMPRESA = "1";
        private const string ALMACEN = "ALG";

        private IRepositorioEtiquetasHueco repositorio;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetas impresora;
        private ServicioEtiquetasHueco servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioEtiquetasHueco>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraEtiquetas>();
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Carlos", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas2");
            servicio = new ServicioEtiquetasHueco(repositorio, parametros, impresora);
        }

        // ---- El EPL de la etiqueta ----

        [TestMethod]
        public void Epl_UnHueco_CodigoDeBarrasArribaYPasilloFilaColumnaDebajo()
        {
            string epl = GeneradorEtiquetasHueco.Epl(new[] { "002004001" });

            Assert.AreEqual(
                "I8,A,034\r\n" +
                "q240\r\n" +
                "Q160,24\r\n" +
                "N\r\n" +
                "B18,8,0,1,2,2,56,N,\"002004001\"\r\n" +
                "A54,72,0,3,1,1,N,\"Pasillo:002\"\r\n" +
                "A54,96,0,3,1,1,N,\"Fila...:004\"\r\n" +
                "A54,120,0,3,1,1,N,\"Columna:001\"\r\n" +
                "P1\r\n",
                epl);
        }

        [TestMethod]
        public void Epl_VariosHuecos_UnaEtiquetaPorHuecoEnElMismoTrabajo()
        {
            string epl = GeneradorEtiquetasHueco.Epl(new[] { "002004001", "002005001" });

            Assert.AreEqual(1, Ocurrencias(epl, "I8,A,034"), "La cabecera va una vez");
            Assert.AreEqual(2, Ocurrencias(epl, "P1\r\n"));
            StringAssert.Contains(epl, "\"Fila...:005\"");
        }

        // ---- Qué huecos se piden ----

        [TestMethod]
        public void Huecos_UnaListaConBarrasOSinEllas_SeNormalizanSinRepetirYEnOrdenDeRecorrido()
        {
            var peticion = new EtiquetasHuecoDTO { Huecos = new List<string> { "003001002", " 2/4/1 ", "002004001", "002/001/002" } };

            List<string> huecos = ServicioEtiquetasHueco.HuecosPedidos(peticion, out string motivo);

            Assert.IsNull(motivo);
            // Pasillo, columna, fila: como se recorre la estantería
            CollectionAssert.AreEqual(new[] { "002004001", "002001002", "003001002" }, huecos);
        }

        [TestMethod]
        public void Huecos_UnoQueNoEsUnHueco_DiceCual()
        {
            List<string> huecos = ServicioEtiquetasHueco.HuecosPedidos(new EtiquetasHuecoDTO { Huecos = new List<string> { "12345" } }, out string motivo);

            Assert.IsNull(huecos);
            StringAssert.Contains(motivo, "12345");
        }

        [TestMethod]
        public void Huecos_UnRango_TodasLasFilasYColumnasDelPasillo()
        {
            var peticion = new EtiquetasHuecoDTO { Pasillo = "2", FilaDesde = "1", FilaHasta = "2", ColumnaDesde = "1", ColumnaHasta = "2" };

            List<string> huecos = ServicioEtiquetasHueco.HuecosPedidos(peticion, out string motivo);

            Assert.IsNull(motivo);
            CollectionAssert.AreEqual(new[] { "002001001", "002002001", "002001002", "002002002" }, huecos);
        }

        [TestMethod]
        public void Huecos_UnRangoSinHasta_EsUnaSolaFilaOColumna()
        {
            List<string> huecos = ServicioEtiquetasHueco.HuecosPedidos(
                new EtiquetasHuecoDTO { Pasillo = "002", FilaDesde = "004", ColumnaDesde = "001", ColumnaHasta = "003" }, out _);

            CollectionAssert.AreEqual(new[] { "002004001", "002004002", "002004003" }, huecos);
        }

        [TestMethod]
        public void Huecos_DeMasDe500_NoSeImprimen()
        {
            List<string> huecos = ServicioEtiquetasHueco.HuecosPedidos(
                new EtiquetasHuecoDTO { Pasillo = "1", FilaDesde = "0", FilaHasta = "30", ColumnaDesde = "1", ColumnaHasta = "30" }, out string motivo);

            Assert.IsNull(huecos);
            StringAssert.Contains(motivo, "500");
        }

        [TestMethod]
        public void Huecos_SinNada_LoDice()
        {
            Assert.IsNull(ServicioEtiquetasHueco.HuecosPedidos(new EtiquetasHuecoDTO(), out string motivo));
            Assert.IsNotNull(motivo);
        }

        [TestMethod]
        public void Huecos_UnRangoAlReves_LoDice()
        {
            Assert.IsNull(ServicioEtiquetasHueco.HuecosPedidos(
                new EtiquetasHuecoDTO { Pasillo = "1", FilaDesde = "5", FilaHasta = "2", ColumnaDesde = "1" }, out string motivo));
            Assert.IsNotNull(motivo);
        }

        // ---- Imprimir ----

        [TestMethod]
        public async Task Imprimir_EnLaImpresoraDeEtiquetasDelUsuario()
        {
            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Huecos = new List<string> { "002004001" } }, @"NUEVAVISION\Carlos", ensayo: false);

            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetas2", GeneradorEtiquetasHueco.Epl(new[] { "002004001" }), A<string>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Impresas);
            Assert.AreEqual(@"\\RDS2016\etiquetas2", resultado.Impresora);
            CollectionAssert.AreEqual(new[] { "002004001" }, resultado.Huecos);
        }

        [TestMethod]
        public async Task Imprimir_UnUsuarioSinImpresoraPropia_UsaLaDeDefecto()
        {
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Pedro", "ImpresoraCodBarras")).Returns(null);
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "(defecto)", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas2");

            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Huecos = new List<string> { "002004001" } }, "Pedro", ensayo: false);

            Assert.AreEqual(@"\\RDS2016\etiquetas2", resultado.Impresora);
        }

        [TestMethod]
        public async Task Imprimir_SinImpresora_NoImprimeYDiceElMotivo()
        {
            ArgumentException error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Huecos = new List<string> { "002004001" } }, "Nadie", ensayo: false));

            StringAssert.Contains(error.Message, "ImpresoraCodBarras");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_ConHuecosQueNoValen_NoImprime()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Huecos = new List<string> { "x" } }, "Carlos", ensayo: false));

            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ensayo_NoImprimePeroDiceQueYDonde()
        {
            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Pasillo = "2", FilaDesde = "1", FilaHasta = "2", ColumnaDesde = "1" }, "Carlos", ensayo: true);

            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Impresas);
            Assert.AreEqual(@"\\RDS2016\etiquetas2", resultado.Impresora);
            CollectionAssert.AreEqual(new[] { "002001001", "002002001" }, resultado.Huecos);
        }

        [TestMethod]
        public async Task SoloEnUso_UnRango_PideAlAlmacenLosHuecosConAlgoYSoloImprimeEsos()
        {
            A.CallTo(() => repositorio.LeerHuecosEnUso(EMPRESA, ALMACEN, "002", "000", "999", "001", "005"))
                .Returns(new List<string> { "002007001", "002004001" });

            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Pasillo = "2", FilaDesde = "0", FilaHasta = "999", ColumnaDesde = "1", ColumnaHasta = "5", SoloEnUso = true },
                "Carlos", ensayo: false);

            // Sin SoloEnUso este rango pasaría de 500; en uso solo hay dos
            CollectionAssert.AreEqual(new[] { "002004001", "002007001" }, resultado.Huecos);
            Assert.AreEqual(2, resultado.Impresas);
        }

        [TestMethod]
        public async Task SoloEnUso_UnaLista_QuitaLosHuecosVacios()
        {
            A.CallTo(() => repositorio.LeerHuecosEnUso(EMPRESA, ALMACEN, "002", "000", "999", "000", "999"))
                .Returns(new List<string> { "002004001" });

            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Huecos = new List<string> { "002004001", "002009009" }, SoloEnUso = true }, "Carlos", ensayo: true);

            CollectionAssert.AreEqual(new[] { "002004001" }, resultado.Huecos);
        }

        [TestMethod]
        public async Task SoloEnUso_SinNingunHuecoConAlgo_NoImprimeYLoDice()
        {
            A.CallTo(() => repositorio.LeerHuecosEnUso(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(new List<string>());

            ResultadoEtiquetasHuecoDTO resultado = await servicio.Imprimir(EMPRESA, ALMACEN,
                new EtiquetasHuecoDTO { Pasillo = "9", FilaDesde = "1", ColumnaDesde = "1", SoloEnUso = true }, "Carlos", ensayo: false);

            Assert.AreEqual(0, resultado.Impresas);
            StringAssert.Contains(resultado.Mensaje, "ningún hueco");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        private static int Ocurrencias(string texto, string trozo)
            => (texto.Length - texto.Replace(trozo, string.Empty).Length) / trozo.Length;
    }
}
