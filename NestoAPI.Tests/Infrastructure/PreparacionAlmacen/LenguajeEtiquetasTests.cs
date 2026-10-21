using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#685: la Zebra de Alcobendas (driver «ZDesigner ZD230-203dpi ZPL») sacaba en blanco las etiquetas en EPL. El
    /// idioma de cada impresora se decide en un solo sitio: forzado en Web.config o, si no, por el nombre del driver de la cola.
    /// </summary>
    [TestClass]
    public class LenguajeEtiquetasTests
    {
        [DataTestMethod]
        [DataRow("ZDesigner ZD230-203dpi ZPL", LenguajeEtiquetas.Zpl)]
        [DataRow("ZDesigner ZD420-203dpi ZPL", LenguajeEtiquetas.Zpl)]
        [DataRow("ZDesigner ZT410-203dpi", LenguajeEtiquetas.Zpl)]
        [DataRow("ZDesigner GK420d (EPL)", LenguajeEtiquetas.Epl)]
        [DataRow("Generic / Text Only", LenguajeEtiquetas.Epl)]
        [DataRow("Honeywell PC42d (203 dpi)", LenguajeEtiquetas.Epl)]
        [DataRow("", LenguajeEtiquetas.Epl)]
        [DataRow(null, LenguajeEtiquetas.Epl)]
        public void DelDriver_ZplOZDesignerEsZpl_SalvoQueDigaEpl(string driver, LenguajeEtiquetas esperado)
        {
            Assert.AreEqual(esperado, LenguajeImpresorasWindows.DelDriver(driver));
        }

        [TestMethod]
        public void De_LeeElDriverUnaSolaVezPorImpresora()
        {
            int lecturas = 0;
            var lenguajes = new LenguajeImpresorasWindows(i => { lecturas++; return "ZDesigner ZD230-203dpi ZPL"; });

            Assert.AreEqual(LenguajeEtiquetas.Zpl, lenguajes.De(@"\\RDS2016\etiquetasalcobendas"));
            Assert.AreEqual(LenguajeEtiquetas.Zpl, lenguajes.De(@" \\rds2016\ETIQUETASALCOBENDAS "));

            Assert.AreEqual(1, lecturas);
        }

        [TestMethod]
        public void De_SiNoSePuedeLeerElDriver_EplComoHastaAhoraYLoVuelveAIntentar()
        {
            int lecturas = 0;
            var lenguajes = new LenguajeImpresorasWindows(i =>
            {
                lecturas++;
                if (lecturas == 1)
                {
                    throw new ImpresionEtiquetasException("No se puede abrir la impresora (error de Windows 5).");
                }
                return "ZDesigner ZD420-203dpi ZPL";
            });

            Assert.AreEqual(LenguajeEtiquetas.Epl, lenguajes.De(@"\\RDS2016\etiquetas3"));
            Assert.AreEqual(LenguajeEtiquetas.Zpl, lenguajes.De(@"\\RDS2016\etiquetas3"));
        }

        [TestMethod]
        public void De_LaForzadaEnWebConfigManda_SinLeerElDriver()
        {
            IDictionary<string, LenguajeEtiquetas> forzadas = LenguajeImpresorasWindows.Forzadas(@"\\RDS2016\etiquetas4", @" \\RDS2016\EtiquetasAlcobendas ;");
            var lenguajes = new LenguajeImpresorasWindows(i => throw new InvalidOperationException("no tiene que leer el driver"), () => forzadas);

            Assert.AreEqual(LenguajeEtiquetas.Zpl, lenguajes.De(@"\\RDS2016\etiquetas4"));
            Assert.AreEqual(LenguajeEtiquetas.Epl, lenguajes.De(@"\\RDS2016\etiquetasalcobendas"));
        }

        [TestMethod]
        public void Forzadas_EnLasDos_GanaZpl_YSinNadaVacia()
        {
            Assert.AreEqual(LenguajeEtiquetas.Zpl, LenguajeImpresorasWindows.Forzadas("x", "X")["X"]);
            Assert.AreEqual(0, LenguajeImpresorasWindows.Forzadas(null, " ; ").Count);
        }

        [TestMethod]
        public void De_SinImpresora_Epl()
        {
            var lenguajes = new LenguajeImpresorasWindows(i => throw new InvalidOperationException("no tiene que leer el driver"));

            Assert.AreEqual(LenguajeEtiquetas.Epl, lenguajes.De("  "));
        }

        [TestMethod]
        public void TrabajoEtiquetas_DaElContenidoDelIdioma()
        {
            var trabajo = new TrabajoEtiquetas("epl", "zpl", "Etiquetas");

            Assert.AreEqual("epl", trabajo.En(LenguajeEtiquetas.Epl));
            Assert.AreEqual("zpl", trabajo.En(LenguajeEtiquetas.Zpl));
        }

        [TestMethod]
        public void EscaparZpl_LoQueNoEsAsciiYLosCaracteresDeZplEnHexadecimal()
        {
            Assert.AreEqual("Caf_C3_A9 _5E_7E_5F ok", ComandosZpl.Escapar("Café ^~_ ok\r\n"));
            Assert.AreEqual(string.Empty, ComandosZpl.Escapar(null));
        }
    }
}
