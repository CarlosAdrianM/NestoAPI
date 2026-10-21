using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#672: la impresora de Reina saca la etiqueta de producto corrida a la izquierda con el mismo papel que Algete y
    /// Alcobendas. Cada impresora puede llevar un desplazamiento X/Y (en puntos, 8 = 1 mm) que se suma, en un solo sitio, a las
    /// coordenadas de todo lo que se dibuja. Sin ajuste, el EPL tiene que salir idéntico al de siempre.
    /// </summary>
    [TestClass]
    public class AjusteImpresorasEtiquetasTests
    {
        private const string REINA = @"\\RDS2016\etiquetasreina";

        private const string ALCOBENDAS = @"\\RDS2016\etiquetasalcobendas";

        private IImpresoraEtiquetas impresoraReal;
        private IRepositorioAjustesImpresoras repositorio;
        private ILenguajeImpresoras lenguajes;
        private ImpresoraEtiquetasConAjuste impresora;
        private string impreso;

        [TestInitialize]
        public void Preparar()
        {
            impresoraReal = A.Fake<IImpresoraEtiquetas>();
            repositorio = A.Fake<IRepositorioAjustesImpresoras>();
            lenguajes = A.Fake<ILenguajeImpresoras>();
            A.CallTo(() => lenguajes.De(A<string>._)).Returns(LenguajeEtiquetas.Epl);
            A.CallTo(() => lenguajes.De(ALCOBENDAS)).Returns(LenguajeEtiquetas.Zpl);
            A.CallTo(() => repositorio.Leer(A<string>._)).Returns(null);
            A.CallTo(() => impresoraReal.Imprimir(A<string>._, A<string>._, A<string>._)).Invokes((string i, string c, string n) => impreso = c);
            impresora = new ImpresoraEtiquetasConAjuste(impresoraReal, repositorio, lenguajes);
        }

        private static TrabajoEtiquetas Trabajo(string epl, string nombre) => new TrabajoEtiquetas(epl, "^XA^FO10,20^FDzpl^FS^XZ", nombre);

        // ---- NestoAPI#685: el idioma de la impresora ----

        [TestMethod]
        public void Imprimir_ImpresoraZpl_MandaElZplYDiceQueLoHaMandadoEnZpl()
        {
            LenguajeEtiquetas lenguaje = impresora.Imprimir(ALCOBENDAS, new TrabajoEtiquetas("EPL", "^XA^FO10,20^FDx^FS^XZ", "Bultos reposición 81093"));

            Assert.AreEqual(LenguajeEtiquetas.Zpl, lenguaje);
            Assert.AreEqual("^XA^FO10,20^FDx^FS^XZ", impreso);
            A.CallTo(() => impresoraReal.Imprimir(ALCOBENDAS, A<string>._, "Bultos reposición 81093")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Imprimir_ImpresoraZplConAjuste_SeDesplazaElZpl()
        {
            A.CallTo(() => repositorio.Leer(ALCOBENDAS.ToUpperInvariant())).Returns(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 16, DesplazamientoY = -8 });

            _ = impresora.Imprimir(ALCOBENDAS, GeneradorEtiquetasHueco.Trabajo(new[] { "002004001" }));

            StringAssert.Contains(impreso, "^FO34,0^BY2^BCN,56,N,N,N,A^FH^FD002004001^FS");
            StringAssert.Contains(impreso, "^FO70,64^ADN,18,10^FH^FDPasillo:002^FS");
            Assert.IsFalse(impreso.Contains("q2"), "Ni rastro del EPL");
        }

        [TestMethod]
        public void DesplazarZpl_SumaACadaOrigenSinBajarDeCero_YConCeroYCeroElMismoTrabajo()
        {
            string zpl = "^XA^LH0,0\r\n^FO10,20^GB5,5,1^FS\r\n^FT4,30^A0N,20,12^FD^FO99,99^FS\r\n^XZ\r\n";

            Assert.AreSame(zpl, DesplazadorZpl.Desplazar(zpl, 0, 0));
            Assert.AreEqual("^XA^LH0,0\r\n^FO18,12^GB5,5,1^FS\r\n^FT12,22^A0N,20,12^FD^FO107,91^FS\r\n^XZ\r\n", DesplazadorZpl.Desplazar(zpl, 8, -8));
            StringAssert.Contains(DesplazadorZpl.Desplazar(zpl, -20, -40), "^FO0,0^GB5,5,1^FS");
        }

        [TestMethod]
        public void EtiquetaDePruebaZpl_ElMarcoYLasReglasComoLaEpl()
        {
            string zpl = GeneradorEtiquetaPrueba.Zpl(8, 0);

            StringAssert.StartsWith(zpl, "^XA^CI28^LH0,0\r\n");
            StringAssert.Contains(zpl, "^FO0,0^GB240,160,2^FS");
            StringAssert.Contains(zpl, "^FO80,0^GB1,18,1^FS"); // 10 mm: la marca larga
            StringAssert.Contains(zpl, "^FO0,40^GB12,1,1^FS"); // 5 mm en la regla de Y
            StringAssert.Contains(zpl, "^FDX=8 Y=0 puntos^FS");
            Assert.IsTrue(zpl.EndsWith("^PQ1^XZ\r\n"));
            Assert.IsFalse(zpl.Contains("^PW") || zpl.Contains("^LL"));
        }

        [TestMethod]
        public void ImprimirPrueba_EnUnaImpresoraZpl_MandaLaDeZplDesplazada()
        {
            A.CallTo(() => repositorio.Leer(ALCOBENDAS.ToUpperInvariant())).Returns(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 8, DesplazamientoY = 0 });
            var servicio = new ServicioAjustesImpresoras(repositorio, A.Fake<ILectorParametrosUsuario>(), impresoraReal, lenguajes);

            ResultadoEtiquetaPruebaDTO resultado = servicio.ImprimirPrueba("1", ALCOBENDAS, null, null, "NUEVAVISION\\Paloma");

            StringAssert.Contains(impreso, "^FO8,0^GB240,160,2^FS");
            StringAssert.Contains(resultado.Mensaje, "en ZPL");
        }

        private static readonly string EplHueco =
            "I8,A,034\r\n" +
            "q240\r\n" +
            "Q160,24\r\n" +
            "N\r\n" +
            "B18,8,0,1,2,2,56,N,\"002004001\"\r\n" +
            "A54,72,0,3,1,1,N,\"Pasillo:002\"\r\n" +
            "A54,96,0,3,1,1,N,\"Fila...:004\"\r\n" +
            "A54,120,0,3,1,1,N,\"Columna:001\"\r\n" +
            "P1\r\n";

        private static readonly string EplProducto =
            "I8,A,034\r\n" +
            "q240\r\n" +
            "Q160,24\r\n" +
            "N\r\n" +
            "A12,6,0,2,1,1,N,\"ROLLO PAPEL CAMILLA\"\r\n" +
            "A12,24,0,2,1,1,N,\"Desechables\"\r\n" +
            "A12,42,0,2,1,1,N,\"100 m\"\r\n" +
            "B12,62,0,1,2,2,52,N,\"17404\"\r\n" +
            "A12,120,0,4,1,1,N,\"17404\"\r\n" +
            "P1\r\n";

        private static readonly string EplBulto =
            "I8,A,034\r\n" +
            "q240\r\n" +
            "Q160,24\r\n" +
            "N\r\n" +
            "B57,8,0,1,1,1,64,N,\"R81010-1\"\r\n" +
            "A18,84,0,3,1,1,N,\"81010 - bulto 1/1\"\r\n" +
            "A66,112,0,3,1,1,N,\"ALC > ALG\"\r\n" +
            "P1\r\n";

        private static string Hueco() => GeneradorEtiquetasHueco.Epl(new[] { "002004001" });

        private static string Producto() => GeneradorEtiquetasProducto.Epl(GeneradorEtiquetasProducto.Componer(new FichaProductoAlmacen
        {
            Producto = "17404",
            Nombre = "ROLLO PAPEL CAMILLA",
            Subgrupo = "Desechables",
            Tamano = 100,
            UnidadMedida = "m"
        }), 1);

        private static string Bulto() => GeneradorEtiquetasBulto.Epl(
            new List<BultoReposicionDTO> { new BultoReposicionDTO { Codigo = "R81010-1", Bulto = 1, Total = 1 } }, 81010, "ALC", "ALG");

        // ---- Sin ajuste, lo de siempre ----

        [TestMethod]
        public void Generadores_LaSalidaDeHoy_NoCambia()
        {
            Assert.AreEqual(EplHueco, Hueco());
            Assert.AreEqual(EplProducto, Producto());
            Assert.AreEqual(EplBulto, Bulto());
        }

        [TestMethod]
        public void Desplazar_ConCeroYCero_DevuelveElMismoTrabajo()
        {
            foreach (string epl in new[] { Hueco(), Producto(), Bulto(), GeneradorEtiquetaPrueba.Epl(0, 0) })
            {
                Assert.AreSame(epl, DesplazadorEpl.Desplazar(epl, 0, 0));
            }
        }

        [TestMethod]
        public void Imprimir_ImpresoraSinAjuste_MandaCadaEtiquetaExactamenteComoHoy()
        {
            foreach (string epl in new[] { EplHueco, EplProducto, EplBulto })
            {
                _ = impresora.Imprimir(@"\\RDS2016\etiquetas2", Trabajo(epl, "Etiquetas"));
                Assert.AreEqual(epl, impreso);
            }
        }

        [TestMethod]
        public void Imprimir_AjusteGuardadoACero_TambienSaleComoHoy()
        {
            A.CallTo(() => repositorio.Leer(A<string>._)).Returns(new AjusteImpresoraEtiquetasDTO { Impresora = REINA.ToUpperInvariant() });

            _ = impresora.Imprimir(REINA, Trabajo(EplProducto, "Etiqueta"));

            Assert.AreEqual(EplProducto, impreso);
        }

        [TestMethod]
        public void Imprimir_SiNoSePuedeLeerElAjuste_ImprimeComoHoy()
        {
            A.CallTo(() => repositorio.Leer(A<string>._)).Throws(new InvalidOperationException("sin conexión"));

            _ = impresora.Imprimir(REINA, Trabajo(EplHueco, "Etiquetas"));

            Assert.AreEqual(EplHueco, impreso);
        }

        // ---- Con ajuste ----

        [TestMethod]
        public void Imprimir_ConAjuste_TodasLasCoordenadasDeLaEtiquetaDeHuecoSeDesplazan()
        {
            A.CallTo(() => repositorio.Leer(REINA.ToUpperInvariant())).Returns(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 24, DesplazamientoY = -4 });

            _ = impresora.Imprimir("  " + REINA.ToLowerInvariant() + " ", Trabajo(Hueco(), "Etiquetas de hueco"));

            Assert.AreEqual(
                "I8,A,034\r\n" +
                "q264\r\n" + // la anchura crece lo mismo que se desplaza a la derecha (X=24)
                "Q160,24\r\n" +
                "N\r\n" +
                "B42,4,0,1,2,2,56,N,\"002004001\"\r\n" +
                "A78,68,0,3,1,1,N,\"Pasillo:002\"\r\n" +
                "A78,92,0,3,1,1,N,\"Fila...:004\"\r\n" +
                "A78,116,0,3,1,1,N,\"Columna:001\"\r\n" +
                "P1\r\n",
                impreso);
            A.CallTo(() => impresoraReal.Imprimir("  " + REINA.ToLowerInvariant() + " ", A<string>._, "Etiquetas de hueco")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Imprimir_ConAjuste_LaDeProductoYLaDeBultoTambien()
        {
            A.CallTo(() => repositorio.Leer(REINA.ToUpperInvariant())).Returns(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 16, DesplazamientoY = 0 });

            _ = impresora.Imprimir(REINA, Trabajo(Producto(), "Etiqueta producto"));
            StringAssert.Contains(impreso, "A28,6,0,2,1,1,N,\"ROLLO PAPEL CAMILLA\"\r\n");
            StringAssert.Contains(impreso, "B28,62,0,1,2,2,52,N,\"17404\"\r\n");
            StringAssert.Contains(impreso, "A28,120,0,4,1,1,N,\"17404\"\r\n");

            _ = impresora.Imprimir(REINA, Trabajo(Bulto(), "Etiquetas de bulto"));
            StringAssert.Contains(impreso, "B73,8,0,1,1,1,64,N,\"R81010-1\"\r\n");
            StringAssert.Contains(impreso, "A34,84,0,3,1,1,N,\"81010 - bulto 1/1\"\r\n");
            StringAssert.Contains(impreso, "A82,112,0,3,1,1,N,\"ALC > ALG\"\r\n");
        }

        [TestMethod]
        public void Desplazar_NoTocaLaCabeceraNiLosTextosEntreComillas()
        {
            string epl = "I8,A,034\r\nq240\r\nQ160,24\r\nN\r\nA10,10,0,2,1,1,N,\"B12,34 A5,6\"\r\nP1\r\n";

            string desplazado = DesplazadorEpl.Desplazar(epl, 8, 8);

            // La anchura (q) crece lo mismo que se desplaza a la derecha; el resto de la cabecera no se toca
            Assert.AreEqual("I8,A,034\r\nq248\r\nQ160,24\r\nN\r\nA18,18,0,2,1,1,N,\"B12,34 A5,6\"\r\nP1\r\n", desplazado);
        }

        [TestMethod]
        public void Desplazar_ALaDerecha_AmpliaLaAnchuraParaQueElMarcoNoSeSalga()
        {
            // 19/10/26, impresora de Reina: con X=16 el marco iba a 255 con q240 y salió un recuadro negro relleno
            string epl = "I8,A,034\r\nq240\r\nQ160,24\r\nN\r\nX0,0,2,239,159\r\nP1\r\n";

            string desplazado = DesplazadorEpl.Desplazar(epl, 16, 0);

            StringAssert.Contains(desplazado, "q256\r\n");
            StringAssert.Contains(desplazado, "X16,0,2,255,159\r\n");
        }

        [TestMethod]
        public void Desplazar_ALaIzquierda_NoCambiaLaAnchura()
        {
            string epl = "I8,A,034\r\nq240\r\nQ160,24\r\nN\r\nA20,10,0,2,1,1,N,\"X\"\r\nP1\r\n";

            StringAssert.Contains(DesplazadorEpl.Desplazar(epl, -8, 0), "q240\r\n");
        }

        [TestMethod]
        public void Desplazar_LasCajasYLasLineasInclinadasMuevenSusDosPuntos_YLasRectasSoloElPrincipio()
        {
            Assert.AreEqual("X10,20,2,249,179\r\n", DesplazadorEpl.DesplazarLinea("X0,0,2,239,159\r\n", 10, 20));
            Assert.AreEqual("LS10,20,3,110,120\r\n", DesplazadorEpl.DesplazarLinea("LS0,0,3,100,100\r\n", 10, 20));
            Assert.AreEqual("LO18,8,1,6\r\n", DesplazadorEpl.DesplazarLinea("LO8,0,1,6\r\n", 10, 8));
        }

        [TestMethod]
        public void Desplazar_HaciaAtrasNoBajaDeCero()
        {
            Assert.AreEqual("A0,2,0,3,1,1,N,\"x\"\r\n", DesplazadorEpl.DesplazarLinea("A12,6,0,3,1,1,N,\"x\"\r\n", -20, -4));
        }

        // ---- La etiqueta de prueba ----

        [TestMethod]
        public void EtiquetaDePrueba_LlevaElMarcoEnElBordeYLasReglasCadaMilimetro()
        {
            string epl = GeneradorEtiquetaPrueba.Epl(0, 0);

            StringAssert.StartsWith(epl, "I8,A,034\r\nq240\r\nQ160,24\r\nN\r\n");
            StringAssert.Contains(epl, "X0,0,2,239,159\r\n");
            string[] lineas = epl.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(29, lineas.Count(l => l.StartsWith("LO") && !l.StartsWith("LO0,")), "Regla de X: 1 a 29 mm");
            Assert.AreEqual(19, lineas.Count(l => l.StartsWith("LO0,")), "Regla de Y: 1 a 19 mm");
            StringAssert.Contains(epl, "LO80,0,1,18\r\n"); // 10 mm: la marca larga
            StringAssert.Contains(epl, "LO40,0,1,12\r\n"); // 5 mm
            StringAssert.Contains(epl, "LO8,0,1,6\r\n");   // 1 mm
            StringAssert.Contains(epl, "\"X=0 Y=0 puntos\"");
            Assert.IsTrue(epl.EndsWith("P1\r\n"));
        }

        [TestMethod]
        public void ImprimirPrueba_SinValores_UsaElAjusteGuardadoYSaleDesplazada()
        {
            var parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => repositorio.Leer(REINA.ToUpperInvariant())).Returns(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 24, DesplazamientoY = 0 });
            var servicio = new ServicioAjustesImpresoras(repositorio, parametros, impresoraReal, lenguajes);

            ResultadoEtiquetaPruebaDTO resultado = servicio.ImprimirPrueba("1", REINA, null, null, "NUEVAVISION\\Carlos");

            Assert.AreEqual(24, resultado.DesplazamientoX);
            StringAssert.Contains(impreso, "X24,0,2,263,159\r\n");
            StringAssert.Contains(impreso, "\"X=24 Y=0 puntos\"");
            A.CallTo(() => impresoraReal.Imprimir(REINA, A<string>._, "Etiqueta de prueba")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ImprimirPrueba_ConValores_LosPruebaSinGuardarlos_YSinImpresoraVaALaDelUsuario()
        {
            var parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametro("1", "Carlos", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas2 ");
            var servicio = new ServicioAjustesImpresoras(repositorio, parametros, impresoraReal, lenguajes);

            ResultadoEtiquetaPruebaDTO resultado = servicio.ImprimirPrueba("1", null, 8, 16, "NUEVAVISION\\Carlos");

            Assert.AreEqual(@"\\RDS2016\etiquetas2", resultado.Impresora);
            StringAssert.Contains(impreso, "X8,16,2,247,175\r\n");
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        // ---- Guardar ----

        [TestMethod]
        public void Guardar_NormalizaLaImpresora()
        {
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<int>._, A<string>._)).Returns(true);
            var servicio = new ServicioAjustesImpresoras(repositorio, A.Fake<ILectorParametrosUsuario>(), impresoraReal, lenguajes);

            _ = servicio.Guardar(new AjusteImpresoraEtiquetasDTO { Impresora = " " + REINA + " ", DesplazamientoX = 24 }, "NUEVAVISION\\Carlos");

            A.CallTo(() => repositorio.Guardar(REINA.ToUpperInvariant(), 24, 0, "NUEVAVISION\\Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Guardar_ValoresMayoresQueLaEtiqueta_NoSeGuardan()
        {
            var servicio = new ServicioAjustesImpresoras(repositorio, A.Fake<ILectorParametrosUsuario>(), impresoraReal, lenguajes);

            _ = Assert.ThrowsException<ArgumentException>(() => servicio.Guardar(new AjusteImpresoraEtiquetasDTO { Impresora = REINA, DesplazamientoX = 300 }, "Carlos"));
            _ = Assert.ThrowsException<ArgumentException>(() => servicio.Guardar(new AjusteImpresoraEtiquetasDTO { DesplazamientoX = 8 }, "Carlos"));
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Guardar_SinLaTabla_AvisaDeQueFaltaElScript()
        {
            A.CallTo(() => repositorio.Guardar(A<string>._, A<int>._, A<int>._, A<string>._)).Returns(false);
            var servicio = new ServicioAjustesImpresoras(repositorio, A.Fake<ILectorParametrosUsuario>(), impresoraReal, lenguajes);

            _ = Assert.ThrowsException<InvalidOperationException>(() => servicio.Guardar(new AjusteImpresoraEtiquetasDTO { Impresora = REINA, DesplazamientoX = 8 }, "Carlos"));
        }

        [TestMethod]
        public void Leer_SinAjusteGuardado_CeroYCero()
        {
            var servicio = new ServicioAjustesImpresoras(repositorio, A.Fake<ILectorParametrosUsuario>(), impresoraReal, lenguajes);

            AjusteImpresoraEtiquetasDTO ajuste = servicio.Leer(REINA);

            Assert.AreEqual(0, ajuste.DesplazamientoX);
            Assert.AreEqual(0, ajuste.DesplazamientoY);
            Assert.AreEqual(REINA.ToUpperInvariant(), ajuste.Impresora);
        }
    }
}
