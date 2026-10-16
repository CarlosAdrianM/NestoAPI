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
    /// NestoAPI#654 (sugerencia 546 de Paloma): la etiqueta de código de barras de producto, de 30×20 mm, como la de la foto del
    /// comentario 167: «ROLLO PAPEL CAMILLA» / «Desechables» / «100 m», el código de barras con el número y el número debajo.
    /// En las pruebas la impresora es un fake: no se imprime nada.
    /// </summary>
    [TestClass]
    public class EtiquetasProductoAlmacenTests
    {
        private const string EMPRESA = "1";

        private IRepositorioFichasProducto fichas;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetas impresora;
        private ServicioEtiquetasProducto servicio;

        private static FichaProductoAlmacen RolloCamilla() => new FichaProductoAlmacen
        {
            Producto = "17404",
            Nombre = "ROLLO PAPEL CAMILLA",
            Familia = "Productos Genéricos",
            Subgrupo = "Desechables",
            Tamano = 100,
            UnidadMedida = "m"
        };

        [TestInitialize]
        public void Preparar()
        {
            fichas = A.Fake<IRepositorioFichasProducto>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraEtiquetas>();
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Paloma", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetasalcobendas");
            A.CallTo(() => fichas.LeerFichas(EMPRESA, A<IReadOnlyCollection<string>>.That.Contains("17404")))
                .Returns(new List<FichaProductoAlmacen> { RolloCamilla() });
            servicio = new ServicioEtiquetasProducto(fichas, parametros, impresora);
        }

        // ---- Qué lleva la etiqueta ----

        [TestMethod]
        public void Componer_ComoLaDeLaFoto_NombreSubgrupoYTamanoEnLaFuenteNormal()
        {
            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.Componer(RolloCamilla());

            CollectionAssert.AreEqual(new[] { "ROLLO PAPEL CAMILLA", "Desechables", "100 m" }, etiqueta.Lineas);
            Assert.AreEqual(GeneradorEtiquetasProducto.FUENTE_NORMAL, etiqueta.Fuente);
            Assert.AreEqual("17404", etiqueta.Producto);
        }

        [TestMethod]
        public void Epl_ComoLaDeLaFoto_TextoArribaCodigoConElNumeroYElNumeroDebajo()
        {
            string epl = GeneradorEtiquetasProducto.Epl(GeneradorEtiquetasProducto.Componer(RolloCamilla()), 1);

            Assert.AreEqual(
                "I8,A,034\r\n" +
                "q240\r\n" +
                "Q160,24\r\n" +
                "N\r\n" +
                "A12,6,0,2,1,1,N,\"ROLLO PAPEL CAMILLA\"\r\n" +
                "A12,24,0,2,1,1,N,\"Desechables\"\r\n" +
                "A12,42,0,2,1,1,N,\"100 m\"\r\n" +
                "B12,62,0,1,2,2,52,N,\"17404\"\r\n" +
                "A12,120,0,4,1,1,N,\"17404\"\r\n" +
                "P1\r\n",
                epl);
        }

        [TestMethod]
        public void Epl_VariasCopias_UnSoloTrabajoConLasCopiasEnP()
        {
            string epl = GeneradorEtiquetasProducto.Epl(GeneradorEtiquetasProducto.Componer(RolloCamilla()), 5);

            Assert.IsTrue(epl.EndsWith("P5\r\n"));
            Assert.AreEqual(1, epl.Split(new[] { "\r\nN\r\n" }, StringSplitOptions.None).Length - 1);
        }

        [TestMethod]
        public void Componer_SinSubgrupoNiTamano_SoloElNombre()
        {
            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.Componer(new FichaProductoAlmacen { Producto = "12345", Nombre = "CERA TIBIA" });

            CollectionAssert.AreEqual(new[] { "CERA TIBIA" }, etiqueta.Lineas);
        }

        [TestMethod]
        public void Componer_NombreDeDosLineas_ElSubgrupoYElTamanoVanJuntosEnLaTercera()
        {
            var ficha = new FichaProductoAlmacen
            {
                Producto = "20001",
                Nombre = "CREMA CORPORAL HIDRATANTE ALOE",
                Subgrupo = "Corporal",
                Tamano = 500,
                UnidadMedida = "ml"
            };

            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.Componer(ficha);

            CollectionAssert.AreEqual(new[] { "CREMA CORPORAL", "HIDRATANTE ALOE", "Corporal 500 ml" }, etiqueta.Lineas);
            Assert.AreEqual(GeneradorEtiquetasProducto.FUENTE_NORMAL, etiqueta.Fuente);
        }

        [TestMethod]
        public void Componer_NoCabeEnLaFuenteNormal_PasaALaPequena()
        {
            var ficha = new FichaProductoAlmacen
            {
                Producto = "20002",
                Nombre = "AMPOLLAS FLASH EFECTO LIFTING INMEDIATO CAJA DE DIEZ",
                Subgrupo = "Tratamientos faciales",
                Tamano = 2,
                UnidadMedida = "ml"
            };

            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.Componer(ficha);

            Assert.AreEqual(GeneradorEtiquetasProducto.FUENTE_PEQUENA, etiqueta.Fuente);
            Assert.AreEqual(3, etiqueta.Lineas.Count);
            Assert.IsTrue(etiqueta.Lineas.All(l => l.Length <= GeneradorEtiquetasProducto.CARACTERES_PEQUENA));
        }

        [TestMethod]
        public void Partir_UnaPalabraMasLargaQueLaLinea_SeCorta()
        {
            List<string> lineas = GeneradorEtiquetasProducto.Partir("SUPERCALIFRAGILISTICO", 10);

            CollectionAssert.AreEqual(new[] { "SUPERCALIF", "RAGILISTIC", "O" }, lineas);
        }

        [TestMethod]
        public void Epl_ComillasYBarrasEnElNombre_VanEscapadas()
        {
            var etiqueta = new EtiquetaProducto { Producto = "1", Lineas = new List<string> { "PINZA 5\" A\\B" }, Fuente = 2 };

            string epl = GeneradorEtiquetasProducto.Epl(etiqueta, 1);

            StringAssert.Contains(epl, "\"PINZA 5\\\" A\\\\B\"");
        }

        [TestMethod]
        public void Epl_NumeroLargo_BarrasDeUnPuntoParaQueQuepa()
        {
            var etiqueta = new EtiquetaProducto { Producto = "ABCDEFGHIJKL", Lineas = new List<string>(), Fuente = 2 };

            string epl = GeneradorEtiquetasProducto.Epl(etiqueta, 1);

            StringAssert.Contains(epl, ",0,1,1,1,52,N,\"ABCDEFGHIJKL\"");
        }

        // ---- El servicio ----

        [TestMethod]
        public async Task Imprimir_MandaUnTrabajoALaImpresoraDelUsuario()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = " 17404 ", Copias = 2 }, @"NUEVAVISION\Paloma", false);

            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasalcobendas",
                A<string>.That.Matches(e => e.Contains("B12,62,0,1,2,2,52,N,\"17404\"") && e.EndsWith("P2\r\n")),
                "Etiqueta producto 17404")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, resultado.Impresas);
            Assert.AreEqual(@"\\RDS2016\etiquetasalcobendas", resultado.Impresora);
            CollectionAssert.AreEqual(new[] { "ROLLO PAPEL CAMILLA", "Desechables", "100 m" }, resultado.Lineas);
            Assert.AreEqual(@"2 etiquetas de 17404 mandadas a \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_SinCopias_UnaEtiqueta()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Paloma", false);

            Assert.AreEqual(1, resultado.Impresas);
            Assert.AreEqual(@"1 etiqueta de 17404 mandada a \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_Ensayo_NoImprimeYDiceDonde()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Paloma", true);

            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Impresas);
            Assert.AreEqual(@"Se imprimirían 1 etiqueta de 17404 en \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_UsuarioSinImpresora_LaDeDefecto()
        {
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "(defecto)", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas1");

            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Nadie", false);

            Assert.AreEqual(@"\\RDS2016\etiquetas1", resultado.Impresora);
        }

        [TestMethod]
        public async Task Imprimir_ProductoQueNoExiste_ArgumentExceptionSinImprimir()
        {
            A.CallTo(() => fichas.LeerFichas(EMPRESA, A<IReadOnlyCollection<string>>._)).Returns(new List<FichaProductoAlmacen>());

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "99999" }, "Paloma", false));

            Assert.AreEqual("No existe el producto 99999.", ex.Message);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_SinProducto_ArgumentException()
        {
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA, new EtiquetasProductoDTO(), "Paloma", false));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA, null, "Paloma", false));
        }

        [TestMethod]
        public async Task Imprimir_DemasiadasCopias_ArgumentException()
        {
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404", Copias = ServicioEtiquetasProducto.MAXIMO_COPIAS + 1 }, "Paloma", false));
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void LaFichaDelAlmacenTraeElNombre()
        {
            StringAssert.Contains(RepositorioFichasProducto.SQL_FICHAS, "RTRIM(p.Nombre) AS Nombre");
        }
    }
}
