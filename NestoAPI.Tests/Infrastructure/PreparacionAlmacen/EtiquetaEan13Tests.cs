using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models.PreparacionAlmacen;
using NestoAPI.Tests.Infrastructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#762: la etiqueta 3×2 con el EAN-13 (módulo de 2 puntos a 203 ppp, ~80 % del nominal): el del fabricante si la
    /// unidad tiene uno y, si no, el código interno (29…), que se le da al imprimir. En las pruebas la impresora es un fake.
    /// </summary>
    [TestClass]
    public class EtiquetaEan13Tests
    {
        private const string EMPRESA = "1";

        private IRepositorioFichasProducto fichas;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetasAlmacen impresora;
        private RepositorioCodigosBarrasFalso codigos;
        private ServicioEtiquetasProducto servicio;
        private TrabajoEtiquetas impreso;

        private static FichaProductoAlmacen Ficha(string producto, string nombre) => new FichaProductoAlmacen
        {
            Producto = producto,
            Nombre = nombre,
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
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Andre", "ImpresoraCodBarras")).Returns(@"\\RDS2016\zebraalg");
            A.CallTo(() => fichas.LeerFichas(EMPRESA, A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((string e, IReadOnlyCollection<string> p) => new List<FichaProductoAlmacen>
                {
                    Ficha("17404", "ROLLO PAPEL CAMILLA"),
                    Ficha("32565", "GUANTES NITRILO T/M"),
                    Ficha("40100", "CAJA GUANTES")
                }.Where(f => p.Contains(f.Producto)).ToList());
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._))
                .Invokes((string i, TrabajoEtiquetas t) => impreso = t).Returns(LenguajeEtiquetas.Zpl);
            codigos = new RepositorioCodigosBarrasFalso();
            codigos.Producto("17404", null, "ROLLO PAPEL CAMILLA");
            codigos.Producto("32565", "8437017506379", "GUANTES NITRILO T/M");
            codigos.Producto("40100", null, "CAJA GUANTES");
            codigos.Fila("40100", "8437017509998", cantidad: 100); // solo el de la caja
            servicio = new ServicioEtiquetasProducto(fichas, parametros, impresora, new ServicioCodigosBarras(codigos));
        }

        private Task<ResultadoEtiquetasProductoDTO> Imprimir(string producto, int copias = 1, bool ensayo = false, string formato = "Ean13")
            => servicio.Imprimir(EMPRESA, new EtiquetasProductoDTO { Producto = producto, Copias = copias, Formato = formato }, @"NUEVAVISION\Andre", ensayo);

        // ---- Lo que lleva ----

        [TestMethod]
        public void ComponerEan13_ElNombreArribaYLaReferenciaConElTamano()
        {
            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.ComponerEan13(Ficha("17404", "ROLLO PAPEL CAMILLA"), "2900000174047");

            CollectionAssert.AreEqual(new[] { "ROLLO PAPEL CAMILLA", "Ref. 17404 · 100 m" }, etiqueta.Lineas);
            Assert.AreEqual("2900000174047", etiqueta.CodigoBarras);
        }

        [TestMethod]
        public void ComponerEan13_NombreLargo_UnaLineaSolo()
        {
            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.ComponerEan13(
                Ficha("17404", "CREMA MASAJE NEUTRA CON ACEITE DE ALMENDRAS 1000 ML"), "2900000174047");

            Assert.AreEqual(2, etiqueta.Lineas.Count);
            Assert.IsTrue(etiqueta.Lineas[0].Length <= GeneradorEtiquetasProducto.CARACTERES_PEQUENA);
        }

        [TestMethod]
        public void ComponerEan13_LoQueNoEsUnEan13_Lanza()
        {
            Assert.ThrowsException<ArgumentException>(() => GeneradorEtiquetasProducto.ComponerEan13(Ficha("17404", "X"), "2900000174040"));
            Assert.ThrowsException<ArgumentException>(() => GeneradorEtiquetasProducto.ComponerEan13(Ficha("17404", "X"), "84370175"));
        }

        [TestMethod]
        public void ZplEan13_ModuloDe2Centrado_80PuntosDeAlto_Y12Cifras()
        {
            string zpl = GeneradorEtiquetasProducto.ZplEan13(GeneradorEtiquetasProducto.ComponerEan13(Ficha("17404", "ROLLO PAPEL CAMILLA"), "2900000174047"), 2);

            Assert.AreEqual(
                "^XA^CI28^LH0,0\r\n" +
                "^FO12,6^A0N,12,8^FH^FDROLLO PAPEL CAMILLA^FS\r\n" +
                "^FO12,20^A0N,12,8^FH^FDRef. 17404 _C2_B7 100 m^FS\r\n" +
                "^FO25,34^BY2^BEN,80,Y,N^FD290000017404^FS\r\n" +
                "^PQ2^XZ\r\n",
                zpl);
        }

        [TestMethod]
        public void EplEan13_E30ConLasCifrasDebajo()
        {
            string epl = GeneradorEtiquetasProducto.EplEan13(GeneradorEtiquetasProducto.ComponerEan13(Ficha("17404", "ROLLO PAPEL CAMILLA"), "2900000174047"), 1);

            StringAssert.StartsWith(epl, "I8,A,034\r\nq240\r\nQ160,24\r\nN\r\n");
            StringAssert.Contains(epl, "A12,6,0,1,1,1,N,\"ROLLO PAPEL CAMILLA\"\r\n");
            StringAssert.Contains(epl, "B25,34,0,E30,2,2,80,B,\"290000017404\"\r\n");
            Assert.IsTrue(epl.EndsWith("P1\r\n"));
        }

        [TestMethod]
        public void Ean13_CabeEnLaEtiquetaConSusZonasDeSilencio()
        {
            int ancho = GeneradorEtiquetasProducto.EAN_MODULOS * GeneradorEtiquetasProducto.EAN_MODULO;
            int x = GeneradorEtiquetasProducto.EanX;

            Assert.IsTrue(x >= 11 * GeneradorEtiquetasProducto.EAN_MODULO, "zona de silencio izquierda");
            Assert.IsTrue(GeneradorEtiquetasHueco.ANCHO - x - ancho >= 7 * GeneradorEtiquetasProducto.EAN_MODULO, "zona de silencio derecha");
            Assert.IsTrue(GeneradorEtiquetasProducto.EAN_Y + GeneradorEtiquetasProducto.EAN_ALTO + 22 <= GeneradorEtiquetasHueco.ALTO, "las cifras de debajo caben");
        }

        // ---- Qué código lleva ----

        [TestMethod]
        public async Task Imprimir_ProductoSinCodigo_LeDaSuCodigoInternoYLoImprime()
        {
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("17404", copias: 3);

            Assert.AreEqual("2900000174047", resultado.CodigoBarras);
            Assert.IsTrue(resultado.CodigoInterno);
            Assert.IsTrue(resultado.CodigoAsignado);
            Assert.AreEqual(3, resultado.Impresas);
            Assert.IsTrue(codigos.Filas.Any(f => f.Producto == "17404" && f.Codigo == "2900000174047" && f.Activo && !f.Principal));
            StringAssert.Contains(impreso.Zpl, "^BEN,80,Y,N^FD290000017404^FS");
            StringAssert.Contains(impreso.Zpl, "^PQ3^XZ");
            StringAssert.Contains(resultado.Mensaje, "código interno 2900000174047");
        }

        [TestMethod]
        public async Task Imprimir_LaSegundaVez_ElMismoCodigoYaNoAsignado()
        {
            await Imprimir("17404");
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("17404");

            Assert.AreEqual("2900000174047", resultado.CodigoBarras);
            Assert.IsFalse(resultado.CodigoAsignado);
            Assert.AreEqual(1, codigos.Filas.Count(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task Imprimir_ConElDelFabricante_EseYNoSeLeDaInterno()
        {
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("32565");

            Assert.AreEqual("8437017506379", resultado.CodigoBarras);
            Assert.IsFalse(resultado.CodigoInterno);
            Assert.IsFalse(resultado.CodigoAsignado);
            Assert.IsFalse(codigos.Filas.Any(f => f.Producto == "32565" && CodigoBarrasInterno.EsInterno(f.Codigo)));
            StringAssert.Contains(impreso.Zpl, "^FD843701750637^FS");
        }

        [TestMethod]
        public async Task Imprimir_SoloTieneElDeLaCaja_LeDaElInterno()
        {
            // Cada lectura del de la caja contaría 100: en la unidad va el interno
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("40100");

            Assert.IsTrue(resultado.CodigoInterno);
            Assert.AreEqual(CodigoBarrasInterno.DeReferencia("40100"), resultado.CodigoBarras);
        }

        [TestMethod]
        public async Task Imprimir_Ensayo_DiceElCodigoQueLeDariaSinGuardarNiImprimir()
        {
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("17404", ensayo: true);

            Assert.AreEqual("2900000174047", resultado.CodigoBarras);
            Assert.IsFalse(resultado.CodigoAsignado);
            Assert.AreEqual(0, resultado.Impresas);
            Assert.IsFalse(codigos.Filas.Any(f => f.Producto == "17404"));
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
            StringAssert.Contains(resultado.Mensaje, "que se le daría ahora");
        }

        [TestMethod]
        public async Task Imprimir_SinImpresora_NoLeDaCodigo()
        {
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Imprimir("17404"));

            Assert.IsFalse(codigos.Filas.Any(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task Imprimir_SinFormato_LaDeSiempreConLaReferencia()
        {
            ResultadoEtiquetasProductoDTO resultado = await Imprimir("17404", formato: null);

            Assert.IsNull(resultado.CodigoBarras);
            StringAssert.Contains(impreso.Zpl, "^BCN,52,N,N,N,A^FH^FD17404^FS");
            Assert.IsFalse(codigos.Filas.Any(f => f.Producto == "17404"));
        }

        [TestMethod]
        public async Task Imprimir_FormatoDesconocido_ArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Imprimir("17404", formato: "QR"));
        }

        [TestMethod]
        public async Task Imprimir_Ean13SinServicioDeCodigos_ArgumentException()
        {
            servicio = new ServicioEtiquetasProducto(fichas, parametros, impresora);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Imprimir("17404"));
        }

        [TestMethod]
        public void CodigoDeLaUnidad_ElDelFabricantePrincipalAntesQueElInterno()
        {
            var lista = new List<CodigoBarrasProductoDTO>
            {
                new CodigoBarrasProductoDTO { Codigo = "2900000174047", Cantidad = 1, Activo = true },
                new CodigoBarrasProductoDTO { Codigo = "8437017509998", Cantidad = 1, Activo = true },
                new CodigoBarrasProductoDTO { Codigo = "8437017506379", Cantidad = 1, Activo = true, Principal = true },
                new CodigoBarrasProductoDTO { Codigo = "8437017502227", Cantidad = 1, Activo = false, Principal = false }
            };

            Assert.AreEqual("8437017506379", ServicioEtiquetasProducto.CodigoDeLaUnidad(lista).Codigo);
            Assert.AreEqual("2900000174047", ServicioEtiquetasProducto.CodigoDeLaUnidad(lista.Take(1)).Codigo);
            Assert.IsNull(ServicioEtiquetasProducto.CodigoDeLaUnidad(lista.Skip(3)));
        }
    }
}
