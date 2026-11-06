using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models.PreparacionAlmacen;
using NestoAPI.Tests.Infrastructure.Productos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#654 (sugerencia 546 de Paloma): la etiqueta de código de barras de producto, de 30×20 mm, en la impresora de
    /// etiquetas del usuario. Desde NestoAPI#791 siempre con el EAN-13 (lo que lleva está en <see cref="EtiquetaEan13Tests"/>).
    /// En las pruebas la impresora es un fake: no se imprime nada.
    /// </summary>
    [TestClass]
    public class EtiquetasProductoAlmacenTests
    {
        private const string EMPRESA = "1";

        private IRepositorioFichasProducto fichas;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetasAlmacen impresora;
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
            impresora = A.Fake<IImpresoraEtiquetasAlmacen>();
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Paloma", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetasalcobendas");
            A.CallTo(() => fichas.LeerFichas(EMPRESA, A<IReadOnlyCollection<string>>.That.Contains("17404")))
                .Returns(new List<FichaProductoAlmacen> { RolloCamilla() });
            var codigos = new RepositorioCodigosBarrasFalso();
            _ = codigos.Producto("17404", "8437017506379", "ROLLO PAPEL CAMILLA");
            servicio = new ServicioEtiquetasProducto(fichas, parametros, impresora, new ServicioCodigosBarras(codigos));
        }

        // ---- Lo que va en el trabajo ----

        [TestMethod]
        public void Partir_UnaPalabraMasLargaQueLaLinea_SeCorta()
        {
            List<string> lineas = GeneradorEtiquetasProducto.Partir("SUPERCALIFRAGILISTICO", 10);

            CollectionAssert.AreEqual(new[] { "SUPERCALIF", "RAGILISTIC", "O" }, lineas);
        }

        [TestMethod]
        public void EplEan13_ComillasYBarrasEnElNombre_VanEscapadas()
        {
            var etiqueta = new EtiquetaProducto { Producto = "1", Lineas = new List<string> { "PINZA 5\" A\\B" }, Fuente = 1, CodigoBarras = "2900000174047" };

            string epl = GeneradorEtiquetasProducto.EplEan13(etiqueta, 1);

            StringAssert.Contains(epl, "\"PINZA 5\\\" A\\\\B\"");
        }

        [TestMethod]
        public void ZplEan13_TildesYCaracteresDeZpl_EnHexadecimalUtf8()
        {
            var etiqueta = new EtiquetaProducto { Producto = "1", Lineas = new List<string> { "PEÑA ^A_B~" }, Fuente = 1, CodigoBarras = "2900000174047" };

            string zpl = GeneradorEtiquetasProducto.ZplEan13(etiqueta, 1);

            StringAssert.Contains(zpl, "^FH^FDPE_C3_91A _5EA_5FB_7E^FS");
        }

        [TestMethod]
        public void EplEan13_VariasCopias_UnSoloTrabajoConLasCopiasEnP()
        {
            string epl = GeneradorEtiquetasProducto.EplEan13(GeneradorEtiquetasProducto.ComponerEan13(RolloCamilla(), "8437017506379"), 5);

            Assert.IsTrue(epl.EndsWith("P5\r\n"));
            Assert.AreEqual(1, epl.Split(new[] { "\r\nN\r\n" }, StringSplitOptions.None).Length - 1);
        }

        // ---- El servicio ----

        [TestMethod]
        public async Task Imprimir_MandaUnTrabajoEan13ALaImpresoraDeCodigosDeBarrasDelUsuario()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = " 17404 ", Copias = 2 }, @"NUEVAVISION\Paloma", false);

            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasalcobendas",
                A<TrabajoEtiquetas>.That.Matches(t => t.Epl.Contains("B25,50,0,E30,2,2,80,B,\"843701750637\"") && t.Epl.EndsWith("P2\r\n")
                    && t.Zpl.Contains("^BEN,80,Y,N^FD843701750637^FS") && t.Zpl.EndsWith("^PQ2^XZ\r\n")
                    && t.Nombre == "Etiqueta EAN-13 producto 17404"))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, resultado.Impresas);
            Assert.AreEqual(@"\\RDS2016\etiquetasalcobendas", resultado.Impresora);
            Assert.AreEqual("8437017506379", resultado.CodigoBarras);
            CollectionAssert.AreEqual(new[] { "ROLLO PAPEL CAMILLA · 100 m", "Ref. 17404" }, resultado.Lineas);
            Assert.AreEqual(@"2 etiquetas de 17404 (8437017506379) mandadas a \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_SinCopias_UnaEtiqueta()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Paloma", false);

            Assert.AreEqual(1, resultado.Impresas);
            Assert.AreEqual(@"1 etiqueta de 17404 (8437017506379) mandada a \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_Ensayo_NoImprimeYDiceDonde()
        {
            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Paloma", true);

            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
            Assert.AreEqual(0, resultado.Impresas);
            Assert.AreEqual(@"Se imprimirían 1 etiqueta de 17404 (8437017506379) en \\RDS2016\etiquetasalcobendas.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Imprimir_UsuarioSinImpresora_LaDeDefecto()
        {
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "(defecto)", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas1");

            ResultadoEtiquetasProductoDTO resultado = await servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "17404" }, "Nadie", false);

            Assert.AreEqual(@"\\RDS2016\etiquetas1", resultado.Impresora);
            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetas1", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Imprimir_ProductoQueNoExiste_ArgumentExceptionSinImprimir()
        {
            A.CallTo(() => fichas.LeerFichas(EMPRESA, A<IReadOnlyCollection<string>>._)).Returns(new List<FichaProductoAlmacen>());

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Imprimir(EMPRESA,
                new EtiquetasProductoDTO { Producto = "99999" }, "Paloma", false));

            Assert.AreEqual("No existe el producto 99999.", ex.Message);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
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
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SinServicioDeCodigos_NoSePuedeCrear()
        {
            _ = Assert.ThrowsException<ArgumentNullException>(() => new ServicioEtiquetasProducto(fichas, parametros, impresora, null));
        }

        [TestMethod]
        public void LaFichaDelAlmacenTraeElNombre()
        {
            StringAssert.Contains(RepositorioFichasProducto.SQL_FICHAS, "RTRIM(p.Nombre) AS Nombre");
        }
    }
}
