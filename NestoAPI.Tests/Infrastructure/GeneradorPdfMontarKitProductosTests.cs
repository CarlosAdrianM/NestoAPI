using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF del informe "Montar kit" (migración RDLC -> backend,
    /// Nesto#340). Verifican que se produce un PDF válido (cabecera %PDF) con datos, con
    /// lista vacía y con lista null.
    /// </summary>
    [TestClass]
    public class GeneradorPdfMontarKitProductosTests
    {
        private GeneradorPdfMontarKitProductos _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfMontarKitProductos();
        }

        [TestMethod]
        public void GenerarPdf_ConLineasDelKit_DevuelvePdfValido()
        {
            var lineas = new List<MontarKitProductosDTO>
            {
                new MontarKitProductosDTO { Producto = "17404", Nombre = "GEL HIDRATANTE", Tamanno = 500, UnidadMedida = "ml", Familia = "Lisap", Cantidad = 5, Pasillo = "2", Fila = "3", Columna = "B", CodigoBarras = "8436566600001" },
                new MontarKitProductosDTO { Producto = "17405", Nombre = "CHAMPU SUAVE", Tamanno = null, UnidadMedida = null, Familia = "Agrado", Cantidad = 10, Pasillo = "1", Fila = "1", Columna = "A", CodigoBarras = "8436566600002" }
            };

            var resultado = _generador.GenerarPdf(123456, lineas);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaVacia_DevuelvePdfValido()
        {
            var resultado = _generador.GenerarPdf(123456, new List<MontarKitProductosDTO>());

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Una lista vacía también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_ListaNull_NoLanzaYDevuelvePdf()
        {
            var resultado = _generador.GenerarPdf(123456, null);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        // Los PDF empiezan por la firma "%PDF" (0x25 0x50 0x44 0x46).
        private static void ComprobarCabeceraPdf(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length >= 4, "El PDF es demasiado corto");
            Assert.AreEqual(0x25, bytes[0]);
            Assert.AreEqual(0x50, bytes[1]);
            Assert.AreEqual(0x44, bytes[2]);
            Assert.AreEqual(0x46, bytes[3]);
        }
    }
}
